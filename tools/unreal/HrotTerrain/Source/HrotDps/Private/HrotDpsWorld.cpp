// CE-4651 (U-18b) — see HrotDpsWorld.h
#include "HrotDpsWorld.h"
#include "HrotDpsStation.h"
#include "HrotDpsNative.h"
#include "HrotDpsSubsystem.h"
#include "HrotVehicle.h"
#include "HrotShotRenderer.h"
#include "HrotSoundscape.h"
#include "HrotExplosionFx.h"
#include "HrotMissile.h"
#include "NiagaraComponent.h"
#include "NiagaraFunctionLibrary.h"
#include "NiagaraDataInterfaceArrayFunctionLibrary.h"
#include "HrotFire.h"
#include "HrotAnimal.h"
#include "HrotRiding.h"
#include "HrotPeopleLod.h"
#include "Engine/DecalActor.h"
#include "Components/DecalComponent.h"
#include "Engine/StaticMesh.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"
#include "HrotPersonKit.h"
#include "HrotPersonAnimInstance.h"
#include "HrotRuntimeLibrary.h"
#include "GameFramework/Pawn.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "Animation/AnimSequence.h"
#include "Engine/Engine.h"
#include "Engine/World.h"
#include "Engine/StaticMesh.h"
#include "Engine/StaticMeshActor.h"
#include "Animation/SkeletalMeshActor.h"
#include "Animation/AnimationAsset.h"
#include "Components/StaticMeshComponent.h"
#include "Components/SkeletalMeshComponent.h"
#include "HAL/PlatformTime.h"
#include "Kismet/GameplayStatics.h"
#include "HrotCardLayer.h"
#include "Misc/PackageName.h"

DEFINE_LOG_CATEGORY_STATIC(LogHrotDpsWorld, Log, All);

FString UHrotDpsWorld::ShowTerrain(const FString& Terrain)
{
	UHrotDpsSubsystem* Dps = GEngine ? GEngine->GetEngineSubsystem<UHrotDpsSubsystem>() : nullptr;
	if (!Dps) return TEXT("no DPS subsystem");
	FString Why;
	const FString Level = Dps->ResolveTerrain(Terrain, Why);
	if (Level.IsEmpty()) { UE_LOG(LogHrotDpsWorld, Warning, TEXT("DPS world: cannot show terrain '%s': %s"), *Terrain, *Why); return Why; }
	ShownTerrain = Terrain;
	if (Level == UWorld::RemovePIEPrefix(GetWorld()->GetOutermost()->GetName())) return FString();
	PendingLevel = Level;
	UE_LOG(LogHrotDpsWorld, Display, TEXT("DPS world: showing terrain '%s' -> %s"), *Terrain, *Level);
	return FString();
}

void UHrotDpsWorld::FollowTerrain(uint32 NewEpoch, const FString& Terrain)
{
	// U-18r — the world the station presents is its TERRAIN: show it in the level that draws it, or say why we cannot (ClientWorld Failed)
	UHrotDpsSubsystem* Dps = GEngine ? GEngine->GetEngineSubsystem<UHrotDpsSubsystem>() : nullptr;
	WorldFail.Reset();
	ShownTerrain = Terrain;
	if (!Dps || !bFollowTerrain || Terrain.IsEmpty()) { if (Dps) Dps->EndTravel(); return; }
	const FString Here = UWorld::RemovePIEPrefix(GetWorld()->GetOutermost()->GetName());
	FString Why;
	const FString Level = Dps->ResolveTerrainVersion(Terrain, ShownBase, ShownOverlay, Why);   // U-18t: mounts the terrain's package; U-18x: by version
	if (Level.IsEmpty())
	{
		WorldFail = Why;
		UE_LOG(LogHrotDpsWorld, Warning, TEXT("DPS world: %s"), *WorldFail);
		Dps->EndTravel();
		return;
	}
	if (Level == Here) { Dps->EndTravel(); return; }
	Dps->BeginTravel(NewEpoch, Level, FString::Printf(TEXT("level %s for terrain %s"), *FPackageName::GetShortName(Level), *Terrain), bWaitForMe);
	bLeaving = true;
	PendingLevel = Level;
	UE_LOG(LogHrotDpsWorld, Display, TEXT("DPS world: epoch %u terrain '%s' -> level %s (here %s)"), NewEpoch, *Terrain, *Level, *Here);
}

namespace
{
	using FSession = SimNetGateway_Protocol_DdsPlayer_Session;
	using FEntity = SimNetGateway_Protocol_DdsPlayer_Entity;
	using FState = SimNetGateway_Protocol_DdsPlayer_EntityState;
	using FArt = SimNetGateway_Protocol_DdsPlayer_Articulation;
	using FFire = SimNetGateway_Protocol_DdsPlayer_Fire;
	using FDet = SimNetGateway_Protocol_DdsPlayer_Detonation;
	using FEffect = SimNetGateway_Protocol_DdsPlayer_Effect;
	using FPerson = SimNetGateway_Protocol_DdsPlayer_PersonState;
	using FPersonAim = SimNetGateway_Protocol_DdsPlayer_PersonAim;
	using FImpact = SimNetGateway_Protocol_DdsPlayer_Impact;
	using FManDowned = SimNetGateway_Protocol_DdsPlayer_ManDowned;
	using FRound = SimNetGateway_Protocol_DdsPlayer_Round;
	using FMunitionLook = SimNetGateway_Protocol_DdsPlayer_MunitionLook;
	using FVehicleLook = SimNetGateway_Protocol_DdsPlayer_VehicleLook;
	using FTkbType = SimNetGateway_Protocol_DdsPlayer_TkbType;
	using FAnimalS = SimNetGateway_Protocol_DdsPlayer_Animal;
	using FFlameJet = SimNetGateway_Protocol_DdsPlayer_FlameJet;
	using FFireSpots = SimNetGateway_Protocol_DdsPlayer_FireSpots;
	using FGroundMarks = SimNetGateway_Protocol_DdsPlayer_GroundMarks;
	using FMoverS = SimNetGateway_Protocol_DdsPlayer_Mover;
	using FMoverTracks = SimNetGateway_Protocol_DdsPlayer_MoverTracks;
	/** a contract direction (ENU) → Unreal (no scale) */
	FVector Dir(const SimNetGateway_Protocol_DdsPlayer_Vec3f& V) { return FVector(V.X, -V.Y, V.Z).GetSafeNormal(); }

	const TCHAR* DomainName(uint8 D)
	{
		switch (D)
		{
		case SimNetGateway_Protocol_DdsPlayer_DomainPerson: return TEXT("Person");
		case SimNetGateway_Protocol_DdsPlayer_DomainGround: return TEXT("Ground");
		case SimNetGateway_Protocol_DdsPlayer_DomainAir: return TEXT("Air");
		case SimNetGateway_Protocol_DdsPlayer_DomainSurface: return TEXT("Surface");
		case SimNetGateway_Protocol_DdsPlayer_DomainSubsurface: return TEXT("Subsurface");
		case SimNetGateway_Protocol_DdsPlayer_DomainMunition: return TEXT("Munition");
		case SimNetGateway_Protocol_DdsPlayer_DomainAnimal: return TEXT("Animal");
		default: return TEXT("Other");
		}
	}
	FVector3d V3(const SimNetGateway_Protocol_DdsPlayer_Vec3d& V) { return FVector3d(V.X, V.Y, V.Z); }
	FVector3d V3(const SimNetGateway_Protocol_DdsPlayer_Vec3f& V) { return FVector3d(V.X, V.Y, V.Z); }
	double RealNow() { return FPlatformTime::Seconds(); }
}

UHrotDpsWorld* UHrotDpsWorld::Get(const UObject* WorldContext)
{
	const UWorld* W = GEngine ? GEngine->GetWorldFromContextObject(WorldContext, EGetWorldErrorMode::ReturnNull) : nullptr;
	return W ? W->GetSubsystem<UHrotDpsWorld>() : nullptr;
}

bool UHrotDpsWorld::Start(int32 Domain, const FString& Partition)
{
	Stop();
	UHrotDpsSubsystem* Dps = GEngine->GetEngineSubsystem<UHrotDpsSubsystem>();
	// ⚠ measured (U-18b): loading a kit on its first spawn stalled the editor ~9 s inside the poll — load the whole table up front
	Preloaded.Reset();
	DefaultLife();
	for (const auto& K : AnimalKinds) if (UObject* O = K.Value.LoadSynchronous()) Preloaded.Add(O);
	for (const auto& K : CarKits) if (UObject* O = K.LoadSynchronous()) Preloaded.Add(O);
	for (const auto& K : VanKits) if (UObject* O = K.LoadSynchronous()) Preloaded.Add(O);
	if (UObject* O = BusKit.LoadSynchronous()) Preloaded.Add(O);
	for (const auto& K : KitsByType) if (UObject* O = K.Value.LoadSynchronous()) Preloaded.Add(O);
	for (const auto& K : KitsByDis) if (UObject* O = K.Value.LoadSynchronous()) Preloaded.Add(O);
	for (const auto& K : KitsByDomain) if (UObject* O = K.Value.LoadSynchronous()) Preloaded.Add(O);
	if (UObject* O = PersonMesh.LoadSynchronous()) Preloaded.Add(O);
	if (UObject* O = PersonClip.LoadSynchronous()) Preloaded.Add(O);
	if (UObject* O = PersonKit.LoadSynchronous()) Preloaded.Add(O);
	if (UObject* O = DefaultMissileKind.LoadSynchronous()) Preloaded.Add(O);
	for (const auto& K : MissileKindsByType) if (UObject* O = K.Value.LoadSynchronous()) Preloaded.Add(O);
	MgFx = UHrotWeaponFx::HeavyMgFx(); RifleFx = UHrotWeaponFx::LyraRifleFx();
	Events.Reset();
	// U-18r: on the same station, KEEP the participant (a restart deletes our ClientWorld writer: the station would read us as gone and stop
	// waiting mid-load); each topic's reader is made fresh instead, so its late-join history reaches THESE handlers
	const bool bSame = Dps->IsRunning() && Dps->Client().GetDomain() == uint32(Domain) && Dps->Client().GetPartition() == Partition;
	if (!bSame)
	{
		Dps->Stop();
		if (!Dps->Start(Domain, Partition)) return false;
	}
	FHrotDpsClient& C = Dps->Client();
	auto Sub = [this, &C](const TCHAR* Topic, void (UHrotDpsWorld::*Fn)(const FHrotDpsSample&))
	{
		C.Refresh(Topic);
		Subs.Add({ Topic, C.Subscribe(Topic, [this, Fn](const FHrotDpsSample& S) { (this->*Fn)(S); }) });
	};
	Sub(TEXT("DPS_Session"), &UHrotDpsWorld::OnSession);
	Sub(TEXT("DPS_Entity"), &UHrotDpsWorld::OnEntity);
	Sub(TEXT("DPS_EntityState"), &UHrotDpsWorld::OnState);
	Sub(TEXT("DPS_Articulation"), &UHrotDpsWorld::OnArticulation);
	Sub(TEXT("DPS_Fire"), &UHrotDpsWorld::OnFire);
	Sub(TEXT("DPS_Detonation"), &UHrotDpsWorld::OnDetonation);
	Sub(TEXT("DPS_Effect"), &UHrotDpsWorld::OnEffect);
	Sub(TEXT("DPS_PersonState"), &UHrotDpsWorld::OnPersonState);
	Sub(TEXT("DPS_PersonAim"), &UHrotDpsWorld::OnPersonAim);
	Sub(TEXT("DPS_Impact"), &UHrotDpsWorld::OnImpact);
	Sub(TEXT("DPS_ManDowned"), &UHrotDpsWorld::OnManDowned);
	Sub(TEXT("DPS_MunitionLook"), &UHrotDpsWorld::OnMunitionLook);
	Sub(TEXT("DPS_Round"), &UHrotDpsWorld::OnRound);
	Sub(TEXT("DPS_VehicleLook"), &UHrotDpsWorld::OnVehicleLook);
	Sub(TEXT("DPS_TkbType"), &UHrotDpsWorld::OnTkbType);
	Sub(TEXT("DPS_Animal"), &UHrotDpsWorld::OnAnimal);
	Sub(TEXT("DPS_FlameJet"), &UHrotDpsWorld::OnFlameJet);
	Sub(TEXT("DPS_FireSpots"), &UHrotDpsWorld::OnFireSpots);
	Sub(TEXT("DPS_GroundMarks"), &UHrotDpsWorld::OnGroundMarks);
	Sub(TEXT("DPS_Mover"), &UHrotDpsWorld::OnMover);
	Sub(TEXT("DPS_MoverTracks"), &UHrotDpsWorld::OnMoverTracks);
	// U-18p — round 7: the station's clock and steps, its suggested view; the echo goes to the probe thread's own reader
	Sub(TEXT("DPS_SimClock"), &UHrotDpsWorld::OnSimClock);
	Sub(TEXT("DPS_ClockStep"), &UHrotDpsWorld::OnClockStep);
	Sub(TEXT("DPS_View"), &UHrotDpsWorld::OnView);
	Sub(TEXT("DPS_GizmoFrame"), &UHrotDpsWorld::OnGizmoFrame);   // U-18z
	StartClock(C, Dps->GetClientId());
	// U-18m — the player station's readers on this fresh client (the old client's went with it)
	if (UHrotDpsStation* St = UHrotDpsStation::Get(this)) St->Reconnect();
	UE_LOG(LogHrotDpsWorld, Display, TEXT("DPS world: joined domain %d partition '%s'"), Domain, *Partition);
	return true;
}

void UHrotDpsWorld::Stop()
{
	if (GEngine)
		if (UHrotDpsSubsystem* Dps = GEngine->GetEngineSubsystem<UHrotDpsSubsystem>())
			for (const TPair<FString, int32>& S : Subs) Dps->Client().Unsubscribe(S.Key, S.Value);
	Subs.Reset();
	StopClock();   // U-18p: the probe thread ends before the client's participant can go
	Clear();
}

void UHrotDpsWorld::Deinitialize()
{
	const bool bOwned = Subs.Num() > 0;
	Stop();
	if (Cards) Cards->Clear();
	if (bOwned && GEngine) if (UHrotDpsSubsystem* Dps = GEngine->GetEngineSubsystem<UHrotDpsSubsystem>()) if (!Dps->IsTraveling()) Dps->Stop();   // U-18r: kept across a level load
	Super::Deinitialize();
}

void UHrotDpsWorld::Clear()
{
	TArray<int32> Ids; Ents.GetKeys(Ids);
	for (int32 Id : Ids) Forget(Id);
	ClearLife();
	Clock.Reset();
	bEpoch = false;
}

void UHrotDpsWorld::Forget(int32 Id)
{
	FEnt E;
	if (!Ents.RemoveAndCopyValue(Id, E)) return;
	if (AActor* A = E.Actor.Get()) { A->Destroy(); ++Removed; }
	for (TWeakObjectPtr<AHrotAnimal>& B : E.Birds) if (AHrotAnimal* X = B.Get()) X->Destroy();
}

// ── samples ──

void UHrotDpsWorld::OnSession(const FHrotDpsSample& S)
{
	if (!S.bValid) return;
	const FSession& D = *static_cast<const FSession*>(S.Data);
	if (!bEpoch || D.WorldEpoch != Epoch || ShownTerrain != UTF8_TO_TCHAR(D.Terrain) || ShownBase != UTF8_TO_TCHAR(D.TerrainBaseHash) || ShownOverlay != UTF8_TO_TCHAR(D.TerrainOverlayHash))
	{
		// U-18r: a new world; U-18x: or a changed terrain / version (the gateway re-sends Session for it)
		ShownBase = UTF8_TO_TCHAR(D.TerrainBaseHash); ShownOverlay = UTF8_TO_TCHAR(D.TerrainOverlayHash);
		FollowTerrain(D.WorldEpoch, UTF8_TO_TCHAR(D.Terrain));
	}
	if (bEpoch && D.WorldEpoch != Epoch)   // §3.11: a new world — drop everything; the next samples rebuild it
	{
		UE_LOG(LogHrotDpsWorld, Display, TEXT("DPS world: epoch %u -> %u, %d entities dropped"), Epoch, D.WorldEpoch, Ents.Num());
		Clear();
		++Resets;
	}
	Epoch = D.WorldEpoch; bEpoch = true;
	const double R = RealNow();
	Clock.OnSample(D.SimTime, R);
	Clock.OnRate(D.Mode == SimNetGateway_Protocol_DdsPlayer_SessionModePaused ? 0.0 : D.TimeScale, R);
}

void UHrotDpsWorld::OnEntity(const FHrotDpsSample& S)
{
	const FEntity& D = *static_cast<const FEntity*>(S.Data);
	if (!S.bAlive || !S.bValid) { if (!S.bAlive) Forget(D.EntityId); return; }
	FEnt& E = Ents.FindOrAdd(D.EntityId);
	const bool bChanged = E.bHasEntity && (E.TypeId != D.TypeId || E.Domain != uint8(D.Domain));
	E.bHasEntity = true;
	E.TypeId = D.TypeId;
	E.Domain = uint8(D.Domain);
	E.Name = UTF8_TO_TCHAR(D.Name);
	E.Side = D.Side;
	E.ParentId = D.ParentId;
	E.Dis = FString::Printf(TEXT("%u.%u.%u.%u.%u.%u.%u"), D.Dis.Kind, D.Dis.Domain, D.Dis.Country, D.Dis.Category, D.Dis.Subcategory,
		D.Dis.Specific, D.Dis.Extra);
	if (bChanged && E.Actor.IsValid()) { E.Actor->Destroy(); E.Actor.Reset(); ++Removed; }   // another type: another model
	SpawnIfReady(D.EntityId, E);
}

void UHrotDpsWorld::OnState(const FHrotDpsSample& S)
{
	const FState& D = *static_cast<const FState*>(S.Data);
	if (!S.bAlive || !S.bValid) { if (!S.bAlive) Forget(D.EntityId); return; }
	const double R = RealNow();
	Clock.OnSample(D.SimTime, R);
	FEnt& E = Ents.FindOrAdd(D.EntityId);
	FHrotDpsTrackPoint K;
	K.T = D.SimTime;
	K.P = V3(D.Position);
	K.Q = FQuat4d(D.Orientation.X, D.Orientation.Y, D.Orientation.Z, D.Orientation.W).GetNormalized();
	K.V = V3(D.LinearVelocity); K.A = V3(D.LinearAcceleration);
	K.W = V3(D.AngularVelocity); K.WDot = V3(D.AngularAcceleration);
	// a correction: blend from where the OLD curve puts it NOW. ⚠ measured (U-18b): blending from the last DRAWN pose (one frame old,
	// or older after a stall) pulled every entity back by a frame of travel — 0.6 m at 12 m/s, 5 m after a 1 s stall
	// U-18p: never across a timeline jump (a seek, a load: TimelineEpoch) — the old curve belongs to another time
	if (E.bHasState && E.SampleReal >= TimelineCut) { E.Blend = HrotDpsMotion::Correction(PoseAt(E, R), HrotDpsMotion::Extrapolate(K, Clock.Now(R))); E.BlendStart = R; }
	else { E.BlendStart = -1; E.Shown = HrotDpsMotion::Extrapolate(K, Clock.Now(R)); }
	E.SampleReal = R;
	E.Track = K;
	E.Bank = D.Bank;
	E.bHasState = true;
	E.StateDamage = uint8(D.Damage);
	ApplyDamage(E);
	SpawnIfReady(D.EntityId, E);
}

// ── shots, hits and blasts (U-18c, U-18e) ──

namespace
{
	// HROT's effect types (Effect.EffectType, the vocabulary "hrot/1": Hrot.Core EffectTkbCatalog) — what a client must tell apart
	enum : int32
	{
		EffMuzzleSmall = 7101, EffMuzzleMedium = 7102, EffMuzzleLarge = 7103,
		EffExplosionSmall = 7111, EffExplosionMedium = 7112, EffExplosionLarge = 7113, EffExplosionHuge = 7114,   // Small (0.8 m) = an IMPACT PUFF
		EffImpactPuffMedium = 7115, EffImpactPuffLarge = 7116,                                                     // an inert round's dust
		EffBackblast = 7161, EffGunBlast = 7162, EffMissileLaunchFlash = 7173,
	};
	enum class EBlastLook : uint8 { Puff, Dust, Grenade, TankHE, Mortar };
	/** an Explosion effect → what it looks like: by HROT's type when the vocabulary is known, else by its size (m across) */
	EBlastLook BlastLookOf(int32 Type, float Size)
	{
		switch (Type)
		{
		case EffExplosionSmall: case EffImpactPuffMedium: case EffImpactPuffLarge: return EBlastLook::Puff;
		case EffBackblast: case EffGunBlast: return EBlastLook::Dust;
		case EffExplosionMedium: return EBlastLook::Grenade;
		case EffExplosionLarge: return EBlastLook::TankHE;
		case EffExplosionHuge: return EBlastLook::Mortar;
		default: return Size <= 3.f ? EBlastLook::Puff : Size <= 8.f ? EBlastLook::Grenade : Size <= 15.f ? EBlastLook::TankHE : EBlastLook::Mortar;
		}
	}
	/** a person's rifle muzzle, else none */
	bool MuzzleOfPerson(AActor* A, FTransform& Out)
	{
		const UHrotPersonState* St = A ? A->FindComponentByClass<UHrotPersonState>() : nullptr;
		UMeshComponent* W = St ? St->GetWeaponComponent() : nullptr;
		if (!W || !W->DoesSocketExist(TEXT("Muzzle"))) return false;
		Out = W->GetSocketTransform(TEXT("Muzzle"));
		return true;
	}
}

void UHrotDpsWorld::OnArticulation(const FHrotDpsSample& S)
{
	const FArt& D = *static_cast<const FArt*>(S.Data);
	if (!S.bValid || D.PartKind != SimNetGateway_Protocol_DdsPlayer_ArticulationKindTurret || D.Index != 0) return;   // the kits have one turret
	FEnt* E = Ents.Find(D.EntityId);
	if (!E) return;
	// Azimuth: rad, counter-clockwise from the hull's forward -> Unreal yaw (clockwise from above); Elevation: rad, up -> Unreal pitch
	E->TurretYaw = -FMath::RadiansToDegrees(D.Azimuth);
	E->GunPitch = FMath::RadiansToDegrees(D.Elevation);
	if (AHrotVehicle* V = Cast<AHrotVehicle>(E->Actor.Get())) { V->SetAim(E->TurretYaw, E->GunPitch); Count(TEXT("turret")); }
}

static void CrackNear(UObject* Ctx, const FVector& From, const FFire& D)
{
	// U-18n — a supersonic round passing near the view cracks where it passes (within 8 m of it, not at the shooter's own position)
	UHrotShotRenderer* R = UHrotShotRenderer::Get(Ctx);
	UHrotSoundscape* Snd = UHrotSoundscape::Get(Ctx);
	if (!R || !Snd) return;
	const FVector Along = Dir(D.Velocity);
	if (Along.IsNearlyZero()) return;
	const FVector V = R->ViewLocation();
	const double T = FVector::DotProduct(V - From, Along);
	if (T < 1500.0 || T > 150000.0) return;
	const FVector PassAt = From + Along * T;   // (not `Near`: HrotCamera::Near is visible in a unity build)
	if (FVector::DistSquared(PassAt, V) > FMath::Square(800.0)) return;
	const double Speed = FVector3d(D.Velocity.X, D.Velocity.Y, D.Velocity.Z).Size();
	if (Speed < 340.0) return;
	R->PlaySound(Snd->Pick(Speed > 850.0 ? TEXT("crack/sniper") : TEXT("crack/bullet")), PassAt, Snd->Falloff(3.f, 40.f));
}

void UHrotDpsWorld::OnFire(const FHrotDpsSample& S)
{
	if (!S.bValid) return;
	const FFire& D = *static_cast<const FFire*>(S.Data);
	const double Real = RealNow();
	Clock.OnSample(D.SimTime, Real);
	UHrotShotRenderer* R = UHrotShotRenderer::Get(GetWorld());
	FEnt* Shooter = Ents.Find(D.ShooterId);
	const bool bPerson = Shooter && Shooter->bHasPerson;
	if (bPerson) Shooter->ShotAt = Real;   // the kick on his upper body (ApplyPerson); his flash comes as an Effect, at his rifle
	FVector From = HrotDpsMotion::ToUnreal(FVector3d(D.Origin.X, D.Origin.Y, D.Origin.Z), OriginCm);
	FTransform Muzzle;
	if (bPerson && MuzzleOfPerson(Shooter->Actor.Get(), Muzzle)) From = Muzzle.GetLocation();   // the round leaves HIS rifle on screen
	AHrotVehicle* ShooterVehicle = Shooter ? Cast<AHrotVehicle>(Shooter->Actor.Get()) : nullptr;
	const bool bKitGun = ShooterVehicle && ShooterVehicle->Kit && !ShooterVehicle->Kit->Muzzle.IsNearlyZero();
	if (bPerson && (D.WeaponCategory == SimNetGateway_Protocol_DdsPlayer_WeaponCategoryRocket || D.WeaponCategory == SimNetGateway_Protocol_DdsPlayer_WeaponCategoryAtgm))
		Shooter->RocketFiredAt = Real;
	switch (D.WeaponCategory)
	{
	case SimNetGateway_Protocol_DdsPlayer_WeaponCategoryTankGun:
	case SimNetGateway_Protocol_DdsPlayer_WeaponCategoryHowitzer:
		if (AHrotVehicle* V = Shooter ? Cast<AHrotVehicle>(Shooter->Actor.Get()) : nullptr)
		{
			V->FireMainGun();   // the kit's tuned flash, blast and smoke at its own muzzle (the turret follows Articulation)
			if (UHrotSoundscape* Snd = UHrotSoundscape::Get(this))   // U-18n: the main gun's report (the library's howitzer)
				if (R) R->PlaySound(Snd->Pick(D.WeaponCategory == SimNetGateway_Protocol_DdsPlayer_WeaponCategoryTankGun ? TEXT("wpn/tank_gun") : TEXT("wpn/howitzer")),
				                    V->Kit && !V->Kit->Muzzle.IsNearlyZero() ? V->MuzzleTransform().GetLocation() : From, Snd->Falloff(30.f, 6000.f));
			Shooter->MainGunAt = Real;
			Count(TEXT("main gun"));
		}
		else Count(TEXT("not drawn: main gun without a vehicle"));
		return;
	case SimNetGateway_Protocol_DdsPlayer_WeaponCategoryMachineGun:
	case SimNetGateway_Protocol_DdsPlayer_WeaponCategoryAutocannon:
		if (R)
		{
			// HROT's FireStreams: every shot of a machine gun or autocannon shows tracer dashes running out from the muzzle toward the aim,
			// up to 500 m - here the shot renderer's one streak per shot (its speed and look); the dash spacing is not ported yet
			if (bKitGun) { From = ShooterVehicle->MuzzleTransform().GetLocation(); Count(TEXT("tracer from a vehicle gun")); }   // U-18f
			// HROT's FireStreams (U-18f): a STREAM of dashes toward the target (its chest when a person), else along the shot for 300 m
			FStream St; St.Shooter = D.ShooterId; St.Origin = From; St.Along = Dir(D.Velocity); St.Born = D.SimTime; St.Toward = FVector::ZeroVector;
			if (const FEnt* T = D.TargetId ? Ents.Find(D.TargetId) : nullptr)
				if (T->bHasState) St.Toward = HrotDpsMotion::ToUnreal(T->Shown.P + FVector3d(0, 0, T->bHasPerson ? 1.0 : 0.0), OriginCm);
			Streams.Add(St);
			UHrotSoundscape* Snd = UHrotSoundscape::Get(this);   // U-18n: the round of its gun, a crack where it passes the view
			USoundBase* Round = Snd ? Snd->Pick(D.WeaponCategory == SimNetGateway_Protocol_DdsPlayer_WeaponCategoryAutocannon ? TEXT("wpn/autocannon_round") :
			                                    bKitGun ? TEXT("wpn/mg_heavy_round") : TEXT("wpn/mg_round")) : nullptr;
			if (Round) R->PlaySound(Round, From, Snd->Falloff(12.f, 2500.f), MgFx.SoundConcurrency);
			else R->PlaySound(MgFx.Sound, From, MgFx.SoundAttenuation, MgFx.SoundConcurrency, MgFx.SoundVolume, MgFx.SoundPitch);
			CrackNear(this, From, D);
		}
		Count(TEXT("tracer"));
		return;
	case SimNetGateway_Protocol_DdsPlayer_WeaponCategorySmallArms:
		// HROT draws no tracer for small arms (FireStreams covers machine guns and autocannons only)
		if (R)
		{
			UHrotSoundscape* Snd = UHrotSoundscape::Get(this);   // U-18n: the pack's rifle shots (the east's side an AK), a crack where it passes
			USoundBase* Shot = Snd ? Snd->Pick(Shooter && Shooter->Side == 2 ? TEXT("wpn/rifle_east") : TEXT("wpn/rifle")) : nullptr;
			if (Shot) R->PlaySound(Shot, From, Snd->Falloff(10.f, 1500.f), RifleFx.SoundConcurrency);
			else R->PlaySound(RifleFx.Sound, From, RifleFx.SoundAttenuation, RifleFx.SoundConcurrency, RifleFx.SoundVolume, RifleFx.SoundPitch);
			CrackNear(this, From, D);
		}
		Count(TEXT("rifle shot"));
		return;
	case SimNetGateway_Protocol_DdsPlayer_WeaponCategoryRocket:
	case SimNetGateway_Protocol_DdsPlayer_WeaponCategoryAtgm:
		if (UHrotSoundscape* Snd = UHrotSoundscape::Get(this))   // U-18n: the launch (the round itself is an entity, drawn as such)
			if (R)
			{
				USoundBase* L = Snd->Pick(TEXT("wpn/rocket_launch"));
				R->PlaySound(L ? L : Snd->Pick(TEXT("wpn/rpg")), From, Snd->Falloff(10.f, 2000.f));
			}
		Count(TEXT("rocket launch"));
		return;
	default:
		Count(TEXT("fire of another category"));   // rockets, missiles, grenades: their rounds are entities (Round), drawn as such
	}
}

void UHrotDpsWorld::OnDetonation(const FHrotDpsSample& S)
{
	if (!S.bValid) return;
	const FDet& D = *static_cast<const FDet*>(S.Data);
	const double Real = RealNow();
	Clock.OnSample(D.SimTime, Real);
	Count(TEXT("detonation"));   // its blast and ground mark arrive as Effect
	// a hit ON a person: he flinches (unless the same hit took him down — his PersonState says so and wins)
	if (FEnt* T = D.TargetId ? Ents.Find(D.TargetId) : nullptr)
		if (T->bHasPerson)
		{
			T->HitAt = Real; Count(TEXT("hit on a person"));
			if (UHrotSoundscape* Snd = UHrotSoundscape::Get(this))   // U-18n
				if (UHrotShotRenderer* R = UHrotShotRenderer::Get(GetWorld()))
					if (AActor* A = T->Actor.Get()) R->PlaySound(Snd->Pick(TEXT("hit/body")), A->GetActorLocation() + FVector(0, 0, 120), Snd->Falloff(2.f, 60.f));
		}
}

void UHrotDpsWorld::OnImpact(const FHrotDpsSample& S)
{
	if (!S.bValid) return;
	const FImpact& D = *static_cast<const FImpact*>(S.Data);
	Clock.OnSample(D.SimTime, RealNow());
	UHrotShotRenderer* R = UHrotShotRenderer::Get(GetWorld());
	const FVector At = HrotDpsMotion::ToUnreal(FVector3d(D.Position.X, D.Position.Y, D.Position.Z), OriginCm);
	FVector N = FVector(D.Normal.X, -D.Normal.Y, D.Normal.Z);
	if (N.IsNearlyZero()) N = FVector::UpVector;
	// the puff comes as an Effect (HROT's one rule); the impact adds what only the surface knows: its sound and, near the view, the
	// chips / dust of Lyra's impact system (the shot renderer's budget)
	if (R)
	{
		// U-18n: the surface's own hit (metal, armour, stone, wood, glass, flesh) from the IG's library, else Lyra's default
		UHrotSoundscape* Snd = UHrotSoundscape::Get(this);
		USoundBase* Hit = Snd ? Snd->ImpactFor(D.Material[0] ? UTF8_TO_TCHAR(D.Material) : TEXT("")) : nullptr;
		R->PlaySound(Hit ? Hit : RifleFx.ImpactSound.Get(), At, Hit ? Snd->Falloff(2.f, 150.f) : RifleFx.ImpactAttenuation.Get(), RifleFx.ImpactConcurrency);
		if (RifleFx.Impact && FVector::Dist(At, R->ViewLocation()) < R->ImpactRange && R->TakeBudget(UHrotShotRenderer::EExtra::Impact))
			if (UNiagaraComponent* C = UNiagaraFunctionLibrary::SpawnSystemAtLocation(GetWorld(), RifleFx.Impact, At, FRotator::ZeroRotator, FVector::OneVector,
			                                                                        false, false, ENCPoolMethod::AutoRelease))
			{
				TArray<FVector> P{ At }, Nn{ N };
				UNiagaraDataInterfaceArrayFunctionLibrary::SetNiagaraArrayVector(C, TEXT("User.ImpactPositions"), P);
				UNiagaraDataInterfaceArrayFunctionLibrary::SetNiagaraArrayVector(C, TEXT("User.ImpactNormals"), Nn);
				C->SetVariableInt(TEXT("User.NumberOfHits"), 1);
				C->SetVariableVec3(TEXT("User.MuzzlePosition"), At + N * 1000.0);
				C->Activate(true);
			}
	}
	Count(FString::Printf(TEXT("impact %s"), D.Material[0] ? UTF8_TO_TCHAR(D.Material) : TEXT("(no material)")));
}

void UHrotDpsWorld::OnManDowned(const FHrotDpsSample& S)
{
	if (!S.bValid) return;
	Clock.OnSample(static_cast<const FManDowned*>(S.Data)->SimTime, RealNow());
	Count(TEXT("man downed"));   // the fall itself is his PersonState (Injury) — this event is for a HUD / a log
}

void UHrotDpsWorld::OnEffect(const FHrotDpsSample& S)
{
	if (!S.bValid) return;
	const FEffect& D = *static_cast<const FEffect*>(S.Data);
	const double Real = RealNow();
	Clock.OnSample(D.SimTime, Real);
	const FVector At = HrotDpsMotion::ToUnreal(FVector3d(D.Position.X, D.Position.Y, D.Position.Z), OriginCm);
	UHrotShotRenderer* R = UHrotShotRenderer::Get(GetWorld());
	FEnt* Anchor = D.AnchorId ? Ents.Find(D.AnchorId) : nullptr;
	switch (D.Kind)
	{
	case SimNetGateway_Protocol_DdsPlayer_EffectKindMuzzleFlash:
	{
		AHrotVehicle* V = Anchor ? Cast<AHrotVehicle>(Anchor->Actor.Get()) : nullptr;
		if (V && Anchor->MainGunAt >= 0 && Real - Anchor->MainGunAt < 0.5) { Count(TEXT("flash (the main gun's own)")); return; }
		if (!R) return;
		// at the anchor's muzzle: a vehicle kit's gun, a person's rifle; else where the producer says, facing its rotation
		FQuat4d Q(D.Rotation.X, D.Rotation.Y, D.Rotation.Z, D.Rotation.W);
		if (Q.SizeSquared() < 0.5) Q = FQuat4d::Identity;
		FTransform M(HrotDpsMotion::ToUnreal(Q.GetNormalized()), At);
		if (V && V->Kit && !V->Kit->Muzzle.IsNearlyZero()) M = V->MuzzleTransform();   // a kit with no gun (the Apache): where the producer says
		else if (Anchor && Anchor->bHasPerson && MuzzleOfPerson(Anchor->Actor.Get(), M)) Count(TEXT("flash at a rifle"));
		R->AddFlash(M, FMath::Clamp(D.Size * FlashPerMetre, 0.5f, 12.f));
		Count(TEXT("flash"));
		return;
	}
	case SimNetGateway_Protocol_DdsPlayer_EffectKindExplosion:
	{
		const EBlastLook Look = BlastLookOf(D.EffectType, D.Size);
		const bool bOnMetal = D.Surface == SimNetGateway_Protocol_DdsPlayer_EffectSurfaceVehicle;
		if (Look == EBlastLook::Puff || Look == EBlastLook::Dust)
		{
			if (!R) return;
			if (Look == EBlastLook::Dust && Anchor && Anchor->MainGunAt >= 0 && Real - Anchor->MainGunAt < 0.5) { Count(TEXT("dust (the main gun's own)")); return; }
			if (Look == EBlastLook::Puff && bOnMetal)   // a round on a vehicle: a spark, not dust
			{
				R->AddFlash(FTransform(At), 0.6f, 0.05f);
				Count(TEXT("spark on a vehicle"));
				return;
			}
			// HROT's puff: Size m across, the effect's own lifetime (a dust cloud lingers longer than a bullet's puff)
			const float Cm = D.Size * 100.f * (Look == EBlastLook::Puff ? PuffSizeScale : 1.f);
			const float Life = Look == EBlastLook::Dust ? FMath::Max(D.LifetimeSeconds, 2.f) : FMath::Max(D.LifetimeSeconds * PuffLifeScale, PuffMinLife);
			const float Bright = D.Surface == SimNetGateway_Protocol_DdsPlayer_EffectSurfaceBuilding ? PuffBrightnessBuilding : PuffBrightnessGround;
			R->AddPuff(At + FVector(0, 0, Cm * 0.2f), Cm * 0.35f, Cm, Life, Bright);
			Count(Look == EBlastLook::Dust ? TEXT("dust cloud") : TEXT("impact puff"));
			return;
		}
		const EHrotBlast Kind = Look == EBlastLook::Grenade ? EHrotBlast::Grenade : Look == EBlastLook::TankHE ? EHrotBlast::TankHE : EHrotBlast::Mortar;
		UHrotExplosionFx::Explode(GetWorld(), At, FVector::UpVector, Kind);
		Count(FString::Printf(TEXT("explosion %s"), *StaticEnum<EHrotBlast>()->GetNameStringByValue(int64(Kind))));
		return;
	}
	case SimNetGateway_Protocol_DdsPlayer_EffectKindGroundMark: Count(TEXT("not drawn: ground mark")); return;
	case SimNetGateway_Protocol_DdsPlayer_EffectKindWreckFlame:
	case SimNetGateway_Protocol_DdsPlayer_EffectKindWreckSmoke:
	{
		// on a vehicle: its own destroyed look (fire, then smoke); anywhere else: a ground fire for the effect's lifetime (U-18f)
		if (Anchor && Cast<AHrotVehicle>(Anchor->Actor.Get())) { Anchor->LookDamage = 2; ApplyDamage(*Anchor); Count(TEXT("wreck fire on a vehicle")); return; }
		if (D.Kind == SimNetGateway_Protocol_DdsPlayer_EffectKindWreckSmoke)
		{
			if (R) R->AddPuff(At + FVector(0, 0, 200), D.Size * 50.f, D.Size * 200.f, FMath::Max(D.LifetimeSeconds, 5.f), 0.25f);
			Count(TEXT("wreck smoke"));
			return;
		}
		FActorSpawnParameters P;
		P.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
		if (AHrotGroundFire* F = GetWorld()->SpawnActor<AHrotGroundFire>(At, FRotator::ZeroRotator, P))
		{
			F->Radius = FMath::Max(D.Size * 50.f, 100.f);
			F->SetLifeSpan(D.LifetimeSeconds > 0 ? D.LifetimeSeconds : 30.f);
		}
		Count(TEXT("ground fire"));
		return;
	}
	default: Count(TEXT("not drawn: effect of another kind"));
	}
}

// ── rounds in flight (U-18e) ──

void UHrotDpsWorld::OnRound(const FHrotDpsSample& S)
{
	const FRound& D = *static_cast<const FRound*>(S.Data);
	if (!S.bValid) return;   // gone with its Entity / EntityState
	FEnt& E = Ents.FindOrAdd(D.EntityId);
	E.bRound = true;
	E.MunitionType = D.MunitionTypeId;
	E.LaunchedAt = D.LaunchedAt; E.BurnSeconds = D.BurnSeconds; E.bLying = D.Lying;
	SpawnIfReady(D.EntityId, E);
}

void UHrotDpsWorld::OnMunitionLook(const FHrotDpsSample& S)
{
	if (!S.bValid) return;
	const FMunitionLook& D = *static_cast<const FMunitionLook*>(S.Data);
	FRoundLook& L = RoundLooks.FindOrAdd(D.MunitionTypeId);
	L.Length = D.Length; L.Diameter = D.Diameter; L.bSmokeTrail = D.SmokeTrail;
}

AActor* UHrotDpsWorld::SpawnRound(FEnt& E, const FTransform& At)
{
	const FRoundLook* L = RoundLooks.Find(E.MunitionType);
	const bool bMissile = E.BurnSeconds > 0 || (L && L->bSmokeTrail);
	UHrotMissileKind* Kind = nullptr;
	if (bMissile)
	{
		if (const TSoftObjectPtr<UHrotMissileKind>* K = MissileKindsByType.Find(E.MunitionType)) Kind = K->LoadSynchronous();
		if (!Kind) Kind = DefaultMissileKind.LoadSynchronous();
	}
	if (Kind)   // ⭐ a rocket / missile: the U-17g missile — its body, the motor while it burns, the smoke trail as it moves
	{
		AHrotMissile* M = AHrotMissile::SpawnMissile(GetWorld(), Kind, At);
		M->bMotor = true;
		M->Launch();   // the backblast at its tail
		E.DrawnAs = FString::Printf(TEXT("missile %s"), *Kind->GetName());
		Count(TEXT("round: missile"));
		return M;
	}
	// a grenade, a shell: a small body of its own size (MunitionLook), at least 6 cm so it is seen
	FActorSpawnParameters P;
	P.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
	AStaticMeshActor* B = GetWorld()->SpawnActor<AStaticMeshActor>(At.GetLocation(), At.Rotator(), P);
	B->SetMobility(EComponentMobility::Movable);
	B->GetStaticMeshComponent()->SetStaticMesh(LoadObject<UStaticMesh>(nullptr, TEXT("/Engine/BasicShapes/Sphere.Sphere")));
	B->GetStaticMeshComponent()->SetCollisionEnabled(ECollisionEnabled::NoCollision);
	const float D = FMath::Max(L && L->Diameter > 0 ? L->Diameter * 100.f : 6.f, 6.f), Len = FMath::Max(L && L->Length > 0 ? L->Length * 100.f : D, D);
	B->GetStaticMeshComponent()->SetRelativeScale3D(FVector(Len, D, D) / 100.f);
	E.DrawnAs = TEXT("round");
	Count(TEXT("round: body"));
	return B;
}

// ── the vehicle look, type data, the launcher (U-18f) ──

void UHrotDpsWorld::ApplyDamage(FEnt& E)
{
	// the worse of EntityState.Damage and VehicleLook (a kill or a fire = destroyed: fire, then smoke; smoking = damaged)
	const uint8 Want = FMath::Min<uint8>(FMath::Max(E.StateDamage, E.LookDamage), 2);
	if (Want == E.Damage) return;
	E.Damage = Want;
	if (AHrotVehicle* V = Cast<AHrotVehicle>(E.Actor.Get())) V->SetDamage(EHrotVehicleDamage(Want));
	Count(Want == 2 ? TEXT("vehicle destroyed / burning") : Want == 1 ? TEXT("vehicle damaged / smoking") : TEXT("vehicle intact"));
}

void UHrotDpsWorld::OnVehicleLook(const FHrotDpsSample& S)
{
	const FVehicleLook& D = *static_cast<const FVehicleLook*>(S.Data);
	if (!S.bValid) return;
	FEnt& E = Ents.FindOrAdd(D.EntityId);
	const bool bKilled = D.KillKind != SimNetGateway_Protocol_DdsPlayer_KillKindNone;
	bool bPartHurt = false;
	for (uint32 I = 0; I < D.Parts._length; ++I) bPartHurt |= D.Parts._buffer[I].State != SimNetGateway_Protocol_DdsPlayer_PartConditionIntact;
	E.LookDamage = (bKilled || D.Burning) ? 2 : (D.Smoking || bPartHurt) ? 1 : 0;
	if (D.Burning && !E.bBurning) Count(TEXT("burning started"));
	E.bBurning = D.Burning;
	ApplyDamage(E);
}

void UHrotDpsWorld::OnTkbType(const FHrotDpsSample& S)
{
	const FTkbType& D = *static_cast<const FTkbType*>(S.Data);
	if (!S.bValid || !D.Json) return;
	// HROT's own serialization ({tkbType, name, descriptors: [{type, partId, data}]}, PascalCase, enums as names) — only what is used:
	// the weapon mounts' categories, so PersonState.ActiveSlot says which weapon a man holds. Keys read case-insensitively (HROT adds
	// fields over time; read only what you use — the guide)
	TSharedPtr<FJsonObject> Root;
	const TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(UTF8_TO_TCHAR(D.Json));
	if (!FJsonSerializer::Deserialize(Reader, Root) || !Root.IsValid()) { Count(TEXT("tkb type: unreadable")); return; }
	auto Field = [](const TSharedPtr<FJsonObject>& O, const TCHAR* Name) -> TSharedPtr<FJsonValue>
	{
		for (const auto& It : O->Values) if (FCString::Stricmp(*It.Key, Name) == 0) return It.Value;   // UE 5.8: the keys are TSharedString
		return nullptr;
	};
	static const TCHAR* Categories[] = { TEXT("unknown"), TEXT("smallarms"), TEXT("machinegun"), TEXT("autocannon"), TEXT("tankgun"), TEXT("atgm"),
		TEXT("mortar"), TEXT("grenade"), TEXT("bomb"), TEXT("rocket"), TEXT("howitzer"), TEXT("sam") };
	TArray<FString> Mounts;
	const TSharedPtr<FJsonValue> Ds = Field(Root, TEXT("descriptors"));
	if (Ds.IsValid() && Ds->Type == EJson::Array)
		for (const TSharedPtr<FJsonValue>& V : Ds->AsArray())
		{
			const TSharedPtr<FJsonObject> O = V->AsObject();
			const TSharedPtr<FJsonValue> T = O.IsValid() ? Field(O, TEXT("type")) : nullptr;
			if (!T.IsValid() || T->AsString() != TEXT("WeaponSuiteDto")) continue;
			const TSharedPtr<FJsonValue> Data = Field(O, TEXT("data"));
			const TSharedPtr<FJsonValue> Ms = Data.IsValid() && Data->Type == EJson::Object ? Field(Data->AsObject(), TEXT("mounts")) : nullptr;
			if (!Ms.IsValid() || Ms->Type != EJson::Array) continue;
			for (const TSharedPtr<FJsonValue>& M : Ms->AsArray())
			{
				const TSharedPtr<FJsonValue> C = M->Type == EJson::Object ? Field(M->AsObject(), TEXT("category")) : nullptr;
				FString Cat = TEXT("unknown");
				if (C.IsValid() && C->Type == EJson::String) Cat = C->AsString().ToLower();
				else if (C.IsValid() && C->Type == EJson::Number) { const int32 N = int32(C->AsNumber()); if (N >= 0 && N < UE_ARRAY_COUNT(Categories)) Cat = Categories[N]; }
				Mounts.Add(Cat);
			}
		}
	MountCategories.Add(D.TypeId, Mounts);
	Count(TEXT("tkb type"));
}

void UHrotDpsWorld::DrawStreams(double SimNow)
{
	// HROT's FireStreams.Dashes, ported 1:1 (Hrot.Look/Effects/FireStreams.cs): for Window s after the shot, dashes DashLength long every
	// Spacing, from the gun's muzzle NOW toward where it aimed, the head running out at Speed, reach ≤ MaxLength, fading 1 → 0
	constexpr double Window = 0.35, Spacing = 500, DashLength = 180, Speed = 90000, MaxLength = 50000, Along = 30000;   // cm
	UHrotShotRenderer* R = UHrotShotRenderer::Get(GetWorld());
	for (int32 I = Streams.Num() - 1; I >= 0; --I)
	{
		const FStream& S = Streams[I];
		const double Since = SimNow - S.Born;
		if (Since > Window) { Streams.RemoveAtSwap(I); continue; }
		if (Since < 0 || !R) continue;
		FVector Muzzle = S.Origin, Forward = S.Along;
		if (const FEnt* E = Ents.Find(S.Shooter))   // a turning turret carries its stream with it
		{
			FTransform M;
			if (AHrotVehicle* V = Cast<AHrotVehicle>(E->Actor.Get()); V && V->Kit && !V->Kit->Muzzle.IsNearlyZero()) { M = V->MuzzleTransform(); Muzzle = M.GetLocation(); }
			else if (E->bHasPerson && MuzzleOfPerson(E->Actor.Get(), M)) Muzzle = M.GetLocation();
		}
		const FVector Line = S.Toward.IsZero() ? FVector::ZeroVector : S.Toward - Muzzle;
		double Reach = Line.Size();
		const FVector D = Reach > 50 ? Line / Reach : Forward;
		Reach = Reach > 50 ? FMath::Min(Reach, MaxLength) : Along;
		const double Front = FMath::Min(Reach, Since * Speed + Spacing);
		const double Phase = FMath::Fmod(Since * Speed, Spacing);
		const float Strength = float(1.0 - Since / Window);
		for (double Dd = Phase; Dd < Front; Dd += Spacing)
			R->AddDash(Muzzle + D * Dd, Muzzle + D * FMath::Min(Dd + DashLength, Front), Strength);
	}
}

bool UHrotDpsWorld::HoldsTube(const FEnt& E, double Real) const
{
	// the mount his ActiveSlot selects is a rocket or an ATGM (the type's WeaponSuiteDto) — or, with no type data, he fired one just now
	if (const TArray<FString>* M = MountCategories.Find(E.TypeId))
		if (M->IsValidIndex(E.ActiveSlot)) return (*M)[E.ActiveSlot] == TEXT("rocket") || (*M)[E.ActiveSlot] == TEXT("atgm");
	return E.RocketFiredAt >= 0 && Real - E.RocketFiredAt < 1.5;
}

// ── people (U-18d) ──

void UHrotDpsWorld::OnPersonState(const FHrotDpsSample& S)
{
	const FPerson& D = *static_cast<const FPerson*>(S.Data);
	if (!S.bValid) return;   // a person that is gone goes with its Entity / EntityState
	Clock.OnSample(D.SimTime, RealNow());
	FEnt& E = Ents.FindOrAdd(D.EntityId);
	HrotDpsPeople::FIn& P = E.Person;
	P.Stance = uint8(D.Stance); P.TargetStance = uint8(D.TargetStance); P.StanceFrom = uint8(D.StanceFrom);
	P.StanceChangedAt = D.StanceChangedAt; P.StanceSeconds = D.StanceSeconds;
	P.Posture = uint8(D.Posture); P.Held = uint8(D.Held); P.Injury = uint8(D.Injury); P.Gesture = uint8(D.Gesture);
	P.bInSeat = D.SeatVehicleId != 0; P.bSeatExposed = D.SeatExposed;
	E.ActiveSlot = D.ActiveSlot;
	E.bPersonBurning = D.Burning;
	E.SeatVehicleId = D.SeatVehicleId; E.SeatIndex = D.SeatIndex;
	E.bHasPerson = true;
}

void UHrotDpsWorld::OnPersonAim(const FHrotDpsSample& S)
{
	const FPersonAim& D = *static_cast<const FPersonAim*>(S.Data);
	if (!S.bValid) return;
	FEnt& E = Ents.FindOrAdd(D.EntityId);
	E.AimTargetId = D.AimTargetId;
	E.AimPoint = FVector3d(D.AimPoint.X, D.AimPoint.Y, D.AimPoint.Z);
}

AActor* UHrotDpsWorld::SpawnPerson(FEnt& E, const FTransform& At)
{
	UHrotPersonKit* K = E.Kit.IsValid() ? E.Kit.Get() : PersonKit.Get();
	if (!K) return nullptr;
	E.Kit = K;
	UWorld* W = GetWorld();
	AActor* A = nullptr;
	// the kit's measured offsets: the actor goes at feet − ZOffset, turned so the mesh (+Y forward, turned YawOffset on its actor) faces
	// the heading — kept on the entity, applied by Place every frame
	E.MeshYaw = -90.f - K->YawOffset;
	E.MeshZ = -K->ZOffset;
	FTransform T = At;
	T.SetRotation(T.GetRotation() * FQuat(FVector::UpVector, FMath::DegreesToRadians(E.MeshYaw)));
	T.AddToTranslation(FVector(0, 0, E.MeshZ));
	if (UClass* Cls = K->BodyClass.LoadSynchronous())
	{
		A = W->SpawnActorDeferred<AActor>(Cls, T, nullptr, nullptr, ESpawnActorCollisionHandlingMethod::AlwaysSpawn);
		if (APawn* Pawn = Cast<APawn>(A)) { Pawn->AutoPossessAI = EAutoPossessAI::Disabled; Pawn->AutoPossessPlayer = EAutoReceiveInput::Disabled; }
		A->FinishSpawning(T);
	}
	else if (K->BodyMesh)
	{
		ASkeletalMeshActor* M = W->SpawnActor<ASkeletalMeshActor>(T.GetLocation(), T.Rotator());
		M->GetSkeletalMeshComponent()->SetSkeletalMeshAsset(K->BodyMesh);
		A = M;
	}
	if (!A) return nullptr;
	// HROT moves the person: a Character's own movement must not (it falls), nor an AI controller possess it (hrot_person.hold_still)
	for (UActorComponent* C : A->GetComponents())
	{
		if (UCharacterMovementComponent* CM = Cast<UCharacterMovementComponent>(C)) { CM->GravityScale = 0.f; CM->SetComponentTickEnabled(false); CM->Deactivate(); }
		if (USceneComponent* SC = Cast<USceneComponent>(C); SC && K->HideComponents.Contains(SC->GetFName())) { SC->SetVisibility(false, true); SC->SetHiddenInGame(true, true); }
	}
	UHrotPersonState* St = UHrotRuntimeLibrary::AddPersonState(A);
	St->AimTurnRate = 0.f;   // the aim is eased here, on SIM time (HROT's 0.12 s), not by the anim instance's own rate
	St->WeaponSocket = K->WeaponSocket;
	St->WeaponOffset = K->WeaponOffset;
	if (const TObjectPtr<UMaterialInterface>* Mat = K->SideSlots.Num() ? K->SideMaterials.Find(E.Side == 2 ? TEXT("B") : TEXT("A")) : nullptr)
	{
		for (const FName& Slot : K->SideSlots) St->MaterialOverrides.Add(Slot, *Mat);
		St->ApplyMaterials();
	}
	E.DrawnAs = FString::Printf(TEXT("person %s"), *K->GetName());
	E.Actor = A;   // ApplyPerson reads it
	ApplyPerson(E, Clock.Now(RealNow()), 0.0);
	St->ApplyAnimation();
	// ⚠ measured LIVE (U-18i): 299 pedestrians + 1078 cars ran at ~1 fps — the DPS people had none of U-16's savings. ⇒ the body's parts
	// merged into one skinned mesh (U-16d: 15 skinning passes a person → 1) and the person handed to the distance tiers (UHrotPeopleLod,
	// U-16: every frame near, 30 Hz mid, 15 Hz far, staggered)
	// ⚠ measured LIVE (U-18i): MergeBodyParts on 300 pedestrians spawned together froze the editor for one 320 s frame — the merge is
	// NOT done here (U-16d's merge was measured on soldiers placed once; ⏳ a merged body per KIT, made once, is the cheap form)
	if (UHrotPeopleLod* Lod = UHrotPeopleLod::Get(this)) Lod->Add(St);
	return A;
}

void UHrotDpsWorld::ApplyPerson(FEnt& E, double SimNow, double Dt)
{
	UHrotPersonKit* K = E.Kit.IsValid() ? E.Kit.Get() : PersonKit.Get();
	AActor* A = E.Actor.Get();
	UHrotPersonState* St = A ? A->FindComponentByClass<UHrotPersonState>() : nullptr;
	if (!K || !St) return;
	HrotDpsPeople::FIn In = E.Person;
	In.Now = SimNow;
	// ⚠ measured LIVE (U-18g): HROT's WeaponPosture.HeldItem is the CIVILIANS' (CE-3234: a phone, binoculars, a stone; only HeldItems.Set
	// writes it) — a soldier's stays None while HROT's map draws his rifle from his MOUNTS (BlockFigureWeapons: the active mount's look).
	// ⇒ None + his type has a weapon on the selected mount (TkbType's WeaponSuiteDto) = the weapon in his hands
	if (In.Held == HrotDpsPeople::HeldNone)
		if (const TArray<FString>* M = MountCategories.Find(E.TypeId))
		{
			const int32 Slot = M->IsValidIndex(E.ActiveSlot) ? E.ActiveSlot : 0;
			if (M->IsValidIndex(Slot) && (*M)[Slot] != TEXT("unknown")) In.Held = HrotDpsPeople::HeldWeapon;
		}
	In.Speed = float(FVector2D(E.Track.V.X, E.Track.V.Y).Size());
	In.LastGait = E.LastGait;
	const HrotDpsPeople::FLook L = HrotDpsPeople::Look(In);
	E.LastGait = L.Gait;
	// U-18n — his boots: a step per stride while he walks or runs on his feet (not prone, not riding, not inside), near the view only
	if (L.Gait != HrotDpsPeople::EGait::Idle && !L.bHidden && !E.bRiding && L.Stance != HrotDpsPeople::Prone && Dt > 0)
	{
		const bool bRun = L.Gait == HrotDpsPeople::EGait::Run;
		E.StepPhase += float(Dt) * (bRun ? 2.7f : 1.8f * FMath::Clamp(In.Speed / 1.4f, 0.7f, 1.4f));
		if (E.StepPhase >= 1.f)
		{
			E.StepPhase -= FMath::FloorToFloat(E.StepPhase);
			if (UHrotSoundscape* Snd = UHrotSoundscape::Get(this))
				if (AActor* Body = E.Actor.Get()) Snd->Step(Body->GetActorLocation(), bRun, L.Stance == HrotDpsPeople::Crouched ? 0.5f : 1.f);
		}
	}

	// the body clip: the role, or its fallback chain when the kit has none
	FName Role = L.Base;
	UAnimSequence* Base = K->Clip(Role);
	for (int32 I = 0; !Base && !Role.IsNone() && I < 6; ++I) { Role = HrotDpsPeople::Fallback(Role); Base = K->Clip(Role); }
	// U-18i — a RIDER: his body sits (the seat's clip, U-17j), his upper body still readies and aims (firing from the saddle)
	if (E.bRiding && E.SeatPose.IsValid()) { Base = E.SeatPose.Get(); Role = TEXT("Seat.Riding"); }
	// a hit on him (U-18e): the flinch plays once over the body, unless he is going down (his Injury wins) or prone
	const double Real = RealNow();
	St->BaseAnim = Base;
	St->bBaseLoops = L.bLoops;
	St->BlendTime = L.BlendTime;
	// the ACTION layer (additive, on top of the pose, held at its time since the event): the flinch of a hit on him wins over the kick
	// of his own shot. ⚠ measured (U-18e): both clips are ADDITIVE (MM_HitReact_* local-space, MM_Rifle_Fire mesh-space) — played as
	// the body clip an additive collapses the pose, so neither replaces the body. No flinch while he is going down, prone or hidden.
	UAnimSequence* Hit = K->Clip(TEXT("Hit.Front"));
	UAnimSequence* Kick = L.bWeapon ? K->Clip(TEXT("Action.Fire")) : nullptr;
	const bool bFlinch = Hit && E.HitAt >= 0 && Real - E.HitAt < Hit->GetPlayLength() && L.bLoops && L.Stance != HrotDpsPeople::Prone && !L.bHidden;
	const bool bKick = !bFlinch && Kick && E.ShotAt >= 0 && Real - E.ShotAt < Kick->GetPlayLength();
	// what STARTED on screen this frame, counted once (a flinch lasts 0.8 s: a sampled look can miss it; a test reads these)
	if (bFlinch && !E.bFlinching) Count(TEXT("drawn: flinch"));
	if (bKick && !E.bKicking) Count(TEXT("drawn: kick"));
	if (Role != E.LastRole) { Count(FString::Printf(TEXT("drawn: %s"), *Role.ToString())); E.LastRole = Role; }
	E.bFlinching = bFlinch; E.bKicking = bKick;
	if (bFlinch) { St->ActionAnim = Hit; St->ActionTime = float(Real - E.HitAt); }
	else if (bKick) { St->ActionAnim = Kick; St->ActionTime = float(Real - E.ShotAt); }
	else if (St->ActionAnim) { St->ActionAnim = nullptr; St->ActionTime = -1.f; }
	// the play rate: a once-clip of a transition over its stated seconds; a gait by the speed over the clip's own; frozen while paused
	float Rate = 1.f;
	if (E.bRiding) Rate = 1.f;
	else if (L.bTransition && !L.bLoops && Base && E.Person.StanceSeconds > 0) Rate = float(Base->GetPlayLength()) / E.Person.StanceSeconds;
	else if (L.Gait != HrotDpsPeople::EGait::Idle && L.bLoops)
	{
		const float Native = L.Stance == HrotDpsPeople::Prone ? K->CrawlSpeed : L.Stance == HrotDpsPeople::Crouched ? K->CrouchSpeed
		                   : L.Gait == HrotDpsPeople::EGait::Run ? K->RunSpeed : K->WalkSpeed;
		Rate = FMath::Clamp(In.Speed / FMath::Max(Native, 0.1f), 0.6f, 1.8f);
	}
	St->PlayRate = Rate * float(Clock.GetScale());
	St->UpperAnim = L.Upper.IsNone() ? nullptr : K->Clip(L.Upper);

	const bool bTube = L.bWeapon && K->Launcher && HoldsTube(E, Real);   // U-18f: the RPG on his shoulder
	UStreamableRenderAsset* Weapon = bTube ? static_cast<UStreamableRenderAsset*>(K->Launcher.Get()) : L.bWeapon ? K->Rifle.Get() : nullptr;
	if (St->Weapon != Weapon)
	{
		St->Weapon = Weapon;
		St->WeaponOffset = bTube ? K->LauncherOffset : K->WeaponOffset;
		St->ApplyWeapon();
		if (bTube) Count(TEXT("drawn: tube on the shoulder"));
	}
	if (bTube && L.Stance != HrotDpsPeople::Prone) St->UpperAnim = K->Clip(TEXT("Upper.Aimed"));   // the tube is held in the aimed pose (the lab's)
	const bool bHide = L.bHidden && !E.bRiding;   // a rider is in an open seat: seen
	if (A->IsHidden() != bHide) A->SetActorHiddenInGame(bHide);

	// the aim (HROT's rule): the target's chest (its shown position + 1 m) or the stated point; eased on sim time
	double Twist = 0, Pitch = 0;
	if (L.bCanAim)
	{
		const FEnt* T = E.AimTargetId ? Ents.Find(E.AimTargetId) : nullptr;
		const bool bPoint = !E.AimPoint.IsNearlyZero();
		if ((T && T->bHasState) || bPoint)
			HrotDpsPeople::Aim(E.Shown.P, E.Shown.Q, L.Stance, T && T->bHasState ? T->Shown.P + FVector3d(0, 0, 1) : E.AimPoint, Twist, Pitch);
	}
	E.AimTwist = Dt > 0 ? HrotDpsPeople::Ease(E.AimTwist, Twist, Dt) : Twist;
	E.AimPitchNow = Dt > 0 ? HrotDpsPeople::Ease(E.AimPitchNow, Pitch, Dt) : Pitch;
	St->AimYaw = -float(E.AimTwist);   // the contract's twist is to the LEFT; the spine's yaw turns to the right
	St->AimPitch = float(E.AimPitchNow);
	E.Look = FString::Printf(TEXT("%s=%s%s upper %s weapon %d hidden %d rate %.2f aim %.1f/%.1f%s"), *Role.ToString(),
		Base ? *Base->GetName() : TEXT("(none)"), L.bLoops ? TEXT("") : TEXT(" once"), L.Upper.IsNone() ? TEXT("-") : *L.Upper.ToString(),
		L.bWeapon, L.bHidden, St->PlayRate, E.AimTwist, E.AimPitchNow, bFlinch ? TEXT(" flinch") : bKick ? TEXT(" kick") : TEXT(""));
	if (bTube) E.Look += TEXT(" tube");
}

FString UHrotDpsWorld::PersonLook(int32 EntityId) const { const FEnt* E = Ents.Find(EntityId); return E ? E->Look : FString(); }

// ── actors ──

UHrotVehicleKit* UHrotDpsWorld::KitFor(const FEnt& E) const
{
	if (const TSoftObjectPtr<UHrotVehicleKit>* K = KitsByType.Find(E.TypeId)) return K->LoadSynchronous();
	const TSoftObjectPtr<UHrotVehicleKit>* Best = nullptr;
	int32 BestLen = -1;
	for (const TPair<FString, TSoftObjectPtr<UHrotVehicleKit>>& P : KitsByDis)   // the longest dotted prefix
		if ((E.Dis == P.Key || E.Dis.StartsWith(P.Key + TEXT("."))) && P.Key.Len() > BestLen) { Best = &P.Value; BestLen = P.Key.Len(); }
	if (Best) return Best->LoadSynchronous();
	if (const TSoftObjectPtr<UHrotVehicleKit>* K = KitsByDomain.Find(DomainName(E.Domain))) return K->LoadSynchronous();
	return nullptr;
}

void UHrotDpsWorld::SpawnIfReady(int32 Id, FEnt& E)
{
	if (E.Actor.IsValid() || !E.bHasEntity || !E.bHasState) return;
	if (E.Domain == SimNetGateway_Protocol_DdsPlayer_DomainAnimal)
	{
		AActor* A = SpawnAnimal(E, HrotDpsMotion::ToUnreal(E.Shown, OriginCm));
		if (!A) return;
#if WITH_EDITOR
		A->SetActorLabel(FString::Printf(TEXT("DPS_%d_%s"), Id, *E.Name));
#endif
		E.Actor = A;
		++Spawned;
		return;
	}
	// ⚠ measured LIVE (U-18g, showcases/player-station): 510 of 551 entities were Domain Other — HROT's doors (no door state yet, the
	// guide's known gap), groups (teams, squads), score holders, a terrain's own buildings (already in the level). None is a body:
	// counted, never a box (the "never invisible" box is for a vehicle or a person with no model)
	if (E.Domain == SimNetGateway_Protocol_DdsPlayer_DomainOther) { if (E.DrawnAs.IsEmpty()) Count(TEXT("not drawn: domain Other")); E.DrawnAs = TEXT("not drawn: domain Other"); return; }
	if (E.Domain == SimNetGateway_Protocol_DdsPlayer_DomainMunition && !E.bRound) { E.DrawnAs = TEXT("(waiting for its Round)"); return; }
	UWorld* W = GetWorld();
	FTransform At = HrotDpsMotion::ToUnreal(E.Shown, OriginCm);
	FActorSpawnParameters P;
	P.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
	AActor* A = nullptr;
	if (E.Domain == SimNetGateway_Protocol_DdsPlayer_DomainMunition)
		A = SpawnRound(E, At);
	else if (E.Domain == SimNetGateway_Protocol_DdsPlayer_DomainPerson && (E.Kit = KitOfPerson(Id, E)).IsValid())
	{
		A = SpawnPerson(E, At);
		if (!A) { E.DrawnAs = TEXT("person kit without a body"); return; }
	}
	else if (E.Domain == SimNetGateway_Protocol_DdsPlayer_DomainPerson && !PersonMesh.IsNull())
	{
		ASkeletalMeshActor* M = W->SpawnActor<ASkeletalMeshActor>(At.GetLocation(), At.Rotator(), P);
		USkeletalMeshComponent* C = M->GetSkeletalMeshComponent();
		C->SetSkeletalMeshAsset(PersonMesh.LoadSynchronous());
		E.MeshYaw = PersonMeshYaw;
		if (UAnimationAsset* Clip = PersonClip.LoadSynchronous()) C->PlayAnimation(Clip, true);
		E.DrawnAs = TEXT("person stand-in");
		A = M;
	}
	else if (UHrotVehicleKit* Kit = (E.Domain == SimNetGateway_Protocol_DdsPlayer_DomainPerson) ? nullptr : KitFor(E))
	{
		AHrotVehicle* V = W->SpawnActorDeferred<AHrotVehicle>(AHrotVehicle::StaticClass(), At, nullptr, nullptr,
			ESpawnActorCollisionHandlingMethod::AlwaysSpawn);
		V->Kit = Kit;
		V->Damage = EHrotVehicleDamage(FMath::Min<uint8>(E.Damage, 2));
		V->TurretYaw = E.TurretYaw; V->GunPitch = E.GunPitch;
		V->FinishSpawning(At);
		E.DrawnAs = Kit->GetName();
		A = V;
	}
	else if (E.ParentId != 0)   // ⚠ measured LIVE (U-18g): the M2 HMG mounted on the HMMWV, a vehicle-sized box over its carrier
	{
		E.DrawnAs = TEXT("not drawn: mounted on another entity"); Count(TEXT("not drawn: mounted, no model")); return;
	}
	else   // ⭐ never invisible: a box of the domain's rough size
	{
		AStaticMeshActor* B = W->SpawnActor<AStaticMeshActor>(At.GetLocation(), At.Rotator(), P);
		B->SetMobility(EComponentMobility::Movable);
		B->GetStaticMeshComponent()->SetStaticMesh(LoadObject<UStaticMesh>(nullptr, TEXT("/Engine/BasicShapes/Cube.Cube")));
		const bool bPerson = E.Domain == SimNetGateway_Protocol_DdsPlayer_DomainPerson;
		// ⚠ measured LIVE (U-18g): HROT's WEAPON SYSTEMS (DIS category 50: the M2 HMG, the M134, Kornet — NedTkbCatalog.WeaponSystemType,
		// a pintle / tripod gun) are their own entities, the M2 standing at the HMMWV: a vehicle-sized box hid the HMMWV ⇒ a gun-sized one
		TArray<FString> DisParts; E.Dis.ParseIntoArray(DisParts, TEXT("."));
		const bool bWeaponSystem = DisParts.Num() > 3 && DisParts[3] == TEXT("50");
		B->GetStaticMeshComponent()->SetRelativeScale3D(bPerson ? FVector(0.4, 0.4, 1.8) : bWeaponSystem ? FVector(1.6, 0.25, 0.35) : FVector(5, 2.5, 2));
		B->GetStaticMeshComponent()->SetRelativeLocation(FVector(0, 0, bPerson ? 90 : bWeaponSystem ? 120 : 100));
		if (bWeaponSystem) E.DrawnAs = TEXT("weapon-system box");
		if (E.DrawnAs.IsEmpty() || E.DrawnAs.StartsWith(TEXT("("))) E.DrawnAs = TEXT("box");
		A = B;
	}
#if WITH_EDITOR
	if (!E.bHasPerson && E.Domain != SimNetGateway_Protocol_DdsPlayer_DomainPerson) A->SetActorLabel(FString::Printf(TEXT("DPS_%d_%s"), Id, *E.Name));   // ⚠ never a person: a rename rebuilds its Blueprint (U-16d)
#endif
	E.Actor = A;
	++Spawned;
}

FHrotDpsPose UHrotDpsWorld::PoseAt(const FEnt& E, double Real) const
{
	const FHrotDpsPose Pose = HrotDpsMotion::Extrapolate(E.Track, Clock.Now(Real));
	if (E.BlendStart < 0) return Pose;
	const double Alpha = (Real - E.BlendStart) / HrotDpsMotion::BlendSeconds;
	return Alpha >= 1 ? Pose : HrotDpsMotion::Corrected(Pose, E.Blend, Alpha);
}

void UHrotDpsWorld::Place(FEnt& E, double Real)
{
	const FHrotDpsPose Pose = PoseAt(E, Real);
	if (E.BlendStart >= 0 && Real - E.BlendStart >= HrotDpsMotion::BlendSeconds) E.BlendStart = -1;
	E.Shown = Pose;
	if (E.bRiding) return;   // U-18i: attached to its mount's seat — the mount carries it
	if (AActor* A = E.Actor.Get())
	{
		FHrotDpsPose Drawn = Pose;
		Drawn.Q = HrotDpsMotion::WithBank(Pose.Q, E.Bank);
		FTransform T = HrotDpsMotion::ToUnreal(Drawn, OriginCm);
		if (E.MeshYaw != 0) T.SetRotation(T.GetRotation() * FQuat(FVector::UpVector, FMath::DegreesToRadians(E.MeshYaw)));   // the model's own facing
		if (E.MeshZ != 0) T.AddToTranslation(FVector(0, 0, E.MeshZ));                                                          // and its origin
		T.SetScale3D(A->GetActorScale3D());   // ⚠ measured (U-18e): the full transform reset a sized body (a grenade, the unknown-type box) to scale 1
		A->SetActorTransform(T);
		// a rocket's motor burns BurnSeconds from its launch (Hrot.Look RoundLook.BurnOf; 0 = no motor data: burning while it flies)
		if (AHrotMissile* M = Cast<AHrotMissile>(A))
			M->bMotor = !E.bLying && (E.BurnSeconds <= 0 || Clock.Now(Real) - E.LaunchedAt < E.BurnSeconds);
	}
}

void UHrotDpsWorld::Tick(float)
{
	const double R = RealNow();
	if (!PendingLevel.IsEmpty())   // U-18r: the level of the station's terrain (opened here, never inside a DDS handler)
	{
		UE_LOG(LogHrotDpsWorld, Display, TEXT("DPS world: opening %s for the station's terrain"), *PendingLevel);
		UGameplayStatics::OpenLevel(this, FName(*PendingLevel), true, TEXT("game=/Script/HrotDps.HrotDpsGameMode"));
		PendingLevel.Reset();
	}
	TickClock(R);   // U-18p: the probe's echoes, ClientWorld
	const double Prev = Placed;
	Placed = Clock.Now(R);
	for (TPair<int32, FEnt>& It : Ents) if (It.Value.bHasState) Place(It.Value, R);
	const double Dt = FMath::Clamp(Placed - Prev, 0.0, 0.5);
	for (TPair<int32, FEnt>& It : Ents) if (It.Value.bHasPerson && It.Value.Actor.IsValid()) UpdateRiding(It.Value);
	for (TPair<int32, FEnt>& It : Ents) if (It.Value.bHasPerson && It.Value.Actor.IsValid()) ApplyPerson(It.Value, Placed, Dt);   // after every pose: an aim reads its target's
	for (TPair<int32, FEnt>& It : Ents)
	{
		FEnt& E = It.Value;
		if (!E.Actor.IsValid()) continue;
		if (E.bAnimal) ApplyAnimal(E, Placed);
		if (E.bJet || E.JetComp.IsValid() || E.bPersonBurning != E.bShownBurning) ApplyFlame(E);
	}
	DrawStreams(Placed);
	TickSpots(Placed);
	TickMovers(R);
	TickCards();   // U-18y: after every pose (a card hangs above the drawn body)
	SendGizmoView(R);   // U-18z
	DrawGizmos();
}

// ── read back ──

double UHrotDpsWorld::SimNow() const { return Clock.Now(RealNow()); }

TArray<int32> UHrotDpsWorld::EntityIds() const { TArray<int32> Ids; Ents.GetKeys(Ids); Ids.Sort(); return Ids; }

AActor* UHrotDpsWorld::ActorOf(int32 EntityId) const { const FEnt* E = Ents.Find(EntityId); return E ? E->Actor.Get() : nullptr; }

FVector UHrotDpsWorld::ShownPosition(int32 EntityId) const { const FEnt* E = Ents.Find(EntityId); return E ? FVector(E->Shown.P) : FVector::ZeroVector; }

FString UHrotDpsWorld::Describe(int32 EntityId) const
{
	const FEnt* E = Ents.Find(EntityId);
	if (!E) return TEXT("unknown");
	return FString::Printf(TEXT("'%s' %s type %lld dis %s -> %s, damage %u, track t %.3f"), *E->Name, DomainName(E->Domain), E->TypeId,
		*E->Dis, E->DrawnAs.IsEmpty() ? TEXT("(waiting)") : *E->DrawnAs, E->Damage, E->Track.T);
}

// ── U-18h: animals, flame, traffic ──

namespace
{
	FVector ToUe(const FVector3d& P, const FVector& OriginCm) { return FVector(P.X, -P.Y, P.Z) * 100.0 + OriginCm; }
	/** the body's heading in the contract's frame (radians, 0 = +x, counter-clockwise) */
	double HeadingOf(const FQuat4d& Q) { const FVector3d F = Q.RotateVector(FVector3d(1, 0, 0)); return FMath::Atan2(F.Y, F.X); }
}

void UHrotDpsWorld::DefaultLife()
{
	// stand-ins from the Animal Variety Pack (U-17e kinds) until real bodies exist: the nearest body by build, sized to the family
	if (AnimalKinds.Num() == 0)
	{
		auto K = [this](const TCHAR* Family, const TCHAR* Kind, float Scale)
		{
			AnimalKinds.Add(Family, TSoftObjectPtr<UHrotAnimalKind>(FSoftObjectPath(FString::Printf(TEXT("/Game/Hrot/Animals/%s.%s"), Kind, Kind))));
			AnimalScales.FindOrAdd(Family, Scale);
		};
		K(TEXT("Cattle"), TEXT("DA_Animal_Stag"), 1.25f);
		K(TEXT("Horse"), TEXT("DA_Animal_Stag"), 1.15f);
		K(TEXT("Sheep"), TEXT("DA_Animal_Doe"), 0.7f);
		K(TEXT("Goat"), TEXT("DA_Animal_Doe"), 0.65f);
		K(TEXT("Pig"), TEXT("DA_Animal_Pig"), 1.f);
		K(TEXT("Dog"), TEXT("DA_Animal_Wolf"), 0.75f);
		K(TEXT("Cat"), TEXT("DA_Animal_Fox"), 0.6f);
		K(TEXT("Rabbit"), TEXT("DA_Animal_Fox"), 0.4f);
		K(TEXT("Bird"), TEXT("DA_Animal_Crow"), 1.5f);
		K(TEXT("Poultry"), TEXT("DA_Animal_Crow"), 1.6f);
	}
	auto Kit = [](const TCHAR* Name) { return TSoftObjectPtr<UHrotVehicleKit>(FSoftObjectPath(FString::Printf(TEXT("/Game/Hrot/Vehicles/%s.%s"), Name, Name))); };
	// U-18k — HROT's military vehicle types on the French pack (user: "use them, replace stand-ins"), by DIS prefix; a job's own entry
	// wins. Green = HROT's own side's look; the desert Leclerc stands for the opposing tank (the T-72) so the two sides differ.
	auto AddDis = [this, &Kit](const TCHAR* Dis, const TCHAR* Name) { if (!KitsByDis.Contains(Dis)) KitsByDis.Add(Dis, Kit(Name)); };
	AddDis(TEXT("1.1.225.1"), TEXT("DA_FR_Leclerc_Green"));   // M1 Abrams, M67 flame tank (was the Sherman)
	AddDis(TEXT("1.1.222.1"), TEXT("DA_FR_Leclerc"));         // T-72 (was the BTR-60, the Russian catch-all)
	AddDis(TEXT("1.1.225.2"), TEXT("DA_FR_VBCI_Green"));      // M2 Bradley IFV (was a box)
	AddDis(TEXT("1.1.225.6"), TEXT("DA_FR_VBL_Green"));       // HMMWV (was the armoured truck)
	AddDis(TEXT("1.1.225.6.9"), TEXT("DA_FR_Serval_Green"));  // HMMWV drone jammer
	AddDis(TEXT("1.1.222"), TEXT("DA_BTR60"));                // any other Russian ground vehicle
	if (CarKits.Num() == 0)
		for (const TCHAR* N : { TEXT("DA_City_vehicle02_Car"), TEXT("DA_City_vehicle03_Car"), TEXT("DA_City_vehicle05_Car"), TEXT("DA_City_vehicle06_Car") })
			CarKits.Add(Kit(N));
	if (VanKits.Num() == 0)
		for (const TCHAR* N : { TEXT("DA_City_vehicle01_Van"), TEXT("DA_City_vehicle04_Truck") }) VanKits.Add(Kit(N));
	if (BusKit.IsNull()) BusKit = Kit(TEXT("DA_City_vehicle10_Bus"));
}

void UHrotDpsWorld::ClearLife()
{
	for (TPair<int32, FMover>& M : Movers) if (AActor* A = M.Value.Actor.Get()) A->Destroy();
	Movers.Reset();
	Walkers.Reset();
	NewestBatchSim = -1e300;
	for (TPair<int32, TArray<FSpot>>& H : Spots) for (FSpot& S : H.Value) if (AActor* A = S.Fire.Get()) A->Destroy();
	Spots.Reset();
	for (TPair<int32, TArray<TWeakObjectPtr<ADecalActor>>>& H : Scorches) for (TWeakObjectPtr<ADecalActor>& D : H.Value) if (ADecalActor* A = D.Get()) A->Destroy();
	Scorches.Reset();
}

// ── animals ──

AActor* UHrotDpsWorld::SpawnAnimal(FEnt& E, const FTransform& At)
{
	const HrotDpsLife::EFamily F = HrotDpsLife::FamilyOfDis(E.Dis);
	E.Family = uint8(F);
	E.bAnimal = true;
	E.MeshYaw = 0; E.MeshZ = 0;   // the kind turns its own body (UHrotAnimalKind::MeshYaw)
	const FString Family = HrotDpsLife::FamilyName(F);
	const TSoftObjectPtr<UHrotAnimalKind>* KP = AnimalKinds.Find(Family);
	UHrotAnimalKind* Kind = KP ? KP->LoadSynchronous() : nullptr;
	if (!Kind) { E.bAnimal = false; E.DrawnAs = FString::Printf(TEXT("not drawn: no kind for %s"), *Family); Count(E.DrawnAs); return nullptr; }
	const bool bFlock = E.TypeId == 707 || E.FlockCount > 1;   // HROT: a flock is type 707, ONE entity drawn as N birds
	if (bFlock)
	{
		// the flock's own actor is only its centre; the birds are actors of their own, placed by FlockBirds every frame
		AActor* C = GetWorld()->SpawnActor<AActor>(AActor::StaticClass(), At);
		USceneComponent* R = NewObject<USceneComponent>(C, TEXT("Root"));
		C->SetRootComponent(R);
		R->RegisterComponent();
		C->SetActorTransform(At);
		E.DrawnAs = FString::Printf(TEXT("flock of %s"), *Kind->GetName());
		Count(TEXT("animal drawn: flock"));
		return C;
	}
	AHrotAnimal* A = GetWorld()->SpawnActorDeferred<AHrotAnimal>(AHrotAnimal::StaticClass(), At, nullptr, nullptr, ESpawnActorCollisionHandlingMethod::AlwaysSpawn);
	A->Kind = Kind;
	A->Family = FName(*Family);   // U-18n: what it sounds like
	A->FinishSpawning(At);
	const float Scale = AnimalScales.FindRef(Family);
	A->SetActorScale3D(FVector(Scale > 0 ? Scale : 1.f));
	E.DrawnAs = FString::Printf(TEXT("animal %s as %s"), *Family, *Kind->GetName());
	Count(FString::Printf(TEXT("animal drawn: %s"), *Family));
	return A;
}

void UHrotDpsWorld::OnAnimal(const FHrotDpsSample& S)
{
	const FAnimalS& D = *static_cast<const FAnimalS*>(S.Data);
	if (!S.bValid) return;   // gone with its Entity
	FEnt& E = Ents.FindOrAdd(D.EntityId);
	E.bHasAnimal = true;
	E.AnimalMode = uint8(D.Mode); E.bDangerous = D.Dangerous; E.bAirborne = D.Airborne;
	E.FlockCount = D.FlockCount; E.FlockSeed = D.FlockSeed; E.FlockMode = uint8(D.FlockMode);
}

void UHrotDpsWorld::ApplyAnimal(FEnt& E, double SimNow)
{
	using namespace HrotDpsLife;
	const EFamily F = EFamily(E.Family);
	const FString Family = FamilyName(F);
	AActor* Actor = E.Actor.Get();
	if (AHrotAnimal* A = Cast<AHrotAnimal>(Actor))
	{
		FAnimalIn In;
		In.Family = F; In.Mode = E.AnimalMode; In.bDangerous = E.bDangerous; In.bAirborne = E.bAirborne;
		In.bDead = E.StateDamage >= SimNetGateway_Protocol_DdsPlayer_PartConditionDestroyed;
		In.Speed = float(FVector2d(E.Track.V.X, E.Track.V.Y).Size());
		const FAnimalOut O = AnimalShown(In);
		const EHrotAnimalState Want = EHrotAnimalState(uint8(O.Shown));
		if (A->State != Want || FMath::Abs(A->PlayRate - O.PlayRate) > 0.05f)
		{
			if (A->State != Want) Count(FString::Printf(TEXT("animal %s: %s"), *Family, *UEnum::GetDisplayValueAsText(Want).ToString()));
			A->PlayRate = O.PlayRate;
			A->SetState(Want);
		}
		return;
	}
	if (!Actor) return;
	// a flock: min(alive, MaxFlockDrawn) birds at FlockBirds' offsets about the centre, in the flock's frame (x along its heading)
	const int32 Want = FMath::Clamp(E.FlockCount, 0, FMath::Min(MaxFlockDrawn, FlockMaxDrawn));
	const TSoftObjectPtr<UHrotAnimalKind>* KP = AnimalKinds.Find(Family);
	UHrotAnimalKind* Kind = KP ? KP->Get() : nullptr;
	while (E.Birds.Num() > Want) { if (AHrotAnimal* B = E.Birds.Pop().Get()) B->Destroy(); }   // the HIGHEST indices go first (HROT)
	const float Scale = AnimalScales.FindRef(Family) > 0 ? AnimalScales.FindRef(Family) : 1.f;
	while (Kind && E.Birds.Num() < Want)
	{
		AHrotAnimal* B = GetWorld()->SpawnActorDeferred<AHrotAnimal>(AHrotAnimal::StaticClass(), Actor->GetActorTransform(), nullptr, nullptr,
			ESpawnActorCollisionHandlingMethod::AlwaysSpawn);
		B->Kind = Kind;
		B->FinishSpawning(Actor->GetActorTransform());
		B->SetActorScale3D(FVector(Scale));
		E.Birds.Add(B);
	}
	const double Yaw = HeadingOf(E.Shown.Q);
	const FQuat4d Turn(FVector3d::UnitZ(), Yaw);
	const EHrotAnimalState Shown = E.FlockMode == FlockFlying ? EHrotAnimalState::Fly : EHrotAnimalState::Idle;
	for (int32 I = 0; I < E.Birds.Num(); ++I)
	{
		AHrotAnimal* B = E.Birds[I].Get();
		if (!B) continue;
		const FVector3f O = FlockOffset(E.FlockSeed, I, SimNow, E.FlockMode, E.FlockCount);
		const FVector3d P = E.Shown.P + Turn.RotateVector(FVector3d(O));
		const double BirdYaw = Yaw + FlockYaw(E.FlockSeed, I, SimNow, E.FlockMode);
		B->SetActorLocationAndRotation(ToUe(P, OriginCm), FRotator(0, -FMath::RadiansToDegrees(BirdYaw), 0));
		if (B->State != Shown) B->SetState(Shown);
	}
}

// ── flame ──

void UHrotDpsWorld::OnFlameJet(const FHrotDpsSample& S)
{
	const FFlameJet& D = *static_cast<const FFlameJet*>(S.Data);
	FEnt* E = Ents.Find(D.EntityId);
	if (!S.bValid || !S.bAlive) { if (E) E->bJet = false; return; }   // the jet went out (disposed when unlit)
	if (!E) E = &Ents.Add(D.EntityId);
	if (!E->bJet) Count(TEXT("flame jet lit"));
	E->bJet = true;
	E->JetAim = V3(D.AimPoint); E->JetTarget = D.TargetId; E->JetRange = D.RangeM; E->JetIntensity = D.Intensity;
}

void UHrotDpsWorld::ApplyFlame(FEnt& E)
{
	AActor* A = E.Actor.Get();
	if (!A) return;
	// a burning man (PersonState.Burning; FlameLook U-3: one body-sized fire)
	if (E.bPersonBurning != E.bShownBurning)
	{
		E.bShownBurning = E.bPersonBurning;
		UHrotBurning::SetBurning(A, E.bPersonBurning ? 1.f : 0.f);
		Count(E.bPersonBurning ? TEXT("man burning") : TEXT("man stopped burning"));
	}
	// the jet: FlameLook — from the muzzle toward the aim (the target's chest when it is an entity we have), min(distance, RangeM) long
	UHrotFlamethrower* F = E.JetComp.Get();
	if (!E.bJet) { if (F && F->bFiring) F->SetFiring(false); return; }
	if (!F)
	{
		if (UHrotPersonState* St = A->FindComponentByClass<UHrotPersonState>())
		{
			St->bFlamethrower = true;
			St->SetFlameFiring(true);
			F = St->ApplyFlame();
		}
		if (!F)   // not a person with a weapon: a flame on its body, 1.5 m up (⏳ a vehicle's flame by its TKB mount)
			F = UHrotFlamethrower::Attach(A->GetRootComponent(), NAME_None, FTransform(FVector(0, 0, 150)), 1.f);
		E.JetComp = F;
		Count(TEXT("flame jet drawn"));
	}
	if (!F) return;
	FVector3d Aim = E.JetAim;
	if (E.JetTarget != 0) if (const FEnt* T = Ents.Find(E.JetTarget); T && T->bHasState) Aim = T->Shown.P + FVector3d(0, 0, 1);
	const FVector From = F->GetComponentLocation(), To = ToUe(Aim, OriginCm);
	const float Dist = float((To - From).Size() / 100.0);
	if (Dist > 0.5f) F->SetWorldRotation((To - From).Rotation());
	F->Range = HrotDpsLife::JetLength(Dist, E.JetRange) * 100.f / FMath::Max(F->Size, 0.01f);
	if (!F->bFiring) F->SetFiring(true);
}

void UHrotDpsWorld::OnFireSpots(const FHrotDpsSample& S)
{
	const FFireSpots& D = *static_cast<const FFireSpots*>(S.Data);
	TArray<FSpot>& L = Spots.FindOrAdd(D.EntityId);
	const uint32 N = (S.bValid && S.bAlive) ? D.Spots._length : 0;
	// the holder's spots, in world coordinates: reuse the fires in order, spawn the new ones, put out the rest
	while (L.Num() > int32(N)) { if (AActor* A = L.Last().Fire.Get()) A->Destroy(); L.Pop(); }
	for (uint32 I = 0; I < N; ++I)
	{
		const auto& X = D.Spots._buffer[I];
		if (int32(I) >= L.Num()) { L.AddDefaulted(); Count(TEXT("fire spot")); }
		FSpot& Sp = L[I];
		Sp.P = V3(X.Position); Sp.Radius = X.Radius; Sp.Intensity = X.Intensity; Sp.Since = X.Since; Sp.Seconds = X.Seconds;
		Sp.ShownStrength = -1;
	}
	if (N == 0) Spots.Remove(D.EntityId);
}

void UHrotDpsWorld::TickSpots(double SimNow)
{
	for (TPair<int32, TArray<FSpot>>& H : Spots)
	{
		// a vehicle that burns already shows its own fire (VehicleLook.Burning → AHrotVehicle destroyed): its spots are not doubled
		const FEnt* Holder = Ents.Find(H.Key);
		if (Holder && Cast<AHrotVehicle>(Holder->Actor.Get()) && Holder->Damage >= 2) continue;
		for (FSpot& Sp : H.Value)
		{
			// strength in quarter steps: the ground fire rebuilds its flames on a change
			const float Strength = FMath::RoundToFloat(HrotDpsLife::SpotStrength(Sp.Intensity, Sp.Since, Sp.Seconds, SimNow) * 4.f) / 4.f;
			if (Strength == Sp.ShownStrength && Sp.Fire.IsValid()) continue;
			Sp.ShownStrength = Strength;
			AHrotGroundFire* G = Sp.Fire.Get();
			const FVector At = ToUe(Sp.P, OriginCm);
			if (!G)
			{
				G = GetWorld()->SpawnActor<AHrotGroundFire>(AHrotGroundFire::StaticClass(), At, FRotator::ZeroRotator);
				Sp.Fire = G;
			}
			else G->SetActorLocation(At);
			// the burning disc: the spot's radius, within FlameLook's 0.3–8 m (the flames themselves are the ground fire's ~1 m ones)
			if (G) G->SetFire(FMath::Clamp(Sp.Radius, 0.4f, 8.f) * 100.f, Strength);
		}
	}
}

void UHrotDpsWorld::OnGroundMarks(const FHrotDpsSample& S)
{
	const FGroundMarks& D = *static_cast<const FGroundMarks*>(S.Data);
	TArray<TWeakObjectPtr<ADecalActor>>& L = Scorches.FindOrAdd(D.EntityId);
	for (TWeakObjectPtr<ADecalActor>& X : L) if (ADecalActor* A = X.Get()) A->Destroy();
	L.Reset();
	if (!S.bValid || !S.bAlive) { Scorches.Remove(D.EntityId); return; }
	static UMaterialInterface* Mat = LoadObject<UMaterialInterface>(nullptr, TEXT("/Game/Hrot/Fx/M_HrotScorch.M_HrotScorch"));   // FHrotFireLook::Scorch (not exported)
	for (uint32 I = 0; I < D.Marks._length; ++I)
	{
		const auto& M = D.Marks._buffer[I];
		// BurnedArea = a scorch (FlameLook.Scorch); VehicleStand / BodyImprint are THERMAL marks (a sensor view's, not the eye's)
		if (M.Kind != SimNetGateway_Protocol_DdsPlayer_HeatMarkKindBurnedArea) { Count(TEXT("not drawn: thermal ground mark")); continue; }
		ADecalActor* A = GetWorld()->SpawnActor<ADecalActor>(ToUe(V3(M.Centre), OriginCm), FRotator(-90.f, -FMath::RadiansToDegrees(M.Heading), 0.f));
		if (!A) continue;
		A->SetDecalMaterial(Mat);
		A->GetDecal()->DecalSize = FVector(150.f, FMath::Max(M.Width, 0.5f) * 50.f, FMath::Max(M.Length, 0.5f) * 50.f);
		A->GetDecal()->MarkRenderStateDirty();
		L.Add(A);
		Count(TEXT("scorch"));
	}
}

// ── traffic ──

void UHrotDpsWorld::OnMover(const FHrotDpsSample& S)
{
	const FMoverS& D = *static_cast<const FMoverS*>(S.Data);
	if (!S.bAlive)   // despawned
	{
		FMover M;
		if (Movers.RemoveAndCopyValue(D.MoverId, M)) if (AActor* A = M.Actor.Get()) A->Destroy();
		Walkers.Remove(D.MoverId);
		return;
	}
	if (!S.bValid) return;
	FMover& M = Movers.FindOrAdd(D.MoverId);
	M.bInfo = true; M.Kind = uint8(D.Kind); M.Length = D.Length; M.Width = D.Width; M.Colour = D.ColourRgba; M.Seed = D.Seed;
}

void UHrotDpsWorld::OnMoverTracks(const FHrotDpsSample& S)
{
	if (!S.bValid) return;
	const FMoverTracks& D = *static_cast<const FMoverTracks*>(S.Data);
	for (uint32 I = 0; I < D.Knots._length; ++I)
	{
		const auto& K = D.Knots._buffer[I];
		TArray<HrotDpsLife::FKnot>& Ks = Movers.FindOrAdd(K.MoverId).Knots;
		HrotDpsLife::FKnot N;
		N.T = D.SimTime + K.Dt;
		if (Ks.Num() > 0 && N.T <= Ks.Last().T) continue;   // a late or repeated knot
		N.P = FVector3d(K.X, K.Y, K.Z);
		N.Heading = K.HeadingCentiDeg * UE_DOUBLE_PI / 18000.0; N.Pitch = K.PitchCentiDeg * UE_DOUBLE_PI / 18000.0; N.Speed = K.Speed;
		Ks.Add(N);
	}
	NewestBatchSim = FMath::Max(NewestBatchSim, D.SimTime);
	NewestBatchAt = RealNow();
}

void UHrotDpsWorld::TickMovers(double Real)
{
	if (NewestBatchSim < -1e299) return;
	// MoverPlayback: the batch clock (at most 0.25 s past the newest batch: a paused sim sends none and the movers stand) less the delay
	const double T = NewestBatchSim + FMath::Clamp(Real - NewestBatchAt, 0.0, HrotDpsLife::MaxExtrapolation) - HrotDpsLife::PlaybackDelay;
	const double PlaybackT = T;
	int32 Drawn = 0, NewThisFrame = 0;
	// U-18i — the traffic DRAW DISTANCE: where the views rendered last frame (an editor viewport, a PIE / game camera, every channel
	// of a cluster), and a mover farther than TrafficDrawRadiusCm from all of them is neither spawned, moved nor animated.
	// ⚠ measured LIVE: ~1050 cars, each an actor of 8 parts moved every frame, ran at ~9 fps (cars only; ~1.3 fps with 300 pedestrians)
	// and Unreal took ~9 GB — most of them kilometres away (demo-city's SUMO net reaches 4.9 km round an 800 m town)
	const TArray<FVector>& Views = GetWorld()->ViewLocationsRenderedLastFrame;
	const double R2 = TrafficDrawRadiusCm > 0 ? double(TrafficDrawRadiusCm) * TrafficDrawRadiusCm : 0;
	auto InRange = [&Views, R2](const FVector& At, bool bShown)
	{
		if (R2 <= 0 || Views.Num() == 0) return true;   // no limit, or no view yet: everything
		const double Lim = bShown ? R2 * 1.1025 : R2;    // 5 % hysteresis: a car on the edge does not flicker
		for (const FVector& V : Views) if (FVector::DistSquared(V, At) <= Lim) return true;
		return false;
	};
	for (TPair<int32, FMover>& It : Movers)
	{
		FMover& M = It.Value;
		AActor* A = M.Actor.Get();
		// shown from its second knot, once playback reaches its first
		HrotDpsLife::FTrackPose P;
		bool bShow = M.bInfo && M.Knots.Num() >= 2 && T >= M.Knots[0].T;
		if (bShow) { HrotDpsLife::TrimBefore(M.Knots, T); bShow = HrotDpsLife::PoseAt(M.Knots, T, P); }
		if (!bShow) { if (A) A->SetActorHiddenInGame(true); continue; }
		const FVector At = ToUe(P.P, OriginCm);
		const FRotator Rot(FMath::RadiansToDegrees(P.Pitch), -FMath::RadiansToDegrees(P.Heading), 0.f);
		if (!InRange(At, A && !A->IsHidden())) { if (A && !A->IsHidden()) A->SetActorHiddenInGame(true); continue; }
		if (!A && !M.bSkipped)
		{
			// U-18i: at most MaxMoverSpawnsPerFrame new bodies a frame — a join into a full town is spread over frames, not one hitch
			if (MaxMoverSpawnsPerFrame > 0 && NewThisFrame >= MaxMoverSpawnsPerFrame) continue;
			++NewThisFrame;
			if (MaxMovers > 0 && Drawn >= MaxMovers) { M.bSkipped = true; Count(TEXT("not drawn: traffic over MaxMovers")); continue; }
			if (M.Kind == SimNetGateway_Protocol_DdsPlayer_MoverKindPedestrian)
			{
				// U-18i — a pedestrian: a civilian body by its seed, driven like any person (idle / walk / run by its speed)
				FEnt& W = Walkers.FindOrAdd(It.Key);
				W.Kit = CivilianKits.Num() ? CivilianKits[M.Seed % uint32(CivilianKits.Num())].LoadSynchronous() : PersonKit.Get();
				W.Name = FString::Printf(TEXT("pedestrian %d"), It.Key);
				W.bHasPerson = true; W.bHasState = true;
				A = W.Kit.IsValid() ? SpawnPerson(W, FTransform(Rot, At)) : nullptr;
				if (!A) { M.bSkipped = true; Count(TEXT("not drawn: pedestrian, no person kit")); continue; }
#if WITH_EDITOR
				A->Tags.Add(TEXT("DPS_Walker"));   // a tag, not a label: renaming a Blueprint person rebuilds it (U-16d)
#endif
				M.Actor = A;
				Count(TEXT("traffic pedestrian"));
			}
			else if (M.Kind != SimNetGateway_Protocol_DdsPlayer_MoverKindCar) { M.bSkipped = true; Count(TEXT("not drawn: mover of unknown kind")); continue; }
			else
			{
			const TSoftObjectPtr<UHrotVehicleKit>* KP = M.Length >= 8.5f ? &BusKit
				: M.Length >= 5.5f ? (VanKits.Num() ? &VanKits[It.Key % VanKits.Num()] : nullptr)
				: (CarKits.Num() ? &CarKits[It.Key % CarKits.Num()] : nullptr);
			UHrotVehicleKit* Kit = KP ? KP->LoadSynchronous() : nullptr;
			if (!Kit) { M.bSkipped = true; Count(TEXT("not drawn: no traffic kit")); continue; }
			const FTransform X(Rot, At);
			AHrotVehicle* V = GetWorld()->SpawnActorDeferred<AHrotVehicle>(AHrotVehicle::StaticClass(), X, nullptr, nullptr, ESpawnActorCollisionHandlingMethod::AlwaysSpawn);
			V->Kit = Kit;
			// U-18i — HROT's street paint (TrafficPaint, chosen by vehicle id in the gateway: every viewer agrees), 0xRRGGBBAA, sRGB
			if (M.Colour != 0) { V->bPainted = true; V->Paint = FLinearColor::FromSRGBColor(FColor(uint8(M.Colour >> 24), uint8(M.Colour >> 16), uint8(M.Colour >> 8))); }
			V->FinishSpawning(X);
#if WITH_EDITOR
			// (a tag, not a label: SetActorLabel is an editor edit per actor — measured U-18i, spawning ran ~45 ms a body)
#endif
			V->Tags.Add(TEXT("DPS_Mover"));
			M.Actor = V; A = V;
			Count(TEXT("traffic car"));
			if (M.Colour != 0) Count(TEXT("traffic car painted"));
			}
		}
		if (!A) continue;
		A->SetActorHiddenInGame(false);
		if (FEnt* W = Walkers.Find(It.Key))
		{
			// a walker: its pose as a person's (the kit's facing and origin), its gait from the track's speed
			W->Shown.P = P.P; W->Shown.Q = FQuat4d(FVector3d::UnitZ(), P.Heading);
			W->Track.V = FVector3d(FMath::Cos(P.Heading), FMath::Sin(P.Heading), 0) * P.Speed;
			FTransform Tw(Rot, At);
			Tw.SetRotation(Tw.GetRotation() * FQuat(FVector::UpVector, FMath::DegreesToRadians(W->MeshYaw)));
			Tw.AddToTranslation(FVector(0, 0, W->MeshZ));
			Tw.SetScale3D(A->GetActorScale3D());
			A->SetActorTransform(Tw);
			ApplyPerson(*W, PlaybackT, 0.0);
		}
		else A->SetActorLocationAndRotation(At, Rot);
		++Drawn;
	}
}

int32 UHrotDpsWorld::MoversDrawn() const
{
	int32 N = 0;
	for (const TPair<int32, FMover>& M : Movers) if (const AActor* A = M.Value.Actor.Get(); A && !A->IsHidden()) ++N;
	return N;
}

FString UHrotDpsWorld::FireSummary() const
{
	int32 Jets = 0, Men = 0, Lit = 0, Marks = 0;
	for (const TPair<int32, FEnt>& E : Ents) { Jets += E.Value.bJet; Men += E.Value.bShownBurning; }
	for (const TPair<int32, TArray<FSpot>>& H : Spots) for (const FSpot& S : H.Value) Lit += S.ShownStrength > 0;
	for (const TPair<int32, TArray<TWeakObjectPtr<ADecalActor>>>& H : Scorches) Marks += H.Value.Num();
	return FString::Printf(TEXT("%d jets, %d men, %d spots, %d scorches"), Jets, Men, Lit, Marks);
}

TArray<int32> UHrotDpsWorld::AnimalIds() const
{
	TArray<int32> Ids;
	for (const TPair<int32, FEnt>& E : Ents) if (E.Value.bAnimal && E.Value.Actor.IsValid()) Ids.Add(E.Key);
	Ids.Sort();
	return Ids;
}

TArray<int32> UHrotDpsWorld::JetShooterIds() const
{
	TArray<int32> Ids;
	for (const TPair<int32, FEnt>& E : Ents) if (E.Value.bJet) Ids.Add(E.Key);
	Ids.Sort();
	return Ids;
}

// ── U-18i: civilians, riders ──

UHrotPersonKit* UHrotDpsWorld::KitOfPerson(int32 Id, const FEnt& E) const
{
	// a CIVILIAN (DIS life form, category 2 — HROT's civilians; 1 = a soldier) gets a crowd body by its id: the same one on every channel
	TArray<FString> Parts; E.Dis.ParseIntoArray(Parts, TEXT("."));
	const bool bCivilian = Parts.Num() > 3 && Parts[3] == TEXT("2");
	if (bCivilian && CivilianKits.Num()) return CivilianKits[uint32(Id) % uint32(CivilianKits.Num())].LoadSynchronous();
	return PersonKit.Get();
}

void UHrotDpsWorld::UpdateRiding(FEnt& E)
{
	AActor* A = E.Actor.Get();
	if (!A) return;
	// HROT's seat (PersonState.SeatVehicleId / SeatIndex) on a mount that HAS seats here — an animal (HROT: a riding horse is a carrier,
	// seat 0 the rider, seat 1 a second rider) or an open vehicle seat (a motorcycle) — puts the rider ON it (U-17j SitOn: attached to
	// the mount, the seat's clip); a closed seat stays hidden inside (U-18d)
	AActor* Mount = nullptr;
	bool bAnimal = false;
	if (E.SeatVehicleId != 0) if (const FEnt* C = Ents.Find(E.SeatVehicleId)) { Mount = C->Actor.Get(); bAnimal = C->bAnimal; }
	const TArray<FHrotSeat> Seats = Mount ? UHrotRiding::SeatsOf(Mount) : TArray<FHrotSeat>();
	const int32 Seat = E.SeatIndex == 255 ? 0 : E.SeatIndex;
	// a kit names only OPEN seats (an animal's saddle, a motorcycle's driver and pillion — U-17j / U-17l); a closed seat (an APC's) has
	// none, so its man stays hidden inside. ⚠ HROT's SeatExposed is per seat RULE, not "visible": a pillion's firing seat says it, a
	// motorcycle driver's need not — the kit's seat list decides
	const bool bCan = Mount && Seats.IsValidIndex(Seat);
	if (E.bRiding && (!bCan || E.RidingOn.Get() != Mount || E.RidingSeat != E.SeatIndex))
	{
		UHrotRiding::GetOff(A);
		E.bRiding = false; E.RidingOn = nullptr; E.SeatPose = nullptr;
		Count(TEXT("rider got off"));
	}
	if (!E.bRiding && bCan && UHrotRiding::SitOn(A, Mount, Seat))
	{
		E.bRiding = true; E.RidingOn = Mount; E.RidingSeat = E.SeatIndex; E.SeatPose = Seats[Seat].Pose.Get();
		Count(FString::Printf(TEXT("rider on %s seat %d"), bAnimal ? TEXT("an animal") : TEXT("a vehicle"), Seat));
	}
	else if (!E.bRiding && E.SeatVehicleId != 0 && Mount && !bCan && bAnimal && !E.bNoSeatCounted)
	{
		E.bNoSeatCounted = true;
		Count(TEXT("not drawn: rider, the mount's kind has no such seat"));
	}
}

int32 UHrotDpsWorld::RidersNow() const
{
	int32 N = 0;
	for (const TPair<int32, FEnt>& E : Ents) N += E.Value.bRiding;
	return N;
}

TArray<int32> UHrotDpsWorld::RiderIds() const
{
	TArray<int32> Ids;
	for (const TPair<int32, FEnt>& E : Ents) if (E.Value.bRiding) Ids.Add(E.Key);
	Ids.Sort();
	return Ids;
}

int32 UHrotDpsWorld::WalkersDrawn() const
{
	int32 N = 0;
	for (const TPair<int32, FEnt>& W : Walkers) if (const AActor* A = W.Value.Actor.Get(); A && !A->IsHidden()) ++N;
	return N;
}

// ── U-18l: for a camera ──

bool UHrotDpsWorld::PoseNow(int32 EntityId, FVector& PositionM, FQuat& Rotation, int32& Domain) const
{
	const FEnt* E = Ents.Find(EntityId);
	if (!E || !E->bHasState) return false;
	const FHrotDpsPose P = PoseAt(*E, RealNow());
	PositionM = FVector(P.P); Rotation = FQuat(P.Q); Domain = E->Domain;
	return true;
}

int32 UHrotDpsWorld::EntityOfActor(const AActor* Actor) const
{
	if (!Actor) return 0;
	for (const TPair<int32, FEnt>& E : Ents) if (E.Value.Actor.Get() == Actor) return E.Key;
	return 0;
}

int32 UHrotDpsWorld::DomainPerson() const { return SimNetGateway_Protocol_DdsPlayer_DomainPerson; }
int32 UHrotDpsWorld::DomainAir() const { return SimNetGateway_Protocol_DdsPlayer_DomainAir; }
int32 UHrotDpsWorld::DomainAnimal() const { return SimNetGateway_Protocol_DdsPlayer_DomainAnimal; }
