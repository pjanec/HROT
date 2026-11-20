// CE-4651 (U-18b) — see HrotDpsWorld.h
#include "HrotDpsWorld.h"
#include "HrotDpsNative.h"
#include "HrotDpsSubsystem.h"
#include "HrotVehicle.h"
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
	if (E.Domain == SimNetGateway_Protocol_DdsPlayer_DomainMunition || E.Domain == SimNetGateway_Protocol_DdsPlayer_DomainAnimal)
	{
		E.DrawnAs = TEXT("not drawn yet");
		return;
	}
	UWorld* W = GetWorld();
	FTransform At = HrotDpsMotion::ToUnreal(E.Shown, OriginCm);
	FActorSpawnParameters P;
	P.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
	AActor* A = nullptr;
	if (E.Domain == SimNetGateway_Protocol_DdsPlayer_DomainPerson && !PersonMesh.IsNull())
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
		A->SetActorTransform(T);
	}
}

void UHrotDpsWorld::Tick(float)
{
	const double R = RealNow();
	Placed = Clock.Now(R);
	for (TPair<int32, FEnt>& It : Ents) if (It.Value.bHasState) Place(It.Value, R);
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
