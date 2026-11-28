// CE-4651 (U-16 / U-17) — see the header.
#include "HrotExplosionFx.h"
#include "HrotRuntimeLibrary.h"

#include "Components/DecalComponent.h"
#include "Engine/World.h"
#include "HrotShotRenderer.h"
#include "HrotSoundscape.h"
#include "Kismet/GameplayStatics.h"
#include "Materials/MaterialInterface.h"
#include "NiagaraFunctionLibrary.h"
#include "NiagaraSystem.h"
#include "NiagaraComponent.h"
#include "Particles/ParticleSystem.h"
#include "Particles/ParticleSystemComponent.h"
#include "Sound/SoundAttenuation.h"
#include "Sound/SoundBase.h"

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

	// ⭐ the sound per kind (U-17k): Lyra's grenade (a MetaSound: body, punch, distant tail), deeper and louder for the bigger kinds —
	// ⚠ a stand-in until a library with shells and rockets (no such sound in Lyra)
	struct FBlastSound { float Volume, Pitch; };
	FBlastSound Sound(EHrotBlast K)
	{
		switch (K)
		{
		case EHrotBlast::Grenade: return { 1.0f, 1.0f };
		case EHrotBlast::Rocket:  return { 1.3f, 0.9f };
		case EHrotBlast::Mortar:  return { 1.7f, 0.75f };
		case EHrotBlast::TankHE:  return { 2.0f, 0.68f };
		}
		return { 1.f, 1.f };
	}
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
	if (R)   // ⭐ heard from afar too — and later: the sound travels (UHrotShotRenderer::PlaySound)
	{
		static USoundBase* Bang = HrotLoadRooted<USoundBase>(TEXT("/Game/Audio/Sounds/Explosions/MSS_Explosions_Grenade.MSS_Explosions_Grenade"));
		static USoundAttenuation* Att = HrotLoadRooted<USoundAttenuation>(TEXT("/Game/Audio/AttenuationPresets/ATT_Grenade.ATT_Grenade"));
		const FBlastSound S = Sound(Kind);
		// U-18n: the bigger blasts from the IG's library (a real rocket, mortar and shell); the grenade stays Lyra's; near ones rattle
		UHrotSoundscape* Snd = UHrotSoundscape::Get(WorldContext);
		// by size (the purchased pack's, else the free library's): a grenade, a rocket, a shell; far away its distant boom
		const bool bFar = FVector::Dist(Location, R->ViewLocation()) > 80000.f;
		USoundBase* Big = !Snd ? nullptr : bFar ? Snd->Pick(TEXT("expl/distant")) :
		                  Kind == EHrotBlast::Grenade ? Snd->Pick(Snd->Pick(TEXT("expl/grenade_b")) && FMath::RandBool() ? TEXT("expl/grenade_b") : TEXT("expl/grenade")) :
		                  Kind == EHrotBlast::Rocket ? Snd->Pick(TEXT("expl/medium")) : Snd->Pick(TEXT("expl/large"));
		if (Big) R->PlaySound(Big, Location, Snd->Falloff(40.f, 8000.f), nullptr, 1.f, Kind == EHrotBlast::TankHE ? 0.92f : 1.f);
		else R->PlaySound(Bang, Location, Att, nullptr, S.Volume, S.Pitch);
		if (Snd && FVector::Dist(Location, R->ViewLocation()) < 6000.f)
			R->PlaySound(Snd->Pick(TEXT("expl/debris_rattle")), Location, Snd->Falloff(5.f, 80.f), nullptr, 0.6f);
	}
	// U-19h (user, live 2026-11-23: "explosions should have visual always no matter how far - they are big and bright"): no distance limit
	// (BlastRange 0 = none); only the per-second budget protects the frame
	if (R && ((R->BlastRange > 0.f && FVector::Dist(Location, R->ViewLocation()) > R->BlastRange) || !R->TakeBudget(UHrotShotRenderer::EExtra::Blast))) return false;

	const FRotator Up = FRotationMatrix::MakeFromZ(Normal.GetSafeNormal()).Rotator();   // the effect's up = the surface normal
	// ... and seen from afar: the pack's systems drop LODs / cull with distance - a blast is drawn at its full LOD with no draw-distance cull
	if (UParticleSystem* Cascade = Cast<UParticleSystem>(Fx))
	{
		if (UParticleSystemComponent* C = UGameplayStatics::SpawnEmitterAtLocation(World, Cascade, Location, Up, FVector(EffectScale), true, EPSCPoolMethod::AutoRelease))
		{
			C->bOverrideLODMethod = true; C->LODMethod = PARTICLESYSTEMLODMETHOD_DirectSet; C->SetLODLevel(0);
			C->LDMaxDrawDistance = 0.f; C->CachedMaxDrawDistance = 0.f;
		}
	}
	else if (UNiagaraSystem* Niagara = Cast<UNiagaraSystem>(Fx))
	{
		if (UNiagaraComponent* C = UNiagaraFunctionLibrary::SpawnSystemAtLocation(World, Niagara, Location, Up, FVector(EffectScale), true, true, ENCPoolMethod::AutoRelease))
		{
			C->LDMaxDrawDistance = 0.f; C->CachedMaxDrawDistance = 0.f;
		}
	}
	static UMaterialInterface* Scorch = HrotLoadRooted<UMaterialInterface>(ScorchMaterial);   // rooted (a plain static dangles)
	if (Scorch)   // the scorch stays two minutes (a decal per blast: budgeted with it)
		UGameplayStatics::SpawnDecalAtLocation(World, Scorch, FVector(60.f, L.ScorchSize, L.ScorchSize), Location, (-Normal).Rotation(), 120.f);
	return true;
}
