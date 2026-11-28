// CE-4651 (U-17e) — see the header.
#include "HrotAnimal.h"

#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "HrotPersonAnimInstance.h"

AHrotAnimal::AHrotAnimal()
{
	PrimaryActorTick.bCanEverTick = false;   // ⭐ nothing per frame of its own: HROT moves it, the pose machinery animates it
	Root = CreateDefaultSubobject<USceneComponent>(TEXT("Root"));
	RootComponent = Root;
	Body = CreateDefaultSubobject<USkeletalMeshComponent>(TEXT("Body"));
	Body->SetupAttachment(Root);
	Body->SetCollisionEnabled(ECollisionEnabled::NoCollision);
	Pose = CreateDefaultSubobject<UHrotPersonState>(TEXT("Pose"));
	Pose->UpperRootBone = NAME_None;   // the whole body plays one clip — no upper layer, no aim
}

void AHrotAnimal::OnConstruction(const FTransform& Transform)
{
	Super::OnConstruction(Transform);
	ApplyState();
}

void AHrotAnimal::BeginPlay()
{
	Super::BeginPlay();
	ApplyState();
}

void AHrotAnimal::SetState(EHrotAnimalState NewState)
{
	State = NewState;
	ApplyState();
}

FHrotAnimalClip AHrotAnimal::ClipFor(EHrotAnimalState S) const
{
	if (!Kind) return FHrotAnimalClip();
	if (const FHrotAnimalClip* C = Kind->Clips.Find(S); C && C->Anim) return *C;
	if (const FHrotAnimalClip* C = Kind->Clips.Find(EHrotAnimalState::Idle)) return *C;
	return FHrotAnimalClip();
}

void AHrotAnimal::ApplyState()
{
	if (!Kind) return;
	if (Body->GetSkeletalMeshAsset() != Kind->Mesh) Body->SetSkeletalMeshAsset(Kind->Mesh);
	Body->SetRelativeRotation(FRotator(0.f, Kind->MeshYaw, 0.f));
	const FHrotAnimalClip C = ClipFor(State);
	Pose->BaseAnim = C.Anim;
	Pose->bBaseLoops = C.bLoops;
	Pose->PlayRate = PlayRate;
	// neighbours out of step: a fixed offset from the actor's name (the same on every IG channel — no random numbers)
	Pose->TimeOffset = (GetTypeHash(GetFName()) % 1000) * 0.001f * 3.f;
	Pose->ApplyAnimation();
}
