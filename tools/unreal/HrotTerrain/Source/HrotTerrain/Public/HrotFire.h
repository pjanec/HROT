// CE-4651 (U-17f) — FIRE as HROT reports it: anything burning (a vehicle, a house, a person, a tree), a burning patch of ground, and a
// flamethrower stream (a soldier's, a helicopter's, the dragon's). ⭐ ONE fire implementation: every fire in the IG is a UHrotBurning
// (on its actor) or an AHrotGroundFire (on the ground); the vehicle wreck burns through UHrotBurning too. State-driven, no tick except
// a flamethrower's while it fires (its landing point follows the stream).
#pragma once

#include "CoreMinimal.h"
#include "Components/ActorComponent.h"
#include "Components/SceneComponent.h"
#include "GameFramework/Actor.h"
#include "HrotFire.generated.h"

class UFXSystemComponent;
class UParticleSystemComponent;
class UDecalComponent;

/** The fire look, in one place (Realistic Starter VFX Pack Vol 2, Cascade) — measured in the fire lab (U-17f). */
struct FHrotFireLook
{
	static const TCHAR* FireBig;
	static const TCHAR* FireSmall;
	static const TCHAR* Smoke;
	static const TCHAR* Embers;
	static const TCHAR* Flamethrower;
	static const TCHAR* Scorch;
	/** ⭐ the size one P_Fire_Big covers at scale 1 (cm across) — fires are spread so each covers about this much */
	static constexpr float BigFireSpan = 250.f;
	/** below this size (cm, the larger horizontal side) a thing burns with the small fire */
	static constexpr float SmallThing = 120.f;
	/** fires per burning thing at most (a house is not 40 fires) */
	static constexpr int32 MaxFires = 6;
};

/**
 * ⭐ Puts an actor ON FIRE: fire spread over its size (its bounds), a smoke column above. Add it to any actor — or call
 * UHrotBurning::SetBurning(Actor, Intensity). Intensity 0 = out (the effects stop; their smoke trails away), 1 = fully ablaze.
 * `bAtAnchor`: one fire at `Anchor` (relative to the actor) of `AnchorScale` instead of spreading — a vehicle's engine deck.
 */
UCLASS(ClassGroup = HROT, meta = (BlueprintSpawnableComponent))
class HROTTERRAIN_API UHrotBurning : public UActorComponent
{
	GENERATED_BODY()

public:
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Fire") float Intensity = 1.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Fire") bool bSmoke = true;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Fire") bool bAtAnchor = false;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Fire") FVector Anchor = FVector::ZeroVector;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Fire") float AnchorScale = 1.f;
	/** the smoke column's size, in P_Smoke_A scales per unit of the thing's size (a wreck: 4 at a tank) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Fire") float SmokeScale = 4.f;

	UHrotBurning();

	/** (re)spawns the fire for the current settings; called by SetBurning, BeginPlay and OnRegister */
	UFUNCTION(BlueprintCallable, Category = "HROT|Fire") void Apply();
	/** puts out every effect it spawned */
	UFUNCTION(BlueprintCallable, Category = "HROT|Fire") void Extinguish();
	UFUNCTION(BlueprintCallable, Category = "HROT|Fire") int32 NumFires() const { return Fx.Num(); }

	/** ⭐ the one call: `Actor` burns at `Intensity` (0 = out). Finds or adds its UHrotBurning. */
	UFUNCTION(BlueprintCallable, Category = "HROT|Fire")
	static UHrotBurning* SetBurning(AActor* Actor, float NewIntensity);
	/** the same, as ONE fire at a point (relative to the actor) — a vehicle's engine deck */
	UFUNCTION(BlueprintCallable, Category = "HROT|Fire")
	static UHrotBurning* SetBurningAt(AActor* Actor, FVector RelativeAnchor, float Scale, float NewIntensity = 1.f);

protected:
	virtual void BeginPlay() override;
	virtual void OnUnregister() override;

private:
	UPROPERTY(Transient) TArray<TObjectPtr<UFXSystemComponent>> Fx;
	void SpawnOn(USceneComponent* Root, const TCHAR* Path, const FVector& RelLocation, float Scale);
};

/** ⭐ A burning patch of GROUND (napalm, a flamethrower's fuel, a burning field): fires spread over a disc of `Radius`, embers, a
 *  scorch underneath. HROT places, sizes and removes it like any entity. */
UCLASS()
class HROTTERRAIN_API AHrotGroundFire : public AActor
{
	GENERATED_BODY()

public:
	AHrotGroundFire();

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Fire") float Radius = 300.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Fire") float Intensity = 1.f;

	UFUNCTION(BlueprintCallable, Category = "HROT|Fire") void ApplyState();
	UFUNCTION(BlueprintCallable, Category = "HROT|Fire") void SetFire(float NewRadius, float NewIntensity);

	virtual void BeginPlay() override;

	UPROPERTY(VisibleAnywhere, Category = "HROT|Fire") TObjectPtr<USceneComponent> Root;
	UPROPERTY(VisibleAnywhere, Category = "HROT|Fire") TObjectPtr<UDecalComponent> Scorch;

private:
	UPROPERTY(Transient) TArray<TObjectPtr<UFXSystemComponent>> Fx;
	float ShownRadius = -1.f, ShownIntensity = -1.f;
};

/** ⭐ A FLAMETHROWER: attach it to any point (a soldier's weapon muzzle, a helicopter's nose, the dragon's mouth) — X = the stream's
 *  direction. bFiring = HROT's state. While firing, the stream's landing point (a trace along X, up to Range) burns. */
UCLASS(ClassGroup = HROT, meta = (BlueprintSpawnableComponent))
class HROTTERRAIN_API UHrotFlamethrower : public USceneComponent
{
	GENERATED_BODY()

public:
	UHrotFlamethrower();

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Fire") bool bFiring = false;
	/** the stream's size (1 = a man-portable flamethrower; the dragon's ×3) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Fire") float Size = 1.f;
	/** how far the stream reaches (cm, at Size 1) — where it lands burns */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Fire") float Range = 1500.f;
	/** the effect's own axis → X (the pack's emitter may point elsewhere: measured in the fire lab) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Fire") FRotator EffectRotation = FRotator::ZeroRotator;

	UFUNCTION(BlueprintCallable, Category = "HROT|Fire") void SetFiring(bool bNewFiring);
	/** where the stream lands now (the last trace) — a test reads it */
	UFUNCTION(BlueprintCallable, Category = "HROT|Fire") FVector LandingPoint() const { return Landing; }

	/** ⭐ adds a flamethrower to `Parent` at `Socket` (a weapon's "Muzzle", or NAME_None + an offset) */
	UFUNCTION(BlueprintCallable, Category = "HROT|Fire")
	static UHrotFlamethrower* Attach(USceneComponent* Parent, FName Socket, FTransform Offset, float NewSize = 1.f);

protected:
	virtual void BeginPlay() override;
	virtual void TickComponent(float DeltaTime, ELevelTick TickType, FActorComponentTickFunction* ThisTickFunction) override;
	virtual void OnUnregister() override;

private:
	UPROPERTY(Transient) TObjectPtr<UParticleSystemComponent> Stream;
	UPROPERTY(Transient) TObjectPtr<UParticleSystemComponent> LandingFire;
	FVector Landing = FVector::ZeroVector;
	bool bShown = false;
	void ApplyFiring();
};
