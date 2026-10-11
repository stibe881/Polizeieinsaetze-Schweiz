#include "SpsMaterials.h"
#include "Materials/MaterialInterface.h"
#include "UObject/UObjectGlobals.h"

namespace
{
	TMap<FString, TWeakObjectPtr<UMaterialInterface>> GCache;

	UMaterialInterface* Load(const FString& Path)
	{
		return LoadObject<UMaterialInterface>(nullptr, *Path, nullptr, LOAD_NoWarn | LOAD_Quiet);
	}
}

UMaterialInterface* SPSMaterials::Get(const FString& Slot)
{
	if (TWeakObjectPtr<UMaterialInterface>* Hit = GCache.Find(Slot)) { if (Hit->IsValid()) return Hit->Get(); }
	UMaterialInterface* M = Load(FString::Printf(TEXT("/Game/SPS/Materials/MI_%s.MI_%s"), *Slot, *Slot));
	if (!M) M = Load(TEXT("/Game/SPS/Materials/M_Sps_VertexColor.M_Sps_VertexColor"));
	if (!M) M = Load(TEXT("/Engine/EngineMaterials/WorldGridMaterial.WorldGridMaterial"));
	GCache.Add(Slot, M);
	return M;
}

FString SPSMaterials::RoadSlot(int32 Sub)
{
	static const TCHAR* N[] = { TEXT("Road0"), TEXT("Road1"), TEXT("Road2"), TEXT("Sidewalk"), TEXT("MarkWhite"), TEXT("MarkYellow") };
	return N[FMath::Clamp(Sub, 0, 5)];
}
FString SPSMaterials::BuildingSlot(int32 Sub)
{
	static const TCHAR* N[] = { TEXT("Facade0"), TEXT("Facade1"), TEXT("Facade2"), TEXT("RoofTiles"), TEXT("RoofFlat"), TEXT("Plaster"), TEXT("Windows"), TEXT("Trim"), TEXT("Shutters"), TEXT("Railing") };
	return N[FMath::Clamp(Sub, 0, 9)];
}
FString SPSMaterials::FurnitureSlot(int32 Sub)
{
	static const TCHAR* N[] = { TEXT("Furniture"), TEXT("SignalLens"), TEXT("SignFace") };
	return N[FMath::Clamp(Sub, 0, 2)];
}
FString SPSMaterials::RailSlot(int32 Sub)
{
	static const TCHAR* N[] = { TEXT("Ballast"), TEXT("Rails"), TEXT("Sleepers"), TEXT("Masts") };
	return N[FMath::Clamp(Sub, 0, 3)];
}
FString SPSMaterials::CarSlot(int32 Sub)
{
	static const TCHAR* N[] = { TEXT("CarPaint"), TEXT("CarGlass"), TEXT("CarTrim"), TEXT("LampFront"), TEXT("LampRear"), TEXT("Indicator"), TEXT("LampBlue"), TEXT("LampBlue"), TEXT("Plate"), TEXT("CarDecal"), TEXT("CarInterior"), TEXT("CarChrome") };
	return N[FMath::Clamp(Sub, 0, 11)];
}
FString SPSMaterials::HumanSlot(int32 Sub)
{
	static const TCHAR* N[] = { TEXT("HumanBody"), TEXT("HumanLeather"), TEXT("HumanPatch") };
	return N[FMath::Clamp(Sub, 0, 2)];
}
FString SPSMaterials::TreeSlot(int32 Sub)
{
	static const TCHAR* N[] = { TEXT("Trunk"), TEXT("Foliage") };
	return N[FMath::Clamp(Sub, 0, 1)];
}
