// CE-4651 (U-18l) — CAMERA RULES, ported 1:1 from HROT (the DPS client guide, "Camera controllers to build"; unit-tested in
// Private/Tests). Pure: no world, no assets. Frame and units are HROT's — metres, ENU (x east, y north, z up), yaw counter-clockwise
// from +x — so the numbers and the rails compare with HROT's own; the camera actor converts to Unreal.
//   ① the UNIT RIG — UnitCameraRig.cs (CE-1293, DESIGN_Unit_Camera.md §4): one camera on a distance continuum — far it orbits the
//      unit's centre, under 3 m it is over his shoulder, at 0 his eyes; a spring arm in front of walls (UC-4), lifted over his head
//      when squeezed (CE-1338); a vehicle's distances ×4, a flyer's ×8.
//   ② the FOLLOW wheel — FollowCameraDriver.Step: ×1.2 a notch, his eyes below 0.4 m, out of the eyes to 1.5 m.
//   ③ FREE FLIGHT — MapCamera3D (CE-1142 / CE-1340 / CE-4408): the speed from the eye's height above the ground, ramped while held,
//      Shift ×4 / Ctrl ×20 / both ×100; a wheel click steps along the view, growing in a fast streak.
#pragma once

#include "CoreMinimal.h"

namespace HrotCamera
{
	enum class EScheme : uint8 { Person, Vehicle, Flyer };

	// ── ① UnitCameraRig ──
	constexpr float Far = 12.f, Near = 3.f;
	constexpr float Side = 0.55f, Lift = 0.2f, SideFullAt = 1.5f;
	constexpr float PersonDefault = 2.2f, VehicleDefault = 9.f, FlyerDefault = 18.f;
	constexpr float VehicleLiftPerMetre = 2.5f / 9.f, FlyerLiftPerMetre = 4.5f / 18.f;
	constexpr float ArmMargin = 0.25f, ArmOutRate = 4.f, SqueezedAboveEyes = 0.6f;

	/** U-19g: ENTERING his eyes the camera keeps the look it had: the offsets (from his heading) that make the eyes' gaze = the orbit's Yaw / Pitch (no jump) */
	inline void EyesEntry(double Yaw, double Pitch, double BodyYaw, double BodyPitch, double& OffYaw, double& OffPitch)
	{
		OffYaw = FMath::UnwindRadians(Yaw - BodyYaw); OffPitch = FMath::Clamp(Pitch - BodyPitch, -1.2, 1.2);
	}
	/** the eyes' gaze: his heading + the held offsets (and, LEAVING the eyes, the orbit's Yaw / Pitch to continue from) */
	inline void EyesLook(double BodyYaw, double BodyPitch, double OffYaw, double OffPitch, double& Yaw, double& Pitch)
	{
		Yaw = BodyYaw + OffYaw; Pitch = FMath::Clamp(BodyPitch + OffPitch, -1.4, 1.4);
	}
	HROTDPS_API float ScaleOf(EScheme S);
	HROTDPS_API float DefaultDistance(EScheme S);
	HROTDPS_API float MaxPlayDistance(EScheme S);
	HROTDPS_API float MaxAhead(EScheme S);
	/** 0 far away (orbit the centre) … 1 near (over the shoulder), smoothstep between Far and Near (× the scale) */
	HROTDPS_API float NearBlend(float Distance, EScheme S);
	/** the point the camera hangs from: near, his eye moved to the Side shoulder (+1 right, −1 left) and up (a person) or up (a chase);
	 *  far, his centre; blended. Heading = the level look (its right is the shoulder's side) */
	HROTDPS_API FVector3d Pivot(const FVector3d& Centre, const FVector3d& Eye, const FVector3d& Heading, float Distance, EScheme S, int32 SideSign);
	/** where the camera stands: Distance behind the pivot along the look — the LEVEL look while playing (looking down does not swing it
	 *  up), the full look while watching (it orbits) */
	HROTDPS_API FVector3d Camera(const FVector3d& Pivot, const FVector3d& Look, float Distance, bool bOrbitPitch);
	/** the spring arm: in front of the first hit by ArmMargin (HitFraction along pivot → wanted, < 0 = nothing in the way) */
	HROTDPS_API FVector3d SpringArm(const FVector3d& Pivot, const FVector3d& Wanted, double HitFraction, float Margin = ArmMargin);
	/** the arm this frame: shorter at once, longer by at most ArmOutRate × scale × dt */
	HROTDPS_API float EaseArm(float Current, float Wanted, float Dt, float Scale = 1.f);
	/** a squeezed arm lifts the camera toward SqueezedAboveEyes over his eyes (never lowers it) */
	HROTDPS_API FVector3d AboveHeadWhenSqueezed(const FVector3d& At, const FVector3d& Eye, float Arm, float FullArm);
	/** a look direction from yaw (CCW from +x) and pitch (up +) */
	HROTDPS_API FVector3d Forward(double Yaw, double Pitch);

	// ── ② FollowCameraDriver ──
	constexpr float FollowStart = 14.f, FollowMax = 400.f, EyesBelow = 0.4f, WheelFactor = 1.2f, LookSpringRate = 4.f, OrbitPerPixel = 0.005f;
	/** the distance after a wheel move: in (+) closer, his eyes below EyesBelow; out of his eyes to the shoulder (SideFullAt) */
	HROTDPS_API float Step(float Distance, float Wheel);

	// ── ③ MapCamera3D free flight ──
	constexpr float FlySpeedPerHeight = 0.4f, FlyMinSpeed = 1.2f, FlyGroundBoost = 2.f, FlyGroundBoostFrom = 3.f, FlyGroundBoostUntil = 30.f;
	constexpr float FlyStartShare = 0.25f, FlyRampSeconds = 1.f, FlyMaxAcceleration = 5.f, FlyAccelerateSeconds = 3.f;
	constexpr float FlyShiftBoost = 4.f, FlyCtrlBoost = 20.f, FlyBothBoost = 100.f, FlyEaseSeconds = 0.08f, LookSpeed = 0.004f;
	constexpr float WheelStepSeconds = 0.1f, WheelStreakSeconds = 0.35f, WheelStreakGrowth = 0.6f, WheelStreakMax = 8.f, WheelGlideSeconds = 0.12f;
	constexpr float MinPitch = -1.5690f, MaxPitch = 1.2f;
	HROTDPS_API float FlyBasisSpeed(float HeightAboveGround);
	HROTDPS_API float FlySpeed(float HeightAboveGround, float HeldSeconds, bool bShift, bool bCtrl);
	HROTDPS_API float WheelStepGrowth(int32 Streak);
	HROTDPS_API float WheelStep(float HeightAboveGround, int32 Streak);
}

// CE-4651 (U-18m) — THE PLAYER (possessed) rules, ported 1:1 from HROT's FpsCameraDriver (PossessionStation.cs), FpsZoom, HitFeedback and
// the station HUD (the DPS client guide, "What the station sends back" / "The player's HUD"). Same frame as above (m, ENU, yaw CCW).
namespace HrotPlayer
{
	using HrotCamera::EScheme;
	/** mouse look (rad per pixel), the zoom field of view and the look's turn per pixel scaled by it (FpsZoom.SensitivityFor) */
	constexpr float LookSensitivity = 0.0025f, NormalFov = 45.f, IronSightsFov = 30.f, BinocularsFov = 10.f;
	HROTDPS_API float SensitivityFor(float FovDegrees);
	/** a person over the shoulder: the body lags the look — standing it is dragged past ShoulderLag, moving it turns at BodyTurnRate */
	constexpr float ShoulderLagDeg = 45.f, BodyTurnRateDeg = 540.f, FreeLookTurnRateDeg = 120.f;
	HROTDPS_API double Wrap(double A);
	HROTDPS_API double TurnToward(double From, double To, double Step);
	HROTDPS_API double DragBody(double Body, double Look, double Neck);
	/** the body yaw this frame (a person over the shoulder): moving → toward the look at BodyTurnRate, else dragged past ShoulderLag */
	HROTDPS_API double ShoulderBodyYaw(double BodyYaw, double LookYaw, bool bMoving, float Dt);
	/** W/A/S/D relative to the CAMERA turned into the BODY's frame (forward, side = +left): Off = look − body */
	HROTDPS_API void CameraRelative(float& Forward, float& Side, double Off);
	/** a flyer's heading: A/D lead it off the hull by FlyerTurnLead */
	constexpr float FlyerTurnLead = 0.8f;
	HROTDPS_API double FlyerHeading(double HullYaw, float Side);
	/** a vehicle driver's look left alone eases back behind the hull */
	constexpr float RecentreAfter = 1.5f, RecentreRateDeg = 90.f;
	/** the camera distance continuum while playing (the wheel): 0.5 m × scale a notch, from MaxAhead in front of the eyes (< 0) to
	 *  MaxPlayDistance; returns the new distance (< 0 ahead of the eyes, 0 the eyes, > 0 over the shoulder) — WheelDistance */
	constexpr float DistanceStep = 0.5f;
	HROTDPS_API float WheelDistance(float Now, float Wheel, EScheme S);
	/** eye heights over the position when there is no drawn eye (m) */
	constexpr float VehicleEyeHeight = 2.5f, RotorcraftEyeHeight = 0.2f, FixedWingEyeHeight = 1.5f, PersonEyeHeight = 1.6f;
	constexpr float CameraAboveGround = 0.4f, AimReach = 1000.f;
	/** the hit marker's and the damage arc's lives (s), the arc's width (deg) and radius (share of the smaller screen side) */
	constexpr float MarkerSeconds = 0.3f, ArcSeconds = 2.f, ArcHalfWidthDeg = 22.f, ArcRadiusShare = 0.38f;
	/** HitFeedback.ScreenAngle: the screen angle (deg; −90 = up, clockwise) of a source seen from a view looking along Forward; false when level-degenerate */
	HROTDPS_API bool ScreenAngle(const FVector3d& Forward, const FVector3d& ToSource, float& OutDeg);
	/** the arc's thickness (px) from the damage (6 … 18) */
	HROTDPS_API float ArcThickness(float Damage);
	/** a spread cone's half-width (rad) as a circle's radius (px) on a screen Height px high seen through FovDegrees (vertical) */
	HROTDPS_API float SpreadRadiusPx(float SpreadRad, float FovDegrees, float Height);
}
