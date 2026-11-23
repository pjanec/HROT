// CE-4651 (U-18d) — see HrotDpsPeople.h
#include "HrotDpsPeople.h"

namespace HrotDpsPeople
{
	static const TCHAR* StanceName(uint8 S) { return S == Crouched ? TEXT("Crouch") : S == Prone ? TEXT("Prone") : TEXT("Stand"); }
	static const TCHAR* GaitName(EGait G) { return G == EGait::Run ? TEXT("Run") : G == EGait::Walk ? TEXT("Walk") : TEXT("Idle"); }

	EGait GaitOf(float Speed, EGait Last)
	{
		if (Speed < IdleBelow) return EGait::Idle;
		if (Last == EGait::Run) return Speed < WalkBelow ? EGait::Walk : EGait::Run;
		return Speed > RunAbove ? EGait::Run : EGait::Walk;
	}

	FLook Look(const FIn& In)
	{
		FLook L;
		// the stance being drawn: a stated transition runs from StanceFrom to TargetStance over StanceSeconds
		const double Into = In.Now - In.StanceChangedAt;
		L.bTransition = In.StanceChangedAt > 0 && In.StanceSeconds > 0 && In.StanceFrom != In.TargetStance && Into >= 0 && Into < In.StanceSeconds;
		L.Stance = (In.StanceChangedAt > 0 && Into >= 0) ? In.TargetStance : In.Stance;
		if (L.bTransition) { L.TransitionLeft = In.StanceSeconds - Into; L.BlendTime = FMath::Clamp(In.StanceSeconds, 0.2f, 1.5f); }

		if (In.bInSeat && !In.bSeatExposed) { L.bHidden = true; L.Base = TEXT("Rifle.Stand.Idle"); return L; }   // inside a vehicle

		// down: dead plays its death ONCE and holds (by the stance he fell from); incapacitated lies dying
		if (In.Injury == InjuryDead || In.Injury == InjuryIncapacitated)
		{
			L.bLoops = false;
			// U-19g: a man shot while prone / crouched lies down in HIS stance (the standing "Dying" would jump him up to fall); standing keeps "Dying"
			// (no clip of its own: Fallback -> Death.Stand); dead from the same stance is the same base, so no fall replays
			L.Base = (In.Injury == InjuryIncapacitated && L.Stance == Standing) ? FName(TEXT("Dying"))
			       : FName(*FString::Printf(TEXT("Death.%s"), StanceName(L.Stance)));
			return L;
		}

		const bool bArmed = In.Held == HeldWeapon;
		L.bWeapon = bArmed;
		// crouched → prone has its own clip, played once over the transition
		if (L.bTransition && L.Stance == Prone && In.StanceFrom != Prone)
		{
			L.Base = TEXT("Transition.CrouchToProne"); L.bLoops = false;
			return L;
		}
		L.Gait = GaitOf(In.Speed, In.LastGait);
		EGait G = L.Gait;
		if (L.Stance != Standing && G == EGait::Run) G = EGait::Walk;   // a crouched or prone figure has no run: its walk, faster
		L.Base = FName(*FString::Printf(TEXT("%s.%s.%s"), bArmed ? TEXT("Rifle") : TEXT("Unarmed"), StanceName(L.Stance), GaitName(G)));

		// a still, standing figure's own business: the phone, a wave, a throw
		if (L.Stance == Standing && G == EGait::Idle && !bArmed)
		{
			if (In.Held == HeldPhone) L.Base = TEXT("Phone");
			else if (In.Gesture == GestureWaving) L.Base = TEXT("Gesture.Wave");
		}
		if (In.Gesture == GestureThrowing && L.Stance == Standing) { L.Base = TEXT("Gesture.Throw"); L.bLoops = false; }

		// the weapon posture on the upper body (not prone: the prone clips hold the rifle themselves)
		if (bArmed && L.Stance != Prone)
		{
			if (In.Posture == Aimed) { L.Upper = TEXT("Upper.Aimed"); L.bCanAim = true; }
			else if (In.Posture == Ready) L.Upper = TEXT("Upper.Ready");
		}
		return L;
	}

	FName Fallback(FName Role)
	{
		FString S = Role.ToString();
		// U-19h: a death falls back Death.Crouch / Death.Prone -> Death.Stand -> Dying (the civilian kits have only Dying: no clip = the bare reference pose, arms out);
		// Dying -> Death.Stand (the soldier kit has no Dying). The two point at each other: the caller stops on a role it has already tried (FallbackChain)
		if (S.StartsWith(TEXT("Death.")) && S != TEXT("Death.Stand")) return TEXT("Death.Stand");
		if (S == TEXT("Death.Stand")) return TEXT("Dying");
		if (S == TEXT("Dying")) return TEXT("Death.Stand");
		if (S.EndsWith(TEXT(".Run"))) return FName(*(S.LeftChop(4) + TEXT(".Walk")));
		if (S.StartsWith(TEXT("Rifle."))) return FName(*(TEXT("Unarmed.") + S.RightChop(6)));
		if (S == TEXT("Phone") || S.StartsWith(TEXT("Gesture."))) return TEXT("Unarmed.Stand.Idle");
		if (S == TEXT("Transition.CrouchToProne")) return TEXT("Rifle.Prone.Idle");
		if (S.StartsWith(TEXT("Unarmed.")) && !S.EndsWith(TEXT(".Idle"))) return FName(*(S.Left(S.Find(TEXT("."), ESearchCase::CaseSensitive, ESearchDir::FromEnd)) + TEXT(".Idle")));
		if (S.StartsWith(TEXT("Unarmed.")) && S != TEXT("Unarmed.Stand.Idle")) return TEXT("Unarmed.Stand.Idle");
		return NAME_None;
	}

	bool Aim(const FVector3d& Feet, const FQuat4d& Body, uint8 Stance, const FVector3d& AimPoint, double& TwistDeg, double& PitchDeg)
	{
		TwistDeg = PitchDeg = 0;
		if (Stance == Prone) return false;
		const FVector3d Shoulder = Feet + FVector3d(0, 0, Stance == Crouched ? ShoulderCrouched : ShoulderStanding);
		const FVector3d L = Body.UnrotateVector(AimPoint - Shoulder);   // the body's frame: x forward, y left, z up
		const double Horizontal = FMath::Sqrt(L.X * L.X + L.Y * L.Y);
		if (Horizontal < MinAimDistance) return false;
		TwistDeg = FMath::Clamp(FMath::RadiansToDegrees(FMath::Atan2(L.Y, L.X)), -MaxTwistDeg, MaxTwistDeg);
		PitchDeg = FMath::Clamp(FMath::RadiansToDegrees(FMath::Atan2(L.Z, Horizontal)), -MaxPitchDeg, MaxPitchDeg);
		return true;
	}

	double Ease(double Current, double Target, double Dt, double Tau)
	{
		return Dt <= 0 ? Current : Current + (Target - Current) * (1.0 - FMath::Exp(-Dt / Tau));
	}
}
