#include "SpsClient.h"
#include "WebSocketsModule.h"
#include "IWebSocket.h"
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
	ServerUrl = TEXT("ws://127.0.0.1:47800/ws"); UiUrl = TEXT("http://127.0.0.1:47800/?embedded=1");
	if (GConfig) { GConfig->GetString(TEXT("/Script/SwissPoliceSim.SpsSettings"), TEXT("ServerUrl"), ServerUrl, GGameIni); GConfig->GetString(TEXT("/Script/SwissPoliceSim.SpsSettings"), TEXT("UiUrl"), UiUrl, GGameIni); }
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
	Atmosphere = GetWorld()->SpawnActor<ASpsAtmosphere>(FVector::ZeroVector, FRotator::ZeroRotator);
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
	Socket->OnClosed().AddLambda([this](int32, const FString&, bool) { bConnected = false; ReconnectIn = 2.f; });
	Socket->OnConnectionError().AddLambda([this](const FString&) { bConnected = false; ReconnectIn = 2.f; });
	Socket->Connect();
}

void ASpsClient::OnConnected()
{
	bConnected = true; Requested.Reset();
	SendText(TEXT("{\"t\":\"hello\",\"role\":\"game\",\"name\":\"unreal\"}"));
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

void ASpsClient::OnBinary(const void* Data, SIZE_T Size, bool bLast)
{
	const int32 Old = Reassembly.Num(); Reassembly.AddUninitialized(static_cast<int32>(Size)); FMemory::Memcpy(Reassembly.GetData() + Old, Data, Size);
	if (bLast) { BinaryQueue.Add(MoveTemp(Reassembly)); Reassembly.Reset(); }
}

void ASpsClient::OnPlayerCrash(AActor* Other, float ImpactMs)
{
	if (const ASpsCar* C = Cast<ASpsCar>(Other)) { if (C->ServerId > 0) SendText(FString::Printf(TEXT("{\"t\":\"collision\",\"vehicleId\":%d,\"rel\":%.2f}"), C->ServerId, ImpactMs)); }
}

// ------------------------------------------------------------------ message handling
void ASpsClient::HandleText(const TSharedPtr<FJsonObject>& J)
{
	const FString T = Str(J, TEXT("t"));
	if (T == TEXT("started"))
	{
		bStarted = true; bPlaying = true; DropAllTiles(); ClearEntities();
		StartE = Num(J, TEXT("e")); StartN = Num(J, TEXT("n")); StartH = Num(J, TEXT("h")); StartVE = Num(J, TEXT("vE")); StartVN = Num(J, TEXT("vN")); StartVYaw = Num(J, TEXT("vYaw")); bStartInVehicle = Bool(J, TEXT("inVehicle"), false); StartYaw = static_cast<float>(Num(J, TEXT("pYaw")));
		OriginE = FMath::FloorToDouble(StartE / 1000.0) * 1000.0; OriginN = FMath::FloorToDouble(StartN / 1000.0) * 1000.0;
		bPlacePending = true;
	}
	else if (T == TEXT("state"))
	{
		const TSharedPtr<FJsonObject>* Sun = nullptr; const TSharedPtr<FJsonObject>* Wx = nullptr; const TSharedPtr<FJsonObject>* Rad = nullptr;
		if (Atmosphere && J->TryGetObjectField(TEXT("sun"), Sun) && J->TryGetObjectField(TEXT("weather"), Wx))
			Atmosphere->Apply(static_cast<float>(Num(*Sun, TEXT("el"))), static_cast<float>(Num(*Sun, TEXT("az"))), static_cast<float>(Num(*Sun, TEXT("daylight"), 1.0)), static_cast<float>(Num(*Wx, TEXT("cloud"))), static_cast<float>(Num(*Wx, TEXT("vis"), 20000.0)), static_cast<float>(Num(*Wx, TEXT("rain"))));
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
	switch (Data[0])
	{
	case SpsMsg::Tile: { FSpsTileData T; if (SpsProtocol::ParseTile(Data, T)) TileQueue.Add(MoveTemp(T)); break; }
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
	if (TObjectPtr<ASpsTile>* T = Tiles.Find(K)) { if (*T) (*T)->Destroy(); Tiles.Remove(K); }
	TileQueue.RemoveAll([X, Y](const FSpsTileData& D) { return D.X == X && D.Y == Y; });
	PendingTrees.RemoveAll([K](const FPendingTrees& P) { return P.Tile == K; });
}

void ASpsClient::DropAllTiles()
{
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
	while (TileQueue.Num() > 0)
	{
		FSpsTileData D = MoveTemp(TileQueue[0]); TileQueue.RemoveAt(0);
		const FIntPoint K(D.X, D.Y); TObjectPtr<ASpsTile>* Found = Tiles.Find(K); ASpsTile* Tile = Found ? Found->Get() : nullptr;
		if (!Tile) { Tile = GetWorld()->SpawnActor<ASpsTile>(FVector::ZeroVector, FRotator::ZeroRotator); Tiles.Add(K, Tile); }
		Tile->Build(D, OriginE, OriginN);
		if (D.Trees.Num() > 0) { PendingTrees.RemoveAll([K](const FPendingTrees& P) { return P.Tile == K; }); FPendingTrees P; P.Tile = K; P.Quality = D.Quality; P.Trees = MoveTemp(D.Trees); PendingTrees.Add(MoveTemp(P)); }
		if (FPlatformTime::Seconds() - Start > BudgetSeconds) break;
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
	if (!GetWorld()->LineTraceSingleByChannel(Hit, At + FVector(0, 0, 30000.f), At - FVector(0, 0, 30000.f), ECC_Visibility)) return;
	const FVector Foot = Hit.ImpactPoint + FVector(0, 0, 100.f);
	if (PlayerFoot && !PlayerFoot->bBodyReady) PlayerFoot->InitBody(*Officer);
	if (PlayerFoot) { PlayerFoot->SetActorLocationAndRotation(Foot, FRotator(0, StartYaw, 0)); PlayerFoot->GetCharacterMovement()->SetMovementMode(MOVE_Walking); }
	FHitResult HitC; const FVector AtC = ToWorld(StartVE, StartVN, StartH);
	FVector CarLoc = AtC; if (GetWorld()->LineTraceSingleByChannel(HitC, AtC + FVector(0, 0, 30000.f), AtC - FVector(0, 0, 30000.f), ECC_Visibility)) CarLoc = HitC.ImpactPoint;
	CarLoc.Z += 105.f;   // the player car's actor origin is the middle of its collision box
	if (!PlayerCar) { PlayerCar = GetWorld()->SpawnActor<ASpsPlayerCar>(CarLoc, FRotator(0, static_cast<float>(StartVYaw), 0)); if (PlayerCar) PlayerCar->Init(*CarModel, TEXT("BE 123456")); }
	else PlayerCar->SetActorLocationAndRotation(CarLoc, FRotator(0, static_cast<float>(StartVYaw), 0));
	bPlacePending = false;
	if (APlayerController* PC = GetWorld()->GetFirstPlayerController()) { if (bStartInVehicle && PlayerCar) { PlayerCar->bOccupied = true; PC->Possess(PlayerCar); } else if (PlayerFoot) PC->Possess(PlayerFoot); }
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
	ProcessTiles(0.006); ProcessTrees(); ProcessSpawns(); ProcessProps(); ApplySnapshot(); TryPlacePlayer();
	PoseTimer += Dt; if (PoseTimer >= 0.1f) { PoseTimer = 0.f; SendPose(); }
	if (ReconnectIn >= 0.f) { ReconnectIn -= Dt; if (ReconnectIn < 0.f) { ReconnectIn = -1.f; if (Socket.IsValid()) { Socket->OnConnected().RemoveAll(this); Socket->OnMessage().RemoveAll(this); Socket->OnBinaryMessage().RemoveAll(this); Socket.Reset(); } Connect(); } }
}
