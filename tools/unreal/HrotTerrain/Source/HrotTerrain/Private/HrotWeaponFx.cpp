// CE-4651 (U-16) — see the header.
#include "HrotWeaponFx.h"
#include "HrotShotRenderer.h"

#include "Components/SceneComponent.h"
#include "Components/StaticMeshComponent.h"
#include "Engine/StaticMesh.h"
#include "NiagaraComponent.h"
#include "NiagaraDataInterfaceArrayFunctionLibrary.h"
#include "NiagaraFunctionLibrary.h"
#include "NiagaraSystem.h"

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

FVector UHrotWeaponFx::FireShot(USceneComponent* Weapon, const FHrotShotFx& Fx, const TArray<FVector>& Hits, const TArray<FVector>& HitNormals)
{
	if (!Weapon || !Weapon->GetWorld()) return FVector::ZeroVector;
	UWorld* World = Weapon->GetWorld();
	const FTransform Muzzle = MuzzleTransform(Weapon, Fx.MuzzleSocket);
	const FVector At = Muzzle.GetLocation(), Dir = Muzzle.GetRotation().GetForwardVector();
	UHrotShotRenderer* R = UHrotShotRenderer::Get(Weapon);
	// ⭐ EVERY shot: the flash and (1 round in TracerEvery) the tracer — instanced, one draw call each for the whole world
	const int32 Round = R ? R->NextRound(Weapon) : 0;
	if (R)
	{
		R->AddFlash(Muzzle);
		if (R->TracerEvery <= 1 || Round % R->TracerEvery == 0)
			R->AddTracer(At, Hits.Num() > 0 ? Hits[0] : At + Dir * 30000.0);   // a miss: out to 300 m
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
