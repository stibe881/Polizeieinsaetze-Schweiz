#pragma once

#include "CoreMinimal.h"

class UMaterialInterface;

/**
 * Material lookup by slot name. Slots map to /Game/SPS/Materials/MI_<Slot> (created by Content/Python/sps_setup.py from the PBR textures);
 * if a slot is missing, M_Sps_VertexColor and finally the engine grid material are used so the world always renders.
 */
struct SPSMaterials
{
	static UMaterialInterface* Get(const FString& Slot);

	/** Slot names for the submeshes of each mesh kind (see docs/PROTOCOL.md). */
	static FString RoadSlot(int32 Sub);
	static FString BuildingSlot(int32 Sub);
	static FString FurnitureSlot(int32 Sub);
	static FString RailSlot(int32 Sub);
	static FString CarSlot(int32 Sub);
	static FString HumanSlot(int32 Sub);
	static FString TreeSlot(int32 Sub);
};
