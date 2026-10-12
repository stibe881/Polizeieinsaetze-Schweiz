#include "SpsProtocol.h"
#include "ProceduralMeshComponent.h"

namespace
{
	/** Bounds-checked little-endian reader. All reads set bOk = false instead of overrunning. */
	struct FReader
	{
		const uint8* Data; int32 Size; int32 Pos = 0; bool bOk = true;
		FReader(const TArray<uint8>& A, int32 Start = 0) : Data(A.GetData()), Size(A.Num()), Pos(Start) {}
		bool Has(int32 N) { if (Pos + N > Size || N < 0) { bOk = false; return false; } return true; }
		uint8 U8() { if (!Has(1)) return 0; return Data[Pos++]; }
		uint16 U16() { if (!Has(2)) return 0; uint16 V; FMemory::Memcpy(&V, Data + Pos, 2); Pos += 2; return V; }
		int32 I32() { if (!Has(4)) return 0; int32 V; FMemory::Memcpy(&V, Data + Pos, 4); Pos += 4; return V; }
		float F32() { if (!Has(4)) return 0; float V; FMemory::Memcpy(&V, Data + Pos, 4); Pos += 4; return V; }
		double F64() { if (!Has(8)) return 0; double V; FMemory::Memcpy(&V, Data + Pos, 8); Pos += 8; return V; }
		FString Str() { int32 L = U16(); if (!Has(L)) return FString(); FString S = FString(FUTF8ToTCHAR(reinterpret_cast<const ANSICHAR*>(Data + Pos), L)); Pos += L; return S; }
	};

	bool ReadMesh(FReader& R, FSpsMeshData& M)
	{
		const int32 V = R.I32(); const int32 SubCount = R.I32(); const uint8 Flags = R.U8();
		if (!R.bOk || V < 0 || SubCount < 0 || SubCount > 64 || V > 20000000) return false;
		if (V == 0) return true;
		const bool bUv2 = (Flags & 1) != 0, bBig = (Flags & 2) != 0;
		M.Vertices.SetNumUninitialized(V); M.Normals.SetNumUninitialized(V); M.UV0.SetNumUninitialized(V); M.Colors.SetNumUninitialized(V);
		if (!R.Has(V * 12 * 2 + V * 8 + V * 4)) return false;
		for (int32 i = 0; i < V; i++)
		{
			const float X = R.F32(), Y = R.F32(), Z = R.F32();
			// Schutz: ein Renderer-Absturz durch NaN/Unendlich/absurde Werte ist nicht abfangbar, also vorher verwerfen
			if (!FMath::IsFinite(X) || !FMath::IsFinite(Y) || !FMath::IsFinite(Z) || FMath::Abs(X) > 1.0e5f || FMath::Abs(Y) > 1.0e5f || FMath::Abs(Z) > 1.0e5f)
			{
				UE_LOG(LogTemp, Warning, TEXT("[SPS] Mesh verworfen: Vertex %d/%d ausserhalb des gueltigen Bereichs (%g, %g, %g)"), i, V, X, Y, Z);
				return false;
			}
			M.Vertices[i] = SpsAxis(X, Y, Z) * 100.0;
		}
		for (int32 i = 0; i < V; i++) { const float X = R.F32(), Y = R.F32(), Z = R.F32(); M.Normals[i] = SpsAxis(X, Y, Z); }
		for (int32 i = 0; i < V; i++) { const float U = R.F32(), Vv = R.F32(); M.UV0[i] = FVector2D(U, 1.0f - Vv); }
		for (int32 i = 0; i < V; i++) { const uint8 Rr = R.U8(), G = R.U8(), B = R.U8(), A = R.U8(); M.Colors[i] = FColor(Rr, G, B, A); }
		if (bUv2) { if (!R.Has(V * 8)) return false; M.UV1.SetNumUninitialized(V); for (int32 i = 0; i < V; i++) { const float U = R.F32(), Vv = R.F32(); M.UV1[i] = FVector2D(U, Vv); } }
		M.Sections.SetNum(SubCount);
		for (int32 s = 0; s < SubCount; s++)
		{
			const int32 C = R.I32(); if (!R.bOk || C < 0 || C % 3 != 0 || !R.Has(C * (bBig ? 4 : 2))) return false;
			TArray<int32>& Idx = M.Sections[s]; Idx.SetNumUninitialized(C);
			for (int32 i = 0; i < C; i++) { const int32 Index = bBig ? R.I32() : static_cast<int32>(R.U16()); if (Index < 0 || Index >= V) return false; Idx[i] = Index; }
		}
		return R.bOk;
	}
}

namespace SpsProtocol
{
	bool ParseTile(const TArray<uint8>& Data, FSpsTileData& Out)
	{
		FReader R(Data, 1); Out.X = R.I32(); Out.Y = R.I32(); Out.Quality = R.U8(); const int32 N = R.U8();
		if (!R.bOk) return false;
		for (int32 i = 0; i < N; i++)
		{
			FSpsTileMesh TM; TM.Kind = R.U8(); if (!ReadMesh(R, TM.Mesh)) return false; Out.Meshes.Add(MoveTemp(TM));
		}
		const int32 T = R.I32(); if (!R.bOk || T < 0 || !R.Has(T * 23)) return false;
		Out.Trees.SetNum(T);
		for (int32 i = 0; i < T; i++) { FSpsTree& Tr = Out.Trees[i]; Tr.LocalE = R.F32(); Tr.LocalN = R.F32(); Tr.Ground = R.F32(); Tr.Height = R.F32(); Tr.Radius = R.F32(); Tr.Kind = R.U8(); Tr.Variant = R.U8(); Tr.Bucket = R.U8(); }
		return R.bOk;
	}

	bool ParseModel(const TArray<uint8>& Data, FSpsModelData& Out)
	{
		FReader R(Data, 1); Out.Key = R.Str(); Out.MetaJson = R.Str(); const int32 N = R.U8(); if (!R.bOk) return false;
		Out.Meshes.SetNum(N);
		for (int32 i = 0; i < N; i++) if (!ReadMesh(R, Out.Meshes[i])) return false;
		return R.bOk;
	}

	bool ParseSnapshot(const TArray<uint8>& Data, FSpsSnapshot& Out)
	{
		FReader R(Data, 1); Out.Time = R.F64();
		int32 N = R.I32(); if (!R.bOk || N < 0 || !R.Has(N * 34)) return false; Out.Cars.SetNum(N);
		for (int32 i = 0; i < N; i++) { FSpsCarState& C = Out.Cars[i]; C.Id = R.I32(); C.E = R.F64(); C.N = R.F64(); C.H = R.F32(); C.Yaw = R.F32(); C.Speed = R.F32(); C.Flags = R.U16(); }
		N = R.I32(); if (!R.bOk || N < 0 || !R.Has(N * 33)) return false; Out.Peds.SetNum(N);
		for (int32 i = 0; i < N; i++) { FSpsPedState& P = Out.Peds[i]; P.Id = R.I32(); P.E = R.F64(); P.N = R.F64(); P.H = R.F32(); P.Yaw = R.F32(); P.State = R.U8(); P.Speed = R.F32(); }
		N = R.I32(); if (!R.bOk || N < 0 || !R.Has(N * 30)) return false; Out.Actors.SetNum(N);
		for (int32 i = 0; i < N; i++) { FSpsActorState& A = Out.Actors[i]; A.Id = R.I32(); A.E = R.F64(); A.N = R.F64(); A.H = R.F32(); A.Yaw = R.F32(); A.Pose = R.U8(); A.Flags = R.U8(); }
		return R.bOk;
	}

	bool ParseTileDrop(const TArray<uint8>& Data, int32& X, int32& Y)
	{
		FReader R(Data, 1); X = R.I32(); Y = R.I32(); return R.bOk;
	}
}

void SpsCreateSection(UProceduralMeshComponent* Target, int32 SectionIndex, const FSpsMeshData& M, int32 Section, bool bCollision, bool bDoubleSided)
{
	if (!Target || !M.Sections.IsValidIndex(Section) || M.Sections[Section].Num() == 0) return;
	const TArray<int32>& Src = M.Sections[Section];
	TArray<int32> Remap; Remap.SetNumUninitialized(M.Vertices.Num());
	for (int32 i = 0; i < Remap.Num(); i++) Remap[i] = -1;
	const bool bUv1 = M.UV1.Num() == M.Vertices.Num();
	TArray<FVector> V, N; TArray<FVector2D> UV, UV1; TArray<FColor> C; TArray<int32> Idx; Idx.SetNumUninitialized(Src.Num());
	for (int32 i = 0; i < Src.Num(); i++)
	{
		const int32 S = Src[i]; int32 R = Remap[S];
		if (R < 0) { R = V.Num(); Remap[S] = R; V.Add(M.Vertices[S]); N.Add(M.Normals[S]); UV.Add(M.UV0[S]); if (bUv1) UV1.Add(M.UV1[S]); C.Add(M.Colors[S]); }
		Idx[i] = R;
	}
	if (bDoubleSided)
	{
		// Rueckseiten mitzeichnen: Boden und Strassen bleiben sichtbar, egal von welcher Seite man sie sieht (Wicklung / Position unter der Oberflaeche)
		const int32 N0 = Idx.Num(); Idx.SetNumUninitialized(N0 * 2);
		for (int32 i = 0; i + 2 < N0; i += 3) { Idx[N0 + i] = Idx[i]; Idx[N0 + i + 1] = Idx[i + 2]; Idx[N0 + i + 2] = Idx[i + 1]; }
	}
	TArray<FProcMeshTangent> NoTangents;
	if (bUv1) { TArray<FVector2D> Empty; Target->CreateMeshSection(SectionIndex, V, Idx, N, UV, UV1, Empty, Empty, C, NoTangents, bCollision); }
	else Target->CreateMeshSection(SectionIndex, V, Idx, N, UV, C, NoTangents, bCollision);
}
