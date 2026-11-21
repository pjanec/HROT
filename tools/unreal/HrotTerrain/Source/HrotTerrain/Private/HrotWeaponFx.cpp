// CE-4651 (U-16) — see the header.
#include "HrotWeaponFx.h"
#include "HrotRuntimeLibrary.h"
#include "HrotShotRenderer.h"

#include "Components/SceneComponent.h"
#include "Components/StaticMeshComponent.h"
#include "Engine/StaticMesh.h"
#include "NiagaraComponent.h"
#include "NiagaraDataInterfaceArrayFunctionLibrary.h"
#include "NiagaraFunctionLibrary.h"
#include "NiagaraSystem.h"
#include "Sound/SoundAttenuation.h"
#include "Sound/SoundBase.h"
#include "Sound/SoundConcurrency.h"


FHrotShotFx UHrotWeaponFx::LyraRifleFx()
{
	// Lyra's B_WeaponFire uses NS_WeaponFire as the rifle's muzzle flash (flash cards + smoke), NS_WeaponFire_Tracer, and
	// NS_WeaponFire_ShellEject; B_WeaponImpacts uses NS_ImpactConcrete for the default surface.
	FHrotShotFx Fx;
	// ⚠ B_WeaponFire's flash is NS_WeaponFire (measured: NS_WeaponFire_MuzzleFlash_Rifle spawned with the same parameters showed nothing)
	Fx.MuzzleFlash = LoadObject<UNiagaraSystem>(nullptr, TEXT("/Game/Effects/Particles/Weapons/NS_WeaponFire.NS_WeaponFire"));
	Fx.Tracer = LoadObject<UNiagaraSystem>(nullptr, TEXT("/Game/Effects/Particles/Weapons/NS_WeaponFire_Tracer.NS_WeaponFire_Tracer"));
	Fx.ShellEject = LoadObject<UNiagaraSystem>(nullptr, TEXT("/Game/Effects/Particles/Weapons/NS_WeaponFire_ShellEject.NS_WeaponFire_ShellEject"));
	Fx.Impact = LoadObject<UNiagaraSystem>(nullptr, TEXT("/Game/Effects/Particles/Impacts/NS_ImpactConcrete.NS_ImpactConcrete"));
	// ⭐ Lyra's own sounds (/Game/Audio, copied from the Lyra project, U-17k): the rifle's MetaSound (close + distant layers), its
	// attenuation and its concurrency (the farthest guns stop when too many fire); the plaster impact
	Fx.Sound = HrotLoadRooted<USoundBase>(TEXT("/Game/Audio/Sounds/Weapons/Rifle/MSS_Weapons_Rifle_Fire.MSS_Weapons_Rifle_Fire"));
	Fx.SoundAttenuation = HrotLoadRooted<USoundAttenuation>(TEXT("/Game/Audio/AttenuationPresets/ATT_Rifle.ATT_Rifle"));
	Fx.SoundConcurrency = HrotLoadRooted<USoundConcurrency>(TEXT("/Game/Audio/Concurrency/SCON_Guns_StopFarthest.SCON_Guns_StopFarthest"));
	Fx.ImpactSound = HrotLoadRooted<USoundBase>(TEXT("/Game/Audio/MetaSounds/sfx_ImpactDefault_nl_meta_Preset.sfx_ImpactDefault_nl_meta_Preset"));
	Fx.ImpactAttenuation = HrotLoadRooted<USoundAttenuation>(TEXT("/Game/Audio/AttenuationPresets/ATT_FX_BulletImpact.ATT_FX_BulletImpact"));
	Fx.ImpactConcurrency = HrotLoadRooted<USoundConcurrency>(TEXT("/Game/Audio/Concurrency/SCON_Impacts.SCON_Impacts"));
	return Fx;
}

FTransform UHrotWeaponFx::MuzzleTransform(USceneComponent* Weapon, FName Socket)
{
	if (!Weapon) return FTransform::Identity;
	if (!Socket.IsNone() && Weapon->DoesSocketExist(Socket)) return Weapon->GetSocketTransform(Socket);
	// no socket: a GUESS — the barrel is the mesh's longest local axis, the muzzle its + end. ⚠ Measured wrong for Lyra's SM_Rifle
	// (its barrel is −Y: the SM is the SK turned 180°) — hold a mesh WITH a Muzzle socket (Lyra's SK_Rifle / SK_Pistol)
	FBox Local(ForceInit);
	if (const UStaticMeshComponent* SM = Cast<UStaticMeshComponent>(Weapon); SM && SM->GetStaticMesh())
		Local = SM->GetStaticMesh()->GetBoundingBox();
	if (!Local.IsValid) return Weapon->GetComponentTransform();
	const FVector Ext = Local.GetExtent();
	const int32 Axis = (Ext.X >= Ext.Y && Ext.X >= Ext.Z) ? 0 : (Ext.Y >= Ext.Z ? 1 : 2);
	FVector Tip = Local.GetCenter(), Dir = FVector::ZeroVector;
	Tip[Axis] = Local.Max[Axis]; Dir[Axis] = 1.0;
	const FTransform& W = Weapon->GetComponentTransform();
	return FTransform(FRotationMatrix::MakeFromX(W.TransformVectorNoScale(Dir)).ToQuat(), W.TransformPosition(Tip));
}

FHrotShotFx UHrotWeaponFx::MinigunFx()
{
	FHrotShotFx Fx = LyraRifleFx();
	Fx.MuzzleSocket = NAME_None;
	Fx.TracerEvery = 1;          // every round: at 50 a second and 850 m/s the rounds fly ~17 m apart ...
	Fx.TracerLength = 900.f;     // ... 9 m streaks: a dashed stream (user, 2026-11-20: "they are great as they are")
	Fx.TracerWidth = 4.f;
	Fx.FlashSize = 1.6f;
	Fx.DustSize = 70.f; Fx.DustLife = 2.0f; Fx.DustBrightness = 0.4f;
	Fx.SoundPitch = 1.15f; Fx.SoundVolume = 0.8f;   // ⚠ a stand-in: the rifle, faster — no minigun sound yet
	return Fx;
}

FHrotShotFx UHrotWeaponFx::HeavyMgFx()
{
	FHrotShotFx Fx = LyraRifleFx();
	Fx.MuzzleSocket = NAME_None;
	Fx.TracerEvery = 5;          // ~10 rounds a second: two tracers a second, each one seen
	Fx.TracerLength = 350.f;
	Fx.TracerWidth = 7.f;
	Fx.FlashSize = 3.f;
	Fx.DustSize = 120.f; Fx.DustLife = 2.5f; Fx.DustBrightness = 0.4f;
	Fx.SoundPitch = 0.62f; Fx.SoundVolume = 1.6f;   // ⚠ a stand-in: the rifle, deeper and louder — no heavy MG sound yet
	return Fx;
}

FVector UHrotWeaponFx::FireShot(USceneComponent* Weapon, const FHrotShotFx& Fx, const TArray<FVector>& Hits, const TArray<FVector>& HitNormals)
{
	if (!Weapon || !Weapon->GetWorld()) return FVector::ZeroVector;
	return FireShotAt(Weapon, MuzzleTransform(Weapon, Fx.MuzzleSocket), Fx, Hits, HitNormals);
}

FVector UHrotWeaponFx::FireShotAt(USceneComponent* Weapon, FTransform Muzzle, const FHrotShotFx& Fx, const TArray<FVector>& Hits, const TArray<FVector>& HitNormals)
{
	if (!Weapon || !Weapon->GetWorld()) return FVector::ZeroVector;
	UWorld* World = Weapon->GetWorld();
	const FVector At = Muzzle.GetLocation(), Dir = Muzzle.GetRotation().GetForwardVector();
	UHrotShotRenderer* R = UHrotShotRenderer::Get(Weapon);
	// ⭐ EVERY shot: the flash and (1 round in N) the tracer — instanced, one draw call each for the whole world
	const int32 Round = R ? R->NextRound(Weapon) : 0;
	if (R)
	{
		R->AddFlash(Muzzle, Fx.FlashSize);
		R->PlaySound(Fx.Sound, At, Fx.SoundAttenuation, Fx.SoundConcurrency, Fx.SoundVolume, Fx.SoundPitch);
		if (Hits.Num() > 0) R->PlaySound(Fx.ImpactSound, Hits[0], Fx.ImpactAttenuation, Fx.ImpactConcurrency);
		const int32 Every = Fx.TracerEvery > 0 ? Fx.TracerEvery : R->TracerEvery;
		if (Every <= 1 || Round % Every == 0)
			R->AddTracer(At, Hits.Num() > 0 ? Hits[0] : At + Dir * 30000.0, Fx.TracerLength, Fx.TracerWidth);   // a miss: out to 300 m
		// ⭐ a dust puff where each round lands — instanced, no range limit (a burst walking across a field is seen from afar)
		if (Fx.DustSize > 0.f)
			for (int32 I = 0; I < Hits.Num(); ++I)
			{
				const FVector N = HitNormals.IsValidIndex(I) ? HitNormals[I] : FVector::UpVector;
				R->AddPuff(Hits[I] + N * Fx.DustSize * 0.3f, Fx.DustSize * 0.5f, Fx.DustSize * 2.5f, Fx.DustLife, Fx.DustBrightness);
			}
	}
	// NEAR the view only, under the per-frame budget: Lyra's Niagara extras (smoke and sparks at the muzzle, the shell) …
	// ⭐ POOLED (AutoRelease): measured, a fresh Niagara component per spawn cost ~0.5 ms per spawn-per-frame (budget 24 ⇒ +14 ms at
	// ~950 rounds/s); the flash, tracer and lights are free at that rate
	const float ToView = R ? FVector::Dist(At, R->ViewLocation()) : 0.f;
	const bool bSocket = !Fx.MuzzleSocket.IsNone() && Weapon->DoesSocketExist(Fx.MuzzleSocket);
	auto Spawn = [&](UNiagaraSystem* System) -> UNiagaraComponent*
	{
		if (!System) return nullptr;
		return bSocket
			? UNiagaraFunctionLibrary::SpawnSystemAttached(System, Weapon, Fx.MuzzleSocket, FVector::ZeroVector, FRotator::ZeroRotator,
			                                               EAttachLocation::SnapToTarget, false, false, ENCPoolMethod::AutoRelease)
			: UNiagaraFunctionLibrary::SpawnSystemAtLocation(World, System, At, Dir.Rotation(), FVector::OneVector, false, false, ENCPoolMethod::AutoRelease);
	};
	const bool bNear = !R || ToView < R->NearRange;
	using EX = UHrotShotRenderer::EExtra;
	{
		if (UNiagaraComponent* C = (bNear && (!R || (R->bNearSmoke && R->TakeBudget(EX::Smoke)))) ? Spawn(Fx.MuzzleFlash) : nullptr)   // NS_WeaponFire: smoke and sparks (the flash is ours)
		{
			C->SetVariableVec3(TEXT("User.Direction"), Dir);
			C->SetVariableBool(TEXT("User.Trigger"), true);
			C->Activate(true);
		}
		UNiagaraComponent* Shell = (!bNear || (R && !(R->bNearShell && R->TakeBudget(EX::Shell)))) ? nullptr : (Fx.ShellEject && Weapon->DoesSocketExist(TEXT("ShellEject")))
			? UNiagaraFunctionLibrary::SpawnSystemAttached(Fx.ShellEject, Weapon, TEXT("ShellEject"), FVector::ZeroVector, FRotator::ZeroRotator,
			                                               EAttachLocation::SnapToTarget, false, false, ENCPoolMethod::AutoRelease)
			: Spawn(Fx.ShellEject);
		if (UNiagaraComponent* C = Shell)
		{
			if (Fx.ShellMesh) C->SetVariableStaticMesh(TEXT("User.ShellEjectStaticMesh"), Fx.ShellMesh);
			C->SetVariableBool(TEXT("User.Trigger"), true);
			C->Activate(true);
		}
	}
	// … and the impact (dust / chips where the round lands), within ImpactRange of the view
	if (Hits.Num() > 0 && Fx.Impact && (!R || (R->bImpacts && FVector::Dist(Hits[0], R->ViewLocation()) < R->ImpactRange && R->TakeBudget(EX::Impact))))
	{
		if (UNiagaraComponent* C = UNiagaraFunctionLibrary::SpawnSystemAtLocation(World, Fx.Impact, Hits[0], FRotator::ZeroRotator, FVector::OneVector, false, false, ENCPoolMethod::AutoRelease))
		{
			TArray<FVector> Normals = HitNormals;
			while (Normals.Num() < Hits.Num()) Normals.Add((At - Hits[Normals.Num()]).GetSafeNormal());
			UNiagaraDataInterfaceArrayFunctionLibrary::SetNiagaraArrayVector(C, TEXT("User.ImpactPositions"), Hits);
			UNiagaraDataInterfaceArrayFunctionLibrary::SetNiagaraArrayVector(C, TEXT("User.ImpactNormals"), Normals);
			C->SetVariableInt(TEXT("User.NumberOfHits"), Hits.Num());
			C->SetVariableVec3(TEXT("User.MuzzlePosition"), At);
			C->Activate(true);
		}
	}
	return At;
}
