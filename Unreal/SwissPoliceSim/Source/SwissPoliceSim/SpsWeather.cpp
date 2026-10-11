#include "SpsWeather.h"
#include "Components/InstancedStaticMeshComponent.h"
#include "Engine/StaticMesh.h"
#include "Engine/World.h"
#include "GameFramework/PlayerController.h"
#include "Camera/PlayerCameraManager.h"
#include "SpsMaterials.h"
#include "Kismet/KismetMaterialLibrary.h"
#include "Materials/MaterialParameterCollection.h"

ASpsWeather::ASpsWeather()
{
	PrimaryActorTick.bCanEverTick = true;
	USceneComponent* Root = CreateDefaultSubobject<USceneComponent>(TEXT("Root"));
	SetRootComponent(Root);
	Drops = CreateDefaultSubobject<UInstancedStaticMeshComponent>(TEXT("Drops"));
	Drops->SetupAttachment(Root);
	static ConstructorHelpers::FObjectFinder<UStaticMesh> Cube(TEXT("/Engine/BasicShapes/Cube.Cube"));
	if (Cube.Succeeded()) Drops->SetStaticMesh(Cube.Object);
	Drops->SetCastShadow(false); Drops->SetCollisionEnabled(ECollisionEnabled::NoCollision); Drops->SetMobility(EComponentMobility::Movable);
}

void ASpsWeather::Tick(float Dt)
{
	Super::Tick(Dt);
	// wet surfaces: rain wets quickly, drying takes a few minutes; the materials read "Wetness" from MPC_Sps (darker albedo, low roughness)
	if (!bCollectionTried) { bCollectionTried = true; Collection = LoadObject<UMaterialParameterCollection>(nullptr, TEXT("/Game/SPS/Materials/MPC_Sps.MPC_Sps"), nullptr, LOAD_NoWarn | LOAD_Quiet); }
	const float WetTarget = (RainRate > 0.02f || SnowRate > 0.02f) ? 1.f : 0.f;
	Wet = FMath::FInterpConstantTo(Wet, WetTarget, Dt, WetTarget > Wet ? 0.25f : 0.004f);
	if (Collection && GetWorld()) UKismetMaterialLibrary::SetScalarParameterValue(GetWorld(), Collection, TEXT("Wetness"), Wet);
	if (Local.Num() == 0)
	{
		Local.SetNum(Count); Phase.SetNum(Count);
		TArray<FTransform> Init; Init.SetNum(Count);
		for (int32 i = 0; i < Count; i++) { Local[i] = FVector(FMath::FRandRange(-BoxXY, BoxXY), FMath::FRandRange(-BoxXY, BoxXY), FMath::FRandRange(-BoxZ, BoxZ)); Phase[i] = FMath::FRandRange(0.f, 6.28f); Drops->AddInstance(FTransform(FRotator::ZeroRotator, Local[i], FVector(0.f, 0.f, 0.f))); }
		Drops->SetMaterial(0, SPSMaterials::Get(TEXT("Rain")));
	}
	const float Rate = FMath::Max(RainRate, SnowRate);
	if (Rate <= 0.001f) { if (Drops->GetInstanceCount() > 0) { TArray<FTransform> Hide; Hide.SetNum(Count); for (int32 i = 0; i < Count; i++) Hide[i] = FTransform(FRotator::ZeroRotator, FVector(0, 0, -1e6), FVector(0.f, 0.f, 0.f)); Drops->BatchUpdateInstancesTransforms(0, Hide, true, true, true); } return; }
	const bool bSnow = SnowRate > RainRate;
	APlayerController* PC = GetWorld() ? GetWorld()->GetFirstPlayerController() : nullptr;
	const FVector Cam = (PC && PC->PlayerCameraManager) ? PC->PlayerCameraManager->GetCameraLocation() : FVector::ZeroVector;
	const int32 Active = FMath::Clamp(static_cast<int32>(Count * Rate), 1, Count);
	TArray<FTransform> T; T.SetNum(Count);
	const float Fall = bSnow ? 160.f : 1500.f;
	const FVector Scale = bSnow ? FVector(0.035f, 0.035f, 0.035f) : FVector(0.008f, 0.008f, 0.55f);
	for (int32 i = 0; i < Count; i++)
	{
		FVector& L = Local[i]; Phase[i] += Dt;
		L.Z -= Fall * Dt; if (bSnow) { L.X += FMath::Sin(Phase[i] * 1.3f) * 40.f * Dt; L.Y += FMath::Cos(Phase[i]) * 40.f * Dt; } else { L.X -= 80.f * Dt; }
		if (L.Z < -BoxZ) { L.Z += 2.f * BoxZ; L.X = FMath::FRandRange(-BoxXY, BoxXY); L.Y = FMath::FRandRange(-BoxXY, BoxXY); }
		T[i] = FTransform(FRotator::ZeroRotator, Cam + L, i < Active ? Scale : FVector(0.f, 0.f, 0.f));
	}
	Drops->BatchUpdateInstancesTransforms(0, T, true, true, true);
}
