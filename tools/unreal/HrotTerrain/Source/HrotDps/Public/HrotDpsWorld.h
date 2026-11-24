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
// ⭐ U-18h — ANIMALS, TRAFFIC, FLAME (rules in HrotDpsLife, ports of HROT's / the gateway's): an Animal entity is an AHrotAnimal of its
// family's kind (the DIS category), its clip from Animal.Mode + speed; a FLOCK is one entity drawn as N birds at HROT's closed-form
// offsets. Traffic (Mover + MoverTracks, never in Entity) is a car kit per mover, played PlaybackDelay behind on the Hermite track. A
// FlameJet lights the shooter's flamethrower (a person's at his weapon's muzzle, pointed by his aim; anything else's on its body,
// pointed at the aim), PersonState.Burning burns the man, FireSpots are ground fires where HROT says, a BurnedArea mark a scorch.
#pragma once

#include "CoreMinimal.h"
#include "HrotCards.h"
#include "Subsystems/WorldSubsystem.h"
#include "HrotDpsMotion.h"
#include "HrotDpsPeople.h"
#include "HrotWeaponFx.h"
#include "HrotDpsLife.h"
#include "HrotVoiceLines.h"
#include "HrotBurstTracker.h"
#include "HrotAmbience.h"
#include "HrotDpsWorld.generated.h"

class UHrotVehicleKit;
class UHrotPersonKit;
class UHrotMissileKind;
class UHrotAnimalKind;
class AHrotAnimal;
class AHrotGroundFire;
class ADecalActor;
class UHrotFlamethrower;
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
	/** U-18i — CIVILIANS' bodies (City Sample crowd people): a civilian entity (DIS life form category 2) gets one by its id, a pedestrian
	 *  mover by its seed (the same person on every channel); empty = PersonKit */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") TArray<TSoftObjectPtr<UHrotPersonKit>> CivilianKits;
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
	/** U-18h — an animal FAMILY ("Cattle", "Sheep", "Horse", "Dog", "Cat", "Bird", "Poultry", "Pig", "Goat", "Rabbit") → its kind and
	 *  its size on that kind's body; an empty map gets the stand-ins from the Animal Variety Pack (DefaultAnimals) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") TMap<FString, TSoftObjectPtr<UHrotAnimalKind>> AnimalKinds;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") TMap<FString, float> AnimalScales;
	/** the most birds drawn per flock (HROT draws up to 400 boxes; each bird here is a skinned actor) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") int32 MaxFlockDrawn = 40;
	/** U-18h — traffic: the kits of SUMO cars by size (a mover's Length: < 5.5 m a car, < 8.5 m a van / truck, else a bus); a car's
	 *  kit picked by its id among CarKits (the same car on every channel) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") TArray<TSoftObjectPtr<UHrotVehicleKit>> CarKits;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") TArray<TSoftObjectPtr<UHrotVehicleKit>> VanKits;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") TSoftObjectPtr<UHrotVehicleKit> BusKit;
	/** the most traffic movers drawn at once (the nearest-to-nothing rule: the first ones seen); 0 = no limit */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") int32 MaxMovers = 0;
	/** U-18i — the most traffic bodies (cars, pedestrians) created in one frame; 0 = no limit */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") int32 MaxMoverSpawnsPerFrame = 16;
	/** U-18i — the traffic draw distance (cm) from the nearest view: farther movers are not spawned, moved nor animated; 0 = all */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") float TrafficDrawRadiusCm = 80000.f;
	/** where the contract's origin (Session's UTM origin) is in this level, cm */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") FVector OriginCm = FVector::ZeroVector;

	// ── run ──
	/** joins the station (restarts the DDS client, so this world gets the late-join history) */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") bool Start(int32 Domain, const FString& Partition);
	/** U-19h: hides the components a kit names (its body's own gun) */
	static void HideKitComponents(AActor* A, const UHrotPersonKit* K);
	/** the default kit tables (animals, ground and AIR DIS kits, the domain fallbacks) where the job gave none; idempotent (a rail reads them) */
	void DefaultLife();
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") void Stop();
	/** TEST / tools (CE-12009): `Count` soldiers of the person kit seated in a vehicle's kit seats (SitOn, the seat's clip) - no HROT needed */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") int32 TestSeatPeople(AActor* Vehicle, int32 Count);

	// ── read back (tests, a HUD) ──
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") double SimNow() const;
	/** the sim time the poses were last computed for (the last world tick) — what ShownPosition belongs to */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") double PlacedAt() const { return Placed; }
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") TArray<int32> EntityIds() const;
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") AActor* ActorOf(int32 EntityId) const;
	/** U-19g: where an entity IS for a screen pick / mark (Unreal cm): a man's body centre (~1 m up), a vehicle's bounds centre, a body-less unit's anchor; false = not drawn / hidden */
	bool CentreOf(int32 EntityId, FVector& OutUnreal) const;
	/** U-19g: the entity's OWN body as a world box (Unreal cm): a man's skeletal meshes (feet -> head), a vehicle's mesh components (not attached actors, effects, a held rifle),
	 *  a body-less unit's card anchor +-40 cm; bOutMan = a person. false = not drawn / hidden */
	bool BodyBox(int32 EntityId, FBox& Out, bool& bOutMan) const;
	/** the box as text "minx miny minz maxx maxy maxz" (m, 1 decimal) - a scene check reads it */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") FString BodyBoxText(int32 EntityId) const;
	/** test hook: a 0.5 m cube ACTOR attached to the entity's actor, UpM metres above it (an M2 mount, an antenna) - the body box and the brackets must ignore it; returns it */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") AActor* AttachTestPart(int32 EntityId, float UpM);
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
	/** U-18h — traffic movers known / drawn now, and one's shown position (contract frame, m) */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") int32 MoversKnown() const { return Movers.Num(); }
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") int32 MoversDrawn() const;
	/** U-18h — what is burning now: "<jets> jets, <men> men, <spots> spots, <scorches> scorches" */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") FString FireSummary() const;
	/** U-18h — the ids of entities drawn as animals, and of lit flame jets' shooters */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") TArray<int32> AnimalIds() const;
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") TArray<int32> JetShooterIds() const;
	/** U-18i — riders seated on a mount now; pedestrian movers drawn now */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") int32 RidersNow() const;
	/** U-18l — for a camera: the entity's pose for THIS frame (the same rule its actor is placed by — HROT's BeforeDraw, CE-1352), in
	 *  the contract's frame (metres, ENU), and its domain; false when there is none */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") bool PoseNow(int32 EntityId, FVector& PositionM, FQuat& Rotation, int32& Domain) const;
	/** the entity an actor draws (0 = none) — a pick under the cursor */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") int32 EntityOfActor(const AActor* Actor) const;
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") FVector EnuMToUnreal(FVector PositionM) const { return FVector(PositionM.X, -PositionM.Y, PositionM.Z) * 100.0 + OriginCm; }
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") FVector UnrealToEnuM(FVector Location) const { const FVector L = (Location - OriginCm) / 100.0; return FVector(L.X, -L.Y, L.Z); }
	int32 DomainPerson() const; int32 DomainAir() const; int32 DomainAnimal() const;
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") int32 WalkersDrawn() const;
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") TArray<int32> RiderIds() const;

	// ── U-18p: round 7 (SimNetGateway DD-022 §3b-3d, §3g) — HrotDpsWorldClock.cpp ──
	/** report this world to the station as one to WAIT FOR on every load (ClientWorld.WaitForMe); false = a passive viewer */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") bool bWaitForMe = true;
	/** U-18r — load the level of the station's terrain (Session.Terrain, UHrotDpsSubsystem::LevelForTerrain); false = stay in this level */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") bool bFollowTerrain = true;
	/** the station's terrain now ("" before a Session) */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") FString StationTerrain() const { return ShownTerrain; }
	/** U-20: HROT's building damage (DPS_BuildingDamage) drawn on the house tiles */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") class UHrotBuildingDamage* Buildings();
	/** U-18t — show a terrain by NAME without a station (a preview; the packaged client's -HrotIgTerrain=<name>): resolve it like a Session's
	 *  (mounting its package) and open its level unless it is this one. "" = done or already here, else why not */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") FString ShowTerrain(const FString& Terrain);
	/** "anchored continuous|stepped|samples sim <t> scale <s> epoch <e> step <n> | offset <ms> rtt <ms> echoes <n>/<dropped> | steps <n> anchors <n>" */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") FString DescribeClock() const;
	/** "none" or "<source> <mode> target <id> d <m> side <s> cut <n> at (x, y, z) fov <deg>" — the station's suggested view now */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") FString DescribeView() const;
	/** what ClientWorld last said: "<epoch> Ready|Loading|Failed <step>" */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") FString DescribeClientWorld() const { return WorldSaid; }

	// ── U-18y: entity labels (HROT's cards; HrotDpsCards.cpp, HrotCardLayer) ──
	/** the owner's numbering (SimNetGateway R-143 / DD-024): 1 = no labels, 2 = commanders (squad and above, with the NATO symbol) + every
	 *  aircraft, 3 = every unit. An aircraft's card is never cut by distance (R-144: rule A is the client's).
	 *  ⚠ until the gateway carries HROT's card gizmos (NOTE-2026-11-21-Unreal-Labels-Via-Gizmos G-1) the cards are a STAND-IN built here
	 *  from Entity data in HROT's shape — and DPS says nothing about commanders yet, so mode 2 shows the aircraft only */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") int32 LabelMode = 2;
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") void CycleLabels() { LabelMode = LabelMode % 3 + 1; }
	// U-18y (user 2026-11-22): the client draws its cards from entity data; HROT's own cards over gizmos (DD-024: UnitCards / UnitSymbols /
	// Labels) are OPTIONAL - subscribed only when this is set (-HrotIgWireCards)
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") bool bWireCards = false;
	/** "labels <mode>: <n> cards, <r> repaints, <b> rebuilds" */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") FString DescribeLabels() const;
	UPROPERTY(Transient) TObjectPtr<class UHrotCardLayer> Cards;

	// ── U-18z: HROT's gizmos (GizmoView / GizmoFrame; HrotDpsGizmos.cpp) ──
	/** the layers asked of the station (HROT DebugTraceLayers names: Paths, Contacts, FireTraces, ...); empty = no view, no work */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") TArray<FString> GizmoLayers;
	/** the gizmo lines' look (compared in the scene): the material variant ("Post" = after TSR, the default; "" = M_HrotGizmoLine after DOF; "NoRaa") and the thinnest line in px */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") FString GizmoLineMaterial = TEXT("Post");
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") float GizmoLinePxFloor = 2.5f;
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") void SetGizmoLayers(const TArray<FString>& Layers);
	// ── U-19b: the squad's radio (HrotVoiceLines → UHrotVoicePlayer; docs/designs/IG/DESIGN_IG_Squad_Radio.md) ──
	/** a Synthetic report (a rail's / the debug API's) is not spoken when this is set (V-4: spoken by default) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") bool bSilentSynthetic = false;
	/** the net the listener is on: the unit of the man the camera follows - his EntityStatus.UnitId (T-068); without one, the nearest same-side IsUnit entity within 150 m (V-2); himself when he is one; 0 = none */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") int32 ListenerNet() const;
	/** "gizmos asked [..], <n> layers shown, <p> primitives, <f> frames" */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") FString DescribeGizmos() const;

	/** the station's suggested view (View) for THIS frame, dead-reckoned on the sim clock like an entity (Hermite-free LIVE mode) */
	struct FViewNow
	{
		bool bHas = false;           // a View instance with a source
		uint8 Source = 0, Mode = 0;  // ViewSource, ViewMode
		int32 TargetId = 0;          // bound to this unit (0 = none)
		float Distance = 0, Side = 1;
		uint32 CutSeq = 0;
		FVector3d P = FVector3d::ZeroVector;   // contract m
		FQuat4d Q = FQuat4d::Identity;         // body -> ENU, forward +x
		float FovDeg = 60;                     // VERTICAL (HROT's FovyDegrees)
	};
	FViewNow ViewNow() const;
	/** U-19e (F-3) — the director's fade and subtitle as the last View sample said (no client-side timing); bHas = the station sends a view */
	struct FOverlayNow { bool bHas = false; FString Subtitle; uint8 Pos = 0; float Alpha = 1, Fade = 0; };
	FOverlayNow OverlayNow() const;
	/** what the live HUD drew in director mode (probes for the scene) */
	void NoteOverlayDrawn(bool bSubtitle, bool bFade) { if (bSubtitle) ++OverlaySubtitlesDrawn; if (bFade) ++OverlayFadesDrawn; }
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "HROT|DPS") int32 OverlaySubtitlesDrawn = 0;
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "HROT|DPS") int32 OverlayFadesDrawn = 0;
	void FollowTerrain(uint32 NewEpoch, const FString& Terrain);
	FString WorldFail, PendingLevel, ShownTerrain, ShownBase, ShownOverlay;
	bool bLeaving = false;
	/** U-18q — the station's TimelineEpoch now (0 before an anchor) */
	int64 TimelineEpochNow() const { return Clock.GetTimelineEpoch(); }

	virtual bool DoesSupportWorldType(const EWorldType::Type T) const override { return T == EWorldType::Game || T == EWorldType::PIE; }
	virtual void Deinitialize() override;
	virtual void Tick(float DeltaTime) override;
	virtual TStatId GetStatId() const override { RETURN_QUICK_DECLARE_CYCLE_STAT(UHrotDpsWorld, STATGROUP_Tickables); }
	int32 PlacedActors = 0;   // U-19h ⑱: actors whose transform Place set this frame (the CSV profiler's HrotDps/PlacedActors)

private:
	struct FEnt
	{
		bool bHasEntity = false, bHasState = false;
		int64 TypeId = 0;
		uint8 Domain = 0, Damage = 0;
		FString Name, Dis, DrawnAs;
		FHrotDpsTrackPoint Track;
		float Bank = 0, MeshYaw = 0, MeshZ = 0;
		float StepPhase = 0;   // U-18n: the stride, a footstep at each whole number
		uint8 Side = 0;
		int32 ParentId = 0;   // Entity.ParentId: mounted on / part of another entity
		HrotCards::FStatusFacts Status;   // U-18y (DD-025): the gateway's EntityStatus (Status.bHas = one arrived); StatusKey = its hash
		uint32 StatusKey = 0;
		int32 UnitId = 0;       // U-19b / T-068: EntityStatus.UnitId - the unit (IsUnit commander) this man belongs to, 0 = none / not sent
		bool bIsUnit = false;   // U-18y: Entity.IsUnit — a body-less commander (DD-024): no stand-in body, its card at its position + 2 m
		// a person (U-18d)
		bool bHasPerson = false;
		double TiltLoggedAt = -1e9;   // the "arrives tilted" diagnostic: last log (real s)
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
		// U-18h — an animal: Animal's state, its flock's birds
		bool bAnimal = false, bHasAnimal = false, bDangerous = false, bAirborne = false;
		uint8 AnimalMode = 0, FlockMode = 0, Family = 0;
		int32 FlockCount = 0;
		uint32 FlockSeed = 0;
		TArray<TWeakObjectPtr<AHrotAnimal>> Birds;
		// U-18h — a lit flame jet (FlameJet), and a burning man (PersonState.Burning)
		bool bJet = false, bPersonBurning = false, bShownBurning = false;
		FVector3d JetAim = FVector3d::ZeroVector;   // contract m
		int32 JetTarget = 0;
		float JetRange = 0, JetIntensity = 0;
		TWeakObjectPtr<UHrotFlamethrower> JetComp;
		// U-18i — the body's kit (a soldier's or a civilian's), and a seat on a mount it RIDES (PersonState.SeatVehicleId / SeatIndex)
		TWeakObjectPtr<UHrotPersonKit> Kit;
		int32 SeatVehicleId = 0;
		uint8 SeatIndex = 255, RidingSeat = 255;
		bool bRiding = false, bNoSeatCounted = false;
		TWeakObjectPtr<AActor> RidingOn;
		TWeakObjectPtr<class UAnimSequence> SeatPose;
		// a round in flight (U-18e)
		bool bRound = false, bLying = false;
		int64 MunitionType = 0;
		bool bGun = false; int32 GunCarrier = 0;   // U-19h: a weapon system drawn as a gun - it sits on the vehicle it stands on (HROT sends no mount height)
		double LaunchedAt = 0;
		float BurnSeconds = 0;
		float TurretYaw = 0, GunPitch = 0;   // Unreal degrees (Articulation turret 0)
		double MainGunAt = -1;               // real seconds of its last main-gun shot (its flash stands for the Effect's)
		FHrotDpsPose Shown;
		FHrotDpsCorrection Blend;
		double BlendStart = -1;   // real seconds; < 0 = no correction running
		double SampleReal = -1;   // U-18p: real seconds of the last EntityState — older than the last timeline jump = no blend
		TWeakObjectPtr<AActor> Actor;
	};
	TMap<int32, FEnt> Ents;
	// U-18p — the clock's feed, the suggested view, ClientWorld
	struct FView
	{
		bool bHas = false;
		uint8 Source = 0, Mode = 0;
		int32 TargetId = 0;
		float Distance = 0, Side = 1, Fov = 60, FovRate = 0;
		float Fade = 0, SubAlpha = 1; uint8 SubPos = 0; FString Subtitle;   // U-19e: the director's fade and subtitle (P-8), as the last sample said
		uint32 CutSeq = 0;
		FHrotDpsTrackPoint Track;
		FHrotDpsCorrection Blend;
		double BlendStart = -1, SampleReal = -1;
	};
	FView View;
	double TimelineCut = -1;   // real seconds of the last timeline jump (a seek, a load): curves never blend across it
	double NextProbe = 0, NextWorldReport = 0;
	uint32 WorldSaidEpoch = 0; uint8 WorldSaidState = 0;
	FString WorldSaid;
	int32 Anchors = 0, Steps = 0, AckWanted = 0, ViewSamples = 0;
	void OnSimClock(const struct FHrotDpsSample& S);
	void OnClockEcho(const struct FHrotDpsSample& S);
	void OnClockStep(const struct FHrotDpsSample& S);
	void OnView(const struct FHrotDpsSample& S);
	void OnTimelineJump(double Real);
	void TickClock(double Real);
	void StartClock(class FHrotDpsClient& C, uint64 ClientId);
	void StopClock();
	TSharedPtr<class FHrotDpsProbeThread> Probe;
	uint64 MyClientId = 0;
	bool bLoggedStepped = false, bLoggedOffset = false;
	FHrotDpsPose ViewPoseAt(double Real) const;
	void TickCards();
	struct FGizmoPrim
	{
		uint8 Shape = 0; FColor Colour = FColor::White; float Width = 0; FString Text; TArray<FVector> Points;
		// HROT M18 draping (GizmoRenderer3D.cs): a shape whose points are all z = 0 lies on the ground. Done ONCE per gizmo frame (the points are kept
		// when the next frame resends the same shape on the same terrain), the camera-driven rebuilds only redo the ribbon widths
		bool bDraped = false, bGround = false; uint32 DrapeStamp = 0;
		TArray<FVector> Drape;   // Unreal world cm: the ground polyline (every 3 m), lifted; a closed shape repeats its first point
	};
	TMap<FString, TArray<FGizmoPrim>> GizmoLayersShown;
	/** U-18y — DD-024: the card-space primitives of each layer, and the cards they make (regrouped when a frame arrives, not per frame) */
	TMap<FString, TArray<HrotCards::FGizmoCardPrim>> GizmoCardPrims;
	struct FWireCard { TArray<HrotCards::FGizmoCardPrim> Prims; uint32 Key = 0; };
	TMap<int32, FWireCard> WireCards;
	bool bWireCardsDirty = false;
	int32 SentLabelMode = -1;
	void RegroupWireCards();
	double NextGizmoView = 0;
	bool bGizmoViewSent = false;
	int32 GizmoFrames = 0, GizmosDrawn = 0;   // GizmosDrawn: primitives drawn in the last frame
	void OnGizmoFrame(const struct FHrotDpsSample& S);
	void OnUtterance(const struct FHrotDpsSample& S);
	void OnReport(const struct FHrotDpsSample& S);
	void OnOrder(const struct FHrotDpsSample& S);
	/** hand a cue to the player: heard from entity `FromId` (the net it speaks on), at his head / unit anchor */
	/** Why: the HROT meaning shown in the sound log (U-19f); "{who}" in it becomes the speaker ("man 31" / "unit 9") */
	void SayCue(FVoiceCue Cue, int32 FromId, int32 ToId, bool bShout, const FString& Why = FString());
	FHrotCallsigns Callsigns;
	// -- U-19c: bursts (DD-027): one held clip per burst, cut when the rounds stop --
	FHrotBurstTracker Bursts;
	// -- U-19d: ambience layers (-> UHrotAmbiencePlayer) and weather (-> the soundscape's RainAmount / WindAmount) --
	FHrotWeatherPick Weather;
	double NextWeatherAt = 0;
	bool bHandSaved = false;   // the soundscape's hand / console rain and wind, kept while weather rows rule
	float HandRain = 0.f, HandWind = 0.f;
	void OnAmbience(const struct FHrotDpsSample& S);
	void OnWeather(const struct FHrotDpsSample& S);
	void ApplyWeather();
public:
	/** "<rain> <wind>" the weather rows give at the camera now, or "none" when no row exists (the soundscape keeps its hand / console values) */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") FString DescribeWeather() const;
private:
	/** true = this round's own sound is handled by the burst logic (started the burst's clip, or covered by it); false = play it per round */
	bool BurstSound(uint32 BurstId, int32 Rounds, int32 Index, const TCHAR* Stem, const FVector& At, class USoundAttenuation* Att, class USoundConcurrency* Con);
	void SendGizmoView(double Real);
	void DrawGizmos();
	/** U-18z: the gizmo lines live in OUR line batcher, rebuilt when a frame arrives (a one-frame DrawDebug from a subsystem's tick was
	 *  flushed before it was drawn: measured "6 drawn", nothing on screen); the texts are text-only cards */
	struct FGizmoText { FVector At; FString Text; FColor Colour; uint32 Key = 0; };
	TArray<FGizmoText> GizmoTexts;
	bool bGizmosDirty = false;
	/** the camera the lines' widths were built for (px -> cm depends on distance and FOV): rebuilt when it moves > 10 % of the nearest line's distance, or the FOV / viewport changes > 15 % */
	FVector GizmoBuiltFwd = FVector::ForwardVector;   // the ribbons face the camera: rebuilt when it turns > 2 deg
	TWeakObjectPtr<class UProceduralMeshComponent> GizmoMesh;   // the thick anti-aliased lines: one camera-facing ribbon quad per segment (M_HrotGizmoLine)
	TArray<FVector> GizVerts, GizNormals; TArray<int32> GizTris; TArray<FVector2D> GizUv; TArray<FColor> GizCols;   // reused, never shrunk (R-2084)
	int32 GizSectionVerts = -1;
	FString GizBuiltMaterial; float GizBuiltFloor = 0;
	FVector GizmoBuiltLoc = FVector::ZeroVector; float GizmoBuiltTanHalfFov = 0, GizmoBuiltW = 0, GizmoNearestCm = 1e9f;
	TWeakObjectPtr<class ULineBatchComponent> GizmoLines;
	// U-18h — traffic (Mover / MoverTracks: never an Entity)
	struct FMover
	{
		bool bInfo = false;
		uint8 Kind = 0;
		float Length = 0, Width = 0;
		uint32 Colour = 0, Seed = 0;
		TArray<HrotDpsLife::FKnot> Knots;
		TWeakObjectPtr<AActor> Actor;
		bool bSkipped = false;
	};
	TMap<int32, FMover> Movers;
	/** U-18i — pedestrian movers drawn as people: a person entry per mover id (ApplyPerson drives the body by its speed) */
	TMap<int32, FEnt> Walkers;
	double NewestBatchSim = -1e300, NewestBatchAt = 0;
	// U-18h — fires and scorches by their holder (FireSpots / GroundMarks keyed by EntityId; the holder need not be drawn)
	struct FSpot { FVector3d P; float Radius = 0, Intensity = 0, Seconds = 0; double Since = 0; TWeakObjectPtr<AHrotGroundFire> Fire; float ShownStrength = -1; };
	TMap<int32, TArray<FSpot>> Spots;
	TMap<int32, TArray<TWeakObjectPtr<ADecalActor>>> Scorches;
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
	void OnStatus(const struct FHrotDpsSample& S);   // DD-025 EntityStatus
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
	void OnAnimal(const struct FHrotDpsSample& S);
	void OnFlameJet(const struct FHrotDpsSample& S);
	void OnFireSpots(const struct FHrotDpsSample& S);
	void OnGroundMarks(const struct FHrotDpsSample& S);
	void OnMover(const struct FHrotDpsSample& S);
	void OnMoverTracks(const struct FHrotDpsSample& S);
	void PreloadPeopleAndSounds();
	bool IsCrowdKit(const UHrotPersonKit* K) const;
	/** how many times each body Blueprint is built at join to collect its random outfit parts (U-19h 15) */
	static constexpr int32 PreloadRounds = 6;
	AActor* SpawnAnimal(FEnt& E, const FTransform& At);
	void ApplyAnimal(FEnt& E, double SimNow);
	void ApplyFlame(FEnt& E);
	void TickSpots(double SimNow);
	void TickMovers(double Real);
	void ClearLife();
	UHrotPersonKit* KitOfPerson(int32 Id, const FEnt& E) const;
	void UpdateRiding(FEnt& E);
	/** U-19g: the ONE owner of "is somebody aboard": a person entity whose seat (PersonState.SeatVehicleId) or ParentId is a vehicle crews it; written to
	 *  AHrotVehicle::bCrewed each frame (the soundscape reads it - it never scans riders itself) */
	void UpdateCrews();
	TSet<int32> CrewScratch;   // reused: no allocation in the steady state
	void ApplyDamage(FEnt& E);
	bool HoldsTube(const FEnt& E, double Real) const;
	AActor* SpawnPerson(FEnt& E, const FTransform& At);
	void ApplyPerson(FEnt& E, double SimNow, double Dt);
	void Count(const FString& Kind) { ++Events.FindOrAdd(Kind); }
	void Forget(int32 Id);
	void Clear();
	void SpawnIfReady(int32 Id, FEnt& E);
	UHrotVehicleKit* KitFor(int32 Id, const FEnt& E) const;
	void Place(FEnt& E, double Real);
	FHrotDpsPose PoseAt(const FEnt& E, double Real) const;
	void GunOnCarrier(FEnt& E, FTransform& T);
	void OnBuildingDamage(const FHrotDpsSample& S);
	/** CE-12006: new people built per frame (a person's construction dresses him, 25-60 ms); 0 = no limit */
	UPROPERTY(EditAnywhere, Category = "HROT|DPS") int32 MaxPersonSpawnsPerFrame = 2;
	int32 PersonSpawnsThisFrame = 0;
	TSet<int32> DeferredSpawns;
	TMap<int32, FEnt> TestRiders;
	UPROPERTY(Transient) TObjectPtr<class UHrotBuildingDamage> BuildingDamage;
	/** U-19h (live: rockets / missiles from the Apache, the aircraft, SAMs and ATGMs showed no rocket and no trail; 23 fired, 0 rounds drawn):
	 *  a rocket / missile Fire with no Round entity from that shooter within FlyOutGrace s flies a stand-in missile (its kind's body, motor
	 *  and smoke trail) from the muzzle to the target (else along the shot) - drawn from the Fire sample like the tracers; a real Round cancels it */
	struct FFlyOut { int32 Shooter = 0, Target = 0; int64 Munition = 0; uint8 Category = 0; FVector From = FVector::ZeroVector, Dir = FVector::ForwardVector;
	                 double Born = 0, EndAt = 0; TWeakObjectPtr<class AHrotMissile> M; };
	TArray<FFlyOut> FlyOuts;
	int32 RoundsSeen = 0;
	void TickFlyOuts(double Real);
	static float FlyOutSpeedMps(uint8 Category);
};
