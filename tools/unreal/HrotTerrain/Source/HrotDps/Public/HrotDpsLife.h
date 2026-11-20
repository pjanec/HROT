// CE-4651 (U-18h) — ANIMALS, TRAFFIC and FLAME from DPS, as pure rules (unit-tested in Private/Tests, no assets, no world).
//
// Each rule is a PORT of the shared one the DPS client guide names (so Unreal and HROT's map agree), never a re-invention:
//   ① animals: the family from the DIS category (Hrot.Look VisualFamily, CE-4623: 3.1.*.200 cattle … 209 rabbit), the gait bands
//      (Hrot.Look AnimalMotion QuadrupedGaits.BandsOf), the drawn state from Animal.Mode + speed; a FLOCK's birds as closed-form
//      offsets of (seed, index, sim time, mode) — Hrot.Look FlockBirds.Offset / Yaw, bit for bit (a seek, a replay, a late joiner
//      and a second channel show the same birds).
//   ② traffic: the delayed track (SimNetGateway.Common TrackCurve, DD-018 §3.6c): a cubic Hermite between knots with their
//      velocities as tangents, the heading the short way, past the newest knot a straight extrapolation; played PlaybackDelay
//      (1.2 s) behind the newest batch (DpsClient MoverPlayback).
//   ③ flame: a burning spot's flame size (Hrot.Look FlameLook.SpotSize: clamp(radius × 0.7, 0.3, 8) m) and its fade over the last
//      30 % of the burn (FlameLook.SpotSmoke's fadeOut); a jet's length (FlameLook: min(distance to the aim, RangeM), RangeM when
//      the aim is under 0.5 m away).
#pragma once

#include "CoreMinimal.h"

namespace HrotDpsLife
{
	// ── ① animals ──────────────────────────────────────────────────────────────────────────────────────────────────────────────
	enum class EFamily : uint8 { Unknown, Cattle, Sheep, Horse, Dog, Cat, Bird, Poultry, Pig, Goat, Rabbit };
	/** the DIS category (the 4th field) → the family (VisualFamily.cs: 200 cattle, 201 sheep, 202 horse, 203 dog, 204 cat, 205 bird,
	 *  206 poultry, 207 pig, 208 goat, 209 rabbit) */
	HROTDPS_API EFamily FamilyOfDisCategory(int32 Category);
	HROTDPS_API EFamily FamilyOfDis(const FString& Dis);
	HROTDPS_API const TCHAR* FamilyName(EFamily F);

	/** QuadrupedGaits.BandsOf: the top speeds of walk, trot, canter (m/s); above canter it gallops */
	struct FGaitBands { float Walk, Trot, Canter; };
	HROTDPS_API FGaitBands BandsOf(EFamily F);
	/** an animal stands below this (AnimalMotion: 0.15 m/s) */
	constexpr float StandBelow = 0.15f;

	// the contract's AnimalMode / FlockMode (DpsTypes.idl order)
	enum : uint8 { ModeUnknown = 0, ModeCalm, ModeAlert, ModeFlee, ModeRecover, ModeRest, ModeGuard, ModeCrouch };
	enum : uint8 { FlockUnknown = 0, FlockFlying, FlockLanded, FlockPerched };

	/** what the body shows — EHrotAnimalState's values (Idle, Walk, Run, Rest, Attack, Fly, Dead) */
	enum class EShown : uint8 { Idle, Walk, Run, Rest, Attack, Fly, Dead };
	struct FAnimalIn
	{
		EFamily Family = EFamily::Unknown;
		uint8 Mode = ModeUnknown;
		bool bDangerous = false, bAirborne = false, bDead = false;
		float Speed = 0;   // m/s
	};
	struct FAnimalOut { EShown Shown = EShown::Idle; float PlayRate = 1.f; };
	/** dead → Dead · a flyer in the air → Fly · Rest → Rest · Guard and attacking → Attack · standing → Idle · within the trot band →
	 *  Walk · faster → Run; the clip's rate follows the speed inside its band (the pack's clips are in place: feet slide less) */
	HROTDPS_API FAnimalOut AnimalShown(const FAnimalIn& In);

	/** FlockBirds: the most birds drawn (HROT: 400; Unreal draws each as a skinned actor — capped lower, see MaxFlockDrawn) */
	constexpr int32 FlockMaxDrawn = 400;
	HROTDPS_API float FlockRadius(int32 Count);
	/** bird i's place (m) in the flock's frame (x forward, y left, z up) — FlockBirds.Offset */
	HROTDPS_API FVector3f FlockOffset(uint32 Seed, int32 I, double T, uint8 Mode, int32 Count);
	/** bird i's heading off the flock's (radians, counter-clockwise) — FlockBirds.Yaw */
	HROTDPS_API float FlockYaw(uint32 Seed, int32 I, double T, uint8 Mode);

	// ── ② traffic ──────────────────────────────────────────────────────────────────────────────────────────────────────────────
	/** DD-018 §3.6b: how far behind the newest batch movers are drawn, and how long the clock runs on past it */
	constexpr double PlaybackDelay = 1.2, MaxExtrapolation = 0.25, KeepBehind = 5.0;
	struct FKnot
	{
		double T = 0;                          // sim s
		FVector3d P = FVector3d::ZeroVector;   // m, ENU
		double Heading = 0, Pitch = 0;         // radians: 0 = +x (east), counter-clockwise; nose up +
		double Speed = 0;                      // m/s along the heading
		FVector3d Velocity() const;
	};
	struct FTrackPose { FVector3d P = FVector3d::ZeroVector; double Heading = 0, Pitch = 0, Speed = 0; };
	HROTDPS_API double WrapPi(double A);
	HROTDPS_API FTrackPose Between(const FKnot& A, const FKnot& B, double T);
	HROTDPS_API FTrackPose Extrapolate(const FKnot& Last, double T);
	/** the pose at T on a whole track (knots in time order): before the first the first, inside the curve, after the last straight on */
	HROTDPS_API bool PoseAt(const TArray<FKnot>& Knots, double T, FTrackPose& Out);
	/** drops knots older than KeepBehind before T (keeping two) */
	HROTDPS_API void TrimBefore(TArray<FKnot>& Knots, double T);

	// ── ③ flame ────────────────────────────────────────────────────────────────────────────────────────────────────────────────
	HROTDPS_API float SpotSize(float Radius);
	/** a spot's strength now: its intensity, faded over the last 30 % of its burn (Phase = (now − Since) / Seconds); 0 when burnt out */
	HROTDPS_API float SpotStrength(float Intensity, double Since, float Seconds, double Now);
	/** a jet's length (m) from the muzzle toward the aim */
	HROTDPS_API float JetLength(float DistanceToAim, float RangeM);
}
