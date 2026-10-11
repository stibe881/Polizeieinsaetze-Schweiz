#include "SpsVehicle.h"
#include "ProceduralMeshComponent.h"
#include "Components/BoxComponent.h"
#include "Components/TextRenderComponent.h"
#include "Materials/MaterialInstanceDynamic.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"
#include "SpsMaterials.h"

namespace
{
	TSharedPtr<FJsonObject> ParseJson(const FString& S)
	{
		TSharedPtr<FJsonObject> Obj; TSharedRef<TJsonReader<>> R = TJsonReaderFactory<>::Create(S);
		if (!FJsonSerializer::Deserialize(R, Obj)) return nullptr;
		return Obj;
	}

	void FillSections(UProceduralMeshComponent* P, const FSpsMeshData& M, TFunctionRef<FString(int32)> Slot)
	{
		for (int32 s = 0; s < M.Sections.Num(); s++)
		{
			if (M.Sections[s].Num() == 0) continue;
			SpsCreateSection(P, s, M, s, false);
			P->SetMaterial(s, SPSMaterials::Get(Slot(s)));
		}
	}

	UMaterialInstanceDynamic* MakeDynamic(UProceduralMeshComponent* P, int32 Section)
	{
		UMaterialInterface* Base = P->GetMaterial(Section);
		if (!Base) return nullptr;
		UMaterialInstanceDynamic* D = UMaterialInstanceDynamic::Create(Base, P);
		P->SetMaterial(Section, D);
		return D;
	}
}

bool FSpsCarVisual::Build(AActor* Owner, USceneComponent* Root, const FSpsModelData& Model, const FString& Plate)
{
	if (Model.Meshes.Num() < 2) return false;
	TSharedPtr<FJsonObject> Meta = ParseJson(Model.MetaJson);
	if (!Meta.IsValid()) return false;
	WheelRadius = static_cast<float>(Meta->GetNumberField(TEXT("wheelRadius"))) * 100.f;
	Length = static_cast<float>(Meta->GetNumberField(TEXT("length"))) * 100.f;
	Width = static_cast<float>(Meta->GetNumberField(TEXT("width"))) * 100.f;
	Height = static_cast<float>(Meta->GetNumberField(TEXT("height"))) * 100.f;
	const TArray<TSharedPtr<FJsonValue>>* Wheels4 = nullptr;
	if (Meta->TryGetArrayField(TEXT("wheels"), Wheels4) && Wheels4->Num() >= 4)
	{
		for (int32 i = 0; i < 4; i++)
		{
			const TArray<TSharedPtr<FJsonValue>>& C = (*Wheels4)[i]->AsArray();
			if (C.Num() >= 3) WheelCentres[i] = SpsAxis(C[0]->AsNumber(), C[1]->AsNumber(), C[2]->AsNumber()) * 100.0;
		}
	}
	const TArray<TSharedPtr<FJsonValue>>* Eye = nullptr;
	if (Meta->TryGetArrayField(TEXT("driverEye"), Eye) && Eye->Num() >= 3) DriverEye = SpsAxis((*Eye)[0]->AsNumber(), (*Eye)[1]->AsNumber(), (*Eye)[2]->AsNumber()) * 100.0;
	const TArray<TSharedPtr<FJsonValue>>* Hood = nullptr;
	if (Meta->TryGetArrayField(TEXT("hoodCam"), Hood) && Hood->Num() >= 3) HoodCam = SpsAxis((*Hood)[0]->AsNumber(), (*Hood)[1]->AsNumber(), (*Hood)[2]->AsNumber()) * 100.0;

	Body = NewObject<UProceduralMeshComponent>(Owner, TEXT("Body"));
	Body->SetupAttachment(Root); Body->bUseAsyncCooking = true; Body->SetCollisionEnabled(ECollisionEnabled::NoCollision); Body->SetCastShadow(true); Body->RegisterComponent();
	FillSections(Body, Model.Meshes[0], [](int32 S) { return SPSMaterials::CarSlot(S); });
	LampFront = MakeDynamic(Body, 3); LampRear = MakeDynamic(Body, 4); Indicator = MakeDynamic(Body, 5); BlueL = MakeDynamic(Body, 6); BlueR = MakeDynamic(Body, 7);

	for (int32 i = 0; i < 4; i++)
	{
		USceneComponent* Pivot = NewObject<USceneComponent>(Owner, *FString::Printf(TEXT("WheelPivot%d"), i));
		Pivot->SetupAttachment(Root); Pivot->SetRelativeLocation(WheelCentres[i]); Pivot->RegisterComponent(); WheelPivots[i] = Pivot;
		UProceduralMeshComponent* W = NewObject<UProceduralMeshComponent>(Owner, *FString::Printf(TEXT("Wheel%d"), i));
		W->SetupAttachment(Pivot); W->bUseAsyncCooking = true; W->SetCollisionEnabled(ECollisionEnabled::NoCollision); W->SetCastShadow(true); W->RegisterComponent();
		if (WheelCentres[i].Y < 0) W->SetRelativeRotation(FRotator(0.f, 180.f, 0.f));   // left wheels: mirror the rim
		FillSections(W, Model.Meshes[1], [](int32 S) { return FString(S == 0 ? TEXT("Tyre") : TEXT("Rim")); });
		Wheels[i] = W;
	}

	// number plates: text above the (unlit) plate quads, front and rear
	if (Model.Meshes[0].Sections.IsValidIndex(8) && Model.Meshes[0].Sections[8].Num() > 0)
	{
		const FSpsMeshData& M = Model.Meshes[0]; FVector SumF = FVector::ZeroVector, SumR = FVector::ZeroVector; int32 NF = 0, NR = 0;
		TSet<int32> Seen;
		for (int32 Idx : M.Sections[8]) { if (Seen.Contains(Idx)) continue; Seen.Add(Idx); const FVector& P = M.Vertices[Idx]; if (P.X > 0) { SumF += P; NF++; } else { SumR += P; NR++; } }
		auto AddText = [&](const FVector& Centre, bool bFront)
		{
			UTextRenderComponent* T = NewObject<UTextRenderComponent>(Owner, bFront ? TEXT("PlateFront") : TEXT("PlateRear"));
			T->SetupAttachment(Root); T->SetRelativeLocation(Centre + FVector(bFront ? 0.6f : -0.6f, 0, 0)); T->SetRelativeRotation(FRotator(0.f, bFront ? 0.f : 180.f, 0.f));
			T->SetText(FText::FromString(Plate)); T->SetWorldSize(6.5f); T->SetTextRenderColor(FColor(15, 15, 18)); T->SetHorizontalAlignment(EHTA_Center); T->SetVerticalAlignment(EVRTA_TextCenter); T->SetCastShadow(false); T->RegisterComponent();
		};
		if (NF > 0) AddText(SumF / NF, true);
		if (NR > 0) AddText(SumR / NR, false);
	}
	return true;
}

void FSpsCarVisual::Roll(float DistanceCm, float SteerDeg)
{
	if (WheelRadius < 1.f) return;
	Spin = FMath::Fmod(Spin + FMath::RadiansToDegrees(DistanceCm / WheelRadius), 360.f);
	for (int32 i = 0; i < 4; i++)
	{
		if (!Wheels[i] || !WheelPivots[i]) continue;
		WheelPivots[i]->SetRelativeRotation(FRotator(0.f, i < 2 ? SteerDeg : 0.f, 0.f));
		Wheels[i]->SetRelativeRotation(FRotator(WheelCentres[i].Y < 0 ? -Spin : Spin, WheelCentres[i].Y < 0 ? 180.f : 0.f, 0.f));
	}
}

void FSpsCarVisual::SetLights(bool bBrake, bool bIndLeft, bool bIndRight, bool bHazard, bool bBlue, bool bHeadlights, double Time)
{
	const bool bBlink = FMath::Fmod(Time, 0.8) < 0.4;
	if (LampRear) LampRear->SetScalarParameterValue(TEXT("Glow"), bBrake ? 6.f : 0.4f);
	if (LampFront) LampFront->SetScalarParameterValue(TEXT("Glow"), bHeadlights ? 4.f : 0.f);
	if (Indicator) Indicator->SetScalarParameterValue(TEXT("Glow"), ((bIndLeft || bIndRight || bHazard) && bBlink) ? 6.f : 0.f);
	const bool bPhase = FMath::Fmod(Time, 0.3) < 0.15;
	if (BlueL) BlueL->SetScalarParameterValue(TEXT("Glow"), (bBlue && bPhase) ? 30.f : 0.f);
	if (BlueR) BlueR->SetScalarParameterValue(TEXT("Glow"), (bBlue && !bPhase) ? 30.f : 0.f);
}

ASpsCar::ASpsCar()
{
	PrimaryActorTick.bCanEverTick = true;
	USceneComponent* Root = CreateDefaultSubobject<USceneComponent>(TEXT("Root"));
	SetRootComponent(Root);
	Hit = CreateDefaultSubobject<UBoxComponent>(TEXT("Hit"));
	Hit->SetupAttachment(Root);
	Hit->SetRelativeLocation(FVector(0.f, 0.f, 85.f));
	Hit->SetBoxExtent(FVector(235.f, 95.f, 60.f));
	Hit->SetCollisionProfileName(TEXT("BlockAllDynamic"));
	Hit->SetCanEverAffectNavigation(false);
}

void ASpsCar::Init(const FSpsModelData& Model, const FString& Plate)
{
	if (!Visual.Build(this, GetRootComponent(), Model, Plate)) return;
	// the hit box sits at the middle of the body (60 cm half height + 25 cm ground clearance)
	Hit->SetBoxExtent(FVector(Visual.Length * 0.48f, Visual.Width * 0.48f, 60.f));
}

void ASpsCar::SetTarget(const FVector& Pos, float YawDeg, float SpeedMs, uint16 Flags)
{
	TargetPos = Pos; TargetYaw = YawDeg; Speed = SpeedMs; FlagsNow = Flags;
	if (bFirst) { bFirst = false; SetActorLocationAndRotation(TargetPos, FRotator(0, YawDeg, 0)); PrevYaw = YawDeg; }
}

void ASpsCar::SetStatic(const FVector& Pos, float YawDeg, bool bHazard, bool bBlue)
{
	bProp = true; bBlueLights = bBlue; bHazardLights = bHazard; SetActorLocationAndRotation(Pos, FRotator(0, YawDeg, 0)); SetActorTickEnabled(true); bFirst = false; TargetPos = GetActorLocation(); TargetYaw = YawDeg;
}

void ASpsCar::Tick(float Dt)
{
	Super::Tick(Dt);
	const double Now = GetWorld() ? GetWorld()->GetTimeSeconds() : 0.0;
	if (!bProp)
	{
		const FVector Old = GetActorLocation();
		const FVector New = FMath::VInterpTo(Old, TargetPos, Dt, 14.f);
		const float Yaw = FMath::FInterpTo(GetActorRotation().Yaw, FRotator::NormalizeAxis(TargetYaw), Dt, 10.f);
		// the ground slope pitch comes from the next snapshots (heights differ front to rear): cheap approximation from the speed vector
		SetActorLocationAndRotation(New, FRotator(0.f, Yaw, 0.f));
		const float Moved = FVector::Dist2D(Old, New);
		const float SteerDeg = FMath::Clamp(FMath::FindDeltaAngleDegrees(PrevYaw, Yaw) * 8.f, -28.f, 28.f); PrevYaw = Yaw;
		Visual.Roll(Moved, SteerDeg);
		Visual.SetLights((FlagsNow & SpsCarFlag::Brake) != 0, (FlagsNow & SpsCarFlag::IndLeft) != 0, (FlagsNow & SpsCarFlag::IndRight) != 0, (FlagsNow & SpsCarFlag::Hazard) != 0, false, true, Now);
	}
	else
	{
		Visual.SetLights(false, false, false, bHazardLights, bBlueLights, false, Now);
	}
}

// ------------------------------------------------------------------ props
ASpsProp::ASpsProp()
{
	PrimaryActorTick.bCanEverTick = false;
	SetRootComponent(CreateDefaultSubobject<USceneComponent>(TEXT("Root")));
}

void ASpsProp::Build(const FString& Kind, float YawDeg)
{
	FVector Half(20.f, 5.f, 15.f); FColor Col(38, 38, 44);
	if (Kind == TEXT("cargo")) { Half = FVector(60.f, 30.f, 40.f); Col = FColor(184, 140, 90); }
	else if (Kind == TEXT("roadblock")) { Half = FVector(40.f, 300.f, 55.f); Col = FColor(220, 60, 40); }
	TArray<FVector> V, N; TArray<FVector2D> UV; TArray<FColor> C; TArray<int32> I; TArray<FProcMeshTangent> NoTangents;
	const FVector Dirs[6] = { FVector(1, 0, 0), FVector(-1, 0, 0), FVector(0, 1, 0), FVector(0, -1, 0), FVector(0, 0, 1), FVector(0, 0, -1) };
	for (int32 f = 0; f < 6; f++)
	{
		const FVector Nrm = Dirs[f]; const FVector U = FMath::Abs(Nrm.Z) > 0.5f ? FVector(1, 0, 0) : FVector(0, 0, 1); const FVector T = FVector::CrossProduct(Nrm, U).GetSafeNormal(); const FVector B = FVector::CrossProduct(Nrm, T).GetSafeNormal();
		const int32 Base = V.Num();
		const FVector Ext(FMath::Abs(Nrm.X) > 0.5f ? 0.f : Half.X, FMath::Abs(Nrm.Y) > 0.5f ? 0.f : Half.Y, FMath::Abs(Nrm.Z) > 0.5f ? 0.f : Half.Z);
		const FVector Centre = Nrm * (FMath::Abs(Nrm.X) * Half.X + FMath::Abs(Nrm.Y) * Half.Y + FMath::Abs(Nrm.Z) * Half.Z);
		const FVector Tv = T * (FMath::Abs(T.X) * Half.X + FMath::Abs(T.Y) * Half.Y + FMath::Abs(T.Z) * Half.Z), Bv = B * (FMath::Abs(B.X) * Half.X + FMath::Abs(B.Y) * Half.Y + FMath::Abs(B.Z) * Half.Z);
		(void)Ext;
		V.Add(Centre - Tv - Bv); V.Add(Centre + Tv - Bv); V.Add(Centre + Tv + Bv); V.Add(Centre - Tv + Bv);
		for (int32 k = 0; k < 4; k++) { N.Add(Nrm); UV.Add(FVector2D(k == 1 || k == 2, k >= 2)); C.Add(Col); }
		I.Append({ Base, Base + 2, Base + 1, Base, Base + 3, Base + 2 });
		// make the winding clockwise seen from outside whatever orientation the basis has
		const FVector Tri = FVector::CrossProduct(V[Base + 2] - V[Base], V[Base + 1] - V[Base]);
		if (FVector::DotProduct(Tri, Nrm) < 0.f) { I.SetNum(I.Num() - 6); I.Append({ Base, Base + 1, Base + 2, Base, Base + 2, Base + 3 }); }
	}
	UProceduralMeshComponent* P = NewObject<UProceduralMeshComponent>(this, TEXT("Box"));
	P->SetupAttachment(GetRootComponent()); P->bUseAsyncCooking = true; P->SetCollisionEnabled(ECollisionEnabled::QueryAndPhysics); P->SetCastShadow(true); P->RegisterComponent();
	P->CreateMeshSection(0, V, I, N, UV, C, NoTangents, true); P->SetMaterial(0, SPSMaterials::Get(TEXT("Prop")));
	P->SetRelativeLocation(FVector(0, 0, Half.Z)); SetActorRotation(FRotator(0, YawDeg, 0));
}
