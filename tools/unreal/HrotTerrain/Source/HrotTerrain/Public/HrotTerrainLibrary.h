// CE-4651 — the HROT terrain import for the Unreal image generator.
#pragma once

#include "CoreMinimal.h"
#include "Kismet/BlueprintFunctionLibrary.h"
#include "HrotTerrainLibrary.generated.h"

class ALandscape;

/**
 * Imports HROT's ground into the editor world EXACTLY: one landscape vertex per HROT height post.
 * Everything that decides the result (the 16-bit heights, the location, the scale, the component layout) is computed by the exporter
 * (scripts/terrain-to-unreal.py → <name>.height.r16 + <name>.frame.json "unreal") — this only hands it to ALandscape::Import, so the
 * frame is checkable outside Unreal and nothing is resampled here.
 */
UCLASS()
class HROTTERRAIN_API UHrotTerrainLibrary : public UBlueprintFunctionLibrary
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
};
