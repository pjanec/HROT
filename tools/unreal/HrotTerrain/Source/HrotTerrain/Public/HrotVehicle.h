// CE-4651 (U-17) — a VEHICLE as HROT reports it: where the hull is (the actor), where its turret and gun point, how damaged it is, and
// when its main gun fires. A KIT (data asset) says which meshes make it and where its pivots are — built once from the vendor's parts
// (scripts/unreal/hrot_vehicle_kit.py: the Sherman's showcase map places every part); the actor turns two pivots, no animation system.
#pragma once

#include "CoreMinimal.h"
#include "Engine/DataAsset.h"
#include "GameFramework/Actor.h"
#include "HrotVehicle.generated.h"

class UStaticMesh;
class UStaticMeshComponent;
class UFXSystemComponent;

UENUM(BlueprintType)
enum class EHrotVehicleParent : uint8 { Hull, Turret, Gun };

UENUM(BlueprintType)
enum class EHrotVehicleDamage : uint8
{
	Intact,
	Damaged,     // smoke from the engine deck
	Destroyed,   // burnt material on every part, fire then smoke, the turret knocked askew
};

USTRUCT(BlueprintType)
struct FHrotVehiclePart
{
	GENERATED_BODY()
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") TObjectPtr<UStaticMesh> Mesh;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") EHrotVehicleParent Parent = EHrotVehicleParent::Hull;
	/** relative to its parent's pivot (the hull = the actor: X forward, Z up, z 0 = the ground under the tracks) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") FTransform Transform;
};

UCLASS(BlueprintType)
class HROTTERRAIN_API UHrotVehicleKit : public UPrimaryDataAsset
{
	GENERATED_BODY()
public:
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") TArray<FHrotVehiclePart> Parts;
	/** the turret ring centre, relative to the hull; the turret yaws about its Z */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") FTransform TurretPivot;
	/** the gun's trunnion, relative to the turret pivot; the gun pitches about its Y */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") FTransform GunPivot;
	/** the muzzle, in the gun pivot's space (X = the bore) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") FVector Muzzle = FVector::ZeroVector;
	/** the main gun's flash, in rifle flashes (UHrotShotRenderer::AddFlash) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") float FlashSize = 8.f;
	/** how long the main gun's flash lasts, seconds (a rifle's: 0.06) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") float FlashLife = 0.15f;
	/** where a damaged vehicle smokes from / a destroyed one burns (hull space: the engine deck) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") FVector EngineDeck = FVector::ZeroVector;
	/** the damage effects' size for this vehicle (1 = a tank; ⚠ measured: a tank-sized fire hid a whole car) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") float DamageFxScale = 1.f;
	/** ⭐ a FLAMETHROWER (U-17f): the helicopter's, the dragon's mouth — at FlameMount (hull space, X = the stream), FlameSize */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") bool bFlamethrower = false;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") FTransform FlameMount;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") float FlameSize = 1.f;
};

UCLASS()
class HROTTERRAIN_API AHrotVehicle : public AActor
{
	GENERATED_BODY()

public:
	AHrotVehicle();

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") TObjectPtr<UHrotVehicleKit> Kit;

	// ---- the STATE (HROT's): write the fields, then ApplyState — or call the setters --------------------------------------------
	/** the turret's yaw relative to the hull, degrees (+ = to the left, Unreal's yaw) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") float TurretYaw = 0.f;
	/** the gun's elevation, degrees (+ = up) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") float GunPitch = 0.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") EHrotVehicleDamage Damage = EHrotVehicleDamage::Intact;
	/** the kit's flamethrower streams (HROT's state; a destroyed vehicle never fires) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") bool bFlaming = false;
	UFUNCTION(BlueprintCallable, Category = "HROT|Vehicle") void SetFlaming(bool bNewFlaming);

	UFUNCTION(BlueprintCallable, Category = "HROT|Vehicle") void ApplyState();
	UFUNCTION(BlueprintCallable, Category = "HROT|Vehicle") void SetAim(float Yaw, float Pitch);
	UFUNCTION(BlueprintCallable, Category = "HROT|Vehicle") void SetDamage(EHrotVehicleDamage NewDamage);
	/** the main gun fires: a big muzzle flash (instanced, UHrotShotRenderer), a flash of light, a puff of smoke (budgeted) */
	UFUNCTION(BlueprintCallable, Category = "HROT|Vehicle") void FireMainGun();
	UFUNCTION(BlueprintCallable, Category = "HROT|Vehicle") FTransform MuzzleTransform() const;
	/** (re)builds the part components from the kit */
	UFUNCTION(BlueprintCallable, Category = "HROT|Vehicle") void Build();

	virtual void OnConstruction(const FTransform& Transform) override;
	virtual void BeginPlay() override;

	UPROPERTY(VisibleAnywhere, Category = "HROT|Vehicle") TObjectPtr<USceneComponent> Hull;
	UPROPERTY(VisibleAnywhere, Category = "HROT|Vehicle") TObjectPtr<USceneComponent> Turret;
	UPROPERTY(VisibleAnywhere, Category = "HROT|Vehicle") TObjectPtr<USceneComponent> Gun;

private:
	UPROPERTY(Transient) TArray<TObjectPtr<UStaticMeshComponent>> PartComps;
	UPROPERTY(Transient) TArray<TObjectPtr<UFXSystemComponent>> DamageFx;
	UPROPERTY(Transient) TObjectPtr<class UHrotFlamethrower> Flame;
	EHrotVehicleDamage Shown = EHrotVehicleDamage::Intact;
	void ClearDamageFx();
};
