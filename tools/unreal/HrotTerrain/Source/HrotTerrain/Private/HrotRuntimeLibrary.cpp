// CE-4651 (U-18s) — see HrotRuntimeLibrary.h
#include "HrotRuntimeLibrary.h"
#include "HrotPersonAnimInstance.h"
#include "Components/SkeletalMeshComponent.h"
#include "GameFramework/Actor.h"
#include "Modules/ModuleManager.h"

IMPLEMENT_MODULE(FDefaultModuleImpl, HrotTerrain)

UHrotPersonState* UHrotRuntimeLibrary::AddPersonState(AActor* Actor)
{
	if (!Actor) return nullptr;
	if (UHrotPersonState* Existing = Actor->FindComponentByClass<UHrotPersonState>()) return Existing;
	UHrotPersonState* St = NewObject<UHrotPersonState>(Actor, TEXT("HrotPersonState"), RF_Transactional);
	Actor->AddInstanceComponent(St);
	St->RegisterComponent();
	return St;
}

void UHrotRuntimeLibrary::PoseNow(USkeletalMeshComponent* Mesh, float Seconds, int32 Steps)
{
	if (!Mesh) return;
	if (!Mesh->GetAnimInstance()) Mesh->InitAnim(true);
	const int32 N = FMath::Max(1, Steps);
	for (int32 I = 0; I < N; ++I)
	{
		Mesh->TickAnimation(Seconds / N, false);
		Mesh->RefreshBoneTransforms();
	}
	Mesh->MarkRenderDynamicDataDirty();
}
