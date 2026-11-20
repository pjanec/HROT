// CE-4651 (U-17g) — a MISSILE / ROCKET in flight as HROT reports it: where it is and which way it points (the actor's transform, set
// every frame), whether its motor burns. What the IG adds: the body (a KIND: mesh, size, nose onto +X), the motor's flame at the tail
// and a SMOKE TRAIL left behind — puffs dropped along the path, so the trajectory stays visible (UHrotShotRenderer::AddPuff: every
// puff of the world one instanced mesh). One class for all: helicopter rockets, RPG-7, ATGM (Javelin), MANPADS (Igla / Stinger),
// air-to-air (AIM-120), surface-to-air.
#pragma once

#include "CoreMinimal.h"
#include "Engine/DataAsset.h"
#include "GameFramework/Actor.h"
#include "HrotExplosionFx.h"
#include "HrotMissile.generated.h"

class UStaticMesh;
class UStaticMeshComponent;

UCLASS(BlueprintType)
class HROTTERRAIN_API UHrotMissileKind : public UPrimaryDataAsset
{
	GENERATED_BODY()
public:
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Missile") TObjectPtr<UStaticMesh> Mesh;
	/** the mesh inside the actor: its rotation (the nose onto +X), its scale (the packs' wrong sizes, one model for several kinds) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Missile") FTransform MeshTransform;
	/** the body's length (cm, as drawn) — the motor's flame and the trail start at its tail, −Length/2 */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Missile") float Length = 100.f;
	/** the motor's flame (in rifle flashes, UHrotShotRenderer::AddFlash) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Missile") float FlameSize = 2.f;
	/** the trail: a puff `PuffSize` cm across at birth growing to ×PuffGrowth, lasting PuffLife s, one every PuffSpacing cm */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Missile") float PuffSize = 60.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Missile") float PuffGrowth = 4.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Missile") float PuffLife = 12.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Missile") float PuffSpacing = 80.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Missile") float PuffBrightness = 0.85f;
	/** the LAUNCH: a burst of smoke and dust thrown back from the tail (cm across; 0 = none) — an RPG's / a Javelin's backblast, a
	 *  rocket leaving its pod, a SAM leaving its tube */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Missile") float LaunchBlast = 0.f;
	/** what it is when it blows up (UHrotExplosionFx) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Missile") EHrotBlast Blast = EHrotBlast::Rocket;
};

UCLASS()
class HROTTERRAIN_API AHrotMissile : public AActor
{
	GENERATED_BODY()

public:
	AHrotMissile();

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Missile") TObjectPtr<UHrotMissileKind> Kind;
	/** ---- the STATE (HROT's): the actor's transform each frame (X = the nose), and the motor ----------------------------------- */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Missile") bool bMotor = true;

	UFUNCTION(BlueprintCallable, Category = "HROT|Missile") void ApplyKind();
	/** ⭐ a missile appears in the world (HROT's launch: a new entity) — of `Kind`, at `Where` (X = the nose); call Launch() for its backblast */
	UFUNCTION(BlueprintCallable, Category = "HROT|Missile", meta = (WorldContext = "WorldContext"))
	static AHrotMissile* SpawnMissile(UObject* WorldContext, UHrotMissileKind* NewKind, FTransform Where);
	/** it is FIRED (HROT's launch event): the backblast at its tail and a flash; the test flight calls it when its delay ends */
	UFUNCTION(BlueprintCallable, Category = "HROT|Missile") void Launch();
	/** it hits: an explosion of the kind's blast where it is, the body hidden (its trail stays and fades) */
	UFUNCTION(BlueprintCallable, Category = "HROT|Missile") void Detonate();

	/** ⭐ TEST ONLY (the lab, standing in for HROT): fly from here to `To` at `Speed` cm/s over an arc `ArcHeight` cm high, the motor
	 *  burning the first `MotorSeconds` (≤ 0: all the way), starting after `Delay` s; it detonates on arrival when `bDetonate`. */
	UFUNCTION(BlueprintCallable, Category = "HROT|Missile")
	void FlyTest(FVector To, float Speed, float ArcHeight = 0.f, float MotorSeconds = -1.f, float Delay = 0.f, bool bDetonate = true);

	virtual void OnConstruction(const FTransform& Transform) override;
	virtual void BeginPlay() override;
	virtual void Tick(float DeltaTime) override;

	UPROPERTY(VisibleAnywhere, Category = "HROT|Missile") TObjectPtr<USceneComponent> Root;
	UPROPERTY(VisibleAnywhere, Category = "HROT|Missile") TObjectPtr<UStaticMeshComponent> Body;

private:
	FVector LastTail = FVector::ZeroVector;
	bool bHasLast = false, bDone = false, bLaunched = false;
	float Carry = 0.f;
	// the test flight
	bool bTest = false, bTestDetonate = true;
	FVector TestFrom, TestTo;
	float TestSpeed = 0.f, TestArc = 0.f, TestMotor = -1.f, TestDelay = 0.f, TestClock = 0.f;
};
