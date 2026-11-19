// CE-4651 (U-16 / U-17) — see the header.
#include "HrotExplosionFx.h"

#include "Components/DecalComponent.h"
#include "Engine/World.h"
#include "HrotShotRenderer.h"
#include "Kismet/GameplayStatics.h"
#include "Materials/MaterialInterface.h"
#include "NiagaraComponent.h"
#include "NiagaraFunctionLibrary.h"
#include "NiagaraSystem.h"

namespace
{
	struct FBlastLook { float EffectScale, LightIntensity, LightRadius, LightSeconds, ScorchSize; };
	// ⭐ one row per kind — the sizes are a first guess, tuned by eye against the lab's pictures (kill radius ≠ look)
	FBlastLook Look(EHrotBlast K)
	{
		switch (K)
		{
		case EHrotBlast::Grenade: return { 1.0f, 60000.f, 1500.f, 0.12f, 150.f };
		case EHrotBlast::Rocket:  return { 1.4f, 90000.f, 2000.f, 0.15f, 200.f };
		case EHrotBlast::Mortar:  return { 2.2f, 150000.f, 3000.f, 0.20f, 350.f };
		case EHrotBlast::TankHE:  return { 3.0f, 250000.f, 4000.f, 0.25f, 450.f };
		}
		return { 1.f, 60000.f, 1500.f, 0.12f, 150.f };
	}
	// ⏳ Lyra's NS_Grenade_Explosion drew NOTHING when spawned like this (it is set up by Lyra's game-feature code; a stylised look
	// anyway): the realistic explosion pack goes here, one system per kind
	const TCHAR* ExplosionSystem = TEXT("/Game/Effects/Particles/Explosion/NS_Grenade_Explosion.NS_Grenade_Explosion");
	// our own soot decal (scripts/unreal/hrot_shotfx_assets.py) — ⚠ Lyra's M_ExplosionDecal takes its colour from Niagara particles:
	// as a plain decal it painted a flat tan box (measured, the lab's tank shell)
	const TCHAR* ScorchMaterial = TEXT("/Game/Hrot/Fx/M_HrotScorch.M_HrotScorch");
}

bool UHrotExplosionFx::Explode(UObject* WorldContext, FVector Location, FVector Normal, EHrotBlast Kind)
{
	UWorld* World = WorldContext ? WorldContext->GetWorld() : nullptr;
	if (!World) return false;
	const FBlastLook L = Look(Kind);
	UHrotShotRenderer* R = UHrotShotRenderer::Get(WorldContext);
	if (R) R->Flash(Location + Normal * 50.f, L.LightIntensity, L.LightRadius, L.LightSeconds);   // seen from afar too
	if (R && (FVector::Dist(Location, R->ViewLocation()) > R->BlastRange || !R->TakeBudget(UHrotShotRenderer::EExtra::Blast))) return false;

	static UNiagaraSystem* System = LoadObject<UNiagaraSystem>(nullptr, ExplosionSystem);
	if (System)
		if (UNiagaraComponent* C = UNiagaraFunctionLibrary::SpawnSystemAtLocation(World, System, Location, Normal.Rotation() - FRotator(90.f, 0.f, 0.f),
		                                                                          FVector(L.EffectScale), false, false, ENCPoolMethod::AutoRelease))
		{
			// Lyra colours its blast by team — a neutral fire colour here
			C->SetVariableLinearColor(TEXT("User.Team_Color"), FLinearColor(1.f, 0.55f, 0.22f));
			C->Activate(true);
		}
	static UMaterialInterface* Scorch = LoadObject<UMaterialInterface>(nullptr, ScorchMaterial);
	if (Scorch)   // the scorch stays two minutes (a decal per blast: budgeted with it)
		UGameplayStatics::SpawnDecalAtLocation(World, Scorch, FVector(60.f, L.ScorchSize, L.ScorchSize), Location, (-Normal).Rotation(), 120.f);
	return true;
}
