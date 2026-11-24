// CE-4651 (U-16) — MANY shots, cheaply: every muzzle flash and tracer of a world drawn by TWO instanced meshes (one draw call each),
// instead of a Niagara system spawned per shot. The near-only extras (smoke, sparks, shells, impacts, a light) stay Niagara / lights,
// spawned only within range of the view and under a per-frame budget. UHrotWeaponFx::FireShot feeds it.
#pragma once

#include "CoreMinimal.h"
#include "Subsystems/WorldSubsystem.h"
#include "HrotShotRenderer.generated.h"

class UInstancedStaticMeshComponent;
class USoundBase;
class USoundAttenuation;
class USoundConcurrency;
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
	 *  `Size` scales the cards (1 = a rifle; a tank's main gun ~8); `Life` seconds (0 = FlashLife, a rifle's) */
	void AddFlash(const FTransform& Muzzle, float Size = 1.f, float Life = 0.f);
	/** A flash of light: a pooled point light for `Seconds` (a muzzle flash, an explosion). */
	void Flash(const FVector& At, float Intensity, float Radius, float Seconds);
	/** ⭐ A SMOKE PUFF of a trail (a missile's, U-17g): at `At`, `Size0` cm across at birth growing to `Size1`, fading over `Life`
	 *  seconds; `Brightness` 0..1 (a rocket motor's white smoke 0.9, a dirty one 0.4). Every puff of the world: one instanced mesh. */
	void AddPuff(const FVector& At, float Size0, float Size1, float Life, float Brightness = 0.8f);
	/** ⭐ A SOUND in the world (U-17k): heard from the view (each IG channel its own camera — the listener), arriving after the time
	 *  sound takes from `At` to the view (SpeedOfSound) — the flash first, the bang later. Null sound = nothing. */
	void PlaySound(USoundBase* Sound, const FVector& At, USoundAttenuation* Attenuation = nullptr, USoundConcurrency* Concurrency = nullptr,
	               float Volume = 1.f, float Pitch = 1.f);
	/** U-19c — a HELD sound (a burst's one clip): PlaySound's delay and falloff, plus a handle to cut it later; 0 = nothing (null / out of range) */
	int32 PlaySoundHeld(USoundBase* Sound, const FVector& At, USoundAttenuation* Attenuation = nullptr, USoundConcurrency* Concurrency = nullptr,
	                    float Volume = 1.f, float Pitch = 1.f);
	/** fade a held sound out over `Seconds`, scheduled after the SAME speed-of-sound delay as its start (a distant burst is cut when the cut is heard) */
	void FadeHeld(int32 Handle, float Seconds);
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "HROT|Shots") int32 HeldStarted = 0;
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "HROT|Shots") int32 HeldFaded = 0;
	/** Makes this world's audio device the ACTIVE one (unmuted). ⚠ measured (U-17k): the engine mutes every device but the active one,
	 *  and a play world's own device becomes active only when a game window takes focus — never in the lab's hidden Simulate (exact
	 *  digital silence, even a 2D sound). A standalone IG has one device; this is for the lab. */
	UFUNCTION(BlueprintCallable, Category = "HROT|Shots")
	bool ActivateWorldAudio();
	/** A tracer: a short glowing streak flying From → To at TracerSpeed; `Length` / `Width` cm (0 = TracerLength / TracerWidth — a
	 *  minigun's long streaks join into a line, a heavy MG's are fewer and thicker: per weapon, FHrotShotFx) */
	void AddTracer(const FVector& From, const FVector& To, float Length = 0.f, float Width = 0.f);
	/** U-18f — one tracer DASH for the next drawn frame only (a caller that computes its dashes per instant, as HROT's FireStreams does,
	 *  adds them every frame); `Strength` 0..1 fades it; drawn with the tracers (the same glowing cards, at least TracerMinAngle wide) */
	void AddDash(const FVector& From, const FVector& To, float Strength = 1.f);
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
	/** ⭐ a tracer is at least this wide ON SCREEN (radians: width ≥ distance × this) — it GLOWS, so it is seen far beyond its size;
	 *  ⚠ measured (U-17h): a minigun's 4 cm tracers 70 m away drew under a tenth of a pixel — nothing */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Shots") float TracerMinAngle = 0.0025f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Shots") float NearRange = 4000.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Shots") float ImpactRange = 15000.f;
	/** ⭐ measured (200 soldiers, ~970 rounds/s, near the view, 8 spawns a frame each): shell +0.1 ms, muzzle smoke +0.7 ms, IMPACT
	 *  +2.0 ms (Lyra's NS_ImpactConcrete) — the flash, tracer and lights cost nothing measurable. Hence budgets per second, per kind. */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Shots") float SmokePerSecond = 60.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Shots") float ShellPerSecond = 60.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Shots") float ImpactPerSecond = 30.f;
	/** explosions (UHrotExplosionFx): their effect + scorch within BlastRange, at most BlastPerSecond; the flash is always shown */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Shots") float BlastPerSecond = 20.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Shots") float BlastRange = 0.f;   // 0 = no limit: a blast is seen from any distance (user, 2026-11-23)
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Shots") int32 MaxLights = 4;
	/** trail puffs alive at most (the oldest go first) — each is 3 crossed cards */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Shots") int32 MaxPuffs = 6000;
	/** cm/s; 0 = no delay */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Shots") float SpeedOfSound = 34300.f;
	/** sounds further than this are not played at all (cm) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Shots") float SoundRange = 300000.f;
	/** sounds played so far (all time) — a test reads it */
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "HROT|Shots") int32 SoundsPlayed = 0;
	/** log every sound as it starts: game time, how far, how late (the speed of sound), the device's active sounds — a test reads it */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Shots") bool bLogSounds = false;
	/** U-18q — the clock the sound delays run on (DD-022 §3f rule 4: sim time during a render job); unset = the world's real time */
	TFunction<double()> TimeSource;
	/** which near extras are drawn at all (measurement: what each costs) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Shots") bool bNearSmoke = true;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Shots") bool bNearShell = true;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Shots") bool bImpacts = true;

	// ---- counters (the last frame) — read by the measurement -------------------------------------------------------------------
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "HROT|Shots") int32 LiveFlashes = 0;
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "HROT|Shots") int32 LiveTracers = 0;
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "HROT|Shots") int32 LiveDashes = 0;
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "HROT|Shots") int32 LivePuffs = 0;
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
	struct FFlash { FTransform Muzzle; float Born; float Seed; float Size; float Life; };
	struct FTracer { FVector From, Dir; float Range, Born, Length, Width; };
	TArray<FFlash> Flashes;
	TArray<FTracer> Tracers;
	struct FDash { FVector From, To; float Strength; };
	TArray<FDash> Dashes;
	struct FPuff { FVector At; float Born, Life, Size0, Size1, Seed, Bright; };
	TArray<FPuff> Puffs;
	struct FPendingSound { TWeakObjectPtr<USoundBase> Sound; TWeakObjectPtr<USoundAttenuation> Att; TWeakObjectPtr<USoundConcurrency> Con; FVector At; float When, Volume, Pitch, Asked; int32 Handle = 0; };
	struct FHeld { TWeakObjectPtr<class UAudioComponent> C; float Delay = 0.f; float FadeAt = -1.f, FadeSeconds = 0.f; float Born = 0.f; };
	TMap<int32, FHeld> Held;
	int32 NextHeld = 1;
	TArray<FPendingSound> PendingSounds;
	void PlayNow(const FPendingSound& S);
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
	UPROPERTY(Transient) TObjectPtr<UInstancedStaticMeshComponent> PuffIsm;

	bool EnsureComponents();
	float Now() const;
	void FlashLight(const FVector& At);
};
