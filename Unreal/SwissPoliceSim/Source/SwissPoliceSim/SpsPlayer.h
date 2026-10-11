#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Pawn.h"
#include "GameFramework/Character.h"
#include "SpsProtocol.h"
#include "SpsVehicle.h"
#include "SpsHuman.h"
#include "SpsPlayer.generated.h"

class USpringArmComponent;
class UCameraComponent;
class UBoxComponent;
class UAudioComponent;

/** The officer on foot: capsule character with a procedural body and a third/first person camera. */
UCLASS()
class ASpsPlayerFoot : public ACharacter
{
	GENERATED_BODY()

public:
	ASpsPlayerFoot();
	virtual void Tick(float DeltaSeconds) override;
	virtual void SetupPlayerInputComponent(UInputComponent* Input) override;

	void InitBody(const FSpsModelData& Model);
	bool bFirstPerson = false;
	bool bBodyReady = false;
	void ToggleView();

private:
	UPROPERTY() TObjectPtr<USpringArmComponent> Arm;
	UPROPERTY() TObjectPtr<UCameraComponent> Cam;
	FSpsHumanBody Body;
	float MoveF = 0.f, MoveR = 0.f; bool bRun = false;
	void InForward(float V) { MoveF = V; }
	void InRight(float V) { MoveR = V; }
	void RunOn() { bRun = true; }
	void RunOff() { bRun = false; }
	void JumpOn() { Jump(); }
};

/** The patrol car: arcade vehicle model (bicycle steering, ground following by line traces) – no physics asset needed. */
UCLASS()
class ASpsPlayerCar : public APawn
{
	GENERATED_BODY()

public:
	ASpsPlayerCar();
	virtual void Tick(float DeltaSeconds) override;
	virtual void SetupPlayerInputComponent(UInputComponent* Input) override;

	void Init(const FSpsModelData& Model, const FString& Plate);
	bool bReady = false;
	float SpeedMs = 0.f, Fuel = 1.f, Damage = 0.f;
	bool bBlue = false, bSiren = false, bHeadlights = false, bHorn = false;
	/** Camera views: 0 chase, 1 cockpit, 2 bonnet. */
	int32 ViewMode = 0;
	void CycleView();
	void ToggleBlue() { bBlue = !bBlue; }
	void ToggleSiren() { bSiren = !bSiren; }
	void ToggleHeadlights() { bHeadlights = !bHeadlights; }
	void LookDelta(float Yaw, float Pitch);
	/** Called by the controller after possessing: stops the car when nobody drives. */
	bool bOccupied = false;
	FSpsCarVisual Visual;

private:
	UPROPERTY() TObjectPtr<UBoxComponent> Hit;
	UPROPERTY() TObjectPtr<USceneComponent> VisualRoot;
	UPROPERTY() TObjectPtr<USpringArmComponent> Arm;
	UPROPERTY() TObjectPtr<UCameraComponent> Cam;
	float KeyThrottle = 0.f, KeyBrake = 0.f, KeyLeft = 0.f, KeyRight = 0.f, KeyHandbrake = 0.f;
	float LookYawOffset = 0.f, LookPitchOffset = 0.f, LastLookTime = 0.f, SteerAngle = 0.f;
	float VisualPitch = 0.f, VisualRoll = 0.f, GroundZ = 0.f; bool bGroundKnown = false;
	void InThrottle(float V) { KeyThrottle = V; }
	void InBrake(float V) { KeyBrake = V; }
	void InLeft(float V) { KeyLeft = V; }
	void InRight(float V) { KeyRight = V; }
	void HandbrakeOn() { KeyHandbrake = 1.f; }
	void HandbrakeOff() { KeyHandbrake = 0.f; }
	void HornOn() { bHorn = true; }
	void HornOff() { bHorn = false; }
	bool TraceGround(const FVector& At, float& OutZ) const;
	void ApplyCamera();
};
