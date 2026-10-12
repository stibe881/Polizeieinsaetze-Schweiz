#include "SpsTile.h"
#include "ProceduralMeshComponent.h"
#include "SpsMaterials.h"
#include "Materials/MaterialInterface.h"

ASpsTile::ASpsTile()
{
	PrimaryActorTick.bCanEverTick = true; PrimaryActorTick.bStartWithTickEnabled = false;
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

void ASpsTile::Tick(float Dt)
{
	Super::Tick(Dt);
	if (OldDestroyIn > 0.f) { OldDestroyIn -= Dt; return; }
	for (UProceduralMeshComponent* P : OldParts) { if (P) P->DestroyComponent(); }
	OldParts.Reset(); SetActorTickEnabled(false);
}

FString ASpsTile::Describe() const
{
	FString Out = FString::Printf(TEXT("Tile %d/%d q%d bBuilding=%d Parts=%d OldParts=%d"), TileX, TileY, Quality, bBuilding ? 1 : 0, Parts.Num(), OldParts.Num());
	for (UProceduralMeshComponent* P : Parts)
	{
		if (!P) { Out += TEXT(" [null]"); continue; }
		UMaterialInterface* M = P->GetNumMaterials() > 0 ? P->GetMaterial(0) : nullptr;
		Out += FString::Printf(TEXT(" [%s vis=%d reg=%d sek=%d koll=%d mat=%s Z %.0f..%.0f]"), *P->GetName(), P->IsVisible() ? 1 : 0, P->IsRegistered() ? 1 : 0, P->GetNumSections(), P->GetCollisionEnabled() != ECollisionEnabled::NoCollision ? 1 : 0, M ? *M->GetName() : TEXT("-"), P->Bounds.Origin.Z - P->Bounds.BoxExtent.Z, P->Bounds.Origin.Z + P->Bounds.BoxExtent.Z);
	}
	return Out;
}

int32 ASpsTile::SetKindVisible(const FString& Filter, bool bVisible)
{
	int32 N = 0;
	for (UProceduralMeshComponent* P : Parts) { if (P && P->GetName().Contains(Filter)) { P->SetVisibility(bVisible); N++; } }
	if (TreePart && TreePart->GetName().Contains(Filter)) { TreePart->SetVisibility(bVisible); N++; }
	return N;
}

void ASpsTile::Clear()
{
	for (UProceduralMeshComponent* P : OldParts) { if (P) P->DestroyComponent(); }
	OldParts.Reset();
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

void ASpsTile::BeginBuild(FSpsTileData&& Data, double OriginE, double OriginN)
{
	// Doppelpuffer: die bisherigen Teile (und ihre Kollision) bleiben, bis die neuen fertig und ihre Kollision gebacken ist
	for (UProceduralMeshComponent* P : OldParts) { if (P) P->DestroyComponent(); }
	OldParts = Parts; Parts.Reset(); OldDestroyIn = 1.0e9f;
	Staged = MoveTemp(Data); TileX = Staged.X; TileY = Staged.Y; Quality = Staged.Quality;
	SetActorLocation(FVector((Staged.Y * 1000.0 - OriginN) * 100.0, (Staged.X * 1000.0 - OriginE) * 100.0, 0.0));
	bBuilding = true; StageMesh = 0; StageSection = 0; StageComponent = nullptr;
}

void ASpsTile::StartMesh(const FSpsTileMesh& TM)
{
	const bool bNear = Staged.Quality == 1;
	switch (TM.Kind)
	{
	// Boden und Strassen tragen auch in der mittleren Qualitaet Kollision, sonst findet das Auto dort keinen Boden und sinkt ein
	case SpsTileKind::Terrain: StageComponent = NewPart(TEXT("Terrain"), false, Staged.Quality <= 2); break;
	case SpsTileKind::Roads: StageComponent = NewPart(TEXT("Roads"), false, Staged.Quality <= 2); break;
	case SpsTileKind::BuildingsFar: StageComponent = NewPart(TEXT("BuildingsFar"), bNear, bNear); break;
	case SpsTileKind::BuildingsDetail: StageComponent = NewPart(TEXT("BuildingsDetail"), bNear, bNear); break;
	case SpsTileKind::Water: StageComponent = NewPart(TEXT("Water"), false, false); break;
	case SpsTileKind::Furniture: StageComponent = NewPart(TEXT("Furniture"), bNear, false); break;
	case SpsTileKind::Rail: StageComponent = NewPart(TEXT("Rail"), false, false); break;
	default: StageComponent = nullptr; break;
	}
	if (StageComponent) { StageComponent->SetVisibility(false); Parts.Add(StageComponent); }   // erst sichtbar, wenn der ganze Tile fertig ist
}

bool ASpsTile::BuildStep()
{
	if (!bBuilding) return true;
	while (StageMesh < Staged.Meshes.Num())
	{
		const FSpsTileMesh& TM = Staged.Meshes[StageMesh];
		if (TM.Mesh.IsEmpty() || (StageSection == 0 && (StartMesh(TM), StageComponent == nullptr))) { StageMesh++; StageSection = 0; continue; }
		// find the next non-empty section of this mesh
		while (StageSection < TM.Mesh.Sections.Num() && TM.Mesh.Sections[StageSection].Num() == 0) StageSection++;
		if (StageSection >= TM.Mesh.Sections.Num()) { StageMesh++; StageSection = 0; StageComponent = nullptr; continue; }
		const bool bNear = Staged.Quality == 1; const int32 S = StageSection;
		const bool bColl = (Staged.Quality <= 2 && (TM.Kind == SpsTileKind::Terrain || TM.Kind == SpsTileKind::Roads)) || (bNear && (TM.Kind == SpsTileKind::BuildingsFar || TM.Kind == SpsTileKind::BuildingsDetail));
		SpsCreateSection(StageComponent, S, TM.Mesh, S, bColl);
		FString Slot;
		switch (TM.Kind)
		{
		case SpsTileKind::Terrain: Slot = TEXT("Terrain"); break;
		case SpsTileKind::Roads: Slot = SPSMaterials::RoadSlot(S); break;
		case SpsTileKind::BuildingsFar: case SpsTileKind::BuildingsDetail: Slot = SPSMaterials::BuildingSlot(S); break;
		case SpsTileKind::Water: Slot = TEXT("Water"); break;
		case SpsTileKind::Furniture: Slot = SPSMaterials::FurnitureSlot(S); break;
		default: Slot = SPSMaterials::RailSlot(S); break;
		}
		StageComponent->SetMaterial(S, SPSMaterials::Get(Slot));
		StageSection++;
		return false;   // one section per call
	}
	bBuilding = false; Staged = FSpsTileData();
	for (UProceduralMeshComponent* P : Parts) { if (P) P->SetVisibility(true); }
	{
		FString Info;
		for (UProceduralMeshComponent* P : Parts)
		{
			if (!P) continue;
			UMaterialInterface* M = P->GetNumMaterials() > 0 ? P->GetMaterial(0) : nullptr;
			Info += FString::Printf(TEXT(" %s[%d Sek, Mat %s, Z %.0f..%.0f]"), *P->GetName(), P->GetNumSections(), M ? *M->GetName() : TEXT("-"), P->Bounds.Origin.Z - P->Bounds.BoxExtent.Z, P->Bounds.Origin.Z + P->Bounds.BoxExtent.Z);
		}
		UE_LOG(LogTemp, Log, TEXT("[SPS] Tile %d/%d q%d fertig:%s"), TileX, TileY, Quality, *Info);
	}
	if (OldParts.Num() > 0) { OldDestroyIn = 3.f; SetActorTickEnabled(true); }   // alte Teile erst nach 3 s entfernen (Zeit fuer die asynchrone Kollisions-Berechnung)
	return true;
}
