// CE-4651 (U-16) — see the header.
#include "HrotPersonAnimInstance.h"

#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimSequence.h"
#include "Animation/AttributesRuntime.h"
#include "AnimationRuntime.h"
#include "BonePose.h"
#include "GameFramework/Actor.h"

namespace
{
	float Wrap(float T, const UAnimSequence* A)
	{
		const float L = A ? (float)A->GetPlayLength() : 0.f;
		return L > 0.f ? FMath::Fmod(FMath::Max(T, 0.f), L) : 0.f;
	}

	struct FHrotPersonProxy : public FAnimInstanceProxy
	{
		explicit FHrotPersonProxy(UAnimInstance* In) : FAnimInstanceProxy(In) {}

		TObjectPtr<UAnimSequence> Base, Upper, Action;
		FName UpperRoot;
		float BaseT = 0.f, UpperT = 0.f, ActionT = 0.f, ActionW = 1.f, Yaw = 0.f, Pitch = 0.f;

		virtual void PreUpdate(UAnimInstance* InAnimInstance, float DeltaSeconds) override
		{
			FAnimInstanceProxy::PreUpdate(InAnimInstance, DeltaSeconds);
			const UHrotPersonAnimInstance* P = CastChecked<UHrotPersonAnimInstance>(InAnimInstance);
			Base = P->BaseAnim; Upper = P->UpperAnim; Action = P->ActionAnim; UpperRoot = P->UpperRootBone;
			BaseT = P->BaseTime; UpperT = P->UpperTime; ActionW = P->ActionWeight; Yaw = P->AimYaw; Pitch = P->AimPitch;
			ActionT = P->ActionTime >= 0.f ? P->ActionTime : P->ActionClock;
		}

		static void Sample(UAnimSequence* A, float T, FPoseContext& Into)
		{
			FAnimationPoseData Data(Into);
			A->GetAnimationPose(Data, FAnimExtractContext(double(T), false));
		}

		virtual bool Evaluate(FPoseContext& Output) override
		{
			if (!Base) { Output.ResetToRefPose(); return true; }
			Sample(Base, BaseT, Output);
			const FBoneContainer& BC = Output.Pose.GetBoneContainer();

			// in place: the root keeps no translation (HROT moves the person; the clip only animates)
			Output.Pose[FCompactPoseBoneIndex(0)].SetTranslation(FVector::ZeroVector);

			// the upper body from UpperRoot up, taken from the upper clip (local space)
			if (Upper)
			{
				FPoseContext UpperPose(Output);
				Sample(Upper, UpperT, UpperPose);
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

void UHrotPersonAnimInstance::NativeUpdateAnimation(float DeltaSeconds)
{
	Super::NativeUpdateAnimation(DeltaSeconds);
	if (const AActor* Owner = GetOwningActor())
		if (const UHrotPersonState* St = Owner->FindComponentByClass<UHrotPersonState>())
		{
			BaseAnim = St->BaseAnim; UpperAnim = St->UpperAnim; ActionAnim = St->ActionAnim; UpperRootBone = St->UpperRootBone;
			ActionTime = St->ActionTime; ActionWeight = St->ActionWeight; AimYaw = St->AimYaw; AimPitch = St->AimPitch;
			PlayRate = St->PlayRate; TimeOffset = St->TimeOffset;
		}
	const float Dt = DeltaSeconds * PlayRate;
	BaseTime = Wrap(BaseTime + Dt + (BaseTime == 0.f ? TimeOffset : 0.f), BaseAnim);
	UpperTime = Wrap(UpperTime + Dt + (UpperTime == 0.f ? TimeOffset : 0.f), UpperAnim);
	ActionClock = Wrap(ActionClock + Dt, ActionAnim);
}

FAnimInstanceProxy* UHrotPersonAnimInstance::CreateAnimInstanceProxy()
{
	return new FHrotPersonProxy(this);
}
