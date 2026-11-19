// CE-4651 (U-16 / U-17) — see the header.
#include "HrotExplosionFx.h"

#include "Components/DecalComponent.h"
#include "Engine/World.h"
#include "HrotShotRenderer.h"
#include "Kismet/GameplayStatics.h"
#include "Materials/MaterialInterface.h"
#include "NiagaraFunctionLibrary.h"
#include "NiagaraSystem.h"
#include "Particles/ParticleSystem.h"
#include "Particles/ParticleSystemComponent.h"

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
	// ⭐ the effect per kind: Realistic Starter VFX Pack Vol 2 (Cascade systems — Lyra's NS_Grenade_Explosion drew NOTHING spawned
	// like this). Chosen from the lab's ISOLATED trial (animlab.py --only live --blasts …, each blast 40 m from the last):
	//   Big_B = an earth burst (fire, dust, debris) · Big_A = a fireball, debris, then a smoke cloud · at ×2 both read as a heavy
	//   shell. ⚠ the first trial set them off side by side and its pictures mixed one blast's smoke into the next (superseded).
	//   Kept for later: Big_C (a fireball → a smoke mushroom), Molotov (lasting fire — a burning wreck), Side / Smoke (small)
	struct FBlastEffect { const TCHAR* Path; float Scale; };
	FBlastEffect Effect(EHrotBlast K)
	{
		switch (K)
		{
		case EHrotBlast::Grenade: return { TEXT("P_Explosion_Big_B.P_Explosion_Big_B"), 1.0f };
		case EHrotBlast::Rocket:  return { TEXT("P_Explosion_Big_A.P_Explosion_Big_A"), 1.0f };
		case EHrotBlast::Mortar:  return { TEXT("P_Explosion_Big_B.P_Explosion_Big_B"), 2.0f };
		case EHrotBlast::TankHE:  return { TEXT("P_Explosion_Big_A.P_Explosion_Big_A"), 2.0f };
		}
		return { TEXT("P_Explosion_Big_B.P_Explosion_Big_B"), 0.5f };
	}
	UFXSystemAsset* LoadEffect(EHrotBlast K)
	{
		static TMap<EHrotBlast, TObjectPtr<UFXSystemAsset>> Cache;   // loaded once per kind (rooted by the table below)
		if (TObjectPtr<UFXSystemAsset>* Got = Cache.Find(K)) return *Got;
		UFXSystemAsset* A = LoadObject<UFXSystemAsset>(nullptr, *(FString(TEXT("/Game/Realistic_Starter_VFX_Pack_Vol2/Particles/Explosion/")) + Effect(K).Path));
		if (A) A->AddToRoot();
		Cache.Add(K, A);
		return A;
	}
	// our own soot decal (scripts/unreal/hrot_shotfx_assets.py) — ⚠ Lyra's M_ExplosionDecal takes its colour from Niagara particles:
	// as a plain decal it painted a flat tan box (measured, the lab's tank shell)
	const TCHAR* ScorchMaterial = TEXT("/Game/Hrot/Fx/M_HrotScorch.M_HrotScorch");
}

bool UHrotExplosionFx::Explode(UObject* WorldContext, FVector Location, FVector Normal, EHrotBlast Kind)
{
	return ExplodeWith(WorldContext, Location, Normal, Kind, LoadEffect(Kind), Effect(Kind).Scale);
}

bool UHrotExplosionFx::ExplodeWith(UObject* WorldContext, FVector Location, FVector Normal, EHrotBlast Kind, UFXSystemAsset* Fx, float EffectScale)
{
	UWorld* World = WorldContext ? WorldContext->GetWorld() : nullptr;
	if (!World) return false;
	const FBlastLook L = Look(Kind);
	UHrotShotRenderer* R = UHrotShotRenderer::Get(WorldContext);
	if (R) R->Flash(Location + Normal * 50.f, L.LightIntensity, L.LightRadius, L.LightSeconds);   // seen from afar too
	if (R && (FVector::Dist(Location, R->ViewLocation()) > R->BlastRange || !R->TakeBudget(UHrotShotRenderer::EExtra::Blast))) return false;

	const FRotator Up = FRotationMatrix::MakeFromZ(Normal.GetSafeNormal()).Rotator();   // the effect's up = the surface normal
	if (UParticleSystem* Cascade = Cast<UParticleSystem>(Fx))
		UGameplayStatics::SpawnEmitterAtLocation(World, Cascade, Location, Up, FVector(EffectScale), true, EPSCPoolMethod::AutoRelease);
	else if (UNiagaraSystem* Niagara = Cast<UNiagaraSystem>(Fx))
		UNiagaraFunctionLibrary::SpawnSystemAtLocation(World, Niagara, Location, Up, FVector(EffectScale), true, true, ENCPoolMethod::AutoRelease);
	static UMaterialInterface* Scorch = LoadObject<UMaterialInterface>(nullptr, ScorchMaterial);
	if (Scorch)   // the scorch stays two minutes (a decal per blast: budgeted with it)
		UGameplayStatics::SpawnDecalAtLocation(World, Scorch, FVector(60.f, L.ScorchSize, L.ScorchSize), Location, (-Normal).Rotation(), 120.f);
	return true;
}
