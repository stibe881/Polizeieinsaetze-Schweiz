#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "SpsProtocol.h"
#include "SpsTile.generated.h"

class UProceduralMeshComponent;

/** One 1 km tile of the world: terrain, roads, buildings, water, street furniture, rails and the merged trees as procedural meshes. */
UCLASS()
class ASpsTile : public AActor
{
	GENERATED_BODY()

public:
	ASpsTile();

	int32 TileX = 0, TileY = 0;
	uint8 Quality = 3;

	/** Replaces all meshes at once. OriginE/N are the LV95 coordinates that map to the Unreal origin. */
	void Build(const FSpsTileData& Data, double OriginE, double OriginN);

	/** Staged variant: BeginBuild clears and positions the tile, BuildStep creates ONE mesh section per call (returns true when finished) so large tiles do not stall a frame. */
	void BeginBuild(FSpsTileData&& Data, double OriginE, double OriginN);
	bool BuildStep();
	bool IsBuilding() const { return bBuilding; }

	/** Adds/replaces the merged tree meshes from loaded tree models (index = Kind*... resolved by the caller). */
	void SetTrees(const FSpsMeshData& Merged);

	/** Removes everything (used before a quality upgrade or when the tile leaves the keep range). */
	void Clear();

private:
	FSpsTileData Staged; bool bBuilding = false; int32 StageMesh = 0, StageSection = 0; UProceduralMeshComponent* StageComponent = nullptr;
	void StartMesh(const FSpsTileMesh& TM);
	UPROPERTY() TArray<TObjectPtr<UProceduralMeshComponent>> Parts;
	UPROPERTY() TObjectPtr<UProceduralMeshComponent> TreePart;
	UProceduralMeshComponent* NewPart(const FString& Name, bool bShadow, bool bCollision);
	void FillMesh(UProceduralMeshComponent* P, const FSpsMeshData& M, TFunctionRef<FString(int32)> Slot, bool bCollision);
};
