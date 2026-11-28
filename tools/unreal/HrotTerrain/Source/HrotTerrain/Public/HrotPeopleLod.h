// CE-4651 (U-16) — how OFTEN each person updates, by distance from the view. Measured, 1000 soldiers in play: the cost is the body
// component's tick COUNT (~7.5 µs each, every frame, animation evaluated or not — URO changed nothing); a 10 Hz tick for all brought
// 16.1 → 9.1 ms mean, but with spikes (everyone ticking on the same frame). So: tiers by distance, re-tiered a SLICE per frame — each
// person's interval starts on its own frame, which staggers the updates by construction.
#pragma once

#include "CoreMinimal.h"
#include "Subsystems/WorldSubsystem.h"
#include "HrotPeopleLod.generated.h"

class UHrotPersonState;

UCLASS()
class HROTTERRAIN_API UHrotPeopleLod : public UTickableWorldSubsystem
{
	GENERATED_BODY()

public:
	static UHrotPeopleLod* Get(const UObject* WorldContext);
	void Add(UHrotPersonState* Person);

	/** within NearCm: every frame; within MidCm: MidHz; beyond: FarHz */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|People") float NearCm = 2500.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|People") float MidCm = 6000.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|People") float MidHz = 30.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|People") float FarHz = 15.f;
	/** how many people are re-tiered each frame (round-robin) — all of them within People / Slice frames */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|People") int32 SlicePerFrame = 64;
	/** off = every person every frame (a measurement's comparison) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|People") bool bEnabled = true;
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "HROT|People") int32 People = 0;

	UFUNCTION(BlueprintCallable, Category = "HROT|People", meta = (WorldContext = "WorldContext"))
	static UHrotPeopleLod* ForWorld(UObject* WorldContext) { return Get(WorldContext); }

	virtual void Tick(float DeltaTime) override;
	virtual TStatId GetStatId() const override;
	virtual bool IsTickableInEditor() const override { return false; }
	virtual ETickableTickType GetTickableTickType() const override { return ETickableTickType::Always; }
	virtual bool DoesSupportWorldType(const EWorldType::Type T) const override { return T == EWorldType::Game || T == EWorldType::PIE; }

private:
	TArray<TWeakObjectPtr<UHrotPersonState>> Persons;
	int32 Cursor = 0;
};
