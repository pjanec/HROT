// CE-4651 (U-18d) — PEOPLE from DPS state, as pure rules (unit-tested in Private/Tests, no assets, no world).
//
// ① Look(): PersonState (+ the speed from EntityState) → which clip ROLE plays on the body and the upper body, looped or once, the
//    crossfade, the play rate — the roles are UHrotPersonKit::Clips' keys. ② Aim(): HROT's aim rule (the DPS client guide, "People
//    aiming", = Hrot.Look UpperBody.TryAim): from the shoulder to the aim point in the body's frame, twist ±75°, pitch ±35°, none under
//    0.1 m, none prone or dead. ③ Ease(): the 0.12 s time constant on SIM time.
//
// The contract's enums arrive as their numbers (the generated C enums are private to the module); the values are the IDL's order.
#pragma once

#include "CoreMinimal.h"

namespace HrotDpsPeople
{
	// the contract's enum values (DpsTypes.idl)
	enum : uint8 { Standing = 0, Crouched = 1, Prone = 2 };                                    // Stance
	enum : uint8 { AtEase = 0, Ready = 1, Aimed = 2 };                                         // WeaponPosture
	enum : uint8 { HeldNone = 0, HeldWeapon = 1, HeldThrowable = 2, HeldPhone = 3, HeldBinoculars = 4 };   // HeldItem
	enum : uint8 { InjuryOk = 0, InjuryWounded = 1, InjuryIncapacitated = 2, InjuryDead = 3 }; // Injury
	enum : uint8 { GestureWaving = 4, GestureThrowing = 5 };                                   // Gesture (the ones drawn so far)

	/** HROT's gait thresholds (LocomotionBlend: idle ≤ 0.1, walk 1.5, run ≥ 4.0 m/s) as discrete clips: the switch points, with
	 *  hysteresis so a speed on the line does not flicker */
	constexpr float IdleBelow = 0.2f, RunAbove = 2.9f, WalkBelow = 2.6f;
	/** the aim limits and timing (Hrot.Look UpperBody / PostureBlend, HumanGait.MaxAimTwist) */
	constexpr double MaxTwistDeg = 75.0, MaxPitchDeg = 35.0, AimTau = 0.12, MinAimDistance = 0.1;
	/** the shoulder above the feet when aimed: 1.31/1.8 of the 1.8 m body standing, 0.98/1.8 crouched (metres) */
	constexpr double ShoulderStanding = 1.31, ShoulderCrouched = 0.98;

	/** U-19h 20: a gait clip's play rate so the feet do not slide: the person's ground speed over the clip's own (the kit's WalkSpeed / RunSpeed / ...,
	 *  measured from the clips - scripts/unreal/hrot_kit_speeds.py). The floor is low (a slow walker's legs move slowly; the old floor 0.6 made a 0.5 m/s walker's
	 *  feet run at 1.2x the ground - sliding), the ceiling covers a sprint. */
	constexpr float MinGaitRate = 0.15f, MaxGaitRate = 2.5f;
	inline float GaitRate(float GroundSpeed, float ClipSpeed) { return FMath::Clamp(GroundSpeed / FMath::Max(ClipSpeed, 0.1f), MinGaitRate, MaxGaitRate); }

	enum class EGait : uint8 { Idle, Walk, Run };

	struct FIn
	{
		uint8 Stance = Standing, TargetStance = Standing, StanceFrom = Standing;
		double StanceChangedAt = 0;   // sim s; 0 = no transition stated
		float StanceSeconds = 0;
		uint8 Posture = AtEase, Held = HeldNone, Injury = InjuryOk, Gesture = 0;
		bool bInSeat = false, bSeatExposed = false;
		double Now = 0;               // sim s
		float Speed = 0;              // m/s over the ground
		EGait LastGait = EGait::Idle; // the gait shown last frame (hysteresis)
	};

	struct FLook
	{
		FName Base;              // the body clip's role
		bool bLoops = true;      // false: plays once and holds its last frame (a death, a transition)
		FName Upper;             // the upper-body clip's role (None = none)
		bool bWeapon = false;    // the rifle in the hand
		bool bHidden = false;    // inside a vehicle
		bool bCanAim = false;    // the upper body may twist to the aim
		float BlendTime = 0.25f; // the crossfade into this look
		EGait Gait = EGait::Idle;
		uint8 Stance = Standing; // the stance drawn (the transition's target once it has begun)
		bool bTransition = false;// inside a stated stance transition
		double TransitionLeft = 0; // its seconds still to run
	};

	HROTDPS_API EGait GaitOf(float Speed, EGait Last);
	/** the look for this frame */
	HROTDPS_API FLook Look(const FIn& In);
	/** `Role` with a fallback when the kit has no clip for it: Rifle.* → Unarmed.*, *.Run → *.Walk, a death → Death.Stand */
	HROTDPS_API FName Fallback(FName Role);
	/** the first role of Role's fallback chain (Role itself first) for which Has(role) is true, or NAME_None; never loops (a role already tried ends the chain) */
	template <typename THas> FName ResolveRole(FName Role, THas Has)
	{
		TArray<FName, TInlineAllocator<8>> Tried;
		for (; !Role.IsNone() && !Tried.Contains(Role); Role = Fallback(Role)) { if (Has(Role)) return Role; Tried.Add(Role); }
		return NAME_None;
	}

	/** the twist (deg, + = to the body's LEFT, the contract's sense) and pitch (deg, + = up) from `Feet` (m, ENU) with orientation `Body`
	 *  to `AimPoint`; false = no aim (under 0.1 m horizontally). Clamped to the limits. */
	HROTDPS_API bool Aim(const FVector3d& Feet, const FQuat4d& Body, uint8 Stance, const FVector3d& AimPoint, double& TwistDeg, double& PitchDeg);
	/** `Current` eased toward `Target` over `Dt` seconds with time constant `Tau` */
	HROTDPS_API double Ease(double Current, double Target, double Dt, double Tau = AimTau);
}
