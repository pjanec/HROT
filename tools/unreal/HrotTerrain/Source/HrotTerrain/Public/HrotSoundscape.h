// CE-4651 (U-18n) — THE SOUNDSCAPE: what makes the scene sound real besides the shots — the ambience beds, the engines, the animals.
//
// The sounds are two libraries cut by scripts/unreal/sound_cut.py and imported by hrot_sound_import.py — loops are <name>, one-shot
// variants <name>_01 .. _NN (Pick chooses one at random, from ONE library):
//   /Game/Hrot/AudioPro  the PURCHASED Military Warfare SFX Pack (combat: weapons, hits, cracks, explosions, military vehicles, boots) —
//                        PRIVATE: never published, shipped only cooked inside a packaged IG; it wins wherever it has a set
//   /Game/Hrot/Audio     the free Sonniss GDC 2023 / 2024 bundles (ambience, animals, civilian vehicles) — the fallback
//
//   AMBIENCE  stereo beds around the listener (the view), crossfaded once a second by what is around it:
//             town (buildings and traffic within 80 m) · forest (foliage within 40 m) · open country (neither) × day / night (the sun's
//             elevation) × rain (RainAmount; DPS carries no weather yet) × wind (WindAmount); faded down as the view climbs above 60 m.
//   ENGINES   the nearest MaxEngines vehicles (AHrotVehicle) each get an idle and a run loop, attached, crossfaded and pitched by the
//             speed the vehicle actually moves at; the class from the kit (tracked / heavy wheeled / light / motorcycle / helicopter /
//             propeller); silent when destroyed. The dragon: a wing whoosh on each flap.
//   ANIMALS   calls at random intervals for the animals near the listener, by HROT's family (AHrotAnimal::Family); a horse that
//             trots or gallops is heard doing it.
//   FIRES     a ground fire or a burning wreck near the listener crackles; a firing flamethrower roars.
//   WRECKS    a voiced vehicle that is destroyed goes off once (a car explosion) and falls silent.
//   HITS      ImpactFor(material) — the surface's hit, for UHrotDpsWorld's impacts.
//
// Console: hrot.snd.Rain 0..1 · hrot.snd.Wind 0..1 · hrot.snd.Night -1 auto / 0 / 1 · hrot.snd.Ambience (volume) · hrot.snd.Engines.
// ⚠ Every time here is the WALL clock (the live IG); the film soundtrack on sim time is DD-022's (the gateway's design), not built.
#pragma once

#include "CoreMinimal.h"
#include "Subsystems/WorldSubsystem.h"
#include "HrotSoundscape.generated.h"

class UAudioComponent;
class USoundBase;
class USoundAttenuation;
class AHrotVehicle;

UCLASS()
class HROTTERRAIN_API UHrotSoundscape : public UTickableWorldSubsystem
{
	GENERATED_BODY()

public:
	static UHrotSoundscape* Get(const UObject* WorldContext);
	UFUNCTION(BlueprintCallable, Category = "HROT|Sound", meta = (WorldContext = "WorldContext"))
	static UHrotSoundscape* ForWorld(UObject* WorldContext) { return Get(WorldContext); }

	/** a one-shot of the set (<Set>_01 .. _NN, one at random), or the loop <Set> itself; null when the library lacks it */
	USoundBase* Pick(const FString& Set);
	/** U-19h: ONE-SHOT, at join - loads EVERY sound of both libraries (/Game/Hrot/AudioPro, /Game/Hrot/Audio) into the (strong) cache, so no first-time sound loads
	 *  mid-play (measured: 1-3 s FlushAsyncLoading hitches at the first rifle / body / dirt / beep). Returns the number loaded; logs it and the seconds. */
	int32 PreloadAll();
	/** the library has the set (a variant _01 or the single sound) — quietly: no LibraryMissing */
	bool Has(const FString& Set);
	/** the loop / single sound <Name> */
	USoundBase* One(const FString& Name) { return Load(Name, true); }
	/** the hit of a surface by HROT's material word (metal, armour, stone / concrete / brick, wood, glass, flesh); null = the default */
	USoundBase* ImpactFor(const FString& Material);
	/** a soldier's footstep at `Foot` (heard within 30 m): `Loud` 0..1, louder running */
	void Step(const FVector& Foot, bool bRun, float Loud = 1.f);
	/** the falloff for a sound of this reach (metres): full volume within `Inner`, gone at `Reach` */
	/** a falloff: full to Inner m, silent beyond Reach m. ⭐ U-19h (live 2026-11-23, "shots across the river sound as loud as close ones"):
	 *  by default the INVERSE-DISTANCE law (-6 dB per doubling from Inner) as a custom curve — Unreal's NaturalSound is linear in dB over the
	 *  whole falloff, so a 10 m / 1500 m rifle lost only 7.7 dB at 200 m (a real shot loses 26). bNatural keeps that curve with AtMaxDb at
	 *  Reach: speech (-30, tuned by ear 2026-11-22: faint beyond 40 m) */
	USoundAttenuation* Falloff(float Inner, float Reach, float AtMaxDb = -60.f, bool bNatural = false);
	/** the inverse-distance gain (0..1) at Distance m: 1 within Inner, Inner / Distance beyond, 0 at and past Reach (pure, tested) */
	static float InverseDistanceGain(float Distance, float Inner, float Reach)
	{
		if (Distance >= Reach) return 0.f;
		return Distance <= Inner ? 1.f : Inner / Distance;
	}
	/** U-18q — a render job's soundtrack: every falloff attenuates (distance, LPF) but does NOT spatialize. ⚠ measured: on Unreal's
	 *  non-real-time device every SPATIALIZED sound mixes to digital zero (Epic UE-381932, "spatialized audio … the MRQ audio file is silent";
	 *  the platform reports an 8-speaker layout on a 2-channel device) while attenuated-only sounds play — so the film gets distance and
	 *  air absorption but no stereo panning yet */
	void SetSpatialize(bool bOn);

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Sound") bool bEnabled = true;
	/** 0 by default: the free library's ambience beds are held back for now (user, 2026-11-21: "put those sounds aside for now (will be
	 *  great for ambient but later)"); hrot.snd.Ambience 1 turns them on */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Sound") float AmbienceVolume = 0.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Sound") float EngineVolume = 1.f;
	static constexpr float StillSpeed = 0.5f;
	static constexpr float StillSeconds = 2.f;
	/** U-19g (PURE): does a vehicle get an engine voice? A ground vehicle with nobody aboard that has stood still (< StillSpeed m/s) for StillSeconds does not
	 *  (an empty parked HMMWV must not idle); crewed, moving or flying: yes */
	static bool EngineWanted(bool bGround, bool bCrewed, float StillForSeconds) { return !bGround || bCrewed || StillForSeconds < StillSeconds; }

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Sound") float AnimalVolume = 1.f;
	/** 0..1 — no weather on the wire yet: set by hand / the console */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Sound") float RainAmount = 0.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Sound") float WindAmount = 0.35f;
	/** -1 = by the sun, 0 day, 1 night */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Sound") float NightOverride = -1.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Sound") int32 MaxEngines = 12;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Sound") float EngineReach = 400.f;   // m (aircraft 4x)
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Sound") float AnimalReach = 60.f;    // m

	// ---- what it measured last (a probe / the harness reads these) ----------------------------------------------------------------
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "HROT|Sound") int32 BuildingsNear = 0;
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "HROT|Sound") int32 TreesNear = 0;
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "HROT|Sound") int32 VehiclesNear = 0;
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "HROT|Sound") float Town = 0.f;
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "HROT|Sound") float Forest = 0.f;
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "HROT|Sound") float Night = 0.f;
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "HROT|Sound") float HeightAboveGround = 0.f;
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "HROT|Sound") int32 EnginesPlaying = 0;
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "HROT|Sound") int32 AnimalCalls = 0;
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "HROT|Sound") int32 Steps = 0;
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "HROT|Sound") int32 LibraryMissing = 0;
	/** one line: the beds and their gains, the counts */
	UFUNCTION(BlueprintCallable, Category = "HROT|Sound") FString Describe() const;

	virtual void Tick(float DeltaTime) override;
	virtual TStatId GetStatId() const override;
	virtual bool IsTickableInEditor() const override { return true; }
	virtual void Deinitialize() override;

private:
	struct FBed { const TCHAR* Name; float Want = 0, Have = 0; TWeakObjectPtr<UAudioComponent> C; };
	TArray<FBed> Beds;
	enum class EEngine : uint8 { Tracked, Heavy, Jeep, Light, Motorcycle, Helicopter, Propeller, Jet, Dragon, None };
	struct FVoice
	{
		TWeakObjectPtr<AHrotVehicle> V; EEngine Kind = EEngine::None;
		TWeakObjectPtr<UAudioComponent> Run, Idle; FVector Last = FVector::ZeroVector; float Speed = 0; bool bSeen = false; double NextFlap = 0;
	};
	TArray<FVoice> Voices;
	struct FStill { FVector Last = FVector::ZeroVector; float For = 99.f; bool bHave = false; };   // U-19g: how long a vehicle has stood still (new: assumed still until seen moving)
	TMap<TWeakObjectPtr<AHrotVehicle>, FStill> Stills;
	struct FCaller { double NextAt = 0; FVector Last = FVector::ZeroVector; TWeakObjectPtr<UAudioComponent> Trot; };
	TMap<TWeakObjectPtr<AActor>, FCaller> Callers;
	UPROPERTY(Transient) TMap<FString, TObjectPtr<USoundBase>> Cache;
	TSet<FString> Missing, Reported;
	UPROPERTY(Transient) TArray<TObjectPtr<USoundAttenuation>> Falloffs;
	bool bSpatialize = true;
	/** every component this made (the beds, the engines, the trots) — held so nothing collects them while they play */
	UPROPERTY(Transient) TArray<TObjectPtr<UAudioComponent>> Owned;
	UAudioComponent* Hold(UAudioComponent* C) { if (C) Owned.Add(C); return C; }
	void Drop(TWeakObjectPtr<UAudioComponent>& C);
	double NextProbe = 0, NextPick = 0, NextAnimals = 0, NextScan = 0;
	/** what can stand around the listener, scanned every 15 s (⚠ measured: the city's meshes answer no physics overlap) */
	struct FStand { TWeakObjectPtr<const UPrimitiveComponent> C; bool bTree = false; bool bInstanced = false; };
	TArray<FStand> Stands;
	void ScanStands();
	FString StandClasses;
	/** a library sound; `bCount` = a miss is reported (Pick's probe past the last variant is not) */
	USoundBase* Load(const FString& Name, bool bCount);
	/** one library's sound, quietly (null = not there) */
	USoundBase* LoadAt(const TCHAR* Root, const FString& Name);
	/** the library a set comes from: /Game/Hrot/AudioPro (the purchased pack — private) when it has it, else /Game/Hrot/Audio */
	const TCHAR* LibraryOf(const FString& Set);
	FRandomStream Rng{ 4651 };

	void ProbeSurroundings(const FVector& At);
	void MixBeds(float Dt);
	void UpdateEngines(const FVector& At, float Dt, double Now);
	void UpdateAnimals(const FVector& At, double Now);
	/** fires (ground fires, burning wrecks) crackle; a firing flamethrower roars */
	void UpdateFires(const FVector& At);
	struct FLoop { TWeakObjectPtr<UAudioComponent> C; bool bSeen = false; };
	TMap<TWeakObjectPtr<UObject>, FLoop> Loops;
	static EEngine EngineOf(const AHrotVehicle* V);
	static EEngine EngineOfKit(const FString& KitName, const FString& Dis = FString());
public:
	/** U-19h 20: the engine class a kit name gets (Helicopter / Propeller / Jet / Tracked ...), for a rail */
	static const TCHAR* EngineName(const FString& KitName, const FString& Dis = FString());
private:
};
