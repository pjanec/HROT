// CE-4651 (U-16) — MANY shots, cheaply: every muzzle flash and tracer of a world drawn by TWO instanced meshes (one draw call each),
// instead of a Niagara system spawned per shot. The near-only extras (smoke, sparks, shells, impacts, a light) stay Niagara / lights,
// spawned only within range of the view and under a per-frame budget. UHrotWeaponFx::FireShot feeds it.
#pragma once

#include "CoreMinimal.h"
#include "Subsystems/WorldSubsystem.h"
#include "HrotShotRenderer.generated.h"

class UInstancedStaticMeshComponent;
class UPointLightComponent;
class USceneComponent;

UCLASS()
class HROTTERRAIN_API UHrotShotRenderer : public UTickableWorldSubsystem
{
	GENERATED_BODY()

public:
	static UHrotShotRenderer* Get(const UObject* WorldContext);
	/** The renderer of the world `WorldContext` is in (scripts). */
	UFUNCTION(BlueprintCallable, Category = "HROT|Shots", meta = (WorldContext = "WorldContext"))
	static UHrotShotRenderer* ForWorld(UObject* WorldContext) { return Get(WorldContext); }

	/** A muzzle flash at the muzzle (X = the barrel's direction): 3 cards — one facing along the barrel, two along its sides.
	 *  `Size` scales the cards (1 = a rifle; a tank's main gun ~8) */
	void AddFlash(const FTransform& Muzzle, float Size = 1.f);
	/** A flash of light: a pooled point light for `Seconds` (a muzzle flash, an explosion). */
	void Flash(const FVector& At, float Intensity, float Radius, float Seconds);
	/** A tracer: a short glowing streak flying From → To at TracerSpeed. */
	void AddTracer(const FVector& From, const FVector& To);
	/** This weapon's round count, for the 1-in-N tracer rule. */
	int32 NextRound(const USceneComponent* Weapon);
	/** Where the view is (the player camera in play, the editor viewport otherwise) — for the near-only extras. */
	FVector ViewLocation() const;
	/** The near extras, each with its own RATE budget (per second — a per-frame budget scales with the frame rate). */
	enum class EExtra : uint8 { Smoke, Shell, Impact, Blast };
	/** True while `Kind`'s budget allows one more now; spends it. */
	bool TakeBudget(EExtra Kind);

	// ---- settings (cm, s) --------------------------------------------------------------------------------------------------------
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Shots") float FlashLife = 0.06f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Shots") float FlashLength = 45.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Shots") float FlashWidth = 24.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Shots") float TracerSpeed = 85000.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Shots") float TracerLength = 150.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Shots") float TracerWidth = 3.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Shots") int32 TracerEvery = 5;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Shots") float NearRange = 4000.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Shots") float ImpactRange = 15000.f;
	/** ⭐ measured (200 soldiers, ~970 rounds/s, near the view, 8 spawns a frame each): shell +0.1 ms, muzzle smoke +0.7 ms, IMPACT
	 *  +2.0 ms (Lyra's NS_ImpactConcrete) — the flash, tracer and lights cost nothing measurable. Hence budgets per second, per kind. */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Shots") float SmokePerSecond = 60.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Shots") float ShellPerSecond = 60.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Shots") float ImpactPerSecond = 30.f;
	/** explosions (UHrotExplosionFx): their effect + scorch within BlastRange, at most BlastPerSecond; the flash is always shown */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Shots") float BlastPerSecond = 20.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Shots") float BlastRange = 150000.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Shots") int32 MaxLights = 4;
	/** which near extras are drawn at all (measurement: what each costs) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Shots") bool bNearSmoke = true;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Shots") bool bNearShell = true;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Shots") bool bImpacts = true;

	// ---- counters (the last frame) — read by the measurement -------------------------------------------------------------------
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "HROT|Shots") int32 LiveFlashes = 0;
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "HROT|Shots") int32 LiveTracers = 0;
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "HROT|Shots") int32 NearSpawnedLastFrame = 0;
	/** ⭐ the renderer's own cost last frame (ms, game thread): firing (the test driver's FireShot calls) and the instance rebuild */
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "HROT|Shots") float FireMsLastFrame = 0.f;
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "HROT|Shots") float RebuildMsLastFrame = 0.f;

	/** ⭐ MEASUREMENT: every shooter fires its weapon (UHrotPersonState's) in bursts — `RoundsPerSecond` while in a burst of
	 *  `BurstSeconds`, then `PauseSeconds` quiet, the shooters out of step; each round "hits" 40 m along the barrel. Native, so a
	 *  measurement of hundreds of rounds a second measures the shots, not a script. */
	UFUNCTION(BlueprintCallable, Category = "HROT|Shots")
	void StartTestFire(const TArray<class UHrotPersonState*>& Shooters, float RoundsPerSecond = 10.f, float BurstSeconds = 1.f, float PauseSeconds = 1.f);
	UFUNCTION(BlueprintCallable, Category = "HROT|Shots")
	void StopTestFire();
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "HROT|Shots") int32 TestRoundsFired = 0;

	virtual void Tick(float DeltaTime) override;
	virtual TStatId GetStatId() const override;
	virtual bool IsTickableInEditor() const override { return true; }
	virtual ETickableTickType GetTickableTickType() const override { return ETickableTickType::Always; }
	virtual bool DoesSupportWorldType(const EWorldType::Type T) const override { return T == EWorldType::Game || T == EWorldType::PIE || T == EWorldType::Editor; }

private:
	struct FFlash { FTransform Muzzle; float Born; float Seed; float Size; };
	struct FTracer { FVector From, Dir; float Range, Born; };
	TArray<FFlash> Flashes;
	TArray<FTracer> Tracers;
	TMap<TWeakObjectPtr<const USceneComponent>, int32> Rounds;
	struct FLight { TObjectPtr<UPointLightComponent> C; float Until = 0.f; };
	TArray<FLight> Lights;
	int32 NearThisFrame = 0;
	float Tokens[4] = { 0.f, 0.f, 0.f, 0.f };

	UPROPERTY(Transient) TArray<TObjectPtr<class UHrotPersonState>> TestShooters;
	TArray<float> TestCarry;
	float TestRate = 0.f, TestBurst = 1.f, TestPause = 1.f;
	UPROPERTY(Transient) TObjectPtr<AActor> Holder;
	UPROPERTY(Transient) TObjectPtr<UInstancedStaticMeshComponent> FlashIsm;
	UPROPERTY(Transient) TObjectPtr<UInstancedStaticMeshComponent> TracerIsm;

	bool EnsureComponents();
	float Now() const;
	void FlashLight(const FVector& At);
};
