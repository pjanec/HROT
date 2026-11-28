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
	/** U-19h ⑱ step B: does a drawn body need its transform set? Only when it moved more than PosCm or turned more than AngDeg, or its scale
	 *  differs - measured on Stechovice: re-placing all 89 drawn bodies every frame cost 3.1 ms (each crowd body is several skinned meshes)
	 *  and fed Unreal's end-of-frame updates; a body standing still costs nothing now. 0.1 cm / 0.05 deg are below anything visible. */
	inline bool NeedsPlace(const FTransform& Now, const FTransform& Want, double PosCm = 0.1, double AngDeg = 0.05)
	{
		if (FVector::DistSquared(Now.GetLocation(), Want.GetLocation()) > PosCm * PosCm) return true;
		if (!Now.GetScale3D().Equals(Want.GetScale3D(), 1e-4)) return true;
		return Now.GetRotation().AngularDistance(Want.GetRotation()) > FMath::DegreesToRadians(AngDeg);
	}
}

/** the SIM CLOCK: the producer's simulation time, estimated on this machine. Everything moves by it (the guide: "everything in the map
 *  is driven by sim time … a pause freezes it") — never by Unreal's own game time.
 *  ⭐ It only moves FORWARD on samples: a sample is never newer than the producer's clock, so the newest SimTime seen is a lower bound
 *  (latency only makes samples older). ⚠ A late joiner's TRANSIENT_LOCAL history carries OLD times — they must not pull it back;
 *  a backward jump (a replay seek) comes as a new world epoch, which resets it. */
//
/// ⭐ U-18p (DD-022 §3b, round 7) — once the station's `SimClock` arrives the clock is HROT's SLAVE FORMULA and samples no longer move it:
///   Continuous  sim(now) = AnchorSimTime + (stationNow − AnchorWallTicks) / 10⁷ × TimeScale,  stationNow = LocalTicks(now) + offset
///   Stepped     sim = AnchorSimTime (held; a ClockStep presents its TargetSimTime exactly — never extrapolated past it)
/// A re-anchor within one TimelineEpoch EASES a small difference out over the guide's 0.25 s blend; a pause freezes at once (D-A2); a new
/// TimelineEpoch JUMPS. The offset comes ONLY from the ClockProbe / ClockEcho handshake (HROT's TimeConfig: drop RTT > 200 ms, hard-snap
/// the first or a > 1 s change, else steer 10 %) — ⚠ never from this machine's UtcNow: HROT's tick is UTC read once at ITS process start
/// plus a stopwatch (HighResUtcClock), so two processes on one machine differ.
struct HROTDPS_API FHrotDpsClock
{
	/** sim time at local real time `RealNow` (FPlatformTime::Seconds); 0 before the first sample or anchor */
	double Now(double RealNow) const;
	bool IsValid() const { return bValid || bAnchored; }
	/** sim seconds per real second now (0 while paused or stepped) */
	double GetScale() const { return bAnchored ? (bStepped ? 0.0 : AScale) : Scale; }

	/** a state or event sample stamped `SimTime` arrived at `RealNow` (moves the clock only until the first anchor) */
	void OnSample(double SimTime, double RealNow);
	/** Session: the time scale (0 while paused); keeps the current estimate continuous (ignored once anchored) */
	void OnRate(double NewScale, double RealNow);
	/** a new world: forgets the sample-fed estimate. ⭐ The anchor and the offset stay — SimClock is keep-last-1 and is not re-sent */
	void Reset() { bValid = false; }

	// ── U-18p: the station's anchor ──
	/** SimClock. Returns true when the time JUMPED (a new TimelineEpoch, or the first anchor): curves must not blend across it */
	bool OnAnchor(bool bInStepped, int64 AnchorWallTicks, double AnchorSimTime, double TimeScale, int64 TimelineEpoch, int64 StepSeq, double RealNow);
	/** ClockStep: while Stepped, presents TargetSimTime exactly. Returns true on a jump (a new TimelineEpoch) */
	bool OnStep(int64 StepSeq, double TargetSimTime, int64 TimelineEpoch, double RealNow);
	bool IsAnchored() const { return bAnchored; }
	bool IsStepped() const { return bAnchored && bStepped; }
	int64 GetTimelineEpoch() const { return TEpoch; }
	int64 GetStepSeq() const { return StepSeq; }

	// ── U-18p: this machine's wall ticks and the offset to the station's (100 ns since 0001-01-01 UTC) ──
	/** pins this machine's tick at one real time (once, at start: UtcNow then, the monotonic FPlatformTime::Seconds after) */
	void SetTickBase(int64 Ticks, double Real) { TickBase = Ticks; TickReal = Real; }
	int64 LocalTicks(double RealNow) const { return TickBase + int64(FMath::RoundToDouble((RealNow - TickReal) * 1e7)); }
	/** ClockEcho (NTP: offset = ((recv − send) + (transmit − back)) / 2, RTT = (back − send) − (transmit − recv)). False = dropped (RTT > 200 ms) */
	bool OnEcho(int64 ClientSend, int64 StationReceive, int64 StationTransmit, int64 ClientBack);
	bool HasOffset() const { return bOffset; }
	double GetOffsetTicks() const { return Offset; }
	double GetLastRttMs() const { return LastRttMs; }
	int32 GetEchoes() const { return Echoes; }
	int32 GetEchoesDropped() const { return Dropped; }

	static constexpr double EaseSeconds = 0.25;        // the guide's correction blend
	static constexpr double EaseUpTo = 0.5;            // a larger re-anchor difference is a snap, not an ease
	static constexpr double MaxRttMs = 200.0;          // HROT TimeConfig
	static constexpr double SnapOffsetTicks = 1e7;     // 1 s
	static constexpr double SteerGain = 0.1;

private:
	double Formula(double RealNow) const;
	double Base = 0, RealBase = 0, Scale = 1;
	bool bValid = false;
	// the anchor
	bool bAnchored = false, bStepped = false;
	int64 AnchorWall = 0, TEpoch = 0, StepSeq = 0;
	double AnchorSim = 0, AScale = 1;
	double EaseD = 0, EaseStart = -1;
	// the wall
	int64 TickBase = 0;
	double TickReal = 0, Offset = 0, LastRttMs = -1;
	bool bOffset = false;
	int32 Echoes = 0, Dropped = 0;
};
