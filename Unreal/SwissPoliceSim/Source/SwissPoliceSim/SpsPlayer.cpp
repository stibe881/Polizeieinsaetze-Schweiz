#include "SpsPlayer.h"
#include "Camera/CameraComponent.h"
#include "Components/BoxComponent.h"
#include "Components/CapsuleComponent.h"
#include "Components/InputComponent.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "GameFramework/SpringArmComponent.h"
#include "Engine/World.h"
#include "SpsClient.h"

// ------------------------------------------------------------------ on foot
ASpsPlayerFoot::ASpsPlayerFoot()
{
	PrimaryActorTick.bCanEverTick = true;
	GetCapsuleComponent()->InitCapsuleSize(35.f, 90.f);
	GetCharacterMovement()->MaxWalkSpeed = 170.f;
	GetCharacterMovement()->JumpZVelocity = 420.f;
	GetCharacterMovement()->bOrientRotationToMovement = true;
	GetCharacterMovement()->RotationRate = FRotator(0.f, 540.f, 0.f);
	bUseControllerRotationYaw = false;
	Arm = CreateDefaultSubobject<USpringArmComponent>(TEXT("Arm"));
	Arm->SetupAttachment(GetCapsuleComponent());
	Arm->SetRelativeLocation(FVector(0.f, 0.f, 60.f));
	Arm->TargetArmLength = 320.f; Arm->bUsePawnControlRotation = true; Arm->bDoCollisionTest = true; Arm->ProbeSize = 12.f;
	Cam = CreateDefaultSubobject<UCameraComponent>(TEXT("Camera"));
	Cam->SetupAttachment(Arm, USpringArmComponent::SocketName);
	Cam->SetFieldOfView(70.f);
}

void ASpsPlayerFoot::InitBody(const FSpsModelData& Model)
{
	USceneComponent* Holder = NewObject<USceneComponent>(this, TEXT("BodyRoot"));
	Holder->SetupAttachment(GetCapsuleComponent()); Holder->SetRelativeLocation(FVector(0.f, 0.f, -90.f)); Holder->RegisterComponent();
	bBodyReady = Body.Build(this, Holder, Model);
}

void ASpsPlayerFoot::ToggleView()
{
	bFirstPerson = !bFirstPerson;
	Arm->TargetArmLength = bFirstPerson ? 0.f : 320.f;
}

void ASpsPlayerFoot::SetupPlayerInputComponent(UInputComponent* Input)
{
	Super::SetupPlayerInputComponent(Input);
	Input->BindKey(EKeys::LeftShift, IE_Pressed, this, &ASpsPlayerFoot::RunOn);
	Input->BindKey(EKeys::LeftShift, IE_Released, this, &ASpsPlayerFoot::RunOff);
	Input->BindKey(EKeys::SpaceBar, IE_Pressed, this, &ASpsPlayerFoot::JumpOn);
}

void ASpsPlayerFoot::Tick(float Dt)
{
	Super::Tick(Dt);
	if (!Controller) return;
	// keys: W/S and A/D read directly so both directions work without extra axis bindings
	const APlayerController* PC = Cast<APlayerController>(Controller);
	float F = 0.f, R = 0.f;
	if (PC) { F = (PC->IsInputKeyDown(EKeys::W) ? 1.f : 0.f) - (PC->IsInputKeyDown(EKeys::S) ? 1.f : 0.f); R = (PC->IsInputKeyDown(EKeys::D) ? 1.f : 0.f) - (PC->IsInputKeyDown(EKeys::A) ? 1.f : 0.f); }
	GetCharacterMovement()->MaxWalkSpeed = bRun ? 560.f : 170.f;
	const FRotator Yaw(0.f, Controller->GetControlRotation().Yaw, 0.f);
	AddMovementInput(FRotationMatrix(Yaw).GetUnitAxis(EAxis::X), F);
	AddMovementInput(FRotationMatrix(Yaw).GetUnitAxis(EAxis::Y), R);
	if (bBodyReady)
	{
		Body.Speed = GetVelocity().Size2D() / 100.f; Body.Pose = Body.Speed > 3.f ? SpsPose::Run : (Body.Speed > 0.2f ? SpsPose::Walk : SpsPose::Idle); Body.Animate(Dt);
	}
}

// ------------------------------------------------------------------ patrol car
ASpsPlayerCar::ASpsPlayerCar()
{
	PrimaryActorTick.bCanEverTick = true;
	// the collision box is the root (sweeps need a collision shape); everything visual hangs below it, 105 cm down at ground level
	Hit = CreateDefaultSubobject<UBoxComponent>(TEXT("Hit"));
	SetRootComponent(Hit);
	Hit->SetBoxExtent(FVector(235.f, 92.f, 55.f));
	Hit->SetCollisionProfileName(TEXT("Pawn"));
	VisualRoot = CreateDefaultSubobject<USceneComponent>(TEXT("VisualRoot"));
	VisualRoot->SetupAttachment(Hit); VisualRoot->SetRelativeLocation(FVector(0.f, 0.f, -105.f));
	Arm = CreateDefaultSubobject<USpringArmComponent>(TEXT("Arm"));
	Arm->SetupAttachment(VisualRoot); Arm->SetRelativeLocation(FVector(0.f, 0.f, 150.f)); Arm->TargetArmLength = 700.f; Arm->bUsePawnControlRotation = false; Arm->bDoCollisionTest = true; Arm->bInheritPitch = false; Arm->bInheritRoll = false; Arm->bInheritYaw = true;
	Arm->bEnableCameraLag = true; Arm->CameraLagSpeed = 12.f;
	Cam = CreateDefaultSubobject<UCameraComponent>(TEXT("Camera"));
	Cam->SetupAttachment(Arm, USpringArmComponent::SocketName); Cam->SetFieldOfView(65.f);
	AutoPossessPlayer = EAutoReceiveInput::Disabled;
}

void ASpsPlayerCar::Init(const FSpsModelData& Model, const FString& Plate)
{
	bReady = Visual.Build(this, VisualRoot, Model, Plate);
	if (bReady) Hit->SetBoxExtent(FVector(Visual.Length * 0.48f, Visual.Width * 0.48f, 55.f));
	Visual.SetLights(false, false, false, false, false, false, 0.0);
	ApplyCamera();
}

void ASpsPlayerCar::SetupPlayerInputComponent(UInputComponent* Input)
{
	Super::SetupPlayerInputComponent(Input);
	Input->BindKey(EKeys::SpaceBar, IE_Pressed, this, &ASpsPlayerCar::HandbrakeOn);
	Input->BindKey(EKeys::SpaceBar, IE_Released, this, &ASpsPlayerCar::HandbrakeOff);
	Input->BindKey(EKeys::B, IE_Pressed, this, &ASpsPlayerCar::ToggleBlue);
	Input->BindKey(EKeys::Q, IE_Pressed, this, &ASpsPlayerCar::ToggleSiren);
	Input->BindKey(EKeys::L, IE_Pressed, this, &ASpsPlayerCar::ToggleHeadlights);
	Input->BindKey(EKeys::H, IE_Pressed, this, &ASpsPlayerCar::HornOn);
	Input->BindKey(EKeys::H, IE_Released, this, &ASpsPlayerCar::HornOff);
}

void ASpsPlayerCar::CycleView() { ViewMode = (ViewMode + 1) % 3; ApplyCamera(); }

void ASpsPlayerCar::LookDelta(float Yaw, float Pitch)
{
	LookYawOffset = FMath::Clamp(LookYawOffset + Yaw, -170.f, 170.f); LookPitchOffset = FMath::Clamp(LookPitchOffset + Pitch, -25.f, 40.f); LastLookTime = GetWorld() ? GetWorld()->GetTimeSeconds() : 0.f;
}

void ASpsPlayerCar::ApplyCamera()
{
	if (!bReady) return;
	if (ViewMode == 0) { Arm->SetRelativeLocation(FVector(0.f, 0.f, 150.f)); Arm->TargetArmLength = 700.f; }
	else if (ViewMode == 1) { Arm->SetRelativeLocation(Visual.DriverEye + FVector(0.f, 0.f, 0.f)); Arm->TargetArmLength = 0.f; }
	else { Arm->SetRelativeLocation(Visual.HoodCam); Arm->TargetArmLength = 0.f; }
}

bool ASpsPlayerCar::TraceGround(const FVector& At, float& OutZ) const
{
	FHitResult H; FCollisionQueryParams Q(SCENE_QUERY_STAT(SpsGround), true, this);
	const FVector From = At + FVector(0, 0, 400.f), To = At - FVector(0, 0, 4000.f);
	if (GetWorld()->LineTraceSingleByChannel(H, From, To, ECC_Visibility, Q)) { OutZ = H.ImpactPoint.Z; return true; }
	// Auto ist bereits unter den Boden geraten: hoeher ansetzen und die Oberflaeche wiederfinden
	if (GetWorld()->LineTraceSingleByChannel(H, At + FVector(0, 0, 4000.f), To, ECC_Visibility, Q)) { OutZ = H.ImpactPoint.Z; return true; }
	return false;
}

void ASpsPlayerCar::Tick(float Dt)
{
	Super::Tick(Dt);
	if (!bReady) return;
	Dt = FMath::Min(Dt, 0.05f);
	const double Now = GetWorld()->GetTimeSeconds();
	// Tasten abfragen (BindAxisKey ist in UE 5.8 nur fuer Achsen erlaubt)
	if (APlayerController* KPC = Cast<APlayerController>(Controller))
	{
		KeyThrottle = KPC->IsInputKeyDown(EKeys::W) ? 1.f : 0.f; KeyBrake = KPC->IsInputKeyDown(EKeys::S) ? 1.f : 0.f;
		KeyLeft = KPC->IsInputKeyDown(EKeys::A) ? 1.f : 0.f; KeyRight = KPC->IsInputKeyDown(EKeys::D) ? 1.f : 0.f;
	}
	// ---- driving model (m/s, degrees)
	const float Throttle = bOccupied ? KeyThrottle : 0.f, Brake = bOccupied ? KeyBrake : 0.f, Steer = bOccupied ? (KeyRight - KeyLeft) : 0.f, Hand = bOccupied ? KeyHandbrake : 1.f;
	const float MaxV = 58.f;   // 210 km/h
	float Accel = 0.f;
	if (Throttle > 0.f) Accel += 5.6f * Throttle * FMath::Clamp(1.f - SpeedMs / MaxV, 0.f, 1.f);
	if (Brake > 0.f) Accel += (SpeedMs > 0.5f ? -9.5f : -3.0f) * Brake;
	if (Throttle <= 0.f && Brake <= 0.f) Accel -= FMath::Sign(SpeedMs) * FMath::Min(FMath::Abs(SpeedMs), 1.4f + 0.0009f * SpeedMs * SpeedMs);
	if (Hand > 0.5f) Accel -= FMath::Sign(SpeedMs) * FMath::Min(FMath::Abs(SpeedMs) / FMath::Max(Dt, 0.001f), 7.5f);
	SpeedMs = FMath::Clamp(SpeedMs + Accel * Dt, -9.f, MaxV);
	if (Brake > 0.f && SpeedMs < 0.f && Throttle <= 0.f) SpeedMs = FMath::Max(SpeedMs, -9.f);
	const float MaxSteer = FMath::Lerp(32.f, 5.5f, FMath::Clamp(SpeedMs / 42.f, 0.f, 1.f));
	SteerAngle = FMath::FInterpTo(SteerAngle, Steer * MaxSteer, Dt, 7.f);
	const float Wheelbase = 2.85f;
	const float YawRate = FMath::RadiansToDegrees(SpeedMs / Wheelbase * FMath::Tan(FMath::DegreesToRadians(SteerAngle)));
	FRotator Rot = GetActorRotation(); Rot.Yaw += YawRate * Dt;
	const FVector Fwd = FRotator(0.f, Rot.Yaw, 0.f).Vector();
	const FVector Delta = Fwd * SpeedMs * 100.f * Dt;
	// ---- move (sweep so buildings and other cars stop the car)
	FHitResult Blocked;
	AddActorWorldOffset(FVector(Delta.X, Delta.Y, 0.f), true, &Blocked);
	if (Blocked.bBlockingHit)
	{
		const float Impact = FMath::Abs(SpeedMs);
		{   // Diagnose "unsichtbare Wand": was blockiert? (hoechstens 1x pro Sekunde)
			static double LastBlockLog = 0.0; const double T = GetWorld()->GetTimeSeconds();
			if (T - LastBlockLog > 1.0) { LastBlockLog = T; auto* BC = Blocked.GetComponent(); UE_LOG(LogTemp, Warning, TEXT("[SPS] Auto blockiert von %s / %s, Aufprall %.0f cm ueber Boden, Normale (%.2f, %.2f, %.2f)"), BC ? *BC->GetName() : TEXT("?"), Blocked.GetActor() ? *Blocked.GetActor()->GetName() : TEXT("?"), Blocked.ImpactPoint.Z - GroundZ, Blocked.ImpactNormal.X, Blocked.ImpactNormal.Y, Blocked.ImpactNormal.Z); }
		}
		if (Impact > 2.f) { Damage = FMath::Clamp(Damage + Impact * 0.004f, 0.f, 1.f); if (ASpsClient* C = ASpsClient::Get(GetWorld())) C->OnPlayerCrash(Blocked.GetActor(), Impact); }
		SpeedMs *= -0.15f;
	}
	// ---- follow the ground: height from four wheel traces, pitch/roll from their differences
	const FVector P = GetActorLocation(); float Z[4]; bool bAll = true;
	const FVector Offs[4] = { FVector(150.f, -80.f, 0.f), FVector(150.f, 80.f, 0.f), FVector(-140.f, -80.f, 0.f), FVector(-140.f, 80.f, 0.f) };
	const FRotator YawOnly(0.f, Rot.Yaw, 0.f);
	for (int32 i = 0; i < 4; i++) { if (!TraceGround(P + YawOnly.RotateVector(Offs[i]), Z[i])) { bAll = false; Z[i] = P.Z; } }
	if (bAll || !bGroundKnown)
	{
		const float Avg = (Z[0] + Z[1] + Z[2] + Z[3]) * 0.25f; GroundZ = bGroundKnown ? FMath::FInterpTo(GroundZ, Avg, Dt, 18.f) : Avg; bGroundKnown = bAll || bGroundKnown;
		const float TargetPitch = FMath::RadiansToDegrees(FMath::Atan2((Z[2] + Z[3]) * 0.5f - (Z[0] + Z[1]) * 0.5f, 290.f));
		const float TargetRoll = FMath::RadiansToDegrees(FMath::Atan2((Z[1] + Z[3]) * 0.5f - (Z[0] + Z[2]) * 0.5f, 160.f));
		VisualPitch = FMath::FInterpTo(VisualPitch, TargetPitch, Dt, 12.f); VisualRoll = FMath::FInterpTo(VisualRoll, TargetRoll, Dt, 12.f);
	}
	Rot.Pitch = VisualPitch; Rot.Roll = VisualRoll;
	SetActorLocationAndRotation(FVector(GetActorLocation().X, GetActorLocation().Y, GroundZ + 105.f), Rot);
	Visual.Roll(SpeedMs * 100.f * Dt, SteerAngle);
	Visual.SetLights(Brake > 0.f || (Hand > 0.5f && SpeedMs > 0.5f), false, false, false, bBlue, bHeadlights || bBlue, Now);
	// ---- camera: look offset returns to centre when the mouse is idle
	if (Now - LastLookTime > 2.0) { LookYawOffset = FMath::FInterpTo(LookYawOffset, 0.f, Dt, 2.5f); LookPitchOffset = FMath::FInterpTo(LookPitchOffset, 0.f, Dt, 2.5f); }
	Arm->SetRelativeRotation(FRotator(ViewMode == 0 ? -10.f + LookPitchOffset : LookPitchOffset, LookYawOffset, 0.f));
	Fuel = FMath::Max(0.f, Fuel - FMath::Abs(SpeedMs) * Dt * 0.000012f);
}
