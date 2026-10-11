#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "SpsProtocol.h"
#include "SpsVehicle.generated.h"

class UProceduralMeshComponent;
class UBoxComponent;
class UTextRenderComponent;
class UMaterialInstanceDynamic;

/** Builds the visual of a car from a server model (body + wheel meshes) into <paramref name="Root"/>. Shared by traffic cars, scene props and the player's car. */
struct FSpsCarVisual
{
	UProceduralMeshComponent* Body = nullptr;
	UProceduralMeshComponent* Wheels[4] = { nullptr, nullptr, nullptr, nullptr };
	USceneComponent* WheelPivots[4] = { nullptr, nullptr, nullptr, nullptr };
	UMaterialInstanceDynamic* LampRear = nullptr; UMaterialInstanceDynamic* Indicator = nullptr; UMaterialInstanceDynamic* BlueL = nullptr; UMaterialInstanceDynamic* BlueR = nullptr; UMaterialInstanceDynamic* LampFront = nullptr;
	float WheelRadius = 32.f, Length = 450.f, Width = 180.f, Height = 150.f;
	FVector WheelCentres[4];
	FVector DriverEye = FVector(0, 0, 120), HoodCam = FVector(0, 0, 130);
	float Spin = 0.f;

	/** Creates the components under Owner/Root. Returns false when the model is not a car. */
	bool Build(AActor* Owner, USceneComponent* Root, const FSpsModelData& Model, const FString& Plate);
	/** Rolls the wheels by DistanceCm and steers the front pair. */
	void Roll(float DistanceCm, float SteerDeg);
	/** Lamp state: brake, indicators and the blue light bar (flashing driven by Time). */
	void SetLights(bool bBrake, bool bIndLeft, bool bIndRight, bool bHazard, bool bBlue, bool bHeadlights, double Time);
};

/** A simulated vehicle (traffic, scene prop, service vehicle) driven by server snapshots. */
UCLASS()
class ASpsCar : public AActor
{
	GENERATED_BODY()

public:
	ASpsCar();
	virtual void Tick(float DeltaSeconds) override;

	int32 ServerId = 0;
	bool bProp = false;
	FSpsCarVisual Visual;

	void Init(const FSpsModelData& Model, const FString& Plate);
	void SetTarget(const FVector& Pos, float YawDeg, float SpeedMs, uint16 Flags);
	/** Static scene vehicles: fixed pose, hazards and an optional blue light bar. */
	void SetStatic(const FVector& Pos, float YawDeg, bool bHazard, bool bBlue);

private:
	UPROPERTY() TObjectPtr<UBoxComponent> Hit;
	FVector TargetPos; float TargetYaw = 0.f, Speed = 0.f; uint16 FlagsNow = 0; bool bFirst = true; bool bBlueLights = false, bHazardLights = false;
	float PrevYaw = 0.f;
};

/** Simple scene props built from boxes: debris, cargo, roadblock. */
UCLASS()
class ASpsProp : public AActor
{
	GENERATED_BODY()

public:
	ASpsProp();
	void Build(const FString& Kind, float YawDeg);
};
