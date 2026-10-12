#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "SpsProtocol.h"
#include "SpsHuman.generated.h"

class UProceduralMeshComponent;

/** Poses sent by the server (PoseKind). */
namespace SpsPose { enum : uint8 { Idle = 0, Walk = 1, Run = 2, Talk = 3, Point = 4, Phone = 5, Search = 6, Cuffed = 7, Fallen = 8 }; }

/** Articulated procedural human: pivots in a hierarchy (pelvis, head, arms, legs) with a simple walk cycle and a few poses. */
struct FSpsHumanBody
{
	TMap<FString, USceneComponent*> Parts;
	float Phase = 0.f;
	uint8 Pose = SpsPose::Idle;
	float Speed = 0.f;
	float Height = 175.f;
	class USkeletalMeshComponent* Skel = nullptr; class UAnimationAsset* WalkAnim = nullptr; class UAnimationAsset* IdleAnim = nullptr; int32 SkelState = -1; class UAnimInstance* AnimInst = nullptr;

	bool Build(AActor* Owner, USceneComponent* Root, const FSpsModelData& Model);
	/** Replaces the procedural body by a configured skeletal mesh with walk/idle animation (SpsOverrides). */
	bool ApplyOverride(AActor* Owner, USceneComponent* Root, const FString& ModelKey);
	void Animate(float Dt);
};

/** A pedestrian or scene person driven by server snapshots. */
UCLASS()
class ASpsHuman : public AActor
{
	GENERATED_BODY()

public:
	ASpsHuman();
	virtual void Tick(float DeltaSeconds) override;

	int32 ServerId = 0;
	FSpsHumanBody Body;

	void Init(const FSpsModelData& Model);
	void SetTarget(const FVector& Pos, float YawDeg, float SpeedMs, uint8 Pose);
	void ShiftTarget(const FVector& Delta) { TargetPos += Delta; }
	/** Run over: stays on the ground (the server freezes the pedestrian). */
	void Fall() { bFallen = true; PoseNow = SpsPose::Fallen; Body.Pose = SpsPose::Fallen; }

private:
	UPROPERTY() TObjectPtr<class UCapsuleComponent> Capsule;
	FVector TargetPos; float TargetYaw = 0.f; float GroundZ = 0.f; bool bGroundKnown = false; float NextTrace = 0.f; bool bFirst = true, bFallen = false; uint8 PoseNow = 0;
};
