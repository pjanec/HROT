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

double FHrotDpsClock::Formula(double RealNow) const
{
	if (bStepped) return AnchorSim;
	// ⚠ measured (the Anchor rail): a tick is ~6.4e17, where a double resolves only 128 ticks (12.8 µs) — subtract in int64 FIRST
	const double Since = double(LocalTicks(RealNow) - AnchorWall) + Offset;
	return AnchorSim + Since * 1e-7 * AScale;
}

double FHrotDpsClock::Now(double RealNow) const
{
	if (bAnchored)
	{
		double T = Formula(RealNow);
		if (EaseStart >= 0 && !bStepped)
		{
			const double A = FMath::Clamp((RealNow - EaseStart) / EaseSeconds, 0.0, 1.0);
			T += EaseD * (1.0 - A);
		}
		return T;
	}
	return bValid ? Base + (RealNow - RealBase) * Scale : 0.0;
}

void FHrotDpsClock::OnSample(double SimTime, double RealNow)
{
	if (bAnchored) return;   // U-18p: the station's formula owns the time
	if (!bValid) { Base = SimTime; RealBase = RealNow; bValid = true; return; }
	if (SimTime > Now(RealNow)) { Base = SimTime; RealBase = RealNow; }   // behind the producer: catch up (never back)
}

void FHrotDpsClock::OnRate(double NewScale, double RealNow)
{
	if (bAnchored) return;
	if (bValid) { Base = Now(RealNow); RealBase = RealNow; }
	Scale = FMath::Max(0.0, NewScale);
}

bool FHrotDpsClock::OnAnchor(bool bInStepped, int64 AnchorWallTicks, double AnchorSimTime, double TimeScale, int64 TimelineEpoch, int64 InStepSeq,
	double RealNow)
{
	const bool bJump = !bAnchored || TimelineEpoch != TEpoch;
	const double Before = IsValid() ? Now(RealNow) : 0.0;
	const bool bWasValid = IsValid();
	bAnchored = true;
	bStepped = bInStepped;
	AnchorWall = AnchorWallTicks; AnchorSim = AnchorSimTime; AScale = FMath::Max(0.0, double(TimeScale));
	TEpoch = TimelineEpoch; StepSeq = InStepSeq;
	EaseStart = -1; EaseD = 0;
	// within one epoch a small difference eases out (never on a pause: D-A2 freezes at once; never from the sample-fed estimate's first anchor)
	if (!bJump && bWasValid && !bStepped)
	{
		const double D = Before - Formula(RealNow);
		if (FMath::Abs(D) < EaseUpTo) { EaseD = D; EaseStart = RealNow; }
	}
	return bJump;
}

bool FHrotDpsClock::OnStep(int64 InStepSeq, double TargetSimTime, int64 TimelineEpoch, double RealNow)
{
	const bool bJump = bAnchored && TimelineEpoch != TEpoch;
	if (!bAnchored || bJump)   // a step with no anchor yet, or of a new epoch: it is the anchor (Stepped, held at its target)
	{
		bAnchored = true; bStepped = true; TEpoch = TimelineEpoch;
		AnchorWall = LocalTicks(RealNow) + int64(Offset); AScale = 0;
	}
	if (!bStepped) return bJump;   // a step while running continuous: the formula already carries it
	AnchorSim = TargetSimTime; StepSeq = InStepSeq;
	EaseStart = -1; EaseD = 0;
	return bJump;
}

bool FHrotDpsClock::OnEcho(int64 ClientSend, int64 StationReceive, int64 StationTransmit, int64 ClientBack)
{
	const double Rtt = double(ClientBack - ClientSend) - double(StationTransmit - StationReceive);
	LastRttMs = Rtt * 1e-4;
	if (Rtt < 0 || LastRttMs > MaxRttMs) { ++Dropped; return false; }
	const double New = (double(StationReceive - ClientSend) + double(StationTransmit - ClientBack)) * 0.5;
	if (!bOffset || FMath::Abs(New - Offset) > SnapOffsetTicks) Offset = New;   // the first, or a > 1 s change: hard-snap
	else Offset += (New - Offset) * SteerGain;                                   // else steer 10 %
	bOffset = true;
	++Echoes;
	return true;
}
