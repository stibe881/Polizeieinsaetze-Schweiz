#include "SpsOverrides.h"
#include "Misc/ConfigCacheIni.h"

namespace
{
	TArray<FSpsCarOverride> GCars;
	TArray<FSpsHumanOverride> GHumans;
	bool bLoaded = false;

	float F(const TArray<FString>& P, int32 I, float Def) { return P.IsValidIndex(I) && !P[I].IsEmpty() ? FCString::Atof(*P[I]) : Def; }
}

void SpsOverrides::Load()
{
	if (bLoaded) return;
	bLoaded = true;
	if (!GConfig) return;
	TArray<FString> Lines;
	GConfig->GetArray(TEXT("/Script/SwissPoliceSim.SpsModels"), TEXT("Car"), Lines, GGameIni);
	for (const FString& L : Lines)
	{
		TArray<FString> P; L.ParseIntoArray(P, TEXT("|"), false);
		if (P.Num() < 2) continue;
		FSpsCarOverride O; O.Prefix = P[0]; O.Mesh = P[1]; O.Scale = F(P, 2, 1.f); O.Offset = FVector(F(P, 3, 0.f), F(P, 4, 0.f), F(P, 5, 0.f)); O.Yaw = F(P, 6, 0.f);
		GCars.Add(O);
	}
	Lines.Reset();
	GConfig->GetArray(TEXT("/Script/SwissPoliceSim.SpsModels"), TEXT("Human"), Lines, GGameIni);
	for (const FString& L : Lines)
	{
		TArray<FString> P; L.ParseIntoArray(P, TEXT("|"), false);
		if (P.Num() < 2) continue;
		FSpsHumanOverride O; O.Prefix = P[0]; O.Mesh = P[1]; O.Walk = P.IsValidIndex(2) ? P[2] : FString(); O.Idle = P.IsValidIndex(3) ? P[3] : FString(); O.Scale = F(P, 4, 1.f); O.OffsetX = F(P, 5, 0.f); O.OffsetZ = F(P, 6, 0.f); O.Yaw = F(P, 7, 0.f); O.AnimBP = P.IsValidIndex(8) ? P[8] : FString();
		GHumans.Add(O);
	}
}

const FSpsCarOverride* SpsOverrides::FindCar(const FString& Key)
{
	Load();
	const FSpsCarOverride* Best = nullptr;
	for (const FSpsCarOverride& O : GCars) if (Key.StartsWith(O.Prefix) && (!Best || O.Prefix.Len() > Best->Prefix.Len())) Best = &O;
	return Best;
}

const FSpsHumanOverride* SpsOverrides::FindHuman(const FString& Key)
{
	Load();
	const FSpsHumanOverride* Best = nullptr;
	for (const FSpsHumanOverride& O : GHumans) if (Key.StartsWith(O.Prefix) && (!Best || O.Prefix.Len() > Best->Prefix.Len())) Best = &O;
	return Best;
}
