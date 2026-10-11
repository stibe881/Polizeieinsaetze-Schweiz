#include "SpsTile.h"
#include "ProceduralMeshComponent.h"
#include "SpsMaterials.h"

ASpsTile::ASpsTile()
{
	PrimaryActorTick.bCanEverTick = false;
	USceneComponent* Root = CreateDefaultSubobject<USceneComponent>(TEXT("Root"));
	SetRootComponent(Root);
}

UProceduralMeshComponent* ASpsTile::NewPart(const FString& Name, bool bShadow, bool bCollision)
{
	UProceduralMeshComponent* P = NewObject<UProceduralMeshComponent>(this, *Name);
	P->SetupAttachment(GetRootComponent());
	P->bUseAsyncCooking = true;
	P->bUseComplexAsSimpleCollision = bCollision;
	P->SetCastShadow(bShadow);
	P->SetCollisionEnabled(bCollision ? ECollisionEnabled::QueryAndPhysics : ECollisionEnabled::NoCollision);
	P->RegisterComponent();
	return P;
}

void ASpsTile::FillMesh(UProceduralMeshComponent* P, const FSpsMeshData& M, TFunctionRef<FString(int32)> Slot, bool bCollision)
{
	for (int32 s = 0; s < M.Sections.Num(); s++)
	{
		if (M.Sections[s].Num() == 0) continue;
		SpsCreateSection(P, s, M, s, bCollision);
		P->SetMaterial(s, SPSMaterials::Get(Slot(s)));
	}
}

void ASpsTile::Clear()
{
	for (UProceduralMeshComponent* P : Parts) { if (P) P->DestroyComponent(); }
	Parts.Reset();
	if (TreePart) { TreePart->DestroyComponent(); TreePart = nullptr; }
}

void ASpsTile::Build(const FSpsTileData& Data, double OriginE, double OriginN)
{
	Clear();
	TileX = Data.X; TileY = Data.Y; Quality = Data.Quality;
	// tile-local metres → centimetres; the tile origin is its south-west corner (E = X * 1000, N = Y * 1000)
	SetActorLocation(FVector((Data.Y * 1000.0 - OriginN) * 100.0, (Data.X * 1000.0 - OriginE) * 100.0, 0.0));
	const bool bNear = Data.Quality == 1;
	for (const FSpsTileMesh& TM : Data.Meshes)
	{
		if (TM.Mesh.IsEmpty()) continue;
		switch (TM.Kind)
		{
		case SpsTileKind::Terrain: { auto* P = NewPart(TEXT("Terrain"), true, bNear); FillMesh(P, TM.Mesh, [](int32) { return FString(TEXT("Terrain")); }, bNear); Parts.Add(P); break; }
		case SpsTileKind::Roads: { auto* P = NewPart(TEXT("Roads"), false, bNear); FillMesh(P, TM.Mesh, [](int32 S) { return SPSMaterials::RoadSlot(S); }, bNear); Parts.Add(P); break; }
		case SpsTileKind::BuildingsFar: { auto* P = NewPart(TEXT("BuildingsFar"), true, bNear); FillMesh(P, TM.Mesh, [](int32 S) { return SPSMaterials::BuildingSlot(S); }, bNear); Parts.Add(P); break; }
		case SpsTileKind::BuildingsDetail: { auto* P = NewPart(TEXT("BuildingsDetail"), true, bNear); FillMesh(P, TM.Mesh, [](int32 S) { return SPSMaterials::BuildingSlot(S); }, bNear); Parts.Add(P); break; }
		case SpsTileKind::Water: { auto* P = NewPart(TEXT("Water"), false, false); FillMesh(P, TM.Mesh, [](int32) { return FString(TEXT("Water")); }, false); Parts.Add(P); break; }
		case SpsTileKind::Furniture: { auto* P = NewPart(TEXT("Furniture"), true, false); FillMesh(P, TM.Mesh, [](int32 S) { return SPSMaterials::FurnitureSlot(S); }, false); Parts.Add(P); break; }
		case SpsTileKind::Rail: { auto* P = NewPart(TEXT("Rail"), true, false); FillMesh(P, TM.Mesh, [](int32 S) { return SPSMaterials::RailSlot(S); }, false); Parts.Add(P); break; }
		default: break;
		}
	}
}

void ASpsTile::SetTrees(const FSpsMeshData& Merged)
{
	if (TreePart) { TreePart->DestroyComponent(); TreePart = nullptr; }
	if (Merged.IsEmpty()) return;
	TreePart = NewPart(TEXT("Trees"), true, false);
	FillMesh(TreePart, Merged, [](int32 S) { return SPSMaterials::TreeSlot(S); }, false);
}
