// CE-4651 (U-16) — see the header.
#include "HrotPeopleLod.h"

#include "ProfilingDebugging/CsvProfiler.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/World.h"
#include "HrotPersonAnimInstance.h"
#include "HrotShotRenderer.h"

UHrotPeopleLod* UHrotPeopleLod::Get(const UObject* WorldContext)
{
	UWorld* W = WorldContext ? WorldContext->GetWorld() : nullptr;
	return W ? W->GetSubsystem<UHrotPeopleLod>() : nullptr;
}

TStatId UHrotPeopleLod::GetStatId() const { RETURN_QUICK_DECLARE_CYCLE_STAT(UHrotPeopleLod, STATGROUP_Tickables); }

void UHrotPeopleLod::Add(UHrotPersonState* Person)
{
	if (Person) Persons.AddUnique(Person);
	People = Persons.Num();
}

CSV_DECLARE_CATEGORY_EXTERN(HrotTerrain);

void UHrotPeopleLod::Tick(float DeltaTime)
{
	CSV_SCOPED_TIMING_STAT(HrotTerrain, PeopleLod);
	const UHrotShotRenderer* R = UHrotShotRenderer::Get(this);
	if (Persons.Num() == 0 || !R) return;
	const FVector View = R->ViewLocation();
	const int32 N = FMath::Min(SlicePerFrame, Persons.Num());
	for (int32 K = 0; K < N; ++K)
	{
		Cursor = (Cursor + 1) % Persons.Num();
		UHrotPersonState* P = Persons[Cursor].Get();
		USkeletalMeshComponent* Body = P ? P->FindLeader() : nullptr;
		if (!Body) continue;
		float Interval = 0.f;
		if (bEnabled)
		{
			const float D = FVector::Dist(Body->GetComponentLocation(), View);
			Interval = D < NearCm ? 0.f : (D < MidCm ? 1.f / MidHz : 1.f / FarHz);
		}
		// ⭐ changed on THIS frame only for this slice: the person's first tick at the new rate falls on its own frame (staggered)
		if (!FMath::IsNearlyEqual(Body->PrimaryComponentTick.TickInterval, Interval)) Body->SetComponentTickInterval(Interval);
	}
	Persons.RemoveAll([](const TWeakObjectPtr<UHrotPersonState>& W) { return !W.IsValid(); });
	People = Persons.Num();
}
