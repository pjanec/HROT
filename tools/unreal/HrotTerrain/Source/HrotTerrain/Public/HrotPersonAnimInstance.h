// CE-4651 (U-16) — one person's animation, driven by STATE (what HROT will send per person), no animation graph to author.
#pragma once

#include "CoreMinimal.h"
#include "Animation/AnimInstance.h"
#include "HrotPersonAnimInstance.generated.h"

class UAnimSequence;

/**
 * A person's pose from four inputs, layered the standard way:
 *   BaseAnim      the whole body: stance + gait (stand / crouch idle, walk, jog, crouch-walk …), played in place (root translation removed)
 *   UpperAnim     optional: the arms and torso from UpperRootBone up (weapon + weapon state: rifle aimed, pistol hip-fire …)
 *   ActionAnim    optional: an ADDITIVE clip on top (fire kick, reload, grenade toss) — ActionTime ≥ 0 holds it at that time
 *   AimYaw/Pitch  degrees, spread over the spine bones (the upper body turns toward the aim, the legs keep their gait)
 * Who chooses the clips from HROT's state (stance, gait, weapon, weapon state, action) is the host's table — this class only plays them.
 */
UCLASS(ClassGroup = HROT, meta = (BlueprintSpawnableComponent))
class HROTTERRAIN_API UHrotPersonState : public UActorComponent
{
	GENERATED_BODY()

public:
	/** ⭐ the person's STATE lives on the actor (it persists; HROT's replicated state lands here later) — the anim instance reads it each
	 *  frame (an editor world rebuilds anim instances on every construction, so values set on the instance itself are lost). */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") TObjectPtr<UAnimSequence> BaseAnim;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") TObjectPtr<UAnimSequence> UpperAnim;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") TObjectPtr<UAnimSequence> ActionAnim;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") FName UpperRootBone = TEXT("spine_01");
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") float ActionTime = -1.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") float ActionWeight = 1.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") float AimYaw = 0.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") float AimPitch = 0.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") float PlayRate = 1.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") float TimeOffset = 0.f;
};

UCLASS(Transient, Blueprintable)
class HROTTERRAIN_API UHrotPersonAnimInstance : public UAnimInstance
{
	GENERATED_BODY()

public:
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") TObjectPtr<UAnimSequence> BaseAnim;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") TObjectPtr<UAnimSequence> UpperAnim;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") TObjectPtr<UAnimSequence> ActionAnim;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") FName UpperRootBone = TEXT("spine_01");
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") float ActionTime = -1.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") float ActionWeight = 1.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") float AimYaw = 0.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") float AimPitch = 0.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") float PlayRate = 1.f;
	/** where in the loops to start (a contact sheet sets it so neighbours are not in step) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") float TimeOffset = 0.f;

	float BaseTime = 0.f, UpperTime = 0.f, ActionClock = 0.f;

protected:
	virtual void NativeUpdateAnimation(float DeltaSeconds) override;
	virtual FAnimInstanceProxy* CreateAnimInstanceProxy() override;
};
