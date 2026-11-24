// U-20 (DRAFT_Unreal_Image_Generator.md "U-20 Building destruction in Unreal"): HROT's building damage drawn on our merged house tiles.
// A house stays in its 500 m tile mesh until HROT first damages it; then the tile is swapped for a dynamic copy without it and the house becomes
// its own dynamic mesh: holes are cut by a boolean, broken glass is deleted, a collapse swaps it for rubble. Effects only for a LIVE change.
#pragma once

#include "CoreMinimal.h"
#include "UObject/Object.h"
#include "HrotBuildingDamage.generated.h"

class UDynamicMeshComponent;
class UStaticMesh;

/** one breach / window as HROT sent it (a copy: the DDS sample is valid only during its handler) */
struct FHrotBreachIn { int32 Panel = 0; float From = 0, To = 0, Bottom = 0, Top = 0; double MadeAt = 0; FVector Blast = FVector::ZeroVector; bool bExplosion = true; };   // bExplosion: scorched (HROT does not send the cause yet)
struct FHrotWindowIn { int32 Panel = 0, Opening = 0; uint8 Hits = 0; double HitAt = 0; uint8 U0 = 128, V0 = 128, U1 = 128, V1 = 128; };   // crack points 0-255 across the pane
struct FHrotBuildingDamageIn
{
	FString Key; bool bAlive = false, bCollapsed = false; double CollapsedAt = 0;
	TArray<FHrotBreachIn> Breaches; TArray<FHrotWindowIn> Windows;
};

UCLASS()
class HROTDPS_API UHrotBuildingDamage : public UObject
{
	GENERATED_BODY()
public:
	/** a sample of DPS_BuildingDamage (queued; applied on Tick) */
	void Queue(FHrotBuildingDamageIn&& In) { Pending.Add(MoveTemp(In)); }
	/** applies what arrived; Terrain = the station's terrain (its index), SimNow = the sim time (live vs at join) */
	void Tick(UWorld* World, const FString& Terrain, double SimNow);

	/** TEST / tools: damage a house as if HROT had sent it (Panel < 0: collapse it). True when the house was found. */
	UFUNCTION(BlueprintCallable, Category = "HROT|Buildings")
	bool TestDamage(const FString& Key, int32 Panel, float From, float To, float Bottom, float Top, bool bCollapse, bool bLive);
	/** TEST / tools: break one window (Hits 3) */
	UFUNCTION(BlueprintCallable, Category = "HROT|Buildings")
	bool TestBreakWindow(const FString& Key, int32 Panel, int32 Opening, bool bLive);
	/** TEST / tools: a window hit Hits times (1-2 cracked at the crack points, 3 broken) */
	UFUNCTION(BlueprintCallable, Category = "HROT|Buildings")
	bool TestHitWindow(const FString& Key, int32 Panel, int32 Opening, int32 Hits, int32 U0, int32 V0, int32 U1, int32 V1, bool bLive);
	UFUNCTION(BlueprintCallable, Category = "HROT|Buildings") int32 HousesTakenOut() const { return Houses.Num(); }
	UFUNCTION(BlueprintCallable, Category = "HROT|Buildings") int32 IndexedHouses() const { return Index.Num(); }

	/** TEST / tools: the world and terrain the Test* calls work in (the DPS Tick sets them too) */
	UFUNCTION(BlueprintCallable, Category = "HROT|Buildings") void UseTerrain(UObject* WorldContext, const FString& Terrain);

private:
	struct FOpening { float At = 0, Width = 0, Sill = 0, Head = 0; bool bGlazed = false; };
	struct FWall { FVector2D A, B; float Thickness = 0.2f, Base = 0, Top = 0; TArray<FOpening> Openings; };
	struct FIndexed { FIntPoint Tile; int32 Index = 0; bool bPanels = false; float Base = 0, Top = 0; TArray<FVector2D> Footprint; TArray<FWall> Walls; };
	struct FTile { TWeakObjectPtr<AActor> Static; TWeakObjectPtr<AActor> Dynamic; TSet<int32> Out; };
	struct FHouse { TWeakObjectPtr<AActor> Actor; TWeakObjectPtr<AActor> Rubble; TSet<FString> Cuts; TSet<int64> Broken; bool bCollapsed = false; int32 WallMaterial = 0; FVector TileOrigin = FVector::ZeroVector;
	                TWeakObjectPtr<class UMaterialInterface> WallMat, GlassMat; TMap<int64, int32> Hits; TMultiMap<int64, TWeakObjectPtr<AActor>> Cracks; };
	/** U-20 ⑤ (user: "just debris that fade out"): physics chunks thrown on a LIVE breach / collapse, shrunk away after their life - cosmetic only */
	struct FDebris { TWeakObjectPtr<AActor> A; double Born = 0; double Life = 6; FVector Scale = FVector::OneVector; };
	TArray<FDebris> Debris;
	void SpawnDebris(UWorld* World, const FVector& At, const FVector& Away, int32 Count, float MinCm, float MaxCm, float Speed, UMaterialInterface* Mat, float Spread, bool bFlat = false);
	void TickDebris(UWorld* World);

	bool LoadIndex(const FString& Terrain);
	FTile* TileOf(UWorld* World, const FString& Terrain, FIntPoint T);
	FHouse* TakeOut(UWorld* World, const FString& Terrain, const FString& Key, const FIndexed& H);
	void Apply(UWorld* World, const FString& Terrain, const FHrotBuildingDamageIn& In, double SimNow);
	void Cut(FHouse& House, const FIndexed& H, const FHrotBreachIn& B, bool bLive, UWorld* World);
	void BreakGlass(FHouse& House, const FIndexed& H, int32 Panel, int32 Opening, bool bLive, UWorld* World);
	void Crack(FHouse& House, const FIndexed& H, const FHrotWindowIn& Wd, int32 Which, UWorld* World);
	bool Pane(const FIndexed& H, const FHouse& House, int32 Panel, int32 Opening, FVector& Corner, FVector& Along, FVector& Up, FVector& Normal, float& W, float& Ht) const;
	void Collapse(UWorld* World, FHouse& House, const FIndexed& H, bool bLive);
	void RebuildTile(UWorld* World, FTile& Tile);

	TWeakObjectPtr<UWorld> LastWorld; FString LastTerrain; double LastSimNow = 0;
	TArray<FHrotBuildingDamageIn> Pending;
	FString IndexTerrain;
	TMap<FString, FIndexed> Index;
	TMap<FIntPoint, FTile> Tiles;
	TMap<FString, FHouse> Houses;
};
