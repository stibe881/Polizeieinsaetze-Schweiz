#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "SpsAtmosphere.generated.h"

class UDirectionalLightComponent;
class USkyAtmosphereComponent;
class USkyLightComponent;
class UExponentialHeightFogComponent;
class UVolumetricCloudComponent;
class UPostProcessComponent;

/** Sun, sky, clouds, fog and exposure driven by the server's game clock and weather. */
UCLASS()
class ASpsAtmosphere : public AActor
{
	GENERATED_BODY()

public:
	ASpsAtmosphere();

	/** Sun elevation/azimuth in degrees (azimuth from north, clockwise), daylight 0..1, cloud cover 0..1, fog visibility in metres, rain mm/h. */
	void Apply(float ElevationDeg, float AzimuthDeg, float Daylight, float Cloud, float VisibilityM, float RainMmH);

private:
	UPROPERTY() TObjectPtr<UDirectionalLightComponent> Sun;
	UPROPERTY() TObjectPtr<USkyAtmosphereComponent> Sky;
	UPROPERTY() TObjectPtr<USkyLightComponent> SkyLight;
	UPROPERTY() TObjectPtr<UExponentialHeightFogComponent> Fog;
	UPROPERTY() TObjectPtr<UVolumetricCloudComponent> Clouds;
	UPROPERTY() TObjectPtr<UPostProcessComponent> Post;
	float LastCapture = 0.f;
};
