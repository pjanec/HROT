// CE-4651 (U-16) — see the header.
#include "HrotPersonAnimInstance.h"
#include "HrotFire.h"
#include "HrotPeopleLod.h"

#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimSequence.h"
#include "Animation/AttributesRuntime.h"
#include "AnimationRuntime.h"
#include "BonePose.h"
#include "GameFramework/Actor.h"
#include "Components/SkeletalMeshComponent.h"
#include "Components/StaticMeshComponent.h"
#include "Engine/StaticMesh.h"
#include "Engine/SkeletalMesh.h"
#include "Materials/MaterialInterface.h"
#include "SkeletalMergingLibrary.h"

namespace
{
	float Wrap(float T, const UAnimSequence* A)
	{
		const float L = A ? (float)A->GetPlayLength() : 0.f;
		return L > 0.f ? FMath::Fmod(FMath::Max(T, 0.f), L) : 0.f;
	}

	/** The same footstep phase in another clip: walk → jog keeps the stride (normalised time), never restarts it. */
	float SamePhase(float T, const UAnimSequence* From, const UAnimSequence* To)
	{
		const float LF = From ? (float)From->GetPlayLength() : 0.f, LT = To ? (float)To->GetPlayLength() : 0.f;
		return (LF > 0.f && LT > 0.f) ? FMath::Fmod(T / LF, 1.f) * LT : 0.f;
	}

	struct FHrotPersonProxy : public FAnimInstanceProxy
	{
		explicit FHrotPersonProxy(UAnimInstance* In) : FAnimInstanceProxy(In) {}

		TObjectPtr<UAnimSequence> Base, Upper, Action, PrevBase, PrevUpper;
		FName UpperRoot;
		float BaseT = 0.f, UpperT = 0.f, PrevBaseT = 0.f, PrevUpperT = 0.f, BaseAlpha = 1.f, UpperAlpha = 1.f;
		float ActionT = 0.f, ActionW = 1.f, Yaw = 0.f, Pitch = 0.f;

		virtual void PreUpdate(UAnimInstance* InAnimInstance, float DeltaSeconds) override
		{
			FAnimInstanceProxy::PreUpdate(InAnimInstance, DeltaSeconds);
			const UHrotPersonAnimInstance* P = CastChecked<UHrotPersonAnimInstance>(InAnimInstance);
			Base = P->BaseAnim; Upper = P->UpperAnim; Action = P->ActionAnim; UpperRoot = P->UpperRootBone;
			BaseT = P->BaseTime; UpperT = P->UpperTime; ActionW = P->ActionWeight;
			PrevBase = P->PrevBaseAnim; PrevUpper = P->PrevUpperAnim; PrevBaseT = P->PrevBaseTime; PrevUpperT = P->PrevUpperTime;
			BaseAlpha = P->BaseBlendAlpha; UpperAlpha = P->UpperBlendAlpha;
			Yaw = P->AimYawNow; Pitch = P->AimPitchNow;
			ActionT = P->ActionTime >= 0.f ? P->ActionTime : P->ActionClock;
		}

		static void Sample(UAnimSequence* A, float T, FPoseContext& Into)
		{
			FAnimationPoseData Data(Into);
			A->GetAnimationPose(Data, FAnimExtractContext(double(T), false));
		}

		/** Into = blend(From @ FromT, To @ ToT, Alpha); a null side is the pose already in Into (an upper layer fading in / out). */
		static void SampleBlended(UAnimSequence* From, float FromT, UAnimSequence* To, float ToT, float Alpha, FPoseContext& Into)
		{
			if (Alpha >= 1.f || From == To) { if (To) Sample(To, ToT, Into); return; }
			FPoseContext A(Into), B(Into);
			if (From) Sample(From, FromT, A);
			if (To) Sample(To, ToT, B);
			FAnimationPoseData Out(Into), DA(A), DB(B);
			FAnimationRuntime::BlendTwoPosesTogether(DA, DB, 1.f - Alpha, Out);   // weight of A = 1 − Alpha
		}

		virtual bool Evaluate(FPoseContext& Output) override
		{
			if (!Base) { Output.ResetToRefPose(); return true; }
			SampleBlended(PrevBase, PrevBaseT, Base, BaseT, BaseAlpha, Output);
			const FBoneContainer& BC = Output.Pose.GetBoneContainer();

			// in place: the root keeps no HORIZONTAL travel (HROT moves the person; the clip only animates). Its height stays —
			// measured: clips retargeted onto the crowd skeleton carry the pelvis height in `root` (the retargeter's root-motion op),
			// so clearing all of it sank those people ~80 cm; Lyra's clips have root z = 0, unchanged.
			FTransform& Root = Output.Pose[FCompactPoseBoneIndex(0)];
			Root.SetTranslation(FVector(0.f, 0.f, Root.GetTranslation().Z));

			// ⭐ an ADDITIVE clip in the upper slot (Lyra's MM_Rifle_Fire is one) is a DIFFERENCE, not a pose: played as a pose it
			// collapsed the upper body (measured, live shot). It is added on top instead (below), fading with the same crossfade.
			UAnimSequence* const UpperAdd = (Upper && Upper->IsValidAdditive()) ? Upper.Get() : nullptr;
			UAnimSequence* const PrevAdd = (PrevUpper && PrevUpper->IsValidAdditive()) ? PrevUpper.Get() : nullptr;
			UAnimSequence* const UpperFull = UpperAdd ? nullptr : Upper.Get();
			UAnimSequence* const PrevFull = PrevAdd ? nullptr : PrevUpper.Get();

			// the upper body from UpperRoot up, from the upper clip — crossfading from the previous one (a null side = the base pose)
			if (UpperFull || (PrevFull && UpperAlpha < 1.f))
			{
				FPoseContext UpperPose(Output);
				UpperPose.Pose.CopyBonesFrom(Output.Pose);
				SampleBlended(PrevFull, PrevUpperT, UpperFull, UpperT, UpperAlpha, UpperPose);
				if (!UpperFull && UpperAlpha < 1.f)   // fading OUT: blend the old upper toward the base
				{
					FPoseContext Old(Output); Sample(PrevFull, PrevUpperT, Old);
					FAnimationPoseData O(UpperPose), DA(Old), DB(Output);
					FAnimationRuntime::BlendTwoPosesTogether(DA, DB, 1.f - UpperAlpha, O);
				}
				else if (UpperFull && !PrevFull && UpperAlpha < 1.f)   // fading IN from the base
				{
					FPoseContext New(Output); Sample(UpperFull, UpperT, New);
					FAnimationPoseData O(UpperPose), DA(Output), DB(New);
					FAnimationRuntime::BlendTwoPosesTogether(DA, DB, 1.f - UpperAlpha, O);
				}
				const int32 RootPose = BC.GetPoseBoneIndexForBoneName(UpperRoot);
				if (RootPose != INDEX_NONE)
				{
					const FCompactPoseBoneIndex RootC = BC.MakeCompactPoseIndex(FMeshPoseBoneIndex(RootPose));
					TArray<bool> InUpper; InUpper.Init(false, Output.Pose.GetNumBones());
					for (FCompactPoseBoneIndex I : Output.Pose.ForEachBoneIndex())
					{
						if (I == RootC) { InUpper[I.GetInt()] = true; }
						else if (I.GetInt() > 0)
						{
							const FCompactPoseBoneIndex Parent = BC.GetParentBoneIndex(I);
							if (Parent.GetInt() >= 0 && InUpper[Parent.GetInt()]) InUpper[I.GetInt()] = true;   // parents come first
						}
						if (InUpper[I.GetInt()]) Output.Pose[I] = UpperPose.Pose[I];
					}
				}
			}

			// the additive upper clips: the new one fading in, the previous one fading out
			auto AddOn = [&Output](UAnimSequence* A, float T, float W)
			{
				if (!A || W <= 0.f) return;
				FPoseContext Additive(Output);
				Sample(A, T, Additive);
				FAnimationPoseData OutData(Output), AddData(Additive);
				FAnimationRuntime::AccumulateAdditivePose(OutData, AddData, W, A->GetAdditiveAnimType());
				Output.Pose.NormalizeRotations();
			};
			AddOn(UpperAdd, UpperT, UpperAlpha);
			if (UpperAlpha < 1.f) AddOn(PrevAdd, PrevUpperT, 1.f - UpperAlpha);

			// an additive action on top (fire kick, reload, grenade toss)
			if (Action && Action->IsValidAdditive() && ActionW > 0.f)
			{
				FPoseContext Additive(Output);
				Sample(Action, ActionT, Additive);
				FAnimationPoseData OutData(Output);
				FAnimationPoseData AddData(Additive);
				FAnimationRuntime::AccumulateAdditivePose(OutData, AddData, ActionW, Action->GetAdditiveAnimType());
				Output.Pose.NormalizeRotations();
			}

			// the aim: yaw and pitch spread over the spine (component space, so the axes do not depend on the skeleton's bone frames)
			if (!FMath::IsNearlyZero(Yaw) || !FMath::IsNearlyZero(Pitch))
			{
				static const FName Spine[] = { TEXT("spine_01"), TEXT("spine_02"), TEXT("spine_03"), TEXT("spine_04"), TEXT("spine_05") };
				TArray<FCompactPoseBoneIndex> Bones;
				for (const FName& N : Spine)
				{
					const int32 P = BC.GetPoseBoneIndexForBoneName(N);
					if (P != INDEX_NONE) Bones.Add(BC.MakeCompactPoseIndex(FMeshPoseBoneIndex(P)));
				}
				if (Bones.Num() > 0)
				{
					FCSPose<FCompactPose> CS; CS.InitPose(Output.Pose);
					// the mannequin stands along +Z, faces +Y: yaw turns about Z, pitch about the body's right axis (X); + pitch = aim up (measured)
					const FQuat Step = FQuat(FVector::UpVector, FMath::DegreesToRadians(Yaw) / Bones.Num())
					                 * FQuat(FVector(1, 0, 0), FMath::DegreesToRadians(Pitch) / Bones.Num());
					// each spine bone's TARGET is its original component rotation turned by k+1 steps — the last bone carries exactly the
					// requested yaw / pitch (setting "parent's current × step" would compound: 1+2+…+N steps)
					TArray<FTransform> Orig;
					for (const FCompactPoseBoneIndex& B : Bones) Orig.Add(CS.GetComponentSpaceTransform(B));
					FQuat Acc = FQuat::Identity;
					for (int32 K = 0; K < Bones.Num(); ++K)
					{
						Acc = Step * Acc;
						FTransform T = Orig[K];
						T.SetRotation(Acc * Orig[K].GetRotation());
						T.SetTranslation(Acc.RotateVector(Orig[K].GetTranslation() - Orig[0].GetTranslation()) + Orig[0].GetTranslation());
						TArray<FBoneTransform> One; One.Add(FBoneTransform(Bones[K], T));
						CS.SafeSetCSBoneTransforms(One);
					}
					FCSPose<FCompactPose>::ConvertComponentPosesToLocalPoses(MoveTemp(CS), Output.Pose);
				}
			}
			return true;
		}
	};
}

UHrotPersonState::UHrotPersonState()
{
	PrimaryComponentTick.bCanEverTick = true;
	PrimaryComponentTick.bStartWithTickEnabled = true;
	PrimaryComponentTick.TickGroup = TG_PrePhysics;
	bAutoActivate = true;   // an inactive component never ticks (a component added in the editor is not active by default)
}

USkeletalMeshComponent* UHrotPersonState::FindLeader() const
{
	const AActor* Owner = GetOwner();
	if (!Owner) return nullptr;
	TArray<USkeletalMeshComponent*> Meshes;
	Owner->GetComponents<USkeletalMeshComponent>(Meshes);
	for (USkeletalMeshComponent* M : Meshes)
		for (const USkeletalMeshComponent* F : Meshes)
			if (F != M && F->LeaderPoseComponent.Get() == M) return M;
	for (USkeletalMeshComponent* M : Meshes)
		if (M->GetSkinnedAsset()) return M;
	return nullptr;
}

bool UHrotPersonState::ApplyAnimation()
{
	USkeletalMeshComponent* M = FindLeader();
	if (!M) return false;
	if (M->GetAnimationMode() == EAnimationMode::AnimationBlueprint && M->GetAnimInstance()
		&& M->GetAnimInstance()->IsA<UHrotPersonAnimInstance>()) return false;
	M->SetAnimationMode(EAnimationMode::AnimationBlueprint);
	M->SetAnimInstanceClass(UHrotPersonAnimInstance::StaticClass());
	// ⭐ a person off-screen does not animate (measured, 200 soldiers in play: always-tick +23 ms, tick-when-rendered +8–10 ms);
	// distant people may update less often (URO). A contact sheet that poses people explicitly sets its own option.
	M->VisibilityBasedAnimTickOption = EVisibilityBasedAnimTickOption::OnlyTickPoseWhenRendered;
	M->bEnableUpdateRateOptimizations = true;
	return true;
}

int32 UHrotPersonState::MergeBodyParts()
{
	USkeletalMeshComponent* Leader = FindLeader();
	AActor* Owner = GetOwner();
	if (!Leader || !Owner) return 0;
	USkeletalMesh* LeaderMesh = Cast<USkeletalMesh>(Leader->GetSkinnedAsset());
	if (!LeaderMesh) return 0;
	TArray<USkeletalMeshComponent*> Meshes, Parts;
	Owner->GetComponents<USkeletalMeshComponent>(Meshes);
	FSkeletalMeshMergeParams Params;
	Params.Skeleton = LeaderMesh->GetSkeleton();
	Params.MeshesToMerge.Add(LeaderMesh);
	FString Key = LeaderMesh->GetPathName();
	for (USkeletalMeshComponent* M : Meshes)
	{
		USkeletalMesh* PM = Cast<USkeletalMesh>(M->GetSkinnedAsset());
		if (M == Leader || !PM || M->LeaderPoseComponent.Get() != Leader || !M->IsVisible()) continue;
		if (PM->GetSkeleton() != Params.Skeleton) continue;   // another skeleton (a face with its own rig): left as it is
		Params.MeshesToMerge.Add(PM); Parts.Add(M);
		Key += TEXT("|") + PM->GetPathName();
	}
	if (Parts.Num() == 0) return 0;
	static TMap<FString, TWeakObjectPtr<USkeletalMesh>> Cache;   // one merged mesh per combination of parts, shared
	USkeletalMesh* Merged = Cache.FindRef(Key).Get();
	if (!Merged)
	{
		Merged = USkeletalMergingLibrary::MergeMeshes(Params);
		if (!Merged) return 0;
		Merged->AddToRoot();   // U-19h: the cache is not a UPROPERTY - held for the process, or a later person would merge again (320 s for 300 pedestrians)
		Cache.Add(Key, Merged);
	}
	Leader->SetSkeletalMeshAsset(Merged);
	ApplyAnimation();   // a new mesh re-initialises the anim instance
	ApplyMaterials();   // … and its own materials: the uniform again
	// U-19h: the merged-in parts are DESTROYED, not hidden: a hidden component still has a render proxy whose transform is re-sent every time the actor
	// moves (measured: ~30 % of the client's game thread - 16 skinned meshes a soldier, SendAllEndOfFrameUpdates -> SkelComp UpdateTransform). A part something
	// else is attached to is only hidden.
	for (USkeletalMeshComponent* P : Parts)
	{
		if (P->GetAttachChildren().Num() == 0) P->DestroyComponent(); else P->SetVisibility(false);
	}
	// … and so is any other skinned part that is hidden for good (a kit's HideComponents: the body's own gun) - never the leader or the held weapon
	const UMeshComponent* Held = GetWeaponComponent();
	for (USkeletalMeshComponent* M : Meshes)
		if (IsValid(M) && M != Leader && M != Held && !M->IsVisible() && M->GetAttachChildren().Num() == 0) M->DestroyComponent();
	QuietOwner();
	return Parts.Num();
}

int32 UHrotPersonState::ApplyMaterials()
{
	USkeletalMeshComponent* Body = FindLeader();
	if (!Body) return 0;
	int32 N = 0;
	const TArray<FName> Slots = Body->GetMaterialSlotNames();
	for (const TPair<FName, TObjectPtr<UMaterialInterface>>& O : MaterialOverrides)
		if (O.Value && Slots.Contains(O.Key)) { Body->SetMaterialByName(O.Key, O.Value); ++N; }
	return N;
}

int32 UHrotPersonState::QuietOwner()
{
	AActor* Owner = GetOwner();
	if (!Owner) return 0;
	Owner->SetActorTickEnabled(false);
	USkeletalMeshComponent* Leader = FindLeader();
	const UMeshComponent* WeaponComp = GetWeaponComponent();
	TArray<USkeletalMeshComponent*> Meshes;
	Owner->GetComponents<USkeletalMeshComponent>(Meshes);
	int32 N = 0;
	for (USkeletalMeshComponent* M : Meshes)
	{
		if (M == Leader || M == WeaponComp) continue;
		if (!M->GetSkinnedAsset() || !M->IsVisible()) { M->SetComponentTickEnabled(false); ++N; continue; }
		// ⚠ measured LIVE (U-18i): a City Sample crowd person's FACE runs its own head Anim BP (RigLogic) every frame — 6 pedestrians on
		// screen took the town from ~33 to ~17 fps. ⇒ a visible part that animates ON ITS OWN (not following the leader) follows the
		// leader's pose instead and drops its Anim BP: the face takes the head's motion, with a neutral expression
		if (Leader && !M->LeaderPoseComponent.IsValid() && M->GetAnimInstance())
		{
			M->SetAnimInstanceClass(nullptr);
			M->SetLeaderPoseComponent(Leader);
			++N;
		}
	}
	return N;
}

UMeshComponent* UHrotPersonState::GetWeaponComponent() const
{
	const AActor* Owner = GetOwner();
	if (!Owner) return nullptr;
	TArray<UMeshComponent*> All;
	Owner->GetComponents<UMeshComponent>(All);
	for (UMeshComponent* C : All)
		if (C->GetFName() == TEXT("HrotWeapon")) return C;
	return nullptr;
}

UMeshComponent* UHrotPersonState::ApplyWeapon()
{
	AActor* Owner = GetOwner();
	USkeletalMeshComponent* Hand = FindLeader();
	UMeshComponent* C = GetWeaponComponent();
	if (!Owner || !Hand) return C;
	UStaticMesh* const SM = Cast<UStaticMesh>(Weapon);
	USkeletalMesh* const SK = Cast<USkeletalMesh>(Weapon);
	const bool bWrongKind = C && ((SM && !C->IsA<UStaticMeshComponent>()) || (SK && !C->IsA<USkeletalMeshComponent>()));
	if (bWrongKind || (!SM && !SK))
	{
		if (C)   // unarmed / holstered, or the kind changed
		{
			// ⚠ measured (U-17h, a rifle swapped for the RPG-7): the destroyed component kept the name "HrotWeapon" until collected, and
			// the new one of the other class under that name was a FATAL error ("Cannot replace existing object of a different class")
			C->Rename(*MakeUniqueObjectName(Owner, C->GetClass(), TEXT("HrotWeapon_Old")).ToString(), nullptr, REN_DontCreateRedirectors | REN_NonTransactional);
			Owner->RemoveInstanceComponent(C); C->DestroyComponent(); C = nullptr;
		}
		if (!SM && !SK) return nullptr;
	}
	if (!C)
	{
		if (SM) C = NewObject<UStaticMeshComponent>(Owner, TEXT("HrotWeapon"), RF_Transactional);
		else
		{
			USkeletalMeshComponent* K = NewObject<USkeletalMeshComponent>(Owner, TEXT("HrotWeapon"), RF_Transactional);
			K->SetAnimationMode(EAnimationMode::AnimationSingleNode);   // the weapon's own bones stay in their reference pose
			// ⭐ its sockets ride on its bones (Lyra's Muzzle is on "Barrel"): bones never refreshed ⇒ the muzzle read beside the grip,
			// pointing sideways (measured, live fire) — a weapon has a handful of bones, always refreshed
			K->VisibilityBasedAnimTickOption = EVisibilityBasedAnimTickOption::OnlyTickPoseWhenRendered;
			C = K;
		}
		C->SetMobility(EComponentMobility::Movable);
		C->SetCollisionEnabled(ECollisionEnabled::NoCollision);
		Owner->AddInstanceComponent(C);   // an instance component: saved with the actor, duplicated into play
		C->RegisterComponent();
	}
	if (SM) CastChecked<UStaticMeshComponent>(C)->SetStaticMesh(SM);
	else
	{
		USkeletalMeshComponent* K = CastChecked<USkeletalMeshComponent>(C);
		K->SetSkeletalMeshAsset(SK);
		K->VisibilityBasedAnimTickOption = EVisibilityBasedAnimTickOption::OnlyTickPoseWhenRendered;   // also on a copy made in play
		K->RefreshBoneTransforms();
		// ⭐ its bones never animate: posed ONCE (its sockets right), then no tick — it follows the hand by attachment.
		// Measured, 1000 soldiers in play: the weapons' ticks cost 3.1 ms for nothing
		K->SetComponentTickEnabled(false);
	}
	C->SetVisibility(true);
	C->AttachToComponent(Hand, FAttachmentTransformRules::SnapToTargetNotIncludingScale, WeaponSocket);
	C->SetRelativeTransform(WeaponOffset);
	if (bFlamethrower || bFlameFiring) ApplyFlame();
	return C;
}

void UHrotPersonState::SetFlameFiring(bool bFiring)
{
	bFlameFiring = bFiring;
	ApplyFlame();
}

UHrotFlamethrower* UHrotPersonState::ApplyFlame()
{
	UMeshComponent* C = GetWeaponComponent();
	if (!C) return nullptr;
	UHrotFlamethrower* F = nullptr;
	for (USceneComponent* Child : C->GetAttachChildren()) if (UHrotFlamethrower* X = Cast<UHrotFlamethrower>(Child)) { F = X; break; }
	if (!F && bFlamethrower)
	{
		const FName Muzzle = C->DoesSocketExist(TEXT("Muzzle")) ? FName(TEXT("Muzzle")) : NAME_None;
		F = UHrotFlamethrower::Attach(C, Muzzle, FTransform::Identity, FlameSize);
	}
	if (F) F->SetFiring(bFlamethrower && bFlameFiring);
	return F;
}

void UHrotPersonState::OnRegister()
{
	Super::OnRegister();
	if (!IsTemplate() && GetOwner()) ApplyAnimation();
}

void UHrotPersonState::TickComponent(float DeltaTime, ELevelTick TickType, FActorComponentTickFunction* ThisTickFunction)
{
	Super::TickComponent(DeltaTime, TickType, ThisTickFunction);
	ApplyAnimation();
	ApplyWeapon();   // a body Blueprint rebuilt in play keeps the weapon in the hand
	ApplyMaterials();
	QuietOwner();    // play copies the components with their ticks on: the hidden parts and the actor stop here
	if (UHrotPeopleLod* Lod = UHrotPeopleLod::Get(this)) Lod->Add(this);   // how often it updates: by distance (staggered)
	bAppliedInPlay = true;
	SetComponentTickEnabled(false);   // once: nothing per frame
}

void UHrotPersonAnimInstance::NativeUpdateAnimation(float DeltaSeconds)
{
	Super::NativeUpdateAnimation(DeltaSeconds);
	TObjectPtr<UAnimSequence> NewBase = BaseAnim, NewUpper = UpperAnim;
	if (const AActor* Owner = GetOwningActor())
		if (const UHrotPersonState* St = Owner->FindComponentByClass<UHrotPersonState>())
		{
			NewBase = St->BaseAnim; NewUpper = St->UpperAnim; bBaseLoops = St->bBaseLoops; ActionAnim = St->ActionAnim; UpperRootBone = St->UpperRootBone;
			ActionTime = St->ActionTime; ActionWeight = St->ActionWeight; AimYaw = St->AimYaw; AimPitch = St->AimPitch;
			PlayRate = St->PlayRate; TimeOffset = St->TimeOffset; BlendTime = St->BlendTime; AimTurnRate = St->AimTurnRate;
		}
	const float Dt = DeltaSeconds * PlayRate;
	const bool bFirst = !bStarted;
	bStarted = true;

	// a state change starts a crossfade (the first state is taken at once — nothing to blend from)
	if (NewBase != BaseAnim)
	{
		if (!bFirst && BaseAnim) { PrevBaseAnim = BaseAnim; PrevBaseTime = BaseTime; BaseBlendAlpha = 0.f; BaseTime = bBaseLoops ? SamePhase(BaseTime, BaseAnim, NewBase) : 0.f; }
		else if (!bBaseLoops) BaseTime = 0.f;   // a one-shot always plays from its start
		BaseAnim = NewBase;
	}
	if (NewUpper != UpperAnim)
	{
		if (!bFirst) { PrevUpperAnim = UpperAnim; PrevUpperTime = UpperTime; UpperBlendAlpha = 0.f; UpperTime = 0.f; }
		UpperAnim = NewUpper;
	}
	const float Step = BlendTime > 0.f ? DeltaSeconds / BlendTime : 1.f;
	BaseBlendAlpha = FMath::Min(1.f, BaseBlendAlpha + Step);
	UpperBlendAlpha = FMath::Min(1.f, UpperBlendAlpha + Step);
	if (BaseBlendAlpha >= 1.f) PrevBaseAnim = nullptr;
	if (UpperBlendAlpha >= 1.f) PrevUpperAnim = nullptr;

	if (bFirst) { BaseTime = bBaseLoops ? TimeOffset : 0.f; UpperTime = TimeOffset; AimYawNow = AimYaw; AimPitchNow = AimPitch; }
	BaseTime = bBaseLoops ? Wrap(BaseTime + Dt, BaseAnim)
	                      : FMath::Min(BaseTime + Dt, BaseAnim ? (float)BaseAnim->GetPlayLength() - 1.e-3f : 0.f);   // held at its end
	UpperTime = Wrap(UpperTime + Dt, UpperAnim);
	PrevBaseTime = Wrap(PrevBaseTime + Dt, PrevBaseAnim);
	PrevUpperTime = Wrap(PrevUpperTime + Dt, PrevUpperAnim);
	ActionClock = Wrap(ActionClock + Dt, ActionAnim);
	// the aim turns toward its target at AimTurnRate degrees a second (never snaps)
	const float MaxTurn = AimTurnRate > 0.f ? AimTurnRate * DeltaSeconds : 1e6f;
	AimYawNow += FMath::Clamp(FMath::FindDeltaAngleDegrees(AimYawNow, AimYaw), -MaxTurn, MaxTurn);
	AimPitchNow += FMath::Clamp(AimPitch - AimPitchNow, -MaxTurn, MaxTurn);
}

FAnimInstanceProxy* UHrotPersonAnimInstance::CreateAnimInstanceProxy()
{
	return new FHrotPersonProxy(this);
}
