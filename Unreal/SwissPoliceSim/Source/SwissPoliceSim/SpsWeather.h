#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "SpsWeather.generated.h"

class UInstancedStaticMeshComponent;

/** Rain streaks and snow flakes: instanced cubes in a box around the camera, recycled as they fall. Intensities come from the server's weather. */
UCLASS()
class ASpsWeather : public AActor
{
	GENERATED_BODY()

public:
	ASpsWeather();
	virtual void Tick(float DeltaSeconds) override;
	/** Rain / snow rates 0..1. */
	void SetIntensity(float Rain, float Snow) { RainRate = FMath::Clamp(Rain, 0.f, 1.f); SnowRate = FMath::Clamp(Snow, 0.f, 1.f); }

private:
	UPROPERTY() TObjectPtr<UInstancedStaticMeshComponent> Drops;
	TArray<FVector> Local;      // position inside the box (cm), relative to the camera
	TArray<float> Phase;
	float RainRate = 0.f, SnowRate = 0.f;
	static constexpr int32 Count = 1800;
	static constexpr float BoxXY = 2200.f, BoxZ = 1600.f;
};
