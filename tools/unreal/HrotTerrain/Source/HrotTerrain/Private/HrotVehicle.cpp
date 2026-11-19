// CE-4651 (U-17) — see the header.
#include "HrotVehicle.h"

#include "Components/StaticMeshComponent.h"
#include "Engine/StaticMesh.h"
#include "Engine/World.h"
#include "HrotShotRenderer.h"
#include "Kismet/GameplayStatics.h"
#include "Materials/MaterialInterface.h"
#include "Particles/ParticleSystem.h"
#include "Particles/ParticleSystemComponent.h"

namespace
{
	// ⭐ the look of damage: Realistic Starter VFX Pack Vol 2 (Cascade) + our burnt material (scripts/unreal/hrot_shotfx_assets.py)
	const TCHAR* SmokeFx = TEXT("/Game/Realistic_Starter_VFX_Pack_Vol2/Particles/Smoke/P_Smoke_A.P_Smoke_A");
	const TCHAR* FireFx = TEXT("/Game/Realistic_Starter_VFX_Pack_Vol2/Particles/Fire/P_Fire_Big.P_Fire_Big");
	const TCHAR* MuzzleSmokeFx = TEXT("/Game/Realistic_Starter_VFX_Pack_Vol2/Particles/Explosion/P_Explosion_Smoke.P_Explosion_Smoke");
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
	PrimaryActorTick.bCanEverTick = false;   // ⭐ nothing per frame: the state moves two pivots when it changes
	Hull = CreateDefaultSubobject<USceneComponent>(TEXT("Hull"));
	RootComponent = Hull;
	Turret = CreateDefaultSubobject<USceneComponent>(TEXT("Turret"));
	Turret->SetupAttachment(Hull);
	Gun = CreateDefaultSubobject<USceneComponent>(TEXT("Gun"));
	Gun->SetupAttachment(Turret);
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
	if (PartComps.Num() == 0) Build();
}

void AHrotVehicle::Build()
{
	for (UStaticMeshComponent* C : PartComps) if (C) C->DestroyComponent();
	PartComps.Reset();
	ClearDamageFx();
	Shown = EHrotVehicleDamage::Intact;
	if (!Kit) return;
	for (const FHrotVehiclePart& P : Kit->Parts)
	{
		if (!P.Mesh) continue;
		UStaticMeshComponent* C = NewObject<UStaticMeshComponent>(this, NAME_None, RF_Transient);
		C->CreationMethod = EComponentCreationMethod::UserConstructionScript;   // rebuilt with the actor, never saved
		C->SetMobility(EComponentMobility::Movable);
		C->SetStaticMesh(P.Mesh);
		C->SetupAttachment(P.Parent == EHrotVehicleParent::Turret ? Turret.Get() : P.Parent == EHrotVehicleParent::Gun ? Gun.Get() : Hull.Get());
		C->SetRelativeTransform(P.Transform);
		C->SetCollisionEnabled(ECollisionEnabled::QueryOnly);   // a hit test may need it; no physics
		C->RegisterComponent();
		PartComps.Add(C);
	}
	ApplyState();
}

void AHrotVehicle::SetAim(float Yaw, float Pitch)
{
	TurretYaw = Yaw; GunPitch = Pitch;
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
	if (Damage == Shown) return;

	// ---- the damage look, only when it changes ----------------------------------------------------------------------------------
	ClearDamageFx();
	static UMaterialInterface* Burnt = Load<UMaterialInterface>(BurntMaterial);
	for (UStaticMeshComponent* C : PartComps)
	{
		if (!C) continue;
		for (int32 I = 0; I < C->GetNumMaterials(); ++I) C->SetMaterial(I, bWreck ? Burnt : nullptr);   // nullptr = the mesh's own
	}
	auto Attach = [this](const TCHAR* Path, float Scale)
	{
		if (UParticleSystem* PS = Load<UParticleSystem>(Path))
			if (UParticleSystemComponent* C = UGameplayStatics::SpawnEmitterAttached(PS, Hull, NAME_None, Kit->EngineDeck, FRotator::ZeroRotator,
			                                                                        FVector(Scale), EAttachLocation::KeepRelativeOffset, false))
				DamageFx.Add(C);
	};
	if (Damage == EHrotVehicleDamage::Damaged) Attach(SmokeFx, 1.0f);
	if (bWreck) { Attach(FireFx, 1.0f); Attach(SmokeFx, 1.5f); }
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

void AHrotVehicle::FireMainGun()
{
	UHrotShotRenderer* R = UHrotShotRenderer::Get(this);
	if (!R || !Kit) return;
	const FTransform M = MuzzleTransform();
	R->AddFlash(M, Kit->FlashSize);
	R->Flash(M.GetLocation(), 300000.f, 3000.f, 0.1f);   // seen from afar too
	if (FVector::Dist(M.GetLocation(), R->ViewLocation()) < R->BlastRange && R->TakeBudget(UHrotShotRenderer::EExtra::Blast))
		if (UParticleSystem* PS = Load<UParticleSystem>(MuzzleSmokeFx))
			UGameplayStatics::SpawnEmitterAtLocation(this, PS, M.GetLocation(), M.Rotator(), FVector(0.6f), true, EPSCPoolMethod::AutoRelease);
}
