// CE-4651 — the HROT terrain import for the Unreal image generator. U-18s: the EDITOR module (HrotTerrainEditor) — a packaged client has none of it;
// PoseNow / AddPersonState forward to the runtime UHrotRuntimeLibrary (kept here so the scripts keep calling unreal.HrotTerrainLibrary).
#pragma once

#include "CoreMinimal.h"
#include "Kismet/BlueprintFunctionLibrary.h"
#include "HrotTerrainLibrary.generated.h"

class ALandscape;
class UStaticMesh;
class UMaterialInterface;

/**
 * Imports HROT's ground into the editor world EXACTLY: one landscape vertex per HROT height post.
 * Everything that decides the result (the 16-bit heights, the location, the scale, the component layout) is computed by the exporter
 * (scripts/terrain-to-unreal.py → <name>.height.r16 + <name>.frame.json "unreal") — this only hands it to ALandscape::Import, so the
 * frame is checkable outside Unreal and nothing is resampled here.
 */
UCLASS()
class HROTTERRAINEDITOR_API UHrotTerrainLibrary : public UBlueprintFunctionLibrary
{
	GENERATED_BODY()

public:
	/**
	 * @param R16Path               little-endian uint16 heights, SizeX × SizeY, the NORTH row first (Unreal +Y points south)
	 * @param SizeX, SizeY          vertices; each must be ComponentsX/Y × QuadsPerSection × SectionsPerComponent + 1
	 * @param QuadsPerSection       7 · 15 · 31 · 63 · 127 · 255
	 * @param SectionsPerComponent  1 or 2
	 * @param LocationCm            the north-west vertex, in Unreal cm; Z = the height of the uint16 value 32768
	 * @param ScaleCm               the actor scale: X / Y = the post spacing in cm; Z = cm per 128 uint16 steps
	 * @param Label                 the landscape actor's label
	 * @return the landscape, or null (the reason is logged under LogHrotTerrain)
	 */
	UFUNCTION(BlueprintCallable, Category = "HROT|Terrain")
	static ALandscape* ImportHeights(const FString& R16Path, int32 SizeX, int32 SizeY, int32 QuadsPerSection, int32 SectionsPerComponent,
		FVector LocationCm, FVector ScaleCm, const FString& Label);

	/**
	 * Spawns one actor holding a hierarchical instanced mesh with every instance of a scatter file — trees, bushes, rocks
	 * (scripts/terrain-to-unreal.py writes it from OUR forest / open areas).
	 * @param BinPath  little-endian float32 records of 6: X, Y, Z (Unreal cm, the base of the instance), yaw (degrees), width scale, height scale
	 * @param Mesh     the mesh to instance; Material (optional) overrides its slot 0
	 * @param CullDistanceCm  instances beyond it are not drawn (0 = never culled)
	 * @param MeshScale       multiplies each record's (width, width, height) scale — sizes a unit mesh to a real tree
	 * @param PivotUpCm       the mesh's base sits this far BELOW its pivot (a centred unit cone: 50) — lifted by the instance's height scale
	 * @return the actor, or null (the reason is logged under LogHrotTerrain)
	 */
	UFUNCTION(BlueprintCallable, Category = "HROT|Terrain")
	static AActor* ScatterInstances(const FString& BinPath, UStaticMesh* Mesh, UMaterialInterface* Material, const FString& Label,
		float CullDistanceCm, FVector MeshScale = FVector(1, 1, 1), float PivotUpCm = 0.f);

	/**
	 * Builds a static mesh asset from a mesh file of the exporter (OUR building shapes — exact, nothing approximated) and places it.
	 * @param BinPath    int32 vertex count, int32 triangle count; float32 X, Y, Z per vertex (Unreal cm, relative to LocationCm);
	 *                   int32 a, b, c per triangle; uint8 material slot per triangle
	 * @param AssetPath  e.g. /Game/Hrot/Buildings/SM_Stechovice_3_7 (created or replaced)
	 * @param Materials  one per slot (e.g. 0 = wall, 1 = roof)
	 */
	/**
	 * Makes a Blueprint (an actor prefab) of a placed actor, with every component it carries (a crowd person: body + outfit parts on
	 * a leader pose) — so it can be spawned any number of times in another level. The actor stays as it is (not replaced).
	 * @return the Blueprint's generated class, or null
	 */
	UFUNCTION(BlueprintCallable, Category = "HROT|Terrain")
	static UClass* BlueprintFromActor(AActor* Actor, const FString& AssetPath);

	/** Advances a skeletal mesh's animation by Seconds (in Steps) and poses it NOW — an editor world does not reliably tick animation
	 *  (an unattended editor's viewport is not realtime), so a shot taken after this shows the evaluated pose. */
	UFUNCTION(BlueprintCallable, Category = "HROT|Terrain")
	static void PoseNow(class USkeletalMeshComponent* Mesh, float Seconds = 0.5f, int32 Steps = 10);

	/** Adds (or returns) the HROT person state component on an actor — an instance component, saved with the level. */
	UFUNCTION(BlueprintCallable, Category = "HROT|Terrain")
	static class UHrotPersonState* AddPersonState(AActor* Actor);

	UFUNCTION(BlueprintCallable, Category = "HROT|Terrain")
	static AActor* BuildMesh(const FString& BinPath, const FString& AssetPath, const TArray<UMaterialInterface*>& Materials,
		FVector LocationCm, const FString& Label);
};
