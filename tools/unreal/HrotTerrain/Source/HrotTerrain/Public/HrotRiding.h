// CE-4651 (U-17j) — RIDING: a person on an animal (the deer for a horse) or on the dragon, in a SEAT — HROT says who sits where; the
// IG attaches the rider to the mount (following its gait: an animal's seat rides on a spine bone) and plays the seat's sitting clip.
// Seats are part of what the mount IS (UHrotAnimalKind::Seats, UHrotVehicleKit::Seats), so every rider of a kind sits the same way.
#pragma once

#include "CoreMinimal.h"
#include "Kismet/BlueprintFunctionLibrary.h"
#include "HrotRiding.generated.h"

class UAnimSequence;

USTRUCT(BlueprintType)
struct HROTTERRAIN_API FHrotSeat
{
	GENERATED_BODY()
	/** "driver", "passenger" … */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Riding") FName Name;
	/** the bone of the mount's skinned body the seat rides on (an animal's spine: the rider rises and falls with its stride); None = the
	 *  mount's root (a kit's hull) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Riding") FName Bone;
	/** where the RIDER'S ACTOR goes: its location from the bone (or the root) in the MOUNT ACTOR's frame (X forward, Z up), its rotation
	 *  relative to the mount actor — a person actor's origin is its hips (the capsule centre), so this is the saddle */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Riding") FTransform Offset;
	/** the rider's clip in this seat (sitting); null = leave the rider's own */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Riding") TObjectPtr<UAnimSequence> Pose;
};

UCLASS()
class HROTTERRAIN_API UHrotRiding : public UBlueprintFunctionLibrary
{
	GENERATED_BODY()

public:
	/** the seats of a mount (an AHrotAnimal's kind, an AHrotVehicle's kit); empty when it has none */
	UFUNCTION(BlueprintCallable, Category = "HROT|Riding")
	static TArray<FHrotSeat> SeatsOf(AActor* Mount);

	/** ⭐ `Rider` (a person) sits in seat `Seat` of `Mount`: attached (it follows the mount, and the seat's bone), its person state plays
	 *  the seat's pose. False when the mount has no such seat. */
	UFUNCTION(BlueprintCallable, Category = "HROT|Riding")
	static bool SitOn(AActor* Rider, AActor* Mount, int32 Seat);

	/** ⭐ EMBARKED (U-17m: troops INSIDE an APC — unlike a rider, not seen): attached to the vehicle (it travels with it) and HIDDEN,
	 *  its animation stopped — an embarked soldier costs nothing to draw. How HROT describes embarkation is still to come; this is
	 *  the IG's half. */
	UFUNCTION(BlueprintCallable, Category = "HROT|Riding")
	static bool Embark(AActor* Person, AActor* Vehicle);
	/** out again: detached where it is and shown (HROT places it next) */
	UFUNCTION(BlueprintCallable, Category = "HROT|Riding")
	static void Disembark(AActor* Person);

	/** the rider gets off: detached where it is (HROT places it next) */
	UFUNCTION(BlueprintCallable, Category = "HROT|Riding")
	static void GetOff(AActor* Rider);
};
