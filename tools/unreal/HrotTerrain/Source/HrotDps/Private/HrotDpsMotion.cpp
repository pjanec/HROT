// CE-4651 (U-18b) — see HrotDpsMotion.h
#include "HrotDpsMotion.h"

namespace
{
	/** the rotation of a constant world-frame angular velocity `W` over `Dt` seconds */
	FQuat4d Turn(const FVector3d& W, double Dt)
	{
		const double Angle = W.Size() * Dt;
		if (Angle < 1e-9) return FQuat4d::Identity;
		return FQuat4d(W.GetUnsafeNormal(), Angle);
	}
}

FHrotDpsPose HrotDpsMotion::Extrapolate(const FHrotDpsTrackPoint& K, double T)
{
	const double Dt = FMath::Clamp(T - K.T, 0.0, MaxExtrapolation);
	FHrotDpsPose Out;
	Out.P = K.P + K.V * Dt + K.A * (0.5 * Dt * Dt);
	// the mean rate over the step (W + WDot·dt/2) — exact for a constant angular acceleration about a fixed axis
	Out.Q = (Turn(K.W + K.WDot * (0.5 * Dt), Dt) * K.Q).GetNormalized();
	return Out;
}

FHrotDpsCorrection HrotDpsMotion::Correction(const FHrotDpsPose& Shown, const FHrotDpsPose& NewCurve)
{
	FHrotDpsCorrection C;
	C.DP = Shown.P - NewCurve.P;
	C.DQ = (Shown.Q * NewCurve.Q.Inverse()).GetNormalized();   // world-frame: Shown.Q = DQ · NewCurve.Q
	return C;
}

FHrotDpsPose HrotDpsMotion::Corrected(const FHrotDpsPose& Curve, const FHrotDpsCorrection& C, double Alpha)
{
	const double Left = 1.0 - FMath::Clamp(Alpha, 0.0, 1.0);
	FHrotDpsPose Out;
	Out.P = Curve.P + C.DP * Left;
	Out.Q = (FQuat4d::Slerp(FQuat4d::Identity, C.DQ, Left) * Curve.Q).GetNormalized();
	return Out;
}

FQuat4d HrotDpsMotion::WithBank(const FQuat4d& Q, double Bank)
{
	return Bank == 0.0 ? Q : (Q * FQuat4d(FVector3d::XAxisVector, Bank)).GetNormalized();
}

FVector HrotDpsMotion::ToUnreal(const FVector3d& P, const FVector& OriginCm)
{
	return OriginCm + FVector(P.X * 100.0, -P.Y * 100.0, P.Z * 100.0);
}

FQuat HrotDpsMotion::ToUnreal(const FQuat4d& Q)
{
	// R' = M·R·M with M = diag(1, -1, 1): a mirror flips the rotation's sense, so the axis maps by M and the angle negates
	return FQuat(-Q.X, Q.Y, -Q.Z, Q.W).GetNormalized();
}

FTransform HrotDpsMotion::ToUnreal(const FHrotDpsPose& Pose, const FVector& OriginCm)
{
	return FTransform(ToUnreal(Pose.Q), ToUnreal(Pose.P, OriginCm));
}

void FHrotDpsClock::OnSample(double SimTime, double RealNow)
{
	if (!bValid) { Base = SimTime; RealBase = RealNow; bValid = true; return; }
	if (SimTime > Now(RealNow)) { Base = SimTime; RealBase = RealNow; }   // behind the producer: catch up (never back)
}

void FHrotDpsClock::OnRate(double NewScale, double RealNow)
{
	if (bValid) { Base = Now(RealNow); RealBase = RealNow; }
	Scale = FMath::Max(0.0, NewScale);
}
