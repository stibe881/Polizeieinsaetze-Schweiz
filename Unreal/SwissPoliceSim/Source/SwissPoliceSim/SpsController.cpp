#include "SpsController.h"
#include "SpsClient.h"
#include "SpsPlayer.h"
#include "SpsVehicle.h"
#include "SpsHuman.h"
#include "SWebBrowser.h"
#include "Widgets/SWeakWidget.h"
#include "Engine/GameViewportClient.h"
#include "Engine/Engine.h"
#include "Engine/World.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "Camera/PlayerCameraManager.h"
#include "Components/InputComponent.h"
#include "InputCoreTypes.h"

ASpsController::ASpsController()
{
	PrimaryActorTick.bCanEverTick = true;
	bShowMouseCursor = false;
}

ASpsClient* ASpsController::Client() const { return ASpsClient::Get(GetWorld()); }

void ASpsController::BeginPlay()
{
	Super::BeginPlay();
	SetInputMode(FInputModeGameOnly());
	ASpsClient* C = Client();
	FString Url = C && !C->UiUrl.IsEmpty() ? C->UiUrl : TEXT("http://127.0.0.1:47800/?embedded=1");
	if (GEngine && GEngine->GameViewport)
	{
		UE_LOG(LogTemp, Warning, TEXT("[SPS] Oeffne UI-Overlay: %s"), *Url);
		SAssignNew(Browser, SWebBrowser).InitialURL(Url).ShowControls(false).ShowAddressBar(false).ShowErrorMessage(true).SupportsTransparency(true).BackgroundColor(FColor(0, 0, 0, 0))
			.OnLoadCompleted(FSimpleDelegate::CreateLambda([]() { UE_LOG(LogTemp, Warning, TEXT("[SPS] UI-Overlay geladen")); }))
			.OnLoadError(FSimpleDelegate::CreateLambda([]() { UE_LOG(LogTemp, Warning, TEXT("[SPS] UI-Overlay Ladefehler (Server nicht erreichbar?)")); }));
		SAssignNew(BrowserHolder, SWeakWidget).PossiblyNullContent(Browser.ToSharedRef());
		GEngine->GameViewport->AddViewportWidgetContent(BrowserHolder.ToSharedRef(), 100);
	}
	if (C) FocusHandle = C->OnUiFocus.AddUObject(this, &ASpsController::SetUiFocus);
}

void ASpsController::EndPlay(const EEndPlayReason::Type Reason)
{
	if (ASpsClient* C = Client()) C->OnUiFocus.Remove(FocusHandle);
	if (GEngine && GEngine->GameViewport && BrowserHolder.IsValid()) GEngine->GameViewport->RemoveViewportWidgetContent(BrowserHolder.ToSharedRef());
	BrowserHolder.Reset(); Browser.Reset();
	Super::EndPlay(Reason);
}

void ASpsController::SetUiFocus(bool bOn)
{
	bUiFocus = bOn; bShowMouseCursor = bOn;
	SetIgnoreLookInput(bOn); SetIgnoreMoveInput(bOn);
	if (bOn) { FInputModeUIOnly Mode; if (Browser.IsValid()) Mode.SetWidgetToFocus(Browser); Mode.SetLockMouseToViewportBehavior(EMouseLockMode::DoNotLock); SetInputMode(Mode); }
	else SetInputMode(FInputModeGameOnly());
}

void ASpsController::OpenUi(const TCHAR* Window)
{
	if (bUiFocus) return;
	if (ASpsClient* C = Client()) C->SendUi(TEXT("open"), FString::Printf(TEXT("\"%s\""), Window));
}

void ASpsController::SetupInputComponent()
{
	Super::SetupInputComponent();
	InputComponent->BindAxisKey(EKeys::MouseX, this, &ASpsController::LookX);
	InputComponent->BindAxisKey(EKeys::MouseY, this, &ASpsController::LookY);
	InputComponent->BindKey(EKeys::F, IE_Pressed, this, &ASpsController::OnEnterExit);
	InputComponent->BindKey(EKeys::E, IE_Pressed, this, &ASpsController::OnTalk);
	InputComponent->BindKey(EKeys::R, IE_Pressed, this, &ASpsController::OnRadar);
	InputComponent->BindKey(EKeys::G, IE_Pressed, this, &ASpsController::OnStop);
	InputComponent->BindKey(EKeys::LeftMouseButton, IE_Pressed, this, &ASpsController::OnFire);
	InputComponent->BindKey(EKeys::C, IE_Pressed, this, &ASpsController::OnCamera);
	InputComponent->BindKey(EKeys::V, IE_Pressed, this, &ASpsController::OnCamera);
	InputComponent->BindKey(EKeys::Enter, IE_Pressed, this, &ASpsController::OnAccept);
	InputComponent->BindKey(EKeys::BackSpace, IE_Pressed, this, &ASpsController::OnDecline);
	InputComponent->BindKey(EKeys::F5, IE_Pressed, this, &ASpsController::OnQuickSave);
	InputComponent->BindKey(EKeys::F9, IE_Pressed, this, &ASpsController::OnQuickLoad);
	InputComponent->BindKey(EKeys::Escape, IE_Pressed, this, &ASpsController::OnPause);
	InputComponent->BindKey(EKeys::F2, IE_Pressed, this, &ASpsController::OnToggleUi);
	InputComponent->BindKey(EKeys::Tab, IE_Pressed, this, &ASpsController::OnComputer);
	InputComponent->BindKey(EKeys::M, IE_Pressed, this, &ASpsController::OnMap);
	InputComponent->BindKey(EKeys::P, IE_Pressed, this, &ASpsController::OnReport);
	InputComponent->BindKey(EKeys::N, IE_Pressed, this, &ASpsController::OnNotebook);
	InputComponent->BindKey(EKeys::K, IE_Pressed, this, &ASpsController::OnCareer);
	InputComponent->BindKey(EKeys::F1, IE_Pressed, this, &ASpsController::OnHelp);
}

void ASpsController::Tick(float Dt) { Super::Tick(Dt); }

void ASpsController::LookX(float V)
{
	if (bUiFocus || V == 0.f) return;
	if (ASpsPlayerCar* Car = Cast<ASpsPlayerCar>(GetPawn())) Car->LookDelta(V * 2.2f, 0.f); else AddYawInput(V);
}

void ASpsController::LookY(float V)
{
	if (bUiFocus || V == 0.f) return;
	if (ASpsPlayerCar* Car = Cast<ASpsPlayerCar>(GetPawn())) Car->LookDelta(0.f, V * 1.2f); else AddPitchInput(-V);
}

void ASpsController::OnEnterExit()
{
	ASpsClient* C = Client(); if (!C || bUiFocus || !C->PlayerCar || !C->PlayerFoot) return;
	ASpsPlayerCar* Car = C->PlayerCar; ASpsPlayerFoot* Foot = C->PlayerFoot;
	if (GetPawn() == Car)
	{
		Car->bOccupied = false; Car->SpeedMs = 0.f;
		const FVector Out = Car->GetActorLocation() + Car->GetActorRightVector() * -260.f;
		Foot->SetActorHiddenInGame(false); Foot->SetActorEnableCollision(true); Foot->SetActorLocationAndRotation(Out, FRotator(0, Car->GetActorRotation().Yaw, 0));
		Possess(Foot);
	}
	else if (FVector::Dist2D(Foot->GetActorLocation(), Car->GetActorLocation()) < 650.f)
	{
		Car->bOccupied = true; Foot->SetActorHiddenInGame(true); Foot->SetActorEnableCollision(false); Possess(Car);
	}
}

ASpsCar* ASpsController::FindAimedCar(float& OutDistM) const
{
	ASpsClient* C = Client(); if (!C || !PlayerCameraManager) return nullptr;
	const FVector O = PlayerCameraManager->GetCameraLocation(), F = PlayerCameraManager->GetCameraRotation().Vector();
	ASpsCar* Best = nullptr; float BestAng = 5.f; OutDistM = 0.f;
	for (const TPair<int32, TObjectPtr<ASpsCar>>& P : C->Cars)
	{
		ASpsCar* Car = P.Value; if (!Car) continue;
		const FVector D = Car->GetActorLocation() + FVector(0, 0, 90.f) - O; const float Dist = D.Size(); if (Dist > 45000.f || Dist < 400.f) continue;
		const float Ang = FMath::RadiansToDegrees(FMath::Acos(FMath::Clamp(static_cast<float>(FVector::DotProduct(F, D / Dist)), -1.f, 1.f)));
		const float Tol = FMath::Lerp(5.f, 1.2f, FMath::Clamp(Dist / 30000.f, 0.f, 1.f)); if (Ang > Tol || Ang > BestAng) continue;
		FHitResult H; FCollisionQueryParams Q(SCENE_QUERY_STAT(SpsAim), false); Q.AddIgnoredActor(GetPawn()); Q.AddIgnoredActor(Car);
		if (GetWorld()->LineTraceSingleByChannel(H, O, Car->GetActorLocation() + FVector(0, 0, 90.f), ECC_Visibility, Q) && H.Distance < Dist - 600.f) continue;
		BestAng = Ang; Best = Car; OutDistM = Dist / 100.f;
	}
	return Best;
}

void ASpsController::OnRadar() { ASpsClient* C = Client(); if (!C || bUiFocus) return; C->bRadarOn = !C->bRadarOn; C->SendText(FString::Printf(TEXT("{\"t\":\"radar\",\"on\":%s}"), C->bRadarOn ? TEXT("true") : TEXT("false"))); }

void ASpsController::OnFire()
{
	ASpsClient* C = Client(); if (!C || bUiFocus || !C->bRadarOn) return;
	float Dist = 0.f; if (ASpsCar* Car = FindAimedCar(Dist)) C->SendText(FString::Printf(TEXT("{\"t\":\"measure\",\"vehicleId\":%d,\"dist\":%.1f}"), Car->ServerId, Dist));
}

void ASpsController::OnStop()
{
	ASpsClient* C = Client(); if (!C || bUiFocus) return;
	float Dist = 0.f; ASpsCar* Car = FindAimedCar(Dist);
	C->SendText(FString::Printf(TEXT("{\"t\":\"stop\",\"vehicleId\":%d}"), Car ? Car->ServerId : 0));
}

void ASpsController::OnTalk()
{
	ASpsClient* C = Client(); if (!C || bUiFocus) return;
	const FVector P = GetPawn() ? GetPawn()->GetActorLocation() : FVector::ZeroVector;
	int32 BestCar = 0, BestActor = 0; float BestD = 1200.f;
	for (const TPair<int32, TObjectPtr<ASpsCar>>& K : C->Cars) { if (!K.Value) continue; const float D = FVector::Dist2D(K.Value->GetActorLocation(), P); if (D < BestD && K.Value->GetVelocity().Size() < 30.f) { BestD = D; BestCar = K.Key; BestActor = 0; } }
	for (const TPair<int32, TObjectPtr<ASpsHuman>>& K : C->Actors) { if (!K.Value) continue; const float D = FVector::Dist2D(K.Value->GetActorLocation(), P); if (D < BestD) { BestD = D; BestActor = K.Key; BestCar = 0; } }
	if (BestCar || BestActor) C->SendText(FString::Printf(TEXT("{\"t\":\"approach\",\"vehicleId\":%d,\"actorId\":%d}"), BestCar, BestActor));
}

void ASpsController::OnCamera()
{
	if (bUiFocus) return;
	if (ASpsPlayerCar* Car = Cast<ASpsPlayerCar>(GetPawn())) Car->CycleView(); else if (ASpsPlayerFoot* Foot = Cast<ASpsPlayerFoot>(GetPawn())) Foot->ToggleView();
}

void ASpsController::OnAccept() { if (ASpsClient* C = Client()) { if (!bUiFocus) C->SendText(TEXT("{\"t\":\"accept\"}")); } }
void ASpsController::OnDecline() { if (ASpsClient* C = Client()) { if (!bUiFocus) C->SendText(TEXT("{\"t\":\"decline\"}")); } }
void ASpsController::OnQuickSave() { if (ASpsClient* C = Client()) C->SendText(TEXT("{\"t\":\"save\",\"slot\":1}")); }
void ASpsController::OnQuickLoad() { if (ASpsClient* C = Client()) C->SendText(TEXT("{\"t\":\"load\",\"slot\":1}")); }
// F2 blendet das Web-Overlay aus/ein (Diagnose: zeigt, was die 3D-Welt darunter darstellt)
void ASpsController::OnToggleUi()
{
	if (!BrowserHolder.IsValid()) return;
	const bool bShown = BrowserHolder->GetVisibility() != EVisibility::Collapsed;
	BrowserHolder->SetVisibility(bShown ? EVisibility::Collapsed : EVisibility::SelfHitTestInvisible);
	UE_LOG(LogTemp, Warning, TEXT("[SPS] UI-Overlay %s"), bShown ? TEXT("ausgeblendet") : TEXT("eingeblendet"));
}

void ASpsController::OnPause() { if (bUiFocus) return; if (ASpsClient* C = Client()) { C->SendText(TEXT("{\"t\":\"pause\",\"on\":true}")); C->SendUi(TEXT("open"), TEXT("\"pause\"")); } }
