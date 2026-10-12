#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "SpsProtocol.h"
#include "SpsClient.generated.h"

class IWebSocket;
class ASpsTile;
class ASpsCar;
class ASpsHuman;
class ASpsPlayerCar;
class ASpsPlayerFoot;
class ASpsAtmosphere;
class ASpsWeather;
class FJsonObject;

/**
 * The link to the SwissServer: owns the WebSocket, turns server messages into world content (tiles, cars, people, scene props),
 * sends the player's pose, and exposes the commands the controller needs. One instance per world (spawned by the game mode).
 */
UCLASS()
class ASpsClient : public AActor
{
	GENERATED_BODY()

public:
	ASpsClient();
	virtual void BeginPlay() override;
	virtual void EndPlay(const EEndPlayReason::Type Reason) override;
	virtual void Tick(float DeltaSeconds) override;

	static ASpsClient* Get(UWorld* World);

	// ---- coordinates (LV95 <-> Unreal cm, X = north, Y = east, Z = up)
	double OriginE = 0, OriginN = 0;
	FVector ToWorld(double E, double N, double H) const { return FVector((N - OriginN) * 100.0, (E - OriginE) * 100.0, H * 100.0); }
	void FromWorld(const FVector& P, double& E, double& N, double& H) const { N = OriginN + P.X / 100.0; E = OriginE + P.Y / 100.0; H = P.Z / 100.0; }

	// ---- commands
	void SendText(const FString& Json);
	void SendCommand(const FString& Type, const TMap<FString, FString>& Numbers = TMap<FString, FString>());
	void SendUi(const FString& Key, const FString& Value);   // {"t":"ui", Key: Value}
	void RequestModel(const FString& Key);
	void OnPlayerCrash(AActor* Other, float ImpactMs);

	// ---- state exposed to the controller
	bool bConnected = false, bStarted = false, bRadarOn = false, bPlaying = false;
	TMap<int32, TObjectPtr<ASpsCar>> Cars;
	TMap<int32, TObjectPtr<ASpsHuman>> Peds;
	TMap<int32, TObjectPtr<ASpsHuman>> Actors;       // scene persons (suspects, witnesses)
	TMap<int32, TObjectPtr<ASpsCar>> Props;          // scene vehicles
	UPROPERTY() TObjectPtr<ASpsPlayerCar> PlayerCar;
	UPROPERTY() TObjectPtr<ASpsPlayerFoot> PlayerFoot;
	/** True while the web UI has a window open (cursor shown, game input ignored). */
	bool bUiFocus = false;
	/** Called when the UI asks for focus; the controller switches input mode. */
	DECLARE_MULTICAST_DELEGATE_OneParam(FOnUiFocus, bool);
	FOnUiFocus OnUiFocus;
	FString ServerUrl, UiUrl, HttpBase;   // HttpBase: z.B. http://127.0.0.1:47800 (Tile-Download)
	const FSpsModelData* FindModel(const FString& Key) const { const TSharedPtr<FSpsModelData>* M = Models.Find(Key); return M ? M->Get() : nullptr; }

private:
	TSharedPtr<IWebSocket> Socket;
	TArray<uint8> Reassembly;
	TArray<TArray<uint8>> BinaryQueue;
	TArray<FString> TextQueue;
	TMap<FString, TSharedPtr<FSpsModelData>> Models;
	TSet<FString> Requested;
	TMap<FIntPoint, TObjectPtr<ASpsTile>> Tiles;
	TArray<FSpsTileData> TileQueue;
	struct FPendingTrees { FIntPoint Tile; uint8 Quality; TArray<FSpsTree> Trees; };
	TArray<FPendingTrees> PendingTrees;
	struct FPendingSpawn { FString Kind, Model, Plate; int32 Id; };
	TArray<FPendingSpawn> PendingSpawns;
	struct FPropInfo { int32 Id; FString Kind, Model, Plate; bool bHazard, bBlue; double E, N, H; float Yaw; };
	TArray<FPropInfo> PendingProps;
	FSpsSnapshot LastSnapshot; bool bHaveSnapshot = false;
	TObjectPtr<ASpsAtmosphere> Atmosphere;
	TObjectPtr<ASpsWeather> Weather;
	void MaybeRebase();
	float ReconnectIn = -1.f;
	float StatusTimer = 0.f; FVector LastSafeFoot = FVector::ZeroVector;
	TArray<uint8> ChunkBuffer;   // Zwischenspeicher fuer in Chunks gesendete Grossframes
	float PoseTimer = 0.f; double StartE = 0, StartN = 0, StartH = 0, StartVE = 0, StartVN = 0, StartVYaw = 0; bool bPlacePending = false; double PlaceSince = 0.0, PlaceLogAt = 0.0; bool bStartInVehicle = false; float StartYaw = 0.f;

	void Connect();
	void OnConnected();
	void OnText(const FString& Msg);
	void FetchTile(const FString& Token);
	void OnTileHttp(TSharedPtr<class IHttpRequest, ESPMode::ThreadSafe> Req, TSharedPtr<class IHttpResponse, ESPMode::ThreadSafe> Resp, bool bOk);
	void OnBinary(const void* Data, SIZE_T Size, bool bLast);
	void HandleText(const TSharedPtr<FJsonObject>& J);
	void HandleBinary(const TArray<uint8>& Data);
	void ProcessTiles(double BudgetSeconds);
	TObjectPtr<ASpsTile> BuildingTile;
	void ProcessTrees();
	void ProcessSpawns();
	void ProcessProps();
	void ApplySnapshot();
	void TryPlacePlayer();
	void SendPose();
	void DropTile(int32 X, int32 Y);
	void DropAllTiles();
	void ClearEntities();
	void NeedModel(const FString& Key) { RequestModel(Key); }
	FSpsMeshData MergeTrees(const FPendingTrees& P, bool& bComplete);
};
