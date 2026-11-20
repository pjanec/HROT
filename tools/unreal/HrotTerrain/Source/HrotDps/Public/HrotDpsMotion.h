// CE-4651 (U-18b) — the MOTION of a DPS entity, as pure functions (unit-tested in Private/Tests, no world needed).
//
// ⭐ All curve math runs in the CONTRACT's frame (SimNetGateway DD-017 §3.3: metres, x east, y north, z up, right-handed, body
// forward = +x, sim seconds); the conversion to Unreal (cm, +X east, +Y SOUTH — the terrain importer's convention, HrotTerrainLibrary.h —
// left-handed) happens ONCE, at the end (the guide: "convert once, at the boundary").
//
// ⭐ The curve is the guide's ONE trajectory model (DD-018 §3.6c), LIVE mode: past the newest sample second-order dead reckoning
// (p + v·t + a·t²/2, orientation advanced by the angular velocity), and a new sample blends from the pose on screen to the new curve
// over 0.25 s. It is also the SENDER's reference model (DD-018: a sample is sent when the truth leaves this extrapolation by 0.05 m /
// 1 degree / 0.1 m/s, or after the 5 s heartbeat) — so extrapolating the same way keeps the error within the sender's thresholds.
// ⏳ The DELAYED mode (Hermite between samples, ~1.2 s behind) is for background traffic and is not built yet.
#pragma once

#include "CoreMinimal.h"

/** one sample of an entity's motion, in the contract's frame */
struct FHrotDpsTrackPoint
{
	double T = 0;                          // sim seconds
	FVector3d P = FVector3d::ZeroVector;   // metres, ENU
	FQuat4d Q = FQuat4d::Identity;         // body -> ENU, forward = +x
	FVector3d V = FVector3d::ZeroVector;   // m/s
	FVector3d A = FVector3d::ZeroVector;   // m/s²
	FVector3d W = FVector3d::ZeroVector;   // rad/s, ⚠ taken as WORLD-frame (the contract does not state the frame — an open point)
	FVector3d WDot = FVector3d::ZeroVector;// rad/s²
};

/** a pose in the contract's frame */
struct FHrotDpsPose
{
	FVector3d P = FVector3d::ZeroVector;
	FQuat4d Q = FQuat4d::Identity;
};

/** a correction: how far the pose on screen was from the new curve when the sample came; it fades out over BlendSeconds */
struct FHrotDpsCorrection
{
	FVector3d DP = FVector3d::ZeroVector;
	FQuat4d DQ = FQuat4d::Identity;
};

namespace HrotDpsMotion
{
	/** the longest extrapolation (s): past it the pose is held — 2 heartbeats without a sample means the producer is gone or paused */
	constexpr double MaxExtrapolation = 10.0;
	/** the correction blend (s), the guide's value */
	constexpr double BlendSeconds = 0.25;

	/** second-order dead reckoning of `K` to sim time `T` (clamped to [K.T, K.T + MaxExtrapolation]) */
	HROTDPS_API FHrotDpsPose Extrapolate(const FHrotDpsTrackPoint& K, double T);

	/** the correction that keeps `Shown` (the pose on screen when the sample came) continuous with `NewCurve` (the new sample's curve
	 *  at that same time) */
	HROTDPS_API FHrotDpsCorrection Correction(const FHrotDpsPose& Shown, const FHrotDpsPose& NewCurve);
	/** `Curve` (the new curve NOW) with `C` faded to `1 - Alpha` (Alpha 0..1). ⭐ The offset rides on the MOVING curve: ⚠ measured
	 *  (U-18b) — a lerp from a FIXED start pose trails a moving entity by up to v·BlendSeconds/4 (0.75 m at 12 m/s) at every sample */
	HROTDPS_API FHrotDpsPose Corrected(const FHrotDpsPose& Curve, const FHrotDpsCorrection& C, double Alpha);

	/** `Q` with the look-only bank applied: a right-hand roll of `Bank` rad about the body's forward (+x) — EntityState.Bank, the lean
	 *  HROT's map draws for two-wheelers and aircraft on top of the simulated orientation */
	HROTDPS_API FQuat4d WithBank(const FQuat4d& Q, double Bank);

	/** contract position (m, ENU) -> Unreal location (cm), relative to the Unreal point of the contract's origin */
	HROTDPS_API FVector ToUnreal(const FVector3d& P, const FVector& OriginCm = FVector::ZeroVector);
	/** contract orientation -> Unreal rotation (the y-axis mirror: (x, y, z, w) -> (-x, y, -z, w)) */
	HROTDPS_API FQuat ToUnreal(const FQuat4d& Q);
	/** both, as an actor transform */
	HROTDPS_API FTransform ToUnreal(const FHrotDpsPose& Pose, const FVector& OriginCm = FVector::ZeroVector);
}

/** the SIM CLOCK: the producer's simulation time, estimated on this machine. Everything moves by it (the guide: "everything in the map
 *  is driven by sim time … a pause freezes it") — never by Unreal's own game time.
 *  ⭐ It only moves FORWARD on samples: a sample is never newer than the producer's clock, so the newest SimTime seen is a lower bound
 *  (latency only makes samples older). ⚠ A late joiner's TRANSIENT_LOCAL history carries OLD times — they must not pull it back;
 *  a backward jump (a replay seek) comes as a new world epoch, which resets it. */
struct HROTDPS_API FHrotDpsClock
{
	/** sim time at local real time `RealNow` (FPlatformTime::Seconds); 0 before the first sample */
	double Now(double RealNow) const { return bValid ? Base + (RealNow - RealBase) * Scale : 0.0; }
	bool IsValid() const { return bValid; }
	double GetScale() const { return Scale; }

	/** a state or event sample stamped `SimTime` arrived at `RealNow` */
	void OnSample(double SimTime, double RealNow);
	/** Session: the time scale (0 while paused); keeps the current estimate continuous */
	void OnRate(double NewScale, double RealNow);
	void Reset() { bValid = false; }

private:
	double Base = 0, RealBase = 0, Scale = 1;
	bool bValid = false;
};
