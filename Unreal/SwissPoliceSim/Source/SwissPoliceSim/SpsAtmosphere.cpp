#include "SpsAtmosphere.h"
#include "Components/DirectionalLightComponent.h"
#include "Components/SkyAtmosphereComponent.h"
#include "Components/SkyLightComponent.h"
#include "Components/ExponentialHeightFogComponent.h"
#include "Components/VolumetricCloudComponent.h"
#include "Components/PostProcessComponent.h"
#include "Materials/MaterialInterface.h"

ASpsAtmosphere::ASpsAtmosphere()
{
	PrimaryActorTick.bCanEverTick = false;
	USceneComponent* Root = CreateDefaultSubobject<USceneComponent>(TEXT("Root"));
	SetRootComponent(Root);

	Sun = CreateDefaultSubobject<UDirectionalLightComponent>(TEXT("Sun"));
	Sun->SetupAttachment(Root); Sun->SetMobility(EComponentMobility::Movable); Sun->SetAtmosphereSunLight(true); Sun->SetAtmosphereSunLightIndex(0);
	Sun->SetIntensity(10.f); Sun->SetLightSourceAngle(0.5f); Sun->SetCastShadows(true);

	Fill = CreateDefaultSubobject<UDirectionalLightComponent>(TEXT("Fill"));
	Fill->SetupAttachment(Root); Fill->SetMobility(EComponentMobility::Movable); Fill->SetIntensity(2.5f); Fill->SetCastShadows(false); Fill->SetLightColor(FLinearColor(0.75f, 0.82f, 1.f));
	Fill->SetWorldRotation(FRotator(-60.f, 35.f, 0.f));
	Fill->ForwardShadingPriority = 0; Sun->ForwardShadingPriority = 1;   // beseitigt die Warnung "mehrere gerichtete Lichter konkurrieren"

	Sky = CreateDefaultSubobject<USkyAtmosphereComponent>(TEXT("SkyAtmosphere"));
	Sky->SetupAttachment(Root);

	SkyLight = CreateDefaultSubobject<USkyLightComponent>(TEXT("SkyLight"));
	SkyLight->SetupAttachment(Root); SkyLight->SetMobility(EComponentMobility::Movable); SkyLight->bRealTimeCapture = false;   // Echtzeit-Capture kostet jedes Bild; stattdessen alle paar Sekunden (siehe Apply)
	SkyLight->SpecularScale = 0.3f;   // Asphalt im streifenden Blickwinkel spiegelte sonst den hellen Himmel (blasse Strassen)
	SkyLight->SetIntensity(1.f); SkyLight->bLowerHemisphereIsBlack = false; SkyLight->SetLowerHemisphereColor(FLinearColor(0.25f, 0.25f, 0.25f));

	Fog = CreateDefaultSubobject<UExponentialHeightFogComponent>(TEXT("Fog"));
	Fog->SetupAttachment(Root); Fog->SetFogDensity(0.01f); Fog->SetVolumetricFog(true);

	Clouds = CreateDefaultSubobject<UVolumetricCloudComponent>(TEXT("Clouds"));
	Clouds->SetupAttachment(Root);
	static ConstructorHelpers::FObjectFinder<UMaterialInterface> CloudMat(TEXT("/Engine/EngineSky/VolumetricClouds/m_SimpleVolumetricCloud_Inst.m_SimpleVolumetricCloud_Inst"));
	if (CloudMat.Succeeded()) Clouds->SetMaterial(CloudMat.Object);

	Post = CreateDefaultSubobject<UPostProcessComponent>(TEXT("Post"));
	Post->SetupAttachment(Root); Post->bUnbound = true;
	Post->Settings.bOverride_AutoExposureMethod = true; Post->Settings.AutoExposureMethod = AEM_Histogram;
	Post->Settings.bOverride_AutoExposureMinBrightness = true; Post->Settings.AutoExposureMinBrightness = 0.01f;
	Post->Settings.bOverride_AutoExposureMaxBrightness = true; Post->Settings.AutoExposureMaxBrightness = 8.f;
	Post->Settings.bOverride_AutoExposureBias = true; Post->Settings.bOverride_BloomIntensity = true; Post->Settings.BloomIntensity = 0.45f;
	Post->Settings.bOverride_AmbientOcclusionIntensity = true; Post->Settings.AmbientOcclusionIntensity = 0.6f;
	Post->Settings.bOverride_MotionBlurAmount = true; Post->Settings.MotionBlurAmount = 0.15f;
}

void ASpsAtmosphere::Apply(float ElevationDeg, float AzimuthDeg, float Daylight, float Cloud, float VisibilityM, float RainMmH)
{
	// Unreal axes: X = north, Y = east, Z = up. Direction towards the sun from azimuth/elevation; the light shines the opposite way.
	const float El = FMath::DegreesToRadians(ElevationDeg), Az = FMath::DegreesToRadians(AzimuthDeg);
	const FVector ToSun(FMath::Cos(El) * FMath::Cos(Az), FMath::Cos(El) * FMath::Sin(Az), FMath::Sin(El));
	Sun->SetWorldRotation((-ToSun).Rotation());
	const float Overcast = FMath::Clamp(Cloud, 0.f, 1.f);
	Sun->SetIntensity(FMath::Lerp(0.15f, 10.f, FMath::Clamp(Daylight, 0.f, 1.f)) * FMath::Lerp(1.f, 0.5f, Overcast));
	SkyLight->SetIntensity(FMath::Lerp(0.35f, 1.f, Daylight));   // nachts nie pechschwarz: Mond-/Stadtlicht-Grundhelligkeit
	Fog->SetFogDensity(FMath::Clamp(2.2f / FMath::Max(VisibilityM, 80.f), 0.0002f, 0.05f) * 0.1f);
	Fog->SetFogInscatteringColor(FLinearColor(0.55f, 0.65f, 0.8f) * FMath::Lerp(0.1f, 1.f, Daylight));
	Clouds->SetLayerBottomAltitude(FMath::Lerp(5.f, 1.5f, Overcast));
	// the real-time sky-light capture follows the sun slowly
	LastCapture += 1.f; if (LastCapture > 8.f) { SkyLight->RecaptureSky(); LastCapture = 0.f; }
	Post->Settings.AutoExposureBias = FMath::Lerp(1.5f, 0.f, Daylight);
	// Diagnose: alle 15 s die Licht-Werte ins Log (hilft bei "alles schwarz")
	LastLog += 1.f;
	if (LastLog > 1.f) { static double NextLog = 0.0; const double Now = FPlatformTime::Seconds(); if (Now > NextLog) { NextLog = Now + 15.0; UE_LOG(LogTemp, Warning, TEXT("[SPS] Licht: Sonne Elevation %.1f Azimut %.1f, Tageslicht %.2f, Sonnenstaerke %.2f, Himmelslicht %.2f, Bewoelkung %.2f"), ElevationDeg, AzimuthDeg, Daylight, Sun->Intensity, SkyLight->Intensity, Cloud); } }
}
