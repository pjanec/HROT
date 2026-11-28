// CE-4651 (U-16) — one SHOT drawn: muzzle flash, tracer, shell, impacts. HROT reports shots as events (who fired, where it hit);
// the image generator calls FireShot once per shot. The effects are Lyra's Niagara systems (/Game/Effects, copied as they are),
// driven the way Lyra's own B_WeaponFire / B_WeaponImpacts drive them: their User.* parameters, no Lyra code.
#pragma once

#include "CoreMinimal.h"
#include "Kismet/BlueprintFunctionLibrary.h"
#include "HrotWeaponFx.generated.h"

class UNiagaraSystem;
class UStaticMesh;
class USoundBase;
class USoundAttenuation;
class USoundConcurrency;
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
	// ---- per weapon (U-17h): the look of a rifle vs a minigun vs a heavy MG -------------------------------------------------------
	/** 1 round in N is a tracer (0 = the renderer's TracerEvery) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Weapon") int32 TracerEvery = 0;
	/** the tracer streak (cm; 0 = the renderer's) — a minigun's are long, so at 50 rounds a second they join into a line */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Weapon") float TracerLength = 0.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Weapon") float TracerWidth = 0.f;
	/** the muzzle flash's size (1 = a rifle) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Weapon") float FlashSize = 1.f;
	/** a DUST PUFF where a round lands (cm across at birth, 0 = none) — instanced (UHrotShotRenderer::AddPuff), seen at any range */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Weapon") float DustSize = 0.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Weapon") float DustLife = 1.5f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Weapon") float DustBrightness = 0.5f;
	// ---- sound (U-17k): heard from the view, delayed by the distance (UHrotShotRenderer::PlaySound) ------------------------------
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Weapon") TObjectPtr<USoundBase> Sound;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Weapon") TObjectPtr<USoundAttenuation> SoundAttenuation;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Weapon") TObjectPtr<USoundConcurrency> SoundConcurrency;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Weapon") float SoundVolume = 1.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Weapon") float SoundPitch = 1.f;
	/** where a round lands */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Weapon") TObjectPtr<USoundBase> ImpactSound;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Weapon") TObjectPtr<USoundAttenuation> ImpactAttenuation;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Weapon") TObjectPtr<USoundConcurrency> ImpactConcurrency;
};

UCLASS()
class HROTTERRAIN_API UHrotWeaponFx : public UBlueprintFunctionLibrary
{
	GENERATED_BODY()

public:
	/** Lyra's rifle effects from /Game/Effects (null members when the folder is missing). */
	UFUNCTION(BlueprintCallable, Category = "HROT|Weapon")
	static FHrotShotFx LyraRifleFx();

	/** One shot from `Weapon` (the weapon mesh in the person's hand): the flash at its muzzle and 1 round in N a tracer (instanced,
	 *  UHrotShotRenderer); near the view only and under a per-frame budget, Lyra's smoke / sparks / shell and the impact per hit
	 *  (Hits / HitNormals in world space; no hits = a miss: flash, shell, a tracer out to 300 m, no impact). Returns the muzzle's world location. */
	UFUNCTION(BlueprintCallable, Category = "HROT|Weapon")
	static FVector FireShot(USceneComponent* Weapon, const FHrotShotFx& Fx, const TArray<FVector>& Hits, const TArray<FVector>& HitNormals);

	/** The same from an explicit MUZZLE transform (X = the barrel) — a vehicle's gun (AHrotVehicle::MuzzleTransform): `Weapon` is
	 *  the component it rides on (its round count; the near extras attach to it when it has `Fx.MuzzleSocket`). */
	UFUNCTION(BlueprintCallable, Category = "HROT|Weapon")
	static FVector FireShotAt(USceneComponent* Weapon, FTransform Muzzle, const FHrotShotFx& Fx, const TArray<FVector>& Hits, const TArray<FVector>& HitNormals);

	/** A helicopter's MINIGUN (M134, ~50 rounds/s): every round a tracer, long streaks that join into an almost steady line, dust */
	UFUNCTION(BlueprintCallable, Category = "HROT|Weapon")
	static FHrotShotFx MinigunFx();
	/** A HEAVY MACHINE GUN (M2 / DShK, ~10 rounds/s): 1 in 5 a tracer, a few a second, thick and bright; a big flash, big dust */
	UFUNCTION(BlueprintCallable, Category = "HROT|Weapon")
	static FHrotShotFx HeavyMgFx();

	/** Where the muzzle is: the socket if the mesh has it, else the far end of the mesh's longest local axis (+ direction). */
	UFUNCTION(BlueprintCallable, Category = "HROT|Weapon")
	static FTransform MuzzleTransform(USceneComponent* Weapon, FName Socket);
};
