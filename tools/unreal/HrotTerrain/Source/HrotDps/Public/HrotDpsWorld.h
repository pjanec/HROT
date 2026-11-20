// CE-4651 (U-18b) — the DPS WORLD: the station's entities as Unreal actors (SimNetGateway's DPS client guide, "How DPS works").
//
//   Entity (TRANSIENT_LOCAL)      ─┐
//   EntityState (pose, damage)    ─┼─► FEnt (one per EntityId) ──► actor: AHrotVehicle (a kit), a person stand-in, or a box
//   Session (rate, world epoch)   ─┘        │ the pose: HrotDpsMotion (dead reckoning on the SIM CLOCK, correction blend)
//
// ⭐ one instance per game / PIE world; Start() joins the station. A not-ALIVE Entity or EntityState sample = the entity is gone
// (§3.11); a new Session.WorldEpoch = drop everything (the next samples rebuild it).
// ⭐ Which model a type gets: KitsByType (the contract's type id) → KitsByDis (the DIS type, longest dotted prefix wins) →
// KitsByDomain → a plain box, so nothing the station sends is ever invisible.
// ⭐ U-18c — SHOTS AND BLASTS, drawn from what the station SENDS (the guide: "where DPS carries the result, Unreal draws what it
// receives"): Effect MuzzleFlash → a flash at the anchor's muzzle · Effect Explosion → UHrotExplosionFx by size class · Fire from a
// machine gun / autocannon → a tracer + the gun's sound (FireStreams: tracers are the client's rule, not on the wire) · Fire from a tank
// gun → the vehicle's own main-gun blast (its flash replaces the Effect's) · Articulation turret 0 → AHrotVehicle::SetAim.
// Detonation is counted only (its blast and mark arrive as Effect). What is drawn is counted per kind (EventCounts) for a test.
// ⭐ U-18f — VEHICLES AND AIR FIGHTING: a vehicle's machine-gun tracer and flash at ITS kit's gun (it follows the turret); helicopter
// rockets as missiles of their type's kind; the RPG TUBE on a soldier's shoulder while his ActiveSlot selects a rocket mount (TkbType's
// WeaponSuiteDto); VehicleLook (kill / burning = destroyed, smoking = damaged); a wreck fire off a vehicle = a ground fire; puffs tunable.
// ⭐ U-18e — THE FIREFIGHT: a soldier's shot (his flash at his rifle, the kick), impacts (HROT's puff by its effect type; the surface's
// sound and chips), hits on people (a flinch), rounds in flight (a rocket = the U-17g missile with its motor and trail; a grenade or a
// shell = a small body).
// ⭐ U-18d — PEOPLE: with a PersonKit, a Person entity is the kit's body driven by PersonState + PersonAim through HrotDpsPeople
// (stance and its timed transition, gait from speed, posture on the upper body, HROT's aim twist, death, hidden inside a vehicle);
// without one, the stand-in mesh. ⏳ not yet: seats and carrying in place, gestures beyond wave / throw, the head turn, VehicleLook, ground marks and wreck flame / smoke effects, rounds and animals
// (Domain Munition / Animal are counted, not drawn), the delayed mode for traffic.
#pragma once

#include "CoreMinimal.h"
#include "Subsystems/WorldSubsystem.h"
#include "HrotDpsMotion.h"
#include "HrotDpsPeople.h"
#include "HrotWeaponFx.h"
#include "HrotDpsWorld.generated.h"

class UHrotVehicleKit;
class UHrotPersonKit;
class UHrotMissileKind;
class USkeletalMesh;
class UAnimationAsset;

UCLASS()
class HROTDPS_API UHrotDpsWorld : public UTickableWorldSubsystem
{
	GENERATED_BODY()

public:
	static UHrotDpsWorld* Get(const UObject* WorldContext);
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS", meta = (WorldContext = "WorldContext"))
	static UHrotDpsWorld* ForWorld(UObject* WorldContext) { return Get(WorldContext); }

	// ── what a type looks like (set before Start) ──
	/** the contract's type id (Entity.TypeId) → its vehicle kit */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") TMap<int64, TSoftObjectPtr<UHrotVehicleKit>> KitsByType;
	/** a DIS type prefix "kind.domain.country.category.subcategory.specific.extra" (e.g. "1.1.222.1") → its kit; the longest match wins */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") TMap<FString, TSoftObjectPtr<UHrotVehicleKit>> KitsByDis;
	/** the contract's domain ("Ground", "Air", "Surface", "Subsurface", "Other") → the kit when nothing more specific matches */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") TMap<FString, TSoftObjectPtr<UHrotVehicleKit>> KitsByDomain;
	/** ⭐ the body every person gets (U-18d): its clips by role, driven from PersonState / PersonAim */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") TSoftObjectPtr<UHrotPersonKit> PersonKit;
	/** rounds in flight (U-18e): a munition type (Round.MunitionTypeId) → its missile kind; a round with a motor or a smoke trail and no
	 *  entry gets DefaultMissileKind; every other round is a small body of its MunitionLook size */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") TMap<int64, TSoftObjectPtr<UHrotMissileKind>> MissileKindsByType;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") TSoftObjectPtr<UHrotMissileKind> DefaultMissileKind;
	/** without a PersonKit: this mesh, playing this clip (looped) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") TSoftObjectPtr<USkeletalMesh> PersonMesh;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") TSoftObjectPtr<UAnimationAsset> PersonClip;
	/** the person mesh's yaw offset (deg): a UE character mesh faces +Y, the contract's body faces +x */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") float PersonMeshYaw = -90.f;
	/** U-18f — the impact puff, EXAGGERATED a little over HROT's (0.8 m, 0.35 s, sand-coloured: faithful but hard to see on sand —
	 *  user: "exaggerate a little bit to make it better visible"): its size and life factors, the shortest life, and its brightness */
	// ⚠ measured: ×1.6 and a darker 0.4 came out as faint bluish dots (a dark puff renders more transparent, not more visible)
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") float PuffSizeScale = 2.5f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") float PuffLifeScale = 3.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") float PuffMinLife = 1.5f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") float PuffBrightnessGround = 0.8f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") float PuffBrightnessBuilding = 0.85f;
	/** a muzzle flash's size for the shot renderer per metre of Effect.Size (⚠ a first guess: rifle 1 ≈ 0.3 m, a tank kit's 8 ≈ 2.7 m) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") float FlashPerMetre = 3.f;
	/** where the contract's origin (Session's UTM origin) is in this level, cm */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") FVector OriginCm = FVector::ZeroVector;

	// ── run ──
	/** joins the station (restarts the DDS client, so this world gets the late-join history) */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") bool Start(int32 Domain, const FString& Partition);
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") void Stop();

	// ── read back (tests, a HUD) ──
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") double SimNow() const;
	/** the sim time the poses were last computed for (the last world tick) — what ShownPosition belongs to */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") double PlacedAt() const { return Placed; }
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") TArray<int32> EntityIds() const;
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") AActor* ActorOf(int32 EntityId) const;
	/** the entity's shown position in the CONTRACT's frame (metres, ENU) — what a test compares with the publisher's track */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") FVector ShownPosition(int32 EntityId) const;
	/** one line: name, domain, type, what drew it, damage */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") FString Describe(int32 EntityId) const;
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") int32 SpawnedTotal() const { return Spawned; }
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") int32 RemovedTotal() const { return Removed; }
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") int32 EpochResets() const { return Resets; }
	/** what the event topics drew, per kind ("flash", "tracer", "main gun", "explosion Rocket", "detonation", "turret", "not drawn: …") */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") TMap<FString, int32> EventCounts() const { return Events; }
	/** a person's look this frame: "<role>=<clip> upper <role> weapon <0|1> hidden <0|1> rate <r> aim <twist>/<pitch>" */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") FString PersonLook(int32 EntityId) const;

	virtual bool DoesSupportWorldType(const EWorldType::Type T) const override { return T == EWorldType::Game || T == EWorldType::PIE; }
	virtual void Deinitialize() override;
	virtual void Tick(float DeltaTime) override;
	virtual TStatId GetStatId() const override { RETURN_QUICK_DECLARE_CYCLE_STAT(UHrotDpsWorld, STATGROUP_Tickables); }

private:
	struct FEnt
	{
		bool bHasEntity = false, bHasState = false;
		int64 TypeId = 0;
		uint8 Domain = 0, Damage = 0;
		FString Name, Dis, DrawnAs;
		FHrotDpsTrackPoint Track;
		float Bank = 0, MeshYaw = 0, MeshZ = 0;
		uint8 Side = 0;
		// a person (U-18d)
		bool bHasPerson = false;
		HrotDpsPeople::FIn Person;
		HrotDpsPeople::EGait LastGait = HrotDpsPeople::EGait::Idle;
		int32 AimTargetId = 0;
		FVector3d AimPoint = FVector3d::ZeroVector;
		double AimTwist = 0, AimPitchNow = 0;   // eased, degrees
		FString Look;
		bool bFlinching = false, bKicking = false;
		FName LastRole;
		double ShotAt = -1, HitAt = -1;
		uint8 ActiveSlot = 0;                // PersonState.ActiveSlot: his selected weapon mount (U-18f)
		double RocketFiredAt = -1;           // real seconds of his last rocket / ATGM shot (the tube, when no type data says)
		uint8 StateDamage = 0, LookDamage = 0;   // EntityState.Damage and VehicleLook's verdict; the shown damage is the worse
		bool bBurning = false;   // real seconds of his last shot (the kick) and of the last hit on him (the flinch)
		// a round in flight (U-18e)
		bool bRound = false, bLying = false;
		int64 MunitionType = 0;
		double LaunchedAt = 0;
		float BurnSeconds = 0;
		float TurretYaw = 0, GunPitch = 0;   // Unreal degrees (Articulation turret 0)
		double MainGunAt = -1;               // real seconds of its last main-gun shot (its flash stands for the Effect's)
		FHrotDpsPose Shown;
		FHrotDpsCorrection Blend;
		double BlendStart = -1;   // real seconds; < 0 = no correction running
		TWeakObjectPtr<AActor> Actor;
	};
	TMap<int32, FEnt> Ents;
	FHrotDpsClock Clock;
	uint32 Epoch = 0;
	bool bEpoch = false;
	TArray<TPair<FString, int32>> Subs;
	int32 Spawned = 0, Removed = 0, Resets = 0;
	double Placed = 0;
	TMap<FString, int32> Events;
	struct FRoundLook { float Length = 0, Diameter = 0; bool bSmokeTrail = false; };
	TMap<int64, FRoundLook> RoundLooks;   // MunitionLook, by munition type
	TMap<int64, TArray<FString>> MountCategories;
	/** U-18f — an automatic weapon's tracer STREAM (HROT's FireStreams): who fired (his gun's muzzle NOW starts the dashes), where it ran
	 *  to (Unreal cm; zero = along the shot), the shot's line as fallback, and when (sim s) */
	struct FStream { int32 Shooter = 0; FVector Origin, Along, Toward; double Born = 0; };
	TArray<FStream> Streams;
	void DrawStreams(double SimNow);   // TkbType: a type's weapon mounts' categories, lower case ("smallarms", "rocket" …)
	UPROPERTY(Transient) FHrotShotFx MgFx;
	UPROPERTY(Transient) FHrotShotFx RifleFx;
	UPROPERTY(Transient) TArray<TObjectPtr<UObject>> Preloaded;

	void OnSession(const struct FHrotDpsSample& S);
	void OnEntity(const struct FHrotDpsSample& S);
	void OnState(const struct FHrotDpsSample& S);
	void OnArticulation(const struct FHrotDpsSample& S);
	void OnFire(const struct FHrotDpsSample& S);
	void OnDetonation(const struct FHrotDpsSample& S);
	void OnEffect(const struct FHrotDpsSample& S);
	void OnPersonState(const struct FHrotDpsSample& S);
	void OnPersonAim(const struct FHrotDpsSample& S);
	void OnImpact(const struct FHrotDpsSample& S);
	void OnManDowned(const struct FHrotDpsSample& S);
	void OnRound(const struct FHrotDpsSample& S);
	void OnMunitionLook(const struct FHrotDpsSample& S);
	AActor* SpawnRound(FEnt& E, const FTransform& At);
	void OnVehicleLook(const struct FHrotDpsSample& S);
	void OnTkbType(const struct FHrotDpsSample& S);
	void ApplyDamage(FEnt& E);
	bool HoldsTube(const FEnt& E, double Real) const;
	AActor* SpawnPerson(FEnt& E, const FTransform& At);
	void ApplyPerson(FEnt& E, double SimNow, double Dt);
	void Count(const FString& Kind) { ++Events.FindOrAdd(Kind); }
	void Forget(int32 Id);
	void Clear();
	void SpawnIfReady(int32 Id, FEnt& E);
	UHrotVehicleKit* KitFor(const FEnt& E) const;
	void Place(FEnt& E, double Real);
	FHrotDpsPose PoseAt(const FEnt& E, double Real) const;
};
