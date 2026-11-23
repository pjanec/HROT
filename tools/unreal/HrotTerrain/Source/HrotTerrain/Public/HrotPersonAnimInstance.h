// CE-4651 (U-16) — one person's animation, driven by STATE (what HROT will send per person), no animation graph to author.
#pragma once

#include "CoreMinimal.h"
#include "Animation/AnimInstance.h"
#include "HrotPersonAnimInstance.generated.h"

class UAnimSequence;
class USkeletalMeshComponent;
class UStreamableRenderAsset;
class UMeshComponent;
class UMaterialInterface;

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
	/** false = the base clip plays ONCE from its start and holds its last frame (dying → dead, a stand-up, a throw); true = it loops */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") bool bBaseLoops = true;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") TObjectPtr<UAnimSequence> UpperAnim;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") TObjectPtr<UAnimSequence> ActionAnim;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") FName UpperRootBone = TEXT("spine_01");
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") float ActionTime = -1.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") float ActionWeight = 1.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") float AimYaw = 0.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") float AimPitch = 0.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") float PlayRate = 1.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") float TimeOffset = 0.f;
	/** seconds a clip change takes to crossfade (stance, gait, weapon state) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") float BlendTime = 0.25f;
	/** degrees a second the aim turns toward AimYaw / AimPitch (0 = at once) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") float AimTurnRate = 240.f;

	/** ⭐ the weapon in the hand (null = unarmed / holstered) — PART OF THE PERSON: a component of this actor, so it survives into
	 *  play (measured: a separate weapon actor attached to a Blueprint body lost its attachment in Simulate) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person", meta = (AllowedClasses = "/Script/Engine.StaticMesh,/Script/Engine.SkeletalMesh"))
	TObjectPtr<UStreamableRenderAsset> Weapon;   // a static or a skinned mesh (Lyra's SK_Rifle: its Muzzle / ShellEject sockets)
	/** where it is held: the socket / bone, and the weapon's transform relative to it */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") FName WeaponSocket = TEXT("weapon_r");
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") FTransform WeaponOffset;

	/** ⭐ the uniform: material SLOT → material on the body (the fighting side's camo — both sides on one body asset). Applied with
	 *  ApplyMaterials, and again in play. Slots the body does not have are ignored. */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") TMap<FName, TObjectPtr<UMaterialInterface>> MaterialOverrides;

	/** ⭐ the weapon is a FLAMETHROWER (U-17f): a UHrotFlamethrower rides on its "Muzzle" socket; bFlameFiring = HROT's state */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") bool bFlamethrower = false;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") bool bFlameFiring = false;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") float FlameSize = 1.f;
	/** sets bFlameFiring and applies it (the stream starts / stops) */
	UFUNCTION(BlueprintCallable, Category = "HROT|Person") void SetFlameFiring(bool bFiring);
	/** the flamethrower on the weapon (created when bFlamethrower and a weapon is in the hand), or null */
	UFUNCTION(BlueprintCallable, Category = "HROT|Person") class UHrotFlamethrower* ApplyFlame();

	/** set by the first tick in play (ApplyAnimation + ApplyWeapon ran) — a check the anim lab reads */
	UPROPERTY(Transient, VisibleAnywhere, BlueprintReadOnly, Category = "HROT|Person") bool bAppliedInPlay = false;

	UHrotPersonState();

	/** Puts `Weapon` in the hand (creates / updates / hides the "HrotWeapon" component). Called in play by the first tick too. */
	UFUNCTION(BlueprintCallable, Category = "HROT|Person")
	UMeshComponent* ApplyWeapon();

	/** ⭐ The body's parts (the skinned meshes that follow the leader's pose, same skeleton) MERGED into one mesh on the leader; the
	 *  separate parts hidden. One merged mesh per combination of parts, shared by every person wearing it (cached). Returns how many
	 *  parts were merged (0 = nothing to do). Why: a Fab body like US_Soldier is 15 skinned components — 15 skinning passes and
	 *  scene proxies per person; merged, one. ⚠ Sections (materials) are kept: draw calls per person do not drop. */
	UFUNCTION(BlueprintCallable, Category = "HROT|Person")
	int32 MergeBodyParts();

	/** ⭐ Stops what need not run every frame on a person: the owner actor's tick (HROT moves and drives people; a body Blueprint's own
	 *  per-frame logic has no job here) and every skinned component that is hidden or has no mesh (the parts merged into the body,
	 *  a Character's empty default mesh). Measured, 200 soldiers in play: these ticks cost ~8.7 ms with nothing drawn or animated;
	 *  all off, ~0.25 ms. Called once in play (the first tick) and by MergeBodyParts. Returns how many components were quieted. */
	UFUNCTION(BlueprintCallable, Category = "HROT|Person")
	int32 QuietOwner();

	/** Puts MaterialOverrides on the body (the leader). Returns how many slots it set. */
	UFUNCTION(BlueprintCallable, Category = "HROT|Person")
	int32 ApplyMaterials();

	/** The weapon component in the hand, or null. */
	UFUNCTION(BlueprintCallable, Category = "HROT|Person")
	UMeshComponent* GetWeaponComponent() const;

	/** The skeletal mesh the person's pose is computed on: the one the other parts follow (leader pose), else the first with a mesh. */
	UFUNCTION(BlueprintCallable, Category = "HROT|Person")
	USkeletalMeshComponent* FindLeader() const;

	/** Makes the leader run UHrotPersonAnimInstance (true when it had to change something). */
	UFUNCTION(BlueprintCallable, Category = "HROT|Person")
	bool ApplyAnimation();

protected:
	/** ⭐ In play, once, AFTER the owner's own BeginPlay: a body Blueprint may start its own animation there (measured: the US_Soldier
	 *  Character sets a single-node idle on BeginPlay, replacing ours) — the person state owns the pose, so it re-applies. */
	virtual void TickComponent(float DeltaTime, ELevelTick TickType, FActorComponentTickFunction* ThisTickFunction) override;
	/** an editor rebuild of a Blueprint person (any property edit re-runs its construction) re-registers this component: the leader
	 *  gets our animation back (measured: the rebuild reset it to the Blueprint's own) */
	virtual void OnRegister() override;
};

UCLASS(Transient, Blueprintable)
class HROTTERRAIN_API UHrotPersonAnimInstance : public UAnimInstance
{
	GENERATED_BODY()

public:
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") TObjectPtr<UAnimSequence> BaseAnim;
	/** false = the base clip plays ONCE from its start and holds its last frame (dying → dead, a stand-up, a throw); true = it loops */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") bool bBaseLoops = true;
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

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") float BlendTime = 0.25f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Person") float AimTurnRate = 240.f;

	// the running state (the crossfade from the previous clips, the aim as it turns)
	float BaseTime = 0.f, UpperTime = 0.f, ActionClock = 0.f;
	TObjectPtr<UAnimSequence> PrevBaseAnim, PrevUpperAnim;
	float PrevBaseTime = 0.f, PrevUpperTime = 0.f, BaseBlendAlpha = 1.f, UpperBlendAlpha = 1.f;
	float AimYawNow = 0.f, AimPitchNow = 0.f;
	bool bStarted = false;

protected:
	virtual void NativeUpdateAnimation(float DeltaSeconds) override;
	virtual FAnimInstanceProxy* CreateAnimInstanceProxy() override;
};
