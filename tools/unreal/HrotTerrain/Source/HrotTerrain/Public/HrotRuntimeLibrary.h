// CE-4651 (U-18s) — what the image generator needs from the old terrain library AT RUNTIME (a packaged client): the person state and an
// immediate pose. The importers stay in the editor module (HrotTerrainEditor's UHrotTerrainLibrary, which forwards these two).
#pragma once

#include "CoreMinimal.h"
#include "Kismet/BlueprintFunctionLibrary.h"
#include "UObject/UObjectGlobals.h"
#include "HrotRuntimeLibrary.generated.h"

/** an asset loaded once and kept (rooted): the weapon presets' and the blasts' sounds. ONE definition - two file-local copies collided in a
 *  unity build (U-18s, measured) */
template <class T> T* HrotLoadRooted(const TCHAR* Path)
{
	T* A = LoadObject<T>(nullptr, Path);
	if (A && !A->IsRooted()) A->AddToRoot();
	return A;
}

UCLASS()
class HROTTERRAIN_API UHrotRuntimeLibrary : public UBlueprintFunctionLibrary
{
	GENERATED_BODY()

public:
	/** Advances a skeletal mesh's animation by Seconds (in Steps) and poses it NOW — an editor world does not reliably tick animation
	 *  (an unattended editor's viewport is not realtime), so a shot taken after this shows the evaluated pose. */
	UFUNCTION(BlueprintCallable, Category = "HROT|Runtime")
	static void PoseNow(class USkeletalMeshComponent* Mesh, float Seconds = 0.5f, int32 Steps = 10);

	/** Adds (or returns) the HROT person state component on an actor — an instance component, saved with the level. */
	UFUNCTION(BlueprintCallable, Category = "HROT|Runtime")
	static class UHrotPersonState* AddPersonState(AActor* Actor);
};
