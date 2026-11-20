// CE-4651 (U-17j) — see the header.
#include "HrotRiding.h"

#include "Components/SkeletalMeshComponent.h"
#include "GameFramework/Actor.h"
#include "HrotAnimal.h"
#include "HrotPersonAnimInstance.h"
#include "HrotVehicle.h"

TArray<FHrotSeat> UHrotRiding::SeatsOf(AActor* Mount)
{
	if (const AHrotAnimal* A = Cast<AHrotAnimal>(Mount); A && A->Kind) return A->Kind->Seats;
	if (const AHrotVehicle* V = Cast<AHrotVehicle>(Mount); V && V->Kit) return V->Kit->Seats;
	return {};
}

bool UHrotRiding::SitOn(AActor* Rider, AActor* Mount, int32 Seat)
{
	if (!Rider || !Mount) return false;
	const TArray<FHrotSeat> Seats = SeatsOf(Mount);
	if (!Seats.IsValidIndex(Seat)) return false;
	const FHrotSeat& S = Seats[Seat];
	// the component the seat rides on: the animal's skinned body at the bone, else the mount's root
	USceneComponent* Parent = Mount->GetRootComponent();
	FName Socket = NAME_None;
	if (const AHrotAnimal* A = Cast<AHrotAnimal>(Mount); A && !S.Bone.IsNone() && A->Body && A->Body->DoesSocketExist(S.Bone))
	{
		Parent = A->Body; Socket = S.Bone;
	}
	if (!Parent) return false;
	// ⭐ placed in the MOUNT's frame (upright, facing its way) at the bone's position, then attached keeping that world transform —
	// so it follows the bone (the stride) from there, whatever the bone's own axes are
	const FTransform MountX = Mount->GetActorTransform();
	const FVector At = (Socket.IsNone() ? MountX.GetLocation() : Parent->GetSocketLocation(Socket)) + MountX.TransformVectorNoScale(S.Offset.GetLocation());
	const FQuat Rot = MountX.GetRotation() * S.Offset.GetRotation();
	Rider->DetachFromActor(FDetachmentTransformRules::KeepWorldTransform);
	Rider->SetActorLocationAndRotation(At, Rot);
	Rider->AttachToComponent(Parent, FAttachmentTransformRules::KeepWorldTransform, Socket);
	if (S.Pose)
		if (UHrotPersonState* P = Rider->FindComponentByClass<UHrotPersonState>())
		{
			P->BaseAnim = S.Pose; P->bBaseLoops = true; P->UpperAnim = nullptr;
			P->ApplyAnimation();
		}
	return true;
}

void UHrotRiding::GetOff(AActor* Rider)
{
	if (Rider) Rider->DetachFromActor(FDetachmentTransformRules::KeepWorldTransform);
}
