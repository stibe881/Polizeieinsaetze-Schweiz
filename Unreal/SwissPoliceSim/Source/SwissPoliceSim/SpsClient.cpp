#include "SpsClient.h"
#include "WebSocketsModule.h"
#include "IWebSocket.h"
#include "HttpModule.h"
#include "Interfaces/IHttpRequest.h"
#include "Interfaces/IHttpResponse.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"
#include "Engine/World.h"
#include "EngineUtils.h"
#include "Kismet/GameplayStatics.h"
#include "GameFramework/PlayerController.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "Misc/ConfigCacheIni.h"
#include "Misc/CommandLine.h"
#include "Misc/Parse.h"
#include "HAL/PlatformTime.h"
#include "SpsTile.h"
#include "SpsVehicle.h"
#include "SpsHuman.h"
#include "SpsPlayer.h"
#include "SpsAtmosphere.h"
#include "SpsWeather.h"

namespace
{
	double Num(const TSharedPtr<FJsonObject>& O, const TCHAR* K, double Def = 0.0) { double V = Def; if (O.IsValid()) O->TryGetNumberField(K, V); return V; }
	FString Str(const TSharedPtr<FJsonObject>& O, const TCHAR* K) { FString S; if (O.IsValid()) O->TryGetStringField(K, S); return S; }
	bool Bool(const TSharedPtr<FJsonObject>& O, const TCHAR* K, bool Def = false) { bool B = Def; if (O.IsValid()) O->TryGetBoolField(K, B); return B; }
	const TCHAR* GOfficerModel = TEXT("human:officer:m:0");
	const TCHAR* GPoliceCarModel = TEXT("car:police");
}

ASpsClient::ASpsClient()
{
	PrimaryActorTick.bCanEverTick = true;
	SetRootComponent(CreateDefaultSubobject<USceneComponent>(TEXT("Root")));
	// Hinweis: UE liest "//" in INI-Werten als Kommentar. Deshalb Host und Port getrennt, die URLs werden hier gebaut.
	FString Host = TEXT("127.0.0.1"); int32 Port = 47800;
	if (GConfig) { GConfig->GetString(TEXT("/Script/SwissPoliceSim.SpsSettings"), TEXT("ServerHost"), Host, GGameIni); GConfig->GetInt(TEXT("/Script/SwissPoliceSim.SpsSettings"), TEXT("ServerPort"), Port, GGameIni); }
	ServerUrl = FString::Printf(TEXT("ws://%s:%d/ws"), *Host, Port);
	UiUrl = FString::Printf(TEXT("http://%s:%d/?embedded=1"), *Host, Port);
	HttpBase = FString::Printf(TEXT("http://%s:%d"), *Host, Port);
}

ASpsClient* ASpsClient::Get(UWorld* World)
{
	if (!World) return nullptr;
	for (TActorIterator<ASpsClient> It(World); It; ++It) return *It;
	return nullptr;
}

void ASpsClient::BeginPlay()
{
	Super::BeginPlay();
	FString Override; if (FParse::Value(FCommandLine::Get(), TEXT("-SpsServer="), Override)) ServerUrl = Override;
	// Das Basic-Level bringt eigene Himmel-/Licht-Objekte und einen Boden mit. Sie wuerden mit der Atmosphaere dieses Spiels kollidieren (mehrere Sonnen, Boden auf Z=0).
	{
		TArray<AActor*> Doomed; static const TCHAR* Names[] = { TEXT("DirectionalLight"), TEXT("SkyAtmosphere"), TEXT("SkyLight"), TEXT("ExponentialHeightFog"), TEXT("VolumetricCloud"), TEXT("StaticMeshActor") };
		for (TActorIterator<AActor> It(GetWorld()); It; ++It)
		{
			if (*It == this) continue;
			const FString Cls = It->GetClass()->GetName();
			for (const TCHAR* N : Names) if (Cls == N) { Doomed.Add(*It); break; }
		}
		for (AActor* A : Doomed) A->Destroy();
		if (Doomed.Num() > 0) UE_LOG(LogTemp, Warning, TEXT("[SPS] %d Template-Objekte aus dem Level entfernt"), Doomed.Num());
	}
	// Lumen kennt Procedural-Mesh-Gelaende/-Gebaeude nicht (keine Mesh-Cards/Distanzfelder): ohne diese Umstellung bleiben Fassaden ohne Streu-Licht schwarz.
	// Stattdessen: Himmelslicht + Sonne + SSR/Ambient Occlusion. Laeuft zur Laufzeit, die Projekt-INI bleibt unberuehrt.
	if (GEngine)
	{
		GEngine->Exec(GetWorld(), TEXT("r.DynamicGlobalIlluminationMethod 0"));
		GEngine->Exec(GetWorld(), TEXT("r.ReflectionMethod 0"));   // Bildschirm-Reflexionen spiegelten den hellen Himmel in jede Strasse
		GEngine->Exec(GetWorld(), TEXT("r.SSR.Quality 0"));
		// Virtuelle Shadow Maps sind fuer Nanite gebaut; fuer Procedural-Meshes (Warnung "Jobwarteschlangen-Ueberlauf") sind klassische Kaskaden deutlich billiger
		GEngine->Exec(GetWorld(), TEXT("r.Shadow.Virtual.Enable 0"));
		GEngine->Exec(GetWorld(), TEXT("r.Shadow.CSM.MaxCascades 3"));
		GEngine->Exec(GetWorld(), TEXT("r.Shadow.DistanceScale 0.7"));
		GEngine->Exec(GetWorld(), TEXT("r.AmbientOcclusionStaticFraction 1"));
		// Leistung: Die Welt besteht aus grossen Procedural-Meshes ohne LOD; teure Effekte reduzieren
		GEngine->Exec(GetWorld(), TEXT("r.VolumetricFog 0"));
		GEngine->Exec(GetWorld(), TEXT("r.ScreenPercentage 80"));
		GEngine->Exec(GetWorld(), TEXT("r.Shadow.Virtual.ResolutionLodBiasDirectional 1"));
		GEngine->Exec(GetWorld(), TEXT("r.Shadow.Virtual.MaxPhysicalPages 2048"));
		GEngine->Exec(GetWorld(), TEXT("r.MotionBlurQuality 0"));
		GEngine->Exec(GetWorld(), TEXT("r.DistanceFieldAO 0"));
	}
	Atmosphere = GetWorld()->SpawnActor<ASpsAtmosphere>(FVector::ZeroVector, FRotator::ZeroRotator);
	Weather = GetWorld()->SpawnActor<ASpsWeather>(FVector::ZeroVector, FRotator::ZeroRotator);
	// a flying "hover" pawn gives a sky view behind the menu until the game starts
	PlayerFoot = GetWorld()->SpawnActor<ASpsPlayerFoot>(FVector(0, 0, 30000.f), FRotator::ZeroRotator);
	if (PlayerFoot) { PlayerFoot->GetCharacterMovement()->SetMovementMode(MOVE_Flying); if (APlayerController* PC = GetWorld()->GetFirstPlayerController()) PC->Possess(PlayerFoot); }
	Connect();
}

void ASpsClient::EndPlay(const EEndPlayReason::Type Reason)
{
	if (Socket.IsValid()) { Socket->OnConnected().RemoveAll(this); Socket->OnMessage().RemoveAll(this); Socket->OnBinaryMessage().RemoveAll(this); Socket->OnClosed().RemoveAll(this); Socket->OnConnectionError().RemoveAll(this); Socket->Close(); Socket.Reset(); }
	Super::EndPlay(Reason);
}

// ------------------------------------------------------------------ connection
void ASpsClient::Connect()
{
	Socket = FModuleManager::LoadModuleChecked<FWebSocketsModule>(TEXT("WebSockets")).CreateWebSocket(ServerUrl);
	Socket->OnConnected().AddUObject(this, &ASpsClient::OnConnected);
	Socket->OnMessage().AddUObject(this, &ASpsClient::OnText);
	Socket->OnBinaryMessage().AddUObject(this, &ASpsClient::OnBinary);
	Socket->OnClosed().AddLambda([this](int32 Code, const FString& Why, bool) { UE_LOG(LogTemp, Warning, TEXT("[SPS] Verbindung geschlossen (%d): %s"), Code, *Why); bConnected = false; ReconnectIn = 2.f; });
	Socket->OnConnectionError().AddLambda([this](const FString& Err) { UE_LOG(LogTemp, Warning, TEXT("[SPS] Verbindungsfehler zu %s: %s"), *ServerUrl, *Err); bConnected = false; ReconnectIn = 2.f; });
	UE_LOG(LogTemp, Warning, TEXT("[SPS] Verbinde zu %s"), *ServerUrl);
	Socket->Connect();
}

void ASpsClient::OnConnected()
{
	UE_LOG(LogTemp, Warning, TEXT("[SPS] Verbunden mit dem Server"));
	bConnected = true; Requested.Reset();
	SendText(TEXT("{\"t\":\"hello\",\"role\":\"game\",\"name\":\"unreal\",\"http\":true}"));
	RequestModel(GOfficerModel); RequestModel(GPoliceCarModel);
}

void ASpsClient::SendText(const FString& Json) { if (Socket.IsValid() && Socket->IsConnected()) Socket->Send(Json); }

void ASpsClient::SendCommand(const FString& Type, const TMap<FString, FString>& Fields)
{
	FString S = FString::Printf(TEXT("{\"t\":\"%s\""), *Type);
	for (const TPair<FString, FString>& P : Fields) S += FString::Printf(TEXT(",\"%s\":%s"), *P.Key, *P.Value);
	S += TEXT("}"); SendText(S);
}

void ASpsClient::SendUi(const FString& Key, const FString& Value) { SendText(FString::Printf(TEXT("{\"t\":\"ui\",\"%s\":%s}"), *Key, *Value)); }

void ASpsClient::RequestModel(const FString& Key)
{
	if (Requested.Contains(Key) || Models.Contains(Key)) return;
	Requested.Add(Key); SendText(FString::Printf(TEXT("{\"t\":\"model\",\"key\":\"%s\"}"), *Key));
}

void ASpsClient::OnText(const FString& Msg) { TextQueue.Add(Msg); }

void ASpsClient::FetchTile(const FString& Token)
{
	TSharedRef<IHttpRequest, ESPMode::ThreadSafe> Req = FHttpModule::Get().CreateRequest();
	Req->SetURL(HttpBase + TEXT("/tile/") + Token); Req->SetVerb(TEXT("GET"));
	Req->OnProcessRequestComplete().BindUObject(this, &ASpsClient::OnTileHttp);
	Req->ProcessRequest();
}

void ASpsClient::OnTileHttp(TSharedPtr<IHttpRequest, ESPMode::ThreadSafe> Req, TSharedPtr<IHttpResponse, ESPMode::ThreadSafe> Resp, bool bOk)
{
	if (!bOk || !Resp.IsValid() || Resp->GetResponseCode() != 200) { UE_LOG(LogTemp, Warning, TEXT("[SPS] Tile-Download fehlgeschlagen (HTTP %d)"), Resp.IsValid() ? Resp->GetResponseCode() : 0); return; }
	BinaryQueue.Add(Resp->GetContent());
}

void ASpsClient::OnBinary(const void* Data, SIZE_T Size, bool bLast)
{
	const int32 Old = Reassembly.Num(); Reassembly.AddUninitialized(static_cast<int32>(Size)); FMemory::Memcpy(Reassembly.GetData() + Old, Data, Size);
	if (bLast) { BinaryQueue.Add(MoveTemp(Reassembly)); Reassembly.Reset(); }
}

void ASpsClient::OnPlayerCrash(AActor* Other, float ImpactMs)
{
	if (const ASpsCar* C = Cast<ASpsCar>(Other)) { if (C->ServerId > 0) SendText(FString::Printf(TEXT("{\"t\":\"collision\",\"vehicleId\":%d,\"rel\":%.2f}"), C->ServerId, ImpactMs)); }
	else if (ASpsHuman* H = Cast<ASpsHuman>(Other)) { if (H->ServerId > 0 && ImpactMs > 1.5f) { H->Fall(); SendText(FString::Printf(TEXT("{\"t\":\"pedHit\",\"id\":%d,\"rel\":%.2f,\"actor\":%s}"), H->ServerId, ImpactMs, Actors.Contains(H->ServerId) ? TEXT("true") : TEXT("false"))); } }
}

// ------------------------------------------------------------------ message handling
void ASpsClient::HandleText(const TSharedPtr<FJsonObject>& J)
{
	const FString T = Str(J, TEXT("t"));
	if (T == TEXT("tileReady")) { FetchTile(Str(J, TEXT("token"))); return; }
	if (T == TEXT("started"))
	{
		bStarted = true; bPlaying = true; DropAllTiles(); ClearEntities();
		StartE = Num(J, TEXT("e")); StartN = Num(J, TEXT("n")); StartH = Num(J, TEXT("h")); StartVE = Num(J, TEXT("vE")); StartVN = Num(J, TEXT("vN")); StartVYaw = Num(J, TEXT("vYaw")); bStartInVehicle = Bool(J, TEXT("inVehicle"), false); StartYaw = static_cast<float>(Num(J, TEXT("pYaw")));
		OriginE = FMath::FloorToDouble(StartE / 1000.0) * 1000.0; OriginN = FMath::FloorToDouble(StartN / 1000.0) * 1000.0;
		bPlacePending = true; PlaceSince = FPlatformTime::Seconds(); PlaceLogAt = PlaceSince;
		// bis der Boden fest ist: ueber der Startposition schweben (sonst sieht man das Gelaende von unten: Hoehen sind absolut, Bern liegt bei ~540 m)
		if (PlayerFoot) PlayerFoot->SetActorLocation(ToWorld(StartE, StartN, StartH + 150.0));
		UE_LOG(LogTemp, Warning, TEXT("[SPS] Spielstart bei E %.0f N %.0f H %.1f m, Ursprung %.0f/%.0f"), StartE, StartN, StartH, OriginE, OriginN);
	}
	else if (T == TEXT("state"))
	{
		const TSharedPtr<FJsonObject>* Sun = nullptr; const TSharedPtr<FJsonObject>* Wx = nullptr; const TSharedPtr<FJsonObject>* Rad = nullptr;
		if (Atmosphere && J->TryGetObjectField(TEXT("sun"), Sun) && J->TryGetObjectField(TEXT("weather"), Wx))
		{
			if (Weather) Weather->SetIntensity(static_cast<float>(Num(*Wx, TEXT("rain"))) / 8.f, static_cast<float>(Num(*Wx, TEXT("snow"))) / 5.f);
			Atmosphere->Apply(static_cast<float>(Num(*Sun, TEXT("el"))), static_cast<float>(Num(*Sun, TEXT("az"))), static_cast<float>(Num(*Sun, TEXT("daylight"), 1.0)), static_cast<float>(Num(*Wx, TEXT("cloud"))), static_cast<float>(Num(*Wx, TEXT("vis"), 20000.0)), static_cast<float>(Num(*Wx, TEXT("rain"))));
		}
		if (J->TryGetObjectField(TEXT("radar"), Rad)) bRadarOn = Bool(*Rad, TEXT("on"));
	}
	else if (T == TEXT("spawn"))
	{
		FPendingSpawn S; S.Kind = Str(J, TEXT("k")); S.Id = static_cast<int32>(Num(J, TEXT("id"))); S.Model = Str(J, TEXT("model")); S.Plate = Str(J, TEXT("plate")); PendingSpawns.Add(S);
	}
	else if (T == TEXT("despawn"))
	{
		const FString K = Str(J, TEXT("k")); const TArray<TSharedPtr<FJsonValue>>* Ids = nullptr;
		if (J->TryGetArrayField(TEXT("ids"), Ids)) for (const TSharedPtr<FJsonValue>& V : *Ids)
		{
			const int32 Id = static_cast<int32>(V->AsNumber());
			if (K == TEXT("car")) { if (TObjectPtr<ASpsCar>* C = Cars.Find(Id)) { if (*C) (*C)->Destroy(); Cars.Remove(Id); } PendingSpawns.RemoveAll([Id, &K](const FPendingSpawn& P) { return P.Id == Id && P.Kind == K; }); }
			else { if (TObjectPtr<ASpsHuman>* H = Peds.Find(Id)) { if (*H) (*H)->Destroy(); Peds.Remove(Id); } PendingSpawns.RemoveAll([Id, &K](const FPendingSpawn& P) { return P.Id == Id && P.Kind == K; }); }
		}
	}
	else if (T == TEXT("props"))
	{
		for (auto& P : Props) { if (P.Value) P.Value->Destroy(); } Props.Reset(); PendingProps.Reset();
		const TArray<TSharedPtr<FJsonValue>>* Items = nullptr;
		if (J->TryGetArrayField(TEXT("items"), Items)) for (const TSharedPtr<FJsonValue>& V : *Items)
		{
			const TSharedPtr<FJsonObject> O = V->AsObject(); FPropInfo I; I.Id = static_cast<int32>(Num(O, TEXT("id"))); I.Kind = Str(O, TEXT("kind")); I.Model = Str(O, TEXT("model")); I.Plate = Str(O, TEXT("plate")); I.bHazard = Bool(O, TEXT("hazard")); I.bBlue = Bool(O, TEXT("blue")); I.E = Num(O, TEXT("e")); I.N = Num(O, TEXT("n")); I.H = Num(O, TEXT("h")); I.Yaw = static_cast<float>(Num(O, TEXT("yaw"))); PendingProps.Add(I);
		}
	}
	else if (T == TEXT("actors"))
	{
		TSet<int32> Keep; const TArray<TSharedPtr<FJsonValue>>* Items = nullptr;
		if (J->TryGetArrayField(TEXT("items"), Items)) for (const TSharedPtr<FJsonValue>& V : *Items)
		{
			const TSharedPtr<FJsonObject> O = V->AsObject(); const int32 Id = static_cast<int32>(Num(O, TEXT("id"))); Keep.Add(Id);
			if (!Actors.Contains(Id)) { FPendingSpawn S; S.Kind = TEXT("actor"); S.Id = Id; S.Model = Str(O, TEXT("model")); PendingSpawns.Add(S); }
		}
		for (auto It = Actors.CreateIterator(); It; ++It) if (!Keep.Contains(It.Key())) { if (It.Value()) It.Value()->Destroy(); It.RemoveCurrent(); }
	}
	else if (T == TEXT("ui"))
	{
		if (J->HasField(TEXT("focus"))) { bUiFocus = Bool(J, TEXT("focus")); OnUiFocus.Broadcast(bUiFocus); }
	}
}

void ASpsClient::HandleBinary(const TArray<uint8>& Data)
{
	if (Data.Num() < 2) return;
	if (Data[0] == SpsMsg::Chunk)
	{
		// grosse Frames kommen in Chunks: u8 6, i32 index, i32 count, Nutzdaten (siehe Msg.Chunk im Server)
		if (Data.Num() < 9) return;
		int32 Index = 0, Count = 0; FMemory::Memcpy(&Index, Data.GetData() + 1, 4); FMemory::Memcpy(&Count, Data.GetData() + 5, 4);
		if (Index == 0) ChunkBuffer.Reset();
		ChunkBuffer.Append(Data.GetData() + 9, Data.Num() - 9);
		if (Index == Count - 1) { TArray<uint8> Whole = MoveTemp(ChunkBuffer); ChunkBuffer.Reset(); HandleBinary(Whole); }
		return;
	}
	switch (Data[0])
	{
	case SpsMsg::Tile:
	{
		FSpsTileData T;
		if (SpsProtocol::ParseTile(Data, T)) { UE_LOG(LogTemp, Warning, TEXT("[SPS] Tile-Daten %d/%d empfangen (%.1f MB, Qualitaet %d)"), T.X, T.Y, Data.Num() / 1048576.0, static_cast<int32>(T.Quality)); TileQueue.Add(MoveTemp(T)); }
		else UE_LOG(LogTemp, Warning, TEXT("[SPS] Tile-Daten (%.1f MB) konnten nicht gelesen werden - verworfen"), Data.Num() / 1048576.0);
		break;
	}
	case SpsMsg::Snapshot: { if (SpsProtocol::ParseSnapshot(Data, LastSnapshot)) bHaveSnapshot = true; break; }
	case SpsMsg::TileDrop: { int32 X, Y; if (SpsProtocol::ParseTileDrop(Data, X, Y)) { if (X == INT32_MIN) DropAllTiles(); else DropTile(X, Y); } break; }
	case SpsMsg::Model: { TSharedPtr<FSpsModelData> M = MakeShared<FSpsModelData>(); if (SpsProtocol::ParseModel(Data, *M)) Models.Add(M->Key, M); break; }
	default: break;
	}
}

// ------------------------------------------------------------------ world content
void ASpsClient::DropTile(int32 X, int32 Y)
{
	const FIntPoint K(X, Y);
	if (TObjectPtr<ASpsTile>* T = Tiles.Find(K)) { if (*T == BuildingTile) BuildingTile = nullptr; if (*T) (*T)->Destroy(); Tiles.Remove(K); }
	TileQueue.RemoveAll([X, Y](const FSpsTileData& D) { return D.X == X && D.Y == Y; });
	PendingTrees.RemoveAll([K](const FPendingTrees& P) { return P.Tile == K; });
}

void ASpsClient::DropAllTiles()
{
	BuildingTile = nullptr;
	for (auto& T : Tiles) { if (T.Value) T.Value->Destroy(); }
	Tiles.Reset(); TileQueue.Reset(); PendingTrees.Reset();
}

void ASpsClient::ClearEntities()
{
	for (auto& C : Cars) { if (C.Value) C.Value->Destroy(); } Cars.Reset();
	for (auto& P : Peds) { if (P.Value) P.Value->Destroy(); } Peds.Reset();
	for (auto& A : Actors) { if (A.Value) A.Value->Destroy(); } Actors.Reset();
	for (auto& P : Props) { if (P.Value) P.Value->Destroy(); } Props.Reset();
	PendingSpawns.Reset(); PendingProps.Reset(); bHaveSnapshot = false;
}

void ASpsClient::ProcessTiles(double BudgetSeconds)
{
	const double Start = FPlatformTime::Seconds();
	while (true)
	{
		if (BuildingTile)
		{
			if (BuildingTile->BuildStep()) BuildingTile = nullptr;
			if (FPlatformTime::Seconds() - Start > BudgetSeconds) return;
			continue;
		}
		if (TileQueue.Num() == 0) return;
		FSpsTileData D = MoveTemp(TileQueue[0]); TileQueue.RemoveAt(0);
		UE_LOG(LogTemp, Warning, TEXT("[SPS] Tile %d/%d (Qualitaet %d, %d Meshes, Ursprung %.0f/%.0f)"), D.X, D.Y, static_cast<int32>(D.Quality), D.Meshes.Num(), OriginE, OriginN);
		const FIntPoint K(D.X, D.Y); TObjectPtr<ASpsTile>* Found = Tiles.Find(K); ASpsTile* Tile = Found ? Found->Get() : nullptr;
		if (!Tile) { Tile = GetWorld()->SpawnActor<ASpsTile>(FVector::ZeroVector, FRotator::ZeroRotator); Tiles.Add(K, Tile); }
		if (D.Trees.Num() > 0) { PendingTrees.RemoveAll([K](const FPendingTrees& P) { return P.Tile == K; }); FPendingTrees P; P.Tile = K; P.Quality = D.Quality; P.Trees = D.Trees; PendingTrees.Add(MoveTemp(P)); }
		Tile->BeginBuild(MoveTemp(D), OriginE, OriginN); BuildingTile = Tile;
	}
}

FSpsMeshData ASpsClient::MergeTrees(const FPendingTrees& P, bool& bComplete)
{
	FSpsMeshData Out; bComplete = true; Out.Sections.SetNum(2);
	const int32 Lod = P.Quality == 1 ? 1 : 2;
	for (const FSpsTree& T : P.Trees)
	{
		const FString Key = FString::Printf(TEXT("tree:%d:%d:%d:%d"), T.Kind, T.Variant, Lod, T.Bucket);
		const FSpsModelData* M = FindModel(Key);
		if (!M || M->Meshes.Num() < 1) { RequestModel(Key); bComplete = false; continue; }
		const FSpsMeshData& Src = M->Meshes[0]; if (Src.IsEmpty()) continue;
		const int32 Base = Out.Vertices.Num(); const FVector Loc(T.LocalN * 100.0, T.LocalE * 100.0, T.Ground * 100.0);
		for (int32 i = 0; i < Src.Vertices.Num(); i++) { Out.Vertices.Add(Src.Vertices[i] * T.Height + Loc); Out.Normals.Add(Src.Normals[i]); Out.UV0.Add(Src.UV0[i]); Out.Colors.Add(Src.Colors[i]); }
		for (int32 s = 0; s < Src.Sections.Num() && s < 2; s++) for (int32 Idx : Src.Sections[s]) Out.Sections[s].Add(Base + Idx);
	}
	return Out;
}

void ASpsClient::ProcessTrees()
{
	for (int32 i = PendingTrees.Num() - 1; i >= 0; i--)
	{
		bool bComplete = false; FSpsMeshData M = MergeTrees(PendingTrees[i], bComplete); if (!bComplete) continue;
		TObjectPtr<ASpsTile>* T = Tiles.Find(PendingTrees[i].Tile); if (T && *T) (*T)->SetTrees(M);
		PendingTrees.RemoveAt(i); break;   // one merge per frame
	}
}

void ASpsClient::ProcessSpawns()
{
	for (int32 i = PendingSpawns.Num() - 1; i >= 0; i--)
	{
		const FPendingSpawn& S = PendingSpawns[i]; const FSpsModelData* M = FindModel(S.Model); if (!M) { RequestModel(S.Model); continue; }
		if (S.Kind == TEXT("car")) { ASpsCar* C = GetWorld()->SpawnActor<ASpsCar>(FVector(0, 0, -500000.0), FRotator::ZeroRotator); if (C) { C->ServerId = S.Id; C->Init(*M, S.Plate); Cars.Add(S.Id, C); } }
		else if (S.Kind == TEXT("ped")) { ASpsHuman* H = GetWorld()->SpawnActor<ASpsHuman>(FVector(0, 0, -500000.0), FRotator::ZeroRotator); if (H) { H->ServerId = S.Id; H->Init(*M); Peds.Add(S.Id, H); } }
		else if (S.Kind == TEXT("actor")) { ASpsHuman* H = GetWorld()->SpawnActor<ASpsHuman>(FVector(0, 0, -500000.0), FRotator::ZeroRotator); if (H) { H->ServerId = S.Id; H->Init(*M); Actors.Add(S.Id, H); } }
		PendingSpawns.RemoveAt(i);
	}
}

void ASpsClient::ProcessProps()
{
	for (int32 i = PendingProps.Num() - 1; i >= 0; i--)
	{
		const FPropInfo& P = PendingProps[i]; const FVector Loc = ToWorld(P.E, P.N, P.H);
		if (P.Kind == TEXT("car") || P.Kind == TEXT("ems") || P.Kind == TEXT("tow"))
		{
			const FSpsModelData* M = FindModel(P.Model); if (!M) { RequestModel(P.Model); continue; }
			ASpsCar* C = GetWorld()->SpawnActor<ASpsCar>(Loc, FRotator::ZeroRotator); if (C) { C->ServerId = 0; C->Init(*M, P.Plate); C->SetStatic(Loc, P.Yaw, P.bHazard, P.bBlue); Props.Add(P.Id, C); }
		}
		else
		{
			ASpsProp* Pr = GetWorld()->SpawnActor<ASpsProp>(Loc, FRotator::ZeroRotator); if (Pr) { Pr->Build(P.Kind, P.Yaw); }
		}
		PendingProps.RemoveAt(i);
	}
}

void ASpsClient::ApplySnapshot()
{
	if (!bHaveSnapshot) return;
	for (const FSpsCarState& S : LastSnapshot.Cars) { if (TObjectPtr<ASpsCar>* C = Cars.Find(S.Id)) if (*C) (*C)->SetTarget(ToWorld(S.E, S.N, S.H), S.Yaw, S.Speed, S.Flags); }
	for (const FSpsPedState& S : LastSnapshot.Peds) { if (TObjectPtr<ASpsHuman>* H = Peds.Find(S.Id)) if (*H) (*H)->SetTarget(ToWorld(S.E, S.N, S.H), S.Yaw, S.Speed, S.Speed > 0.1f ? SpsPose::Walk : SpsPose::Idle); }
	for (const FSpsActorState& S : LastSnapshot.Actors) { if (TObjectPtr<ASpsHuman>* H = Actors.Find(S.Id)) if (*H) (*H)->SetTarget(ToWorld(S.E, S.N, S.H), S.Yaw, (S.Flags & 4) ? 5.2f : 0.f, S.Pose); }
}

void ASpsClient::TryPlacePlayer()
{
	if (!bPlacePending) return;
	const FSpsModelData* Officer = FindModel(GOfficerModel); const FSpsModelData* CarModel = FindModel(GPoliceCarModel);
	if (!Officer || !CarModel) return;
	// wait until the ground under the start position is solid (collision of the first tile is cooked asynchronously)
	FHitResult Hit; const FVector At = ToWorld(StartE, StartN, StartH);
	const double Now = FPlatformTime::Seconds();
	const bool bHit = GetWorld()->LineTraceSingleByChannel(Hit, At + FVector(0, 0, 30000.f), At - FVector(0, 0, 30000.f), ECC_Visibility);
	if (!bHit)
	{
		if (Now - PlaceLogAt > 3.0) { PlaceLogAt = Now; UE_LOG(LogTemp, Warning, TEXT("[SPS] Warte auf festen Boden bei Z=%.0f cm (%d Tiles geladen, %d in der Warteschlange)"), At.Z, Tiles.Num(), TileQueue.Num()); }
		if (Now - PlaceSince < 12.0) return;
		UE_LOG(LogTemp, Warning, TEXT("[SPS] Kein Boden-Treffer nach 12 s - platziere ohne Kollision auf der Server-Hoehe (Flugmodus)"));
		Hit.ImpactPoint = At;
		if (PlayerFoot) PlayerFoot->GetCharacterMovement()->SetMovementMode(MOVE_Flying);
	}
	const FVector Foot = Hit.ImpactPoint + FVector(0, 0, 100.f);
	if (PlayerFoot && !PlayerFoot->bBodyReady) PlayerFoot->InitBody(*Officer);
	if (PlayerFoot) { PlayerFoot->SetActorLocationAndRotation(Foot, FRotator(0, StartYaw, 0)); if (bHit) PlayerFoot->GetCharacterMovement()->SetMovementMode(MOVE_Walking); }
	FHitResult HitC; const FVector AtC = ToWorld(StartVE, StartVN, StartH);
	FVector CarLoc = AtC; if (GetWorld()->LineTraceSingleByChannel(HitC, AtC + FVector(0, 0, 30000.f), AtC - FVector(0, 0, 30000.f), ECC_Visibility)) CarLoc = HitC.ImpactPoint;
	CarLoc.Z += 105.f;   // the player car's actor origin is the middle of its collision box
	if (!PlayerCar) { PlayerCar = GetWorld()->SpawnActor<ASpsPlayerCar>(CarLoc, FRotator(0, static_cast<float>(StartVYaw), 0)); if (PlayerCar) PlayerCar->Init(*CarModel, TEXT("BE 123456")); }
	else PlayerCar->SetActorLocationAndRotation(CarLoc, FRotator(0, static_cast<float>(StartVYaw), 0));
	bPlacePending = false;
	if (APlayerController* PC = GetWorld()->GetFirstPlayerController()) { if (bStartInVehicle && PlayerCar) { PlayerCar->bOccupied = true; PC->Possess(PlayerCar); } else if (PlayerFoot) PC->Possess(PlayerFoot); }
}

void ASpsClient::MaybeRebase()
{
	if (bPlacePending || !bStarted) return;
	const AActor* Ref = (PlayerCar && PlayerCar->bOccupied) ? static_cast<AActor*>(PlayerCar) : static_cast<AActor*>(PlayerFoot);
	if (!Ref) return;
	const FVector P = Ref->GetActorLocation();
	if (FMath::Abs(P.X) < 500000.0 && FMath::Abs(P.Y) < 500000.0) return;   // 5 km from the origin: single-precision physics and rendering start to jitter
	const double Sx = FMath::RoundToDouble(P.X / 100000.0) * 100000.0, Sy = FMath::RoundToDouble(P.Y / 100000.0) * 100000.0;   // snap to whole kilometres
	const FVector Off(-Sx, -Sy, 0.0);
	OriginN += Sx / 100.0; OriginE += Sy / 100.0;
	for (auto& T : Tiles) { if (T.Value) T.Value->AddActorWorldOffset(Off, false, nullptr, ETeleportType::TeleportPhysics); }
	for (auto& C : Cars) { if (C.Value) { C.Value->AddActorWorldOffset(Off, false, nullptr, ETeleportType::TeleportPhysics); C.Value->ShiftTarget(Off); } }
	for (auto& C : Props) { if (C.Value) C.Value->AddActorWorldOffset(Off, false, nullptr, ETeleportType::TeleportPhysics); }
	for (auto& H : Peds) { if (H.Value) { H.Value->AddActorWorldOffset(Off, false, nullptr, ETeleportType::TeleportPhysics); H.Value->ShiftTarget(Off); } }
	for (auto& H : Actors) { if (H.Value) { H.Value->AddActorWorldOffset(Off, false, nullptr, ETeleportType::TeleportPhysics); H.Value->ShiftTarget(Off); } }
	for (TActorIterator<ASpsProp> It(GetWorld()); It; ++It) It->AddActorWorldOffset(Off, false, nullptr, ETeleportType::TeleportPhysics);
	if (PlayerCar) PlayerCar->AddActorWorldOffset(Off, false, nullptr, ETeleportType::TeleportPhysics);
	if (PlayerFoot) PlayerFoot->AddActorWorldOffset(Off, false, nullptr, ETeleportType::TeleportPhysics);
}

void ASpsClient::SendPose()
{
	if (!bStarted || bPlacePending) return;
	const bool bInCar = PlayerCar && PlayerCar->bOccupied; double E = 0, N = 0, H = 0, VE = 0, VN = 0, VH = 0; float Yaw = 0, VYaw = 0, SpeedKmh = 0;
	if (PlayerFoot) { FromWorld(PlayerFoot->GetActorLocation() - FVector(0, 0, 90.f), E, N, H); Yaw = PlayerFoot->GetActorRotation().Yaw; }
	if (PlayerCar) { FromWorld(PlayerCar->GetActorLocation() - FVector(0, 0, 105.f), VE, VN, VH); VYaw = PlayerCar->GetActorRotation().Yaw; SpeedKmh = FMath::Abs(PlayerCar->SpeedMs) * 3.6f; }
	if (bInCar) { E = VE; N = VN; H = VH; Yaw = VYaw; } else if (!PlayerCar) { VE = E; VN = N; VH = H; }
	SendText(FString::Printf(TEXT("{\"t\":\"pose\",\"e\":%.3f,\"n\":%.3f,\"h\":%.2f,\"yaw\":%.1f,\"speed\":%.1f,\"inVehicle\":%s,\"siren\":%s,\"blue\":%s,\"vE\":%.3f,\"vN\":%.3f,\"vH\":%.2f,\"vYaw\":%.1f,\"fuel\":%.3f,\"damage\":%.3f}"),
		E, N, H, Yaw, SpeedKmh, bInCar ? TEXT("true") : TEXT("false"), (PlayerCar && PlayerCar->bSiren) ? TEXT("true") : TEXT("false"), (PlayerCar && PlayerCar->bBlue) ? TEXT("true") : TEXT("false"), VE, VN, VH, VYaw, PlayerCar ? PlayerCar->Fuel : 1.f, PlayerCar ? PlayerCar->Damage : 0.f));
}

void ASpsClient::Tick(float Dt)
{
	Super::Tick(Dt);
	// network frames queued by the socket callbacks
	TArray<FString> Texts = MoveTemp(TextQueue); TextQueue.Reset();
	for (const FString& S : Texts) { TSharedPtr<FJsonObject> J; TSharedRef<TJsonReader<>> R = TJsonReaderFactory<>::Create(S); if (FJsonSerializer::Deserialize(R, J) && J.IsValid()) HandleText(J); }
	TArray<TArray<uint8>> Bins = MoveTemp(BinaryQueue); BinaryQueue.Reset();
	for (const TArray<uint8>& B : Bins) HandleBinary(B);
	MaybeRebase(); ProcessTiles(0.006); ProcessTrees(); ProcessSpawns(); ProcessProps(); ApplySnapshot(); TryPlacePlayer();
	// Sicherheitsnetz: faellt der Spieler aus der Welt (fehlende Kollision), zurueck zur letzten sicheren Stelle
	if (PlayerFoot && bStarted && !bPlacePending && PlayerFoot->GetCharacterMovement()->IsMovingOnGround()) LastSafeFoot = PlayerFoot->GetActorLocation();
	if (PlayerFoot && bStarted && !bPlacePending && !LastSafeFoot.IsZero() && PlayerFoot->GetActorLocation().Z < LastSafeFoot.Z - 6000.0)
	{
		UE_LOG(LogTemp, Warning, TEXT("[SPS] Spieler ist durch den Boden gefallen - zurueck auf die letzte sichere Stelle"));
		PlayerFoot->SetActorLocation(LastSafeFoot + FVector(0, 0, 150)); PlayerFoot->GetCharacterMovement()->Velocity = FVector::ZeroVector;
	}
	// Diagnose: alle 6 s Lage des Spielers und Anzahl Tiles (hilft bei "ich sehe nichts")
	StatusTimer += Dt;
	if (StatusTimer > 6.f && bStarted)
	{
		StatusTimer = 0.f; const AActor* Ref = (PlayerCar && PlayerCar->bOccupied) ? static_cast<AActor*>(PlayerCar) : static_cast<AActor*>(PlayerFoot);
		if (Ref) { const FVector P = Ref->GetActorLocation(); FHitResult H; const bool bGround = GetWorld()->LineTraceSingleByChannel(H, P, P - FVector(0, 0, 20000.f), ECC_Visibility); UE_LOG(LogTemp, Warning, TEXT("[SPS] Status: Spieler (%.0f, %.0f, %.0f) cm, %d Tiles, %d in der Warteschlange, Boden darunter: %s%s"), P.X, P.Y, P.Z, Tiles.Num(), TileQueue.Num(), bGround ? TEXT("ja") : TEXT("nein"), bPlacePending ? TEXT(", Platzierung steht aus") : TEXT("")); }
	}
	PoseTimer += Dt; if (PoseTimer >= 0.1f) { PoseTimer = 0.f; SendPose(); }
	if (ReconnectIn >= 0.f) { ReconnectIn -= Dt; if (ReconnectIn < 0.f) { ReconnectIn = -1.f; if (Socket.IsValid()) { Socket->OnConnected().RemoveAll(this); Socket->OnMessage().RemoveAll(this); Socket->OnBinaryMessage().RemoveAll(this); Socket.Reset(); } Connect(); } }
}
