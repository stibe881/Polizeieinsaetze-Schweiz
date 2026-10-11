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

	/** Replaces all meshes. OriginE/N are the LV95 coordinates that map to the Unreal origin. */
	void Build(const FSpsTileData& Data, double OriginE, double OriginN);

	/** Adds/replaces the merged tree meshes from loaded tree models (index = Kind*... resolved by the caller). */
	void SetTrees(const FSpsMeshData& Merged);

	/** Removes everything (used before a quality upgrade or when the tile leaves the keep range). */
	void Clear();

private:
	UPROPERTY() TArray<TObjectPtr<UProceduralMeshComponent>> Parts;
	UPROPERTY() TObjectPtr<UProceduralMeshComponent> TreePart;
	UProceduralMeshComponent* NewPart(const FString& Name, bool bShadow, bool bCollision);
	void FillMesh(UProceduralMeshComponent* P, const FSpsMeshData& M, TFunctionRef<FString(int32)> Slot, bool bCollision);
};
