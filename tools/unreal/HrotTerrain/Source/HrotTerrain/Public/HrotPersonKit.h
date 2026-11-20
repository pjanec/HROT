// CE-4651 (U-18d) — a PERSON BODY as one asset, like UHrotVehicleKit for vehicles: what scripts/unreal/hrot_person.py MEASURES in the
// editor (where the body's leader mesh sits on its actor, where its hand holds Lyra's rifle, its side camouflage), plus the body's CLIP
// TABLE by role. Built by scripts/unreal/hrot_person_kit.py; read at run time by whoever drives people from state (the DPS world).
#pragma once

#include "CoreMinimal.h"
#include "Engine/DataAsset.h"
#include "HrotPersonKit.generated.h"

class UAnimSequence;
class USkeletalMesh;
class UStreamableRenderAsset;
class UMaterialInterface;
class UStaticMesh;

UCLASS(BlueprintType)
class HROTTERRAIN_API UHrotPersonKit : public UPrimaryDataAsset
{
	GENERATED_BODY()

public:
	/** the body: a dressed Blueprint (its leader mesh drives the outfit parts) — or, when null, a bare skeletal mesh */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") TSoftClassPtr<AActor> BodyClass;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") TObjectPtr<USkeletalMesh> BodyMesh;
	/** the leader mesh's yaw relative to its actor (measured; a Character's mesh is usually −90) and how high its origin (the feet) sits
	 *  above the actor's origin (cm) — the actor goes at feet − ZOffset, turned so the mesh faces the person's heading */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") float YawOffset = 0.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") float ZOffset = 0.f;
	/** the body Blueprint's own components to hide (its own gun: the weapon is the person state's) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") TArray<FName> HideComponents;

	/** the rifle in the hand: the mesh, the socket / bone and the weapon's transform relative to it (measured through the shared aim pose) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person", meta = (AllowedClasses = "/Script/Engine.StaticMesh,/Script/Engine.SkeletalMesh"))
	TObjectPtr<UStreamableRenderAsset> Rifle;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") FName WeaponSocket = TEXT("hand_r");
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") FTransform WeaponOffset;

	/** U-18f — the shoulder-fired TUBE (an RPG-7): its mesh and its transform relative to WeaponSocket, on the shoulder in the aimed pose
	 *  (measured by hrot_person_kit.py the way the vehicle lab's shoulder_launcher puts it: along the rifle's barrel, behind its muzzle) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") TObjectPtr<UStaticMesh> Launcher;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") FTransform LauncherOffset;

	/** the side's uniform: side ("A", "B") → material, put on SideSlots */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") TMap<FString, TObjectPtr<UMaterialInterface>> SideMaterials;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") TArray<FName> SideSlots;

	/** ⭐ the clips by ROLE — "Rifle.Stand.Idle", "Unarmed.Crouch.Walk", "Upper.Aimed", "Death.Prone", "Transition.CrouchToProne" … (the
	 *  role names are HrotDpsPeople's); a role the body has no clip for falls back as that code says */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") TMap<FName, TObjectPtr<UAnimSequence>> Clips;
	/** the clips' own ground speeds (m/s): the play rate is the person's speed over these, so the feet do not slide */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") float WalkSpeed = 1.5f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") float RunSpeed = 3.6f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") float CrouchSpeed = 1.2f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") float CrawlSpeed = 0.6f;

	UAnimSequence* Clip(FName Role) const { const TObjectPtr<UAnimSequence>* C = Clips.Find(Role); return C ? C->Get() : nullptr; }
};
