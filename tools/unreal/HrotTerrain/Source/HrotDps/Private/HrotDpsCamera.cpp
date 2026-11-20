// CE-4651 (U-18l) — see HrotDpsCamera.h (each function: its HROT original's name and rule)
#include "HrotDpsCamera.h"

namespace HrotCamera
{
	float ScaleOf(EScheme S) { return S == EScheme::Person ? 1.f : S == EScheme::Vehicle ? 4.f : 8.f; }
	float DefaultDistance(EScheme S) { return S == EScheme::Person ? PersonDefault : S == EScheme::Vehicle ? VehicleDefault : FlyerDefault; }
	float MaxPlayDistance(EScheme S) { return S == EScheme::Person ? 5.f : S == EScheme::Vehicle ? 24.f : 45.f; }
	float MaxAhead(EScheme S) { return 0.75f * ScaleOf(S); }

	float NearBlend(float Distance, EScheme S)
	{
		const float Sc = ScaleOf(S);
		const float T = FMath::Clamp((Far * Sc - Distance) / ((Far - Near) * Sc), 0.f, 1.f);
		return T * T * (3.f - 2.f * T);
	}

	static FVector3d Level(const FVector3d& V)
	{
		const FVector3d L(V.X, V.Y, 0);
		return L.SizeSquared() > 1e-6 ? L.GetUnsafeNormal() : FVector3d(1, 0, 0);
	}
	static FVector3d SafeNormal(const FVector3d& V) { return V.SizeSquared() > 1e-6 ? V.GetUnsafeNormal() : FVector3d(1, 0, 0); }

	FVector3d Pivot(const FVector3d& Centre, const FVector3d& Eye, const FVector3d& Heading, float Distance, EScheme S, int32 SideSign)
	{
		const FVector3d L = Level(Heading);
		const FVector3d Right(L.Y, -L.X, 0);   // Z up, Y left (ENU): the right of a heading
		const double K = FMath::Clamp(Distance / SideFullAt, 0.f, 1.f);
		FVector3d NearP;
		if (S == EScheme::Person) NearP = Eye + Right * (Side * SideSign * K) + FVector3d(0, 0, Lift * K);
		else if (S == EScheme::Vehicle) NearP = Eye + FVector3d(0, 0, VehicleLiftPerMetre * Distance);
		else NearP = Eye + FVector3d(0, 0, FlyerLiftPerMetre * Distance);
		return FMath::Lerp(Centre, NearP, double(NearBlend(Distance, S)));
	}

	FVector3d Camera(const FVector3d& PivotP, const FVector3d& Look, float Distance, bool bOrbitPitch)
	{
		return PivotP - (bOrbitPitch ? SafeNormal(Look) : Level(Look)) * Distance;
	}

	FVector3d SpringArm(const FVector3d& PivotP, const FVector3d& Wanted, double HitFraction, float Margin)
	{
		const FVector3d Arm = Wanted - PivotP;
		const double Length = Arm.Size();
		if (HitFraction < 0 || Length < 1e-3) return Wanted;
		const double Reach = FMath::Clamp(HitFraction * Length - Margin, 0.0, Length);
		return PivotP + Arm / Length * Reach;
	}

	float EaseArm(float Current, float Wanted, float Dt, float Scale)
	{
		return Wanted <= Current ? Wanted : FMath::Min(Wanted, Current + ArmOutRate * Scale * Dt);
	}

	FVector3d AboveHeadWhenSqueezed(const FVector3d& At, const FVector3d& Eye, float Arm, float FullArm)
	{
		if (FullArm <= 1e-3f || Arm >= FullArm) return At;
		const double Squeeze = FMath::Clamp(1.0 - Arm / FullArm, 0.0, 1.0);
		const double Lifted = At.Z + (Eye.Z + SqueezedAboveEyes - At.Z) * Squeeze;
		return FVector3d(At.X, At.Y, FMath::Max(At.Z, Lifted));
	}

	FVector3d Forward(double Yaw, double Pitch)
	{
		return FVector3d(FMath::Cos(Pitch) * FMath::Cos(Yaw), FMath::Cos(Pitch) * FMath::Sin(Yaw), FMath::Sin(Pitch));
	}

	float Step(float Distance, float Wheel)
	{
		if (Distance < EyesBelow) return Wheel < 0.f ? SideFullAt : 0.f;
		const float Next = Wheel > 0.f ? Distance / FMath::Pow(WheelFactor, Wheel) : Distance * FMath::Pow(WheelFactor, -Wheel);
		return (Next < EyesBelow * 2.f && Wheel > 0.f) ? 0.f : FMath::Clamp(Next, EyesBelow, FollowMax);
	}

	float FlyBasisSpeed(float HeightAboveGround)
	{
		const float H = FMath::Max(0.f, HeightAboveGround);
		const float GroundBoost = 1.f + (FlyGroundBoost - 1.f) * FMath::Clamp((FlyGroundBoostUntil - H) / (FlyGroundBoostUntil - FlyGroundBoostFrom), 0.f, 1.f);
		return FMath::Max(FlyMinSpeed, H * FlySpeedPerHeight * GroundBoost);
	}

	float FlySpeed(float HeightAboveGround, float HeldSeconds, bool bShift, bool bCtrl)
	{
		const float Basis = FlyBasisSpeed(HeightAboveGround);
		const float Ramp = HeldSeconds < FlyRampSeconds
			? FlyStartShare + (1.f - FlyStartShare) * (HeldSeconds / FlyRampSeconds)
			: 1.f + (FlyMaxAcceleration - 1.f) * FMath::Min(1.f, (HeldSeconds - FlyRampSeconds) / (FlyAccelerateSeconds - FlyRampSeconds));
		const float Boost = bShift && bCtrl ? FlyBothBoost : bCtrl ? FlyCtrlBoost : bShift ? FlyShiftBoost : 1.f;
		return Basis * Ramp * Boost;
	}

	float WheelStepGrowth(int32 Streak) { return FMath::Min(WheelStreakMax, 1.f + WheelStreakGrowth * Streak); }
	float WheelStep(float HeightAboveGround, int32 Streak) { return FlyBasisSpeed(HeightAboveGround) * WheelStepSeconds * WheelStepGrowth(Streak); }
}
