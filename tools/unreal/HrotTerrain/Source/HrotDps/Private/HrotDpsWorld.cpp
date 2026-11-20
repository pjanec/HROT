// CE-4651 (U-18b) — see HrotDpsWorld.h
#include "HrotDpsWorld.h"
#include "HrotDpsNative.h"
#include "HrotDpsSubsystem.h"
#include "HrotVehicle.h"
#include "HrotShotRenderer.h"
#include "HrotExplosionFx.h"
#include "HrotMissile.h"
#include "NiagaraComponent.h"
#include "NiagaraFunctionLibrary.h"
#include "NiagaraDataInterfaceArrayFunctionLibrary.h"
#include "HrotPersonKit.h"
#include "HrotPersonAnimInstance.h"
#include "HrotTerrainLibrary.h"
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

DEFINE_LOG_CATEGORY_STATIC(LogHrotDpsWorld, Log, All);

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
	Dps->Stop();   // a fresh client: its new readers deliver the late-join history to THESE handlers
	if (!Dps->Start(Domain, Partition)) return false;
	FHrotDpsClient& C = Dps->Client();
	auto Sub = [this, &C](const TCHAR* Topic, void (UHrotDpsWorld::*Fn)(const FHrotDpsSample&))
	{
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
	UE_LOG(LogHrotDpsWorld, Display, TEXT("DPS world: joined domain %d partition '%s'"), Domain, *Partition);
	return true;
}

void UHrotDpsWorld::Stop()
{
	if (GEngine)
		if (UHrotDpsSubsystem* Dps = GEngine->GetEngineSubsystem<UHrotDpsSubsystem>())
			for (const TPair<FString, int32>& S : Subs) Dps->Client().Unsubscribe(S.Key, S.Value);
	Subs.Reset();
	Clear();
}

void UHrotDpsWorld::Deinitialize()
{
	const bool bOwned = Subs.Num() > 0;
	Stop();
	if (bOwned && GEngine) if (UHrotDpsSubsystem* Dps = GEngine->GetEngineSubsystem<UHrotDpsSubsystem>()) Dps->Stop();
	Super::Deinitialize();
}

void UHrotDpsWorld::Clear()
{
	TArray<int32> Ids; Ents.GetKeys(Ids);
	for (int32 Id : Ids) Forget(Id);
	Clock.Reset();
	bEpoch = false;
}

void UHrotDpsWorld::Forget(int32 Id)
{
	FEnt E;
	if (!Ents.RemoveAndCopyValue(Id, E)) return;
	if (AActor* A = E.Actor.Get()) { A->Destroy(); ++Removed; }
}

// ── samples ──

void UHrotDpsWorld::OnSession(const FHrotDpsSample& S)
{
	if (!S.bValid) return;
	const FSession& D = *static_cast<const FSession*>(S.Data);
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
	if (E.bHasState) { E.Blend = HrotDpsMotion::Correction(PoseAt(E, R), HrotDpsMotion::Extrapolate(K, Clock.Now(R))); E.BlendStart = R; }
	else E.Shown = HrotDpsMotion::Extrapolate(K, Clock.Now(R));
	E.Track = K;
	E.Bank = D.Bank;
	E.bHasState = true;
	const uint8 Damage = uint8(D.Damage);
	if (Damage != E.Damage)
	{
		E.Damage = Damage;
		if (AHrotVehicle* V = Cast<AHrotVehicle>(E.Actor.Get())) V->SetDamage(EHrotVehicleDamage(FMath::Min<uint8>(Damage, 2)));
	}
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
	switch (D.WeaponCategory)
	{
	case SimNetGateway_Protocol_DdsPlayer_WeaponCategoryTankGun:
	case SimNetGateway_Protocol_DdsPlayer_WeaponCategoryHowitzer:
		if (AHrotVehicle* V = Shooter ? Cast<AHrotVehicle>(Shooter->Actor.Get()) : nullptr)
		{
			V->FireMainGun();   // the kit's tuned flash, blast and smoke at its own muzzle (the turret follows Articulation)
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
			R->AddTracer(From, From + Dir(D.Velocity) * 50000.0);
			R->PlaySound(MgFx.Sound, From, MgFx.SoundAttenuation, MgFx.SoundConcurrency, MgFx.SoundVolume, MgFx.SoundPitch);
		}
		Count(TEXT("tracer"));
		return;
	case SimNetGateway_Protocol_DdsPlayer_WeaponCategorySmallArms:
		// HROT draws no tracer for small arms (FireStreams covers machine guns and autocannons only)
		if (R) R->PlaySound(RifleFx.Sound, From, RifleFx.SoundAttenuation, RifleFx.SoundConcurrency, RifleFx.SoundVolume, RifleFx.SoundPitch);
		Count(TEXT("rifle shot"));
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
		if (T->bHasPerson) { T->HitAt = Real; Count(TEXT("hit on a person")); }
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
		R->PlaySound(RifleFx.ImpactSound, At, RifleFx.ImpactAttenuation, RifleFx.ImpactConcurrency);
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
		if (V && V->Kit) M = V->MuzzleTransform();
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
			const float Cm = D.Size * 100.f;
			const float Life = Look == EBlastLook::Dust ? FMath::Max(D.LifetimeSeconds, 2.f) : FMath::Max(D.LifetimeSeconds * 3.f, 0.8f);
			const float Bright = D.Surface == SimNetGateway_Protocol_DdsPlayer_EffectSurfaceBuilding ? 0.75f : 0.55f;   // concrete dust lighter than soil
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
	case SimNetGateway_Protocol_DdsPlayer_EffectKindWreckSmoke: Count(TEXT("not drawn: wreck flame / smoke (the vehicle's own damage look)")); return;
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
	UHrotPersonKit* K = PersonKit.Get();
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
	UHrotPersonState* St = UHrotTerrainLibrary::AddPersonState(A);
	St->AimTurnRate = 0.f;   // the aim is eased here, on SIM time (HROT's 0.12 s), not by the anim instance's own rate
	St->WeaponSocket = K->WeaponSocket;
	St->WeaponOffset = K->WeaponOffset;
	if (const TObjectPtr<UMaterialInterface>* Mat = K->SideMaterials.Find(E.Side == 2 ? TEXT("B") : TEXT("A")))
	{
		for (const FName& Slot : K->SideSlots) St->MaterialOverrides.Add(Slot, *Mat);
		St->ApplyMaterials();
	}
	E.DrawnAs = FString::Printf(TEXT("person %s"), *K->GetName());
	E.Actor = A;   // ApplyPerson reads it
	ApplyPerson(E, Clock.Now(RealNow()), 0.0);
	St->ApplyAnimation();
	return A;
}

void UHrotDpsWorld::ApplyPerson(FEnt& E, double SimNow, double Dt)
{
	UHrotPersonKit* K = PersonKit.Get();
	AActor* A = E.Actor.Get();
	UHrotPersonState* St = A ? A->FindComponentByClass<UHrotPersonState>() : nullptr;
	if (!K || !St) return;
	HrotDpsPeople::FIn In = E.Person;
	In.Now = SimNow;
	In.Speed = float(FVector2D(E.Track.V.X, E.Track.V.Y).Size());
	In.LastGait = E.LastGait;
	const HrotDpsPeople::FLook L = HrotDpsPeople::Look(In);
	E.LastGait = L.Gait;

	// the body clip: the role, or its fallback chain when the kit has none
	FName Role = L.Base;
	UAnimSequence* Base = K->Clip(Role);
	for (int32 I = 0; !Base && !Role.IsNone() && I < 6; ++I) { Role = HrotDpsPeople::Fallback(Role); Base = K->Clip(Role); }
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
	if (L.bTransition && !L.bLoops && Base && E.Person.StanceSeconds > 0) Rate = float(Base->GetPlayLength()) / E.Person.StanceSeconds;
	else if (L.Gait != HrotDpsPeople::EGait::Idle && L.bLoops)
	{
		const float Native = L.Stance == HrotDpsPeople::Prone ? K->CrawlSpeed : L.Stance == HrotDpsPeople::Crouched ? K->CrouchSpeed
		                   : L.Gait == HrotDpsPeople::EGait::Run ? K->RunSpeed : K->WalkSpeed;
		Rate = FMath::Clamp(In.Speed / FMath::Max(Native, 0.1f), 0.6f, 1.8f);
	}
	St->PlayRate = Rate * float(Clock.GetScale());
	St->UpperAnim = L.Upper.IsNone() ? nullptr : K->Clip(L.Upper);

	UStreamableRenderAsset* Weapon = L.bWeapon ? K->Rifle.Get() : nullptr;
	if (St->Weapon != Weapon) { St->Weapon = Weapon; St->ApplyWeapon(); }
	if (A->IsHidden() != L.bHidden) A->SetActorHiddenInGame(L.bHidden);

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
	if (E.Domain == SimNetGateway_Protocol_DdsPlayer_DomainAnimal) { E.DrawnAs = TEXT("not drawn yet"); return; }
	if (E.Domain == SimNetGateway_Protocol_DdsPlayer_DomainMunition && !E.bRound) { E.DrawnAs = TEXT("(waiting for its Round)"); return; }
	UWorld* W = GetWorld();
	FTransform At = HrotDpsMotion::ToUnreal(E.Shown, OriginCm);
	FActorSpawnParameters P;
	P.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
	AActor* A = nullptr;
	if (E.Domain == SimNetGateway_Protocol_DdsPlayer_DomainMunition)
		A = SpawnRound(E, At);
	else if (E.Domain == SimNetGateway_Protocol_DdsPlayer_DomainPerson && PersonKit.Get())
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
	else   // ⭐ never invisible: a box of the domain's rough size
	{
		AStaticMeshActor* B = W->SpawnActor<AStaticMeshActor>(At.GetLocation(), At.Rotator(), P);
		B->SetMobility(EComponentMobility::Movable);
		B->GetStaticMeshComponent()->SetStaticMesh(LoadObject<UStaticMesh>(nullptr, TEXT("/Engine/BasicShapes/Cube.Cube")));
		const bool bPerson = E.Domain == SimNetGateway_Protocol_DdsPlayer_DomainPerson;
		B->GetStaticMeshComponent()->SetRelativeScale3D(bPerson ? FVector(0.4, 0.4, 1.8) : FVector(5, 2.5, 2));
		B->GetStaticMeshComponent()->SetRelativeLocation(FVector(0, 0, bPerson ? 90 : 100));
		E.DrawnAs = TEXT("box");
		A = B;
	}
#if WITH_EDITOR
	A->SetActorLabel(FString::Printf(TEXT("DPS_%d_%s"), Id, *E.Name));
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
	const double Prev = Placed;
	Placed = Clock.Now(R);
	for (TPair<int32, FEnt>& It : Ents) if (It.Value.bHasState) Place(It.Value, R);
	const double Dt = FMath::Clamp(Placed - Prev, 0.0, 0.5);
	for (TPair<int32, FEnt>& It : Ents) if (It.Value.bHasPerson && It.Value.Actor.IsValid()) ApplyPerson(It.Value, Placed, Dt);   // after every pose: an aim reads its target's
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
