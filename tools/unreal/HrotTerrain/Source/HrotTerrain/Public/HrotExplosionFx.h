// CE-4651 (U-16 / U-17) — EXPLOSIONS as HROT reports them: where, which way the surface faces, and what blew (a grenade, a mortar
// round, a tank's HE shell, a rocket). The projectile in flight is HROT's position each frame — the IG only moves a mesh; this is the
// moment it ends. One table per kind (the effect's scale, the flash light, the scorch); budgeted, like shots (UHrotShotRenderer).
#pragma once

#include "CoreMinimal.h"
#include "Kismet/BlueprintFunctionLibrary.h"
#include "HrotExplosionFx.generated.h"

class UFXSystemAsset;

UENUM(BlueprintType)
enum class EHrotBlast : uint8
{
	Grenade,   // hand grenade, ~0.2 kg TNT
	Rocket,    // a shoulder-fired rocket (RPG / AT4)
	Mortar,    // 81–120 mm
	TankHE,    // a tank's high-explosive shell
};

UCLASS()
class HROTTERRAIN_API UHrotExplosionFx : public UBlueprintFunctionLibrary
{
	GENERATED_BODY()

public:
	/** One explosion at `Location` on a surface facing `Normal` (up for the ground). Returns false when the budget or the range
	 *  dropped it (the flash light still counts as seen from afar). */
	UFUNCTION(BlueprintCallable, Category = "HROT|Blast", meta = (WorldContext = "WorldContext"))
	static bool Explode(UObject* WorldContext, FVector Location, FVector Normal, EHrotBlast Kind);

	/** The same with an explicit effect (a Cascade ParticleSystem or a Niagara system) at `EffectScale` — the lab tries candidates
	 *  this way; Explode() uses the per-kind table */
	UFUNCTION(BlueprintCallable, Category = "HROT|Blast", meta = (WorldContext = "WorldContext"))
	static bool ExplodeWith(UObject* WorldContext, FVector Location, FVector Normal, EHrotBlast Kind, UFXSystemAsset* Effect, float EffectScale);
};
