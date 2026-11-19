// CE-4651 (U-16) — one SHOT drawn: muzzle flash, tracer, shell, impacts. HROT reports shots as events (who fired, where it hit);
// the image generator calls FireShot once per shot. The effects are Lyra's Niagara systems (/Game/Effects, copied as they are),
// driven the way Lyra's own B_WeaponFire / B_WeaponImpacts drive them: their User.* parameters, no Lyra code.
#pragma once

#include "CoreMinimal.h"
#include "Kismet/BlueprintFunctionLibrary.h"
#include "HrotWeaponFx.generated.h"

class UNiagaraSystem;
class UStaticMesh;
class USceneComponent;

/** The effects of one weapon type (Lyra's rifle set by default — see HrotWeaponFx.cpp). Any may be null: that part is skipped. */
USTRUCT(BlueprintType)
struct HROTTERRAIN_API FHrotShotFx
{
	GENERATED_BODY()

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Weapon") TObjectPtr<UNiagaraSystem> MuzzleFlash;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Weapon") TObjectPtr<UNiagaraSystem> Tracer;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Weapon") TObjectPtr<UNiagaraSystem> ShellEject;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Weapon") TObjectPtr<UStaticMesh> ShellMesh;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Weapon") TObjectPtr<UNiagaraSystem> Impact;
	/** the weapon mesh's socket at the barrel's end; without it the muzzle is the far end of the mesh's longest axis */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Weapon") FName MuzzleSocket = TEXT("Muzzle");
};

UCLASS()
class HROTTERRAIN_API UHrotWeaponFx : public UBlueprintFunctionLibrary
{
	GENERATED_BODY()

public:
	/** Lyra's rifle effects from /Game/Effects (null members when the folder is missing). */
	UFUNCTION(BlueprintCallable, Category = "HROT|Weapon")
	static FHrotShotFx LyraRifleFx();

	/** One shot from `Weapon` (the weapon mesh in the person's hand): the flash at its muzzle, a tracer and an impact per hit
	 *  (Hits / HitNormals in world space; no hits = a miss: flash, shell, a tracer out to 300 m, no impact). Returns the muzzle's world location. */
	UFUNCTION(BlueprintCallable, Category = "HROT|Weapon")
	static FVector FireShot(USceneComponent* Weapon, const FHrotShotFx& Fx, const TArray<FVector>& Hits, const TArray<FVector>& HitNormals);

	/** Where the muzzle is: the socket if the mesh has it, else the far end of the mesh's longest local axis (+ direction). */
	UFUNCTION(BlueprintCallable, Category = "HROT|Weapon")
	static FTransform MuzzleTransform(USceneComponent* Weapon, FName Socket);
};
