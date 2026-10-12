#pragma once

#include "CoreMinimal.h"

/** Wire structures and parsers for the binary frames of the SwissServer protocol (see docs/PROTOCOL.md). */

namespace SpsMsg { enum : uint8 { Tile = 1, Snapshot = 2, TileDrop = 3, Model = 5, Chunk = 6 }; }
namespace SpsTileKind { enum : uint8 { Terrain = 0, Roads = 1, BuildingsFar = 2, BuildingsDetail = 3, Water = 4, Furniture = 5, Rail = 6 }; }

/** One mesh already converted to Unreal axes and centimetres (X = north, Y = east, Z = up). */
struct FSpsMeshData
{
	TArray<FVector> Vertices;
	TArray<FVector> Normals;
	TArray<FVector2D> UV0;
	TArray<FVector2D> UV1;
	TArray<FColor> Colors;
	TArray<TArray<int32>> Sections;
	bool IsEmpty() const { return Vertices.Num() == 0; }
};

struct FSpsTileMesh { uint8 Kind = 0; FSpsMeshData Mesh; };
struct FSpsTree { float LocalE = 0, LocalN = 0, Ground = 0, Height = 0, Radius = 0; uint8 Kind = 0, Variant = 0, Bucket = 0; };

struct FSpsTileData
{
	int32 X = 0, Y = 0;
	uint8 Quality = 3;
	TArray<FSpsTileMesh> Meshes;
	TArray<FSpsTree> Trees;
};

struct FSpsModelData
{
	FString Key;
	FString MetaJson;
	TArray<FSpsMeshData> Meshes;
};

struct FSpsCarState { int32 Id = 0; double E = 0, N = 0; float H = 0, Yaw = 0, Speed = 0; uint16 Flags = 0; };
struct FSpsPedState { int32 Id = 0; double E = 0, N = 0; float H = 0, Yaw = 0; uint8 State = 0; float Speed = 0; };
struct FSpsActorState { int32 Id = 0; double E = 0, N = 0; float H = 0, Yaw = 0; uint8 Pose = 0, Flags = 0; };

struct FSpsSnapshot
{
	double Time = 0;
	TArray<FSpsCarState> Cars;
	TArray<FSpsPedState> Peds;
	TArray<FSpsActorState> Actors;
};

class UProceduralMeshComponent;
/**
 * Creates one mesh section from the indices of <paramref name="Section"/> only: unused vertices are dropped and the indices remapped, so a mesh with many
 * materials does not duplicate its whole vertex buffer per section.
 */
void SpsCreateSection(UProceduralMeshComponent* Target, int32 SectionIndex, const FSpsMeshData& Mesh, int32 Section, bool bCollision, bool bDoubleSided = false);

/** Car state flags (FSpsCarState::Flags). */
namespace SpsCarFlag { enum : uint16 { Brake = 1, Hazard = 2, IndLeft = 4, IndRight = 8, Stopped = 32, Crashed = 64 }; }

/** Server meshes use Unity axes in metres (x = east, y = up, z = north). Converts to Unreal (X = north, Y = east, Z = up) in centimetres. */
FORCEINLINE FVector SpsAxis(float X, float Y, float Z) { return FVector(Z, X, Y); }

namespace SpsProtocol
{
	bool ParseTile(const TArray<uint8>& Data, FSpsTileData& Out);
	bool ParseModel(const TArray<uint8>& Data, FSpsModelData& Out);
	bool ParseSnapshot(const TArray<uint8>& Data, FSpsSnapshot& Out);
	/** Tile-drop frame: returns false if malformed; X = INT_MIN means "drop everything". */
	bool ParseTileDrop(const TArray<uint8>& Data, int32& X, int32& Y);
}
