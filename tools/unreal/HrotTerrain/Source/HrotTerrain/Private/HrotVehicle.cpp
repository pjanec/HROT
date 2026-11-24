// CE-4651 (U-17) — see the header.
#include "HrotVehicle.h"
#include "HrotRuntimeLibrary.h"
#include "Materials/MaterialInstanceDynamic.h"

#include "Components/SkeletalMeshComponent.h"
#include "Components/StaticMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/StaticMesh.h"
#include "Engine/World.h"
#include "HrotFire.h"
#include "HrotShotRenderer.h"
#include "Kismet/GameplayStatics.h"
#include "Materials/MaterialInterface.h"
#include "Particles/ParticleSystem.h"
#include "Particles/ParticleEmitter.h"
#include "Particles/ParticleSystemComponent.h"

namespace
{
	// ⭐ the look of damage: Realistic Starter VFX Pack Vol 2 (Cascade) + our burnt material (scripts/unreal/hrot_shotfx_assets.py)
	const TCHAR* SmokeFx = TEXT("/Game/Realistic_Starter_VFX_Pack_Vol2/Particles/Smoke/P_Smoke_A.P_Smoke_A");
	const TCHAR* MuzzleSmokeFx = TEXT("/Game/Realistic_Starter_VFX_Pack_Vol2/Particles/Explosion/P_Explosion_Smoke.P_Explosion_Smoke");
	const float MuzzleSmokeScale = 1.5f;
	// ⚠ measured (the vehicle lab): P_Smoke_A ×1 from the engine deck read as a faint wisp — ×3 for "damaged", the wreck's ×4
	const float DamagedSmokeScale = 3.f, WreckSmokeScale = 4.f;
	const TCHAR* BurntMaterial = TEXT("/Game/Hrot/Fx/M_HrotBurnt.M_HrotBurnt");
	// a destroyed vehicle's turret: lifted and knocked askew (a fixed pose — the same on every IG channel)
	const FVector KnockedLift(0.f, 0.f, 12.f);
	const FRotator KnockedTilt(-4.f, 9.f, 6.f);

	template <class T> T* Load(const TCHAR* Path)
	{
		return LoadObject<T>(nullptr, Path);
	}
}

AHrotVehicle::AHrotVehicle()
{
	PrimaryActorTick.bCanEverTick = true;    // ⭐ but OFF unless flying with wings (U-17i): the state moves the pivots when it changes
	PrimaryActorTick.bStartWithTickEnabled = false;
	Hull = CreateDefaultSubobject<USceneComponent>(TEXT("Hull"));
	RootComponent = Hull;
	Lift = CreateDefaultSubobject<USceneComponent>(TEXT("Lift"));
	Lift->SetupAttachment(Hull);
	Turret = CreateDefaultSubobject<USceneComponent>(TEXT("Turret"));
	Turret->SetupAttachment(Lift);
	Gun = CreateDefaultSubobject<USceneComponent>(TEXT("Gun"));
	Gun->SetupAttachment(Turret);
	WingLeft = CreateDefaultSubobject<USceneComponent>(TEXT("WingLeft"));
	WingLeft->SetupAttachment(Lift);
	WingRight = CreateDefaultSubobject<USceneComponent>(TEXT("WingRight"));
	WingRight->SetupAttachment(Lift);
	MainRotor = CreateDefaultSubobject<USceneComponent>(TEXT("MainRotor"));
	MainRotor->SetupAttachment(Lift);
	TailRotor = CreateDefaultSubobject<USceneComponent>(TEXT("TailRotor"));
	TailRotor->SetupAttachment(Lift);
}

void AHrotVehicle::OnConstruction(const FTransform& Transform)
{
	Super::OnConstruction(Transform);
	Build();
}

void AHrotVehicle::BeginPlay()
{
	Super::BeginPlay();
	// ⚠ measured: the part components are transient (rebuilt, never saved), so a play-mode COPY of the editor's vehicle arrives
	// without them (only the pivots) — it builds its own
	if (PartComps.Num() == 0 && !SkinnedComp) Build();
}

void AHrotVehicle::Build()
{
	for (UStaticMeshComponent* C : PartComps) if (C) C->DestroyComponent();
	PartComps.Reset(); PartOfComp.Reset();
	if (SkinnedComp) { SkinnedComp->DestroyComponent(); SkinnedComp = nullptr; }
	ClearDamageFx();
	if (Flame) { Flame->DestroyComponent(); Flame = nullptr; }
	if (RotorDisc) { RotorDisc->DestroyComponent(); RotorDisc = nullptr; }
	Shown = EHrotVehicleDamage::Intact;
	if (!Kit) return;
	MainRotor->SetRelativeTransform(Kit->MainRotorPivot);
	TailRotor->SetRelativeTransform(Kit->TailRotorPivot);
	if (Kit->RotorRadius > 0.f)   // the blur: a thin translucent disc in the main rotor's plane, shown while it turns
	{
		static UStaticMesh* Cyl = HrotLoadRooted<UStaticMesh>(TEXT("/Engine/BasicShapes/Cylinder.Cylinder"));
		static UMaterialInterface* DiscMat = HrotLoadRooted<UMaterialInterface>(TEXT("/Game/Hrot/Fx/M_HrotRotorDisc.M_HrotRotorDisc"));
		if (Cyl && DiscMat)
		{
			RotorDisc = NewObject<UStaticMeshComponent>(this, NAME_None, RF_Transient);
			RotorDisc->CreationMethod = EComponentCreationMethod::UserConstructionScript;
			RotorDisc->SetMobility(EComponentMobility::Movable);
			RotorDisc->SetStaticMesh(Cyl);
			RotorDisc->SetMaterial(0, DiscMat);
			RotorDisc->SetupAttachment(Lift);
			FTransform D = Kit->MainRotorPivot;   // the cylinder is 100 cm wide and 100 cm tall, its centre at its middle
			D.SetScale3D(FVector(Kit->RotorRadius * 2.f / 100.f, Kit->RotorRadius * 2.f / 100.f, 0.01f));
			RotorDisc->SetRelativeTransform(D);
			RotorDisc->SetCollisionEnabled(ECollisionEnabled::NoCollision);
			RotorDisc->SetCastShadow(false);
			RotorDisc->SetVisibility(false);
			RotorDisc->RegisterComponent();
		}
	}
	if (Kit->bFlamethrower) Flame = UHrotFlamethrower::Attach(Lift, NAME_None, Kit->FlameMount, Kit->FlameSize);
	if (Kit->SkinnedBody)   // the motorcycle: one skinned mesh in its rest pose
	{
		SkinnedComp = NewObject<USkeletalMeshComponent>(this, NAME_None, RF_Transient);
		SkinnedComp->CreationMethod = EComponentCreationMethod::UserConstructionScript;
		SkinnedComp->SetSkeletalMeshAsset(Kit->SkinnedBody);
		SkinnedComp->SetupAttachment(Lift);
		SkinnedComp->SetRelativeTransform(Kit->SkinnedTransform);
		SkinnedComp->SetCollisionEnabled(ECollisionEnabled::NoCollision);
		SkinnedComp->VisibilityBasedAnimTickOption = EVisibilityBasedAnimTickOption::OnlyTickPoseWhenRendered;
		SkinnedComp->RegisterComponent();
	}
	for (int32 PartIndex = 0; PartIndex < Kit->Parts.Num(); ++PartIndex)
	{
		const FHrotVehiclePart& P = Kit->Parts[PartIndex];
		if (!P.Mesh) continue;
		UStaticMeshComponent* C = NewObject<UStaticMeshComponent>(this, NAME_None, RF_Transient);
		C->CreationMethod = EComponentCreationMethod::UserConstructionScript;   // rebuilt with the actor, never saved
		C->SetMobility(EComponentMobility::Movable);
		C->SetStaticMesh(P.Mesh);
		for (int32 I = 0; I < P.Materials.Num(); ++I) if (P.Materials[I]) C->SetMaterial(I, P.Materials[I]);   // a livery (U-18k)
		USceneComponent* On = Lift.Get();
		switch (P.Parent)
		{
		case EHrotVehicleParent::Turret: On = Turret.Get(); break;
		case EHrotVehicleParent::Gun: On = Gun.Get(); break;
		case EHrotVehicleParent::WingLeft: On = WingLeft.Get(); break;
		case EHrotVehicleParent::WingRight: On = WingRight.Get(); break;
		case EHrotVehicleParent::MainRotor: On = MainRotor.Get(); break;
		case EHrotVehicleParent::TailRotor: On = TailRotor.Get(); break;
		default: break;
		}
		C->SetupAttachment(On);
		// a rotor part keeps its place in the hull frame: its transform is the kit's (Lift-relative) one seen from the rotor's pivot
		C->SetRelativeTransform(P.Parent == EHrotVehicleParent::MainRotor ? P.Transform.GetRelativeTransform(Kit->MainRotorPivot)
			: P.Parent == EHrotVehicleParent::TailRotor ? P.Transform.GetRelativeTransform(Kit->TailRotorPivot) : P.Transform);
		C->SetCollisionEnabled(ECollisionEnabled::QueryOnly);   // a hit test may need it; no physics
		C->RegisterComponent();
		PartComps.Add(C); PartOfComp.Add(PartIndex);
	}
	if (bPainted) SetPaint(Paint);
	ApplyState();
}

int32 AHrotVehicle::SetPaint(FLinearColor NewPaint)
{
	Paint = NewPaint; bPainted = true;
	if (!Kit || Kit->PaintSlot.IsNone()) return 0;
	int32 N = 0;
	for (UStaticMeshComponent* C : PartComps)
	{
		if (!C) continue;
		const int32 I = C->GetMaterialIndex(Kit->PaintSlot);
		if (I == INDEX_NONE) continue;
		UMaterialInstanceDynamic* M = Cast<UMaterialInstanceDynamic>(C->GetMaterial(I));
		if (!M) M = C->CreateDynamicMaterialInstance(I, Kit->PaintMaterial ? Kit->PaintMaterial.Get() : nullptr);
		if (!M) continue;
		M->SetVectorParameterValue(Kit->PaintParam, Paint);
		++N;
	}
	return N;
}

void AHrotVehicle::SetAim(float Yaw, float Pitch)
{
	TurretYaw = Yaw; GunPitch = Pitch;
	ApplyState();
}

bool AHrotVehicle::AimSeatGun(int32 Seat, FVector WorldTarget)
{
	if (!Kit || Seat < 0) return false;
	bool bAny = false;
	for (int32 I = 0; I < PartComps.Num() && I < PartOfComp.Num(); ++I)
	{
		const FHrotVehiclePart& P = Kit->Parts[PartOfComp[I]];
		UStaticMeshComponent* C = PartComps[I];
		if (P.AimSeat != Seat || !C || P.Parent != EHrotVehicleParent::Hull) continue;
		// in the Lift's frame: the pivot and the barrel of the kit pose, the wanted direction, the yaw / pitch between them (clamped)
		const FTransform Base = P.Transform;
		const FVector Pivot = Base.TransformPosition(P.AimPivot);
		const FVector Barrel = Base.TransformVectorNoScale(FVector(0, 1, 0)).GetSafeNormal();
		const FVector Want = (Lift->GetComponentTransform().InverseTransformPosition(WorldTarget) - Pivot).GetSafeNormal();
		if (Want.IsNearlyZero()) continue;
		const float Yaw = FMath::Clamp(FMath::FindDeltaAngleDegrees(Barrel.Rotation().Yaw, Want.Rotation().Yaw), P.AimYawLimits.X, P.AimYawLimits.Y);
		const float Pitch = FMath::Clamp(Want.Rotation().Pitch - Barrel.Rotation().Pitch, P.AimPitchLimits.X, P.AimPitchLimits.Y);
		// yaw about the Lift's Z, then pitch about the barrel's own horizontal side axis, both through the pivot
		const FQuat QYaw(FVector::UpVector, FMath::DegreesToRadians(Yaw));
		const FVector Side = FVector::CrossProduct(QYaw.RotateVector(Barrel), FVector::UpVector).GetSafeNormal();
		const FQuat QPitch(Side, FMath::DegreesToRadians(-Pitch));
		const FQuat Q = QPitch * QYaw;
		FTransform T = Base;
		T.SetRotation(Q * Base.GetRotation());
		T.SetTranslation(Pivot + Q.RotateVector(Base.GetTranslation() - Pivot));
		C->SetRelativeTransform(T);
		bAny = true;
	}
	return bAny;
}

void AHrotVehicle::SetFlying(bool bNewFlying)
{
	bFlying = bNewFlying;
	ApplyState();
}

void AHrotVehicle::ApplyWings(float Angle)
{
	if (!Kit) return;
	// both wings UP together: the left wing (−Y) rolls −Angle, the right (+Y) +Angle, about the hull's X through each shoulder
	FTransform L = Kit->WingLeftPivot, R = Kit->WingRightPivot;
	L.SetRotation(FRotator(0.f, 0.f, -Angle).Quaternion() * L.GetRotation());
	R.SetRotation(FRotator(0.f, 0.f, Angle).Quaternion() * R.GetRotation());
	WingLeft->SetRelativeTransform(L);
	WingRight->SetRelativeTransform(R);
}

void AHrotVehicle::Tick(float DeltaTime)
{
	Super::Tick(DeltaTime);
	if (!Kit) return;
	FlapClock += DeltaTime;
	if (bFlying && Damage != EHrotVehicleDamage::Destroyed && (Kit->MainRotorRate != 0.f || Kit->TailRotorRate != 0.f))
	{
		RotorAngle = FMath::Fmod(RotorAngle + DeltaTime, 3600.f);
		FTransform M = Kit->MainRotorPivot; M.SetRotation(M.GetRotation() * FRotator(0.f, FMath::Fmod(RotorAngle * Kit->MainRotorRate, 360.f), 0.f).Quaternion());
		MainRotor->SetRelativeTransform(M);
		FTransform T = Kit->TailRotorPivot; T.SetRotation(T.GetRotation() * FRotator(0.f, FMath::Fmod(RotorAngle * Kit->TailRotorRate, 360.f), 0.f).Quaternion());
		TailRotor->SetRelativeTransform(T);
	}
	if (Kit->FlapAmplitude <= 0.f) return;
	const float S = FMath::Sin(2.f * PI * Kit->FlapRate * FlapClock);
	ApplyWings(Kit->FlapAmplitude * S);
	// wings down → body high (the downstroke lifts). ⚠ measured (five side pictures): a positive beat angle lowers the wings — with
	// −Bob·S the body rose with the wings, the wrong way round
	Lift->SetRelativeLocation(FVector(0.f, 0.f, Kit->FlapBob * S));
}

void AHrotVehicle::SetFlaming(bool bNewFlaming)
{
	bFlaming = bNewFlaming;
	ApplyState();
}

void AHrotVehicle::SetDamage(EHrotVehicleDamage NewDamage)
{
	Damage = NewDamage;
	ApplyState();
}

void AHrotVehicle::ApplyState()
{
	if (!Kit) return;
	const bool bWreck = Damage == EHrotVehicleDamage::Destroyed;
	FTransform T = Kit->TurretPivot;
	T.SetRotation(T.GetRotation() * FRotator(0.f, TurretYaw, 0.f).Quaternion());
	if (bWreck) { T.AddToTranslation(KnockedLift); T.SetRotation(T.GetRotation() * KnockedTilt.Quaternion()); }
	Turret->SetRelativeTransform(T);
	FTransform G = Kit->GunPivot;
	G.SetRotation(G.GetRotation() * FRotator(GunPitch, 0.f, 0.f).Quaternion());
	Gun->SetRelativeTransform(G);
	if (Flame) Flame->SetFiring(bFlaming && !bWreck);
	const bool bFlap = bFlying && !bWreck && Kit->FlapAmplitude > 0.f;
	if (!bFlap) { ApplyWings(0.f); Lift->SetRelativeLocation(FVector::ZeroVector); }   // at rest: the modelled pose, no bob
	const bool bRotors = bFlying && !bWreck && (Kit->MainRotorRate != 0.f || Kit->TailRotorRate != 0.f);
	if (RotorDisc) RotorDisc->SetVisibility(bRotors);
	SetActorTickEnabled(bFlap || bRotors);
	if (Damage == Shown) return;

	// ---- the damage look, only when it changes ----------------------------------------------------------------------------------
	ClearDamageFx();
	static UMaterialInterface* Burnt = HrotLoadRooted<UMaterialInterface>(BurntMaterial);   // rooted: a plain static of an unrooted asset dangled once it was collected (crash in ApplyState, live 2026-11-23)
	for (UStaticMeshComponent* C : PartComps)
	{
		if (!C) continue;
		for (int32 I = 0; I < C->GetNumMaterials(); ++I) C->SetMaterial(I, bWreck ? Burnt : nullptr);   // nullptr = the mesh's own
	}
	if (SkinnedComp)
		for (int32 I = 0; I < SkinnedComp->GetNumMaterials(); ++I) SkinnedComp->SetMaterial(I, bWreck ? Burnt : nullptr);
	auto Attach = [this](const TCHAR* Path, float Scale)
	{
		Scale *= Kit->DamageFxScale;
		if (UParticleSystem* PS = Load<UParticleSystem>(Path))
			if (UParticleSystemComponent* C = UGameplayStatics::SpawnEmitterAttached(PS, Hull, NAME_None, Kit->EngineDeck, FRotator::ZeroRotator,
			                                                                        FVector(Scale), EAttachLocation::KeepRelativeOffset, false))
				DamageFx.Add(C);
	};
	if (Damage == EHrotVehicleDamage::Damaged) Attach(SmokeFx, DamagedSmokeScale);
	// ⭐ the wreck burns through the ONE fire implementation (UHrotBurning, U-17f): one fire + its smoke at the engine deck
	if (bWreck) { if (UHrotBurning* B = UHrotBurning::SetBurningAt(this, Kit->EngineDeck, Kit->DamageFxScale)) { B->SmokeScale = WreckSmokeScale; B->Apply(); } }
	else UHrotBurning::SetBurning(this, 0.f);
	Shown = Damage;
}

void AHrotVehicle::ClearDamageFx()
{
	for (UFXSystemComponent* C : DamageFx) if (C) C->DestroyComponent();
	DamageFx.Reset();
}

FTransform AHrotVehicle::MuzzleTransform() const
{
	return Kit ? FTransform(Kit->Muzzle) * Gun->GetComponentTransform() : Gun->GetComponentTransform();
}

void AHrotVehicle::FireGun(const FHrotShotFx& Fx, const TArray<FVector>& Hits, const TArray<FVector>& HitNormals)
{
	UHrotWeaponFx::FireShotAt(Gun, MuzzleTransform(), Fx, Hits, HitNormals);
}

void AHrotVehicle::FireMainGun()
{
	UHrotShotRenderer* R = UHrotShotRenderer::Get(this);
	if (!R || !Kit) return;
	const FTransform M = MuzzleTransform();
	R->AddFlash(M, Kit->FlashSize, Kit->FlashLife);   // a big gun's flash lasts longer than a rifle's (0.06 s)
	R->Flash(M.GetLocation(), 400000.f, 4000.f, Kit->FlashLife);   // seen from afar too
	if (FVector::Dist(M.GetLocation(), R->ViewLocation()) < R->BlastRange && R->TakeBudget(UHrotShotRenderer::EExtra::Blast))
	{
		// ⭐ the muzzle BLAST: a small fireball ahead of the muzzle + the smoke cloud that hangs in front of a tank after a shot
		// ⚠ measured: P_Explosion_Big_A at the muzzle threw firework sparks (its debris) — the flash cards + the smoke only
		const FVector Ahead = M.GetLocation() + M.GetRotation().GetForwardVector() * 150.f;
		if (UParticleSystem* PS = Load<UParticleSystem>(MuzzleSmokeFx))
			if (UParticleSystemComponent* C = UGameplayStatics::SpawnEmitterAtLocation(this, PS, Ahead, FRotator::ZeroRotator, FVector(MuzzleSmokeScale),
			                                                                         true, EPSCPoolMethod::AutoRelease))
				// ⚠ measured: P_Explosion_Smoke threw firework sparks too (its own spark / ember emitters) — the smoke only
				for (const UParticleEmitter* Em : PS->Emitters)
				{
					const FString N = Em ? Em->EmitterName.ToString() : FString();
					static TSet<FString> Logged;
					if (!Logged.Contains(N)) { Logged.Add(N); UE_LOG(LogTemp, Warning, TEXT("[HROT-VEHICLE] muzzle smoke emitter '%s'"), *N); }
					if (N.Contains(TEXT("Spark")) || N.Contains(TEXT("Ember")) || N.Contains(TEXT("Debris")) || N.Contains(TEXT("Flare")))
						C->SetEmitterEnable(Em->EmitterName, false);
				}
	}
}
