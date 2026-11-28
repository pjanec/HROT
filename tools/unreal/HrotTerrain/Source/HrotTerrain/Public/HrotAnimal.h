// CE-4651 (U-17e) — an ANIMAL as HROT reports it: where it is (the actor), which way it faces, what it does (idle / walk / run /
// rest / attack / fly / dead). A KIND (data asset) names its body and one clip per state; the pose is played by the SAME machinery as
// a person (UHrotPersonState + UHrotPersonAnimInstance: in place, crossfades, a one-shot held on its last frame) — no second player.
#pragma once

#include "CoreMinimal.h"
#include "Engine/DataAsset.h"
#include "GameFramework/Actor.h"
#include "HrotRiding.h"
#include "HrotAnimal.generated.h"

class UAnimSequence;
class USkeletalMesh;
class USkeletalMeshComponent;
class UHrotPersonState;

UENUM(BlueprintType)
enum class EHrotAnimalState : uint8
{
	Idle,
	Walk,
	Run,
	Rest,     // lying down, alive
	Attack,
	Fly,      // a bird in the air
	Dead,     // the death clip once, then its last frame held (on the ground)
};

USTRUCT(BlueprintType)
struct FHrotAnimalClip
{
	GENERATED_BODY()
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Animal") TObjectPtr<UAnimSequence> Anim;
	/** false = played once and held (a death) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Animal") bool bLoops = true;
};

UCLASS(BlueprintType)
class HROTTERRAIN_API UHrotAnimalKind : public UPrimaryDataAsset
{
	GENERATED_BODY()
public:
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Animal") TObjectPtr<USkeletalMesh> Mesh;
	/** one clip per state; a state the kind has no clip for plays Idle's (a fox does not fly) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Animal") TMap<EHrotAnimalState, FHrotAnimalClip> Clips;
	/** the mesh's yaw inside the actor so the actor's +X is the animal's nose (the Animal Variety Pack faces +Y: −90) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Animal") float MeshYaw = -90.f;
	/** ⭐ where riders sit (U-17j: the deer as a horse — a driver and a passenger), UHrotRiding::SitOn */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Animal") TArray<FHrotSeat> Seats;
};

UCLASS()
class HROTTERRAIN_API AHrotAnimal : public AActor
{
	GENERATED_BODY()

public:
	AHrotAnimal();

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Animal") TObjectPtr<UHrotAnimalKind> Kind;
	/** HROT's family (Horse, Pig, Poultry, Bird, ...) - what it sounds like (UHrotSoundscape, U-18n); the kind is only how it looks */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Animal") FName Family;

	// ---- the STATE (HROT's): write the fields, then ApplyState — or call SetState ------------------------------------------------
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Animal") EHrotAnimalState State = EHrotAnimalState::Idle;
	/** the clip's speed (a walk matched to the speed HROT moves it at) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Animal") float PlayRate = 1.f;

	UFUNCTION(BlueprintCallable, Category = "HROT|Animal") void SetState(EHrotAnimalState NewState);
	/** puts the kind's body and the state's clip on the components (cheap; the crossfade is the person machinery's) */
	UFUNCTION(BlueprintCallable, Category = "HROT|Animal") void ApplyState();
	/** the clip the state plays (the kind's, else Idle's) */
	UFUNCTION(BlueprintCallable, Category = "HROT|Animal") FHrotAnimalClip ClipFor(EHrotAnimalState S) const;

	virtual void OnConstruction(const FTransform& Transform) override;
	virtual void BeginPlay() override;

	UPROPERTY(VisibleAnywhere, Category = "HROT|Animal") TObjectPtr<USceneComponent> Root;
	UPROPERTY(VisibleAnywhere, Category = "HROT|Animal") TObjectPtr<USkeletalMeshComponent> Body;
	UPROPERTY(VisibleAnywhere, Category = "HROT|Animal") TObjectPtr<UHrotPersonState> Pose;
};
