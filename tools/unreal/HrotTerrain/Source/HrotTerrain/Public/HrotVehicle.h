// CE-4651 (U-17) — a VEHICLE as HROT reports it: where the hull is (the actor), where its turret and gun point, how damaged it is, and
// when its main gun fires. A KIT (data asset) says which meshes make it and where its pivots are — built once from the vendor's parts
// (scripts/unreal/hrot_vehicle_kit.py: the Sherman's showcase map places every part); the actor turns two pivots, no animation system.
#pragma once

#include "CoreMinimal.h"
#include "Engine/DataAsset.h"
#include "GameFramework/Actor.h"
#include "HrotWeaponFx.h"
#include "HrotRiding.h"
#include "HrotVehicle.generated.h"

class UStaticMesh;
class USkeletalMesh;
class USkeletalMeshComponent;
class UStaticMeshComponent;
class UFXSystemComponent;

UENUM(BlueprintType)
enum class EHrotVehicleParent : uint8 { Hull, Turret, Gun, WingLeft, WingRight };

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
	/** U-18k — material overrides by slot index (null = the mesh's own): one mesh, several liveries (the French pack's desert and green) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") TArray<TObjectPtr<UMaterialInterface>> Materials;
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
	/** ⭐ WINGS that beat (U-17i, the dragon: its wings cut out of the static mesh): each wing's parts ride on its pivot (hull space, at
	 *  the shoulder); flying, a wing rolls about the hull's X axis by ±FlapAmplitude degrees, FlapRate beats a second (both up together) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") FTransform WingLeftPivot;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") FTransform WingRightPivot;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") float FlapAmplitude = 30.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") float FlapRate = 0.8f;
	/** the body's rise and fall with the beat (cm, on top of HROT's steady altitude): highest when the wings are lowest (the
	 *  downstroke lifts) — U-17i, user: "synchronized up/down motion on top of HROT steady altitude" */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") float FlapBob = 0.f;
	/** ⭐ a SKINNED body (U-17l: the motorcycle — one skeletal mesh, its wheels and fork are bones), shown in its rest pose on Lift,
	 *  beside or instead of the static parts; `SkinnedTransform` places it (hull space) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") TObjectPtr<USkeletalMesh> SkinnedBody;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") FTransform SkinnedTransform;
	/** ⭐ where riders sit (U-17j: the dragon's driver and passenger), UHrotRiding::SitOn */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") TArray<FHrotSeat> Seats;
	/** U-18i — where a PAINT colour goes: the material slot of the body paint and its colour parameter (the City Sample cars:
	 *  veh_carPaint / BaseColor, measured); a kit without that slot is not repainted */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") FName PaintSlot = TEXT("veh_carPaint");
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") FName PaintParam = TEXT("BaseColor");
	/** the material a paint colour is set on (null = the slot's own) — ⚠ measured: the City Sample paint takes its colour from a palette
	 *  texture while its static switch "Paint Variation" is on, so BaseColor did nothing; this is a child with that switch off (hrot_car_paint.py) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") TObjectPtr<UMaterialInterface> PaintMaterial;
};

UCLASS()
class HROTTERRAIN_API AHrotVehicle : public AActor
{
	GENERATED_BODY()

public:
	AHrotVehicle();

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") TObjectPtr<UHrotVehicleKit> Kit;
	/** the DIS (kind.domain.country.category ...) of the entity this body draws, "" for a traffic mover: the soundscape's engine voice reads it (U-19h 21) */
	UPROPERTY(BlueprintReadWrite, Category = "HROT|Vehicle") FString Dis;

	// ---- the STATE (HROT's): write the fields, then ApplyState — or call the setters --------------------------------------------
	/** the turret's yaw relative to the hull, degrees (+ = to the left, Unreal's yaw) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") float TurretYaw = 0.f;
	/** the gun's elevation, degrees (+ = up) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") float GunPitch = 0.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") EHrotVehicleDamage Damage = EHrotVehicleDamage::Intact;
	/** the kit's flamethrower streams (HROT's state; a destroyed vehicle never fires) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") bool bFlaming = false;
	UFUNCTION(BlueprintCallable, Category = "HROT|Vehicle") void SetFlaming(bool bNewFlaming);
	/** U-19g: somebody is aboard (a host that knows its crews - the DPS world - writes it every frame; true = as always for a host that does not).
	 *  The soundscape gives an EMPTY, parked ground vehicle no engine voice */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") bool bCrewed = true;
	/** flying: the kit's wings beat (the actor ticks only then) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") bool bFlying = false;
	/** U-18i — the body's paint (HROT's: a traffic car's street colour); bPainted false = the kit's own */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") bool bPainted = false;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Vehicle") FLinearColor Paint = FLinearColor::White;
	/** sets the paint and applies it (a dynamic instance of the paint slot's material on every part that has the slot); returns the parts repainted */
	UFUNCTION(BlueprintCallable, Category = "HROT|Vehicle") int32 SetPaint(FLinearColor NewPaint);
	UFUNCTION(BlueprintCallable, Category = "HROT|Vehicle") void SetFlying(bool bNewFlying);
	virtual void Tick(float DeltaTime) override;

	UFUNCTION(BlueprintCallable, Category = "HROT|Vehicle") void ApplyState();
	UFUNCTION(BlueprintCallable, Category = "HROT|Vehicle") void SetAim(float Yaw, float Pitch);
	UFUNCTION(BlueprintCallable, Category = "HROT|Vehicle") void SetDamage(EHrotVehicleDamage NewDamage);
	/** the main gun fires: a big muzzle flash (instanced, UHrotShotRenderer), a flash of light, a puff of smoke (budgeted) */
	UFUNCTION(BlueprintCallable, Category = "HROT|Vehicle") void FireMainGun();
	UFUNCTION(BlueprintCallable, Category = "HROT|Vehicle") FTransform MuzzleTransform() const;
	/** one round from the kit's gun (its Muzzle) with a weapon's look: a helicopter's minigun, a turret's heavy MG (U-17h) —
	 *  HROT's shot events, as a soldier's (UHrotWeaponFx::FireShotAt) */
	UFUNCTION(BlueprintCallable, Category = "HROT|Vehicle")
	void FireGun(const FHrotShotFx& Fx, const TArray<FVector>& Hits, const TArray<FVector>& HitNormals);
	/** (re)builds the part components from the kit */
	UFUNCTION(BlueprintCallable, Category = "HROT|Vehicle") void Build();

	virtual void OnConstruction(const FTransform& Transform) override;
	virtual void BeginPlay() override;

	UPROPERTY(VisibleAnywhere, Category = "HROT|Vehicle") TObjectPtr<USceneComponent> Hull;
	/** ⭐ everything the vehicle IS rides on Lift (parts, turret, wings, flamethrower, riders) — Lift moves for the wing beat's bob,
	 *  the actor (HROT's transform) never does */
	UPROPERTY(VisibleAnywhere, Category = "HROT|Vehicle") TObjectPtr<USceneComponent> Lift;
	UPROPERTY(VisibleAnywhere, Category = "HROT|Vehicle") TObjectPtr<USceneComponent> Turret;
	UPROPERTY(VisibleAnywhere, Category = "HROT|Vehicle") TObjectPtr<USceneComponent> Gun;
	UPROPERTY(VisibleAnywhere, Category = "HROT|Vehicle") TObjectPtr<USceneComponent> WingLeft;
	UPROPERTY(VisibleAnywhere, Category = "HROT|Vehicle") TObjectPtr<USceneComponent> WingRight;

private:
	UPROPERTY(Transient) TArray<TObjectPtr<UStaticMeshComponent>> PartComps;
	UPROPERTY(Transient) TObjectPtr<USkeletalMeshComponent> SkinnedComp;
	UPROPERTY(Transient) TArray<TObjectPtr<UFXSystemComponent>> DamageFx;
	UPROPERTY(Transient) TObjectPtr<class UHrotFlamethrower> Flame;
	EHrotVehicleDamage Shown = EHrotVehicleDamage::Intact;
	float FlapClock = 0.f;
	void ApplyWings(float Angle);
	void ClearDamageFx();
};
