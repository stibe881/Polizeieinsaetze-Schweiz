#include "SpsHuman.h"
#include "ProceduralMeshComponent.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"
#include "SpsMaterials.h"

bool FSpsHumanBody::Build(AActor* Owner, USceneComponent* Root, const FSpsModelData& Model)
{
	TSharedPtr<FJsonObject> Meta; TSharedRef<TJsonReader<>> R = TJsonReaderFactory<>::Create(Model.MetaJson);
	if (!FJsonSerializer::Deserialize(R, Meta) || !Meta.IsValid()) return false;
	Height = static_cast<float>(Meta->GetNumberField(TEXT("height"))) * 100.f;
	const TArray<TSharedPtr<FJsonValue>>* PartList = nullptr;
	if (!Meta->TryGetArrayField(TEXT("parts"), PartList)) return false;
	const int32 N = FMath::Min(PartList->Num(), Model.Meshes.Num());
	for (int32 i = 0; i < N; i++)
	{
		const TSharedPtr<FJsonObject> P = (*PartList)[i]->AsObject(); if (!P.IsValid()) continue;
		const FString Name = P->GetStringField(TEXT("name")); FString Parent; P->TryGetStringField(TEXT("parent"), Parent);
		const TArray<TSharedPtr<FJsonValue>>& Pv = P->GetArrayField(TEXT("pivot"));
		const FVector Pivot = Pv.Num() >= 3 ? SpsAxis(Pv[0]->AsNumber(), Pv[1]->AsNumber(), Pv[2]->AsNumber()) * 100.0 : FVector::ZeroVector;
		USceneComponent* Joint = NewObject<USceneComponent>(Owner, *FString::Printf(TEXT("J_%s"), *Name));
		USceneComponent* Attach = Root; if (!Parent.IsEmpty()) { if (USceneComponent** Found = Parts.Find(Parent)) Attach = *Found; }
		Joint->SetupAttachment(Attach); Joint->SetRelativeLocation(Pivot); Joint->RegisterComponent(); Parts.Add(Name, Joint);
		UProceduralMeshComponent* Mesh = NewObject<UProceduralMeshComponent>(Owner, *FString::Printf(TEXT("M_%s"), *Name));
		Mesh->SetupAttachment(Joint); Mesh->bUseAsyncCooking = true; Mesh->SetCollisionEnabled(ECollisionEnabled::NoCollision); Mesh->SetCastShadow(true); Mesh->RegisterComponent();
		const FSpsMeshData& M = Model.Meshes[i];
		for (int32 s = 0; s < M.Sections.Num(); s++)
		{
			if (M.Sections[s].Num() == 0) continue;
			SpsCreateSection(Mesh, s, M, s, false);
			Mesh->SetMaterial(s, SPSMaterials::Get(SPSMaterials::HumanSlot(s)));
		}
	}
	return Parts.Num() > 0;
}

void FSpsHumanBody::Animate(float Dt)
{
	auto Set = [this](const TCHAR* Name, float PitchDeg, float RollDeg = 0.f)
	{
		if (USceneComponent** P = Parts.Find(Name)) (*P)->SetRelativeRotation(FRotator(PitchDeg, 0.f, RollDeg));
	};
	const bool bMoving = Speed > 0.15f && (Pose == SpsPose::Walk || Pose == SpsPose::Run || Pose == SpsPose::Idle);
	Phase += Dt * (3.0f + Speed * 2.6f);
	const float Amp = bMoving ? FMath::Clamp(Speed / 1.5f, 0.3f, 1.5f) * 32.f : 0.f;
	const float S = FMath::Sin(Phase) * Amp, C = FMath::Cos(Phase) * Amp;
	Set(TEXT("ThighL"), S); Set(TEXT("ThighR"), -S);
	Set(TEXT("ShinL"), FMath::Max(0.f, -C) * 0.9f); Set(TEXT("ShinR"), FMath::Max(0.f, C) * 0.9f);
	Set(TEXT("UpperArmL"), -S * 0.8f, 3.f); Set(TEXT("UpperArmR"), S * 0.8f, -3.f);
	Set(TEXT("ForearmL"), -FMath::Abs(S) * 0.4f); Set(TEXT("ForearmR"), -FMath::Abs(S) * 0.4f);
	switch (Pose)
	{
	case SpsPose::Talk: Set(TEXT("UpperArmR"), -35.f + FMath::Sin(Phase * 0.6f) * 12.f); Set(TEXT("ForearmR"), -60.f + FMath::Sin(Phase * 0.9f) * 14.f); break;
	case SpsPose::Point: Set(TEXT("UpperArmR"), -85.f); Set(TEXT("ForearmR"), -5.f); break;
	case SpsPose::Phone: Set(TEXT("UpperArmR"), -70.f); Set(TEXT("ForearmR"), -110.f); break;
	case SpsPose::Search: Set(TEXT("UpperArmL"), -50.f); Set(TEXT("UpperArmR"), -50.f); Set(TEXT("ForearmL"), -30.f); Set(TEXT("ForearmR"), -30.f); break;
	case SpsPose::Cuffed: Set(TEXT("UpperArmL"), 22.f, 6.f); Set(TEXT("UpperArmR"), 22.f, -6.f); Set(TEXT("ForearmL"), 18.f); Set(TEXT("ForearmR"), 18.f); break;
	default: break;
	}
}

ASpsHuman::ASpsHuman()
{
	PrimaryActorTick.bCanEverTick = true;
	USceneComponent* Root = CreateDefaultSubobject<USceneComponent>(TEXT("Root"));
	SetRootComponent(Root);
}

void ASpsHuman::Init(const FSpsModelData& Model) { Body.Build(this, GetRootComponent(), Model); }

void ASpsHuman::SetTarget(const FVector& Pos, float YawDeg, float SpeedMs, uint8 Pose)
{
	TargetPos = Pos; TargetYaw = YawDeg; Body.Speed = SpeedMs; PoseNow = Pose; Body.Pose = Pose;
	if (bFirst) { bFirst = false; SetActorLocationAndRotation(Pos, FRotator(0, YawDeg, 0)); }
}

void ASpsHuman::Tick(float Dt)
{
	Super::Tick(Dt);
	FVector P = FMath::VInterpTo(GetActorLocation(), TargetPos, Dt, 10.f);
	FRotator R = GetActorRotation(); R.Yaw = FMath::FInterpTo(R.Yaw, FRotator::NormalizeAxis(TargetYaw), Dt, 8.f);
	if (PoseNow == SpsPose::Fallen) { R.Roll = 85.f; P.Z += 18.f; } else R.Roll = 0.f;
	SetActorLocationAndRotation(P, R);
	Body.Animate(Dt);
}
