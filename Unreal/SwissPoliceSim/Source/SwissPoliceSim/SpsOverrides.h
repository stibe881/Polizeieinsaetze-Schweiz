#pragma once

#include "CoreMinimal.h"

class USceneComponent;
class AActor;

/**
 * Replacement of the procedural cars and people by real assets (Fab / City Sample / MetaHuman / your own).
 * Configured in Config/DefaultGame.ini, section [/Script/SwissPoliceSim.SpsModels]:
 *
 *   +Car=car:police|/Game/Vehicles/SM_PoliceWagon.SM_PoliceWagon|1.0|0|0|0|0
 *   +Car=car|/Game/Vehicles/SM_Sedan.SM_Sedan|1.0|0|0|0|0
 *   +Human=human|/Game/Characters/SK_Person.SK_Person|/Game/Characters/A_Walk.A_Walk|/Game/Characters/A_Idle.A_Idle|1.0|0|-90|0
 *
 * Car:   <key prefix>|<static mesh>|<scale>|<offset X>|<offset Y>|<offset Z>|<yaw>      (origin of the mesh = middle of the car on the ground, +X forward, cm)
 * Human: <key prefix>|<skeletal mesh>|<walk animation>|<idle animation>|<scale>|<offset X>|<offset Z>|<yaw>
 * The longest matching key prefix wins ("car:police" beats "car"). Without a match the procedural model is used.
 */
struct FSpsCarOverride { FString Prefix, Mesh; float Scale = 1.f; FVector Offset = FVector::ZeroVector; float Yaw = 0.f; };
struct FSpsHumanOverride { FString Prefix, Mesh, Walk, Idle; float Scale = 1.f; float OffsetX = 0.f, OffsetZ = 0.f; float Yaw = 0.f; };

namespace SpsOverrides
{
	void Load();
	const FSpsCarOverride* FindCar(const FString& ModelKey);
	const FSpsHumanOverride* FindHuman(const FString& ModelKey);
}
