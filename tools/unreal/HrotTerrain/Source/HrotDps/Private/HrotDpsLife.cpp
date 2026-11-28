// CE-4651 (U-18h) — see HrotDpsLife.h
#include "HrotDpsLife.h"

namespace HrotDpsLife
{
	EFamily FamilyOfDisCategory(int32 C)
	{
		switch (C)
		{
		case 200: return EFamily::Cattle;
		case 201: return EFamily::Sheep;
		case 202: return EFamily::Horse;
		case 203: return EFamily::Dog;
		case 204: return EFamily::Cat;
		case 205: return EFamily::Bird;
		case 206: return EFamily::Poultry;
		case 207: return EFamily::Pig;
		case 208: return EFamily::Goat;
		case 209: return EFamily::Rabbit;
		default: return EFamily::Unknown;
		}
	}

	EFamily FamilyOfDis(const FString& Dis)
	{
		TArray<FString> Parts; Dis.ParseIntoArray(Parts, TEXT("."));
		return Parts.Num() > 3 ? FamilyOfDisCategory(FCString::Atoi(*Parts[3])) : EFamily::Unknown;
	}

	const TCHAR* FamilyName(EFamily F)
	{
		switch (F)
		{
		case EFamily::Cattle: return TEXT("Cattle");
		case EFamily::Sheep: return TEXT("Sheep");
		case EFamily::Horse: return TEXT("Horse");
		case EFamily::Dog: return TEXT("Dog");
		case EFamily::Cat: return TEXT("Cat");
		case EFamily::Bird: return TEXT("Bird");
		case EFamily::Poultry: return TEXT("Poultry");
		case EFamily::Pig: return TEXT("Pig");
		case EFamily::Goat: return TEXT("Goat");
		case EFamily::Rabbit: return TEXT("Rabbit");
		default: return TEXT("Unknown");
		}
	}

	FGaitBands BandsOf(EFamily F)
	{
		switch (F)   // AnimalMotion.cs QuadrupedGaits.BandsOf (the walk, trot, canter tops)
		{
		case EFamily::Horse: return { 2.2f, 4.8f, 7.8f };
		case EFamily::Cattle: return { 1.7f, 3.6f, 5.8f };
		case EFamily::Sheep: return { 1.4f, 3.0f, 5.0f };
		case EFamily::Goat: return { 1.5f, 3.2f, 5.2f };
		case EFamily::Pig: return { 1.5f, 3.2f, 5.0f };
		case EFamily::Dog: return { 1.8f, 3.8f, 6.0f };
		case EFamily::Cat: return { 1.3f, 3.0f, 5.0f };
		case EFamily::Rabbit: return { 0.8f, 3.0f, 6.0f };
		default: return { 1.5f, 3.5f, 5.5f };
		}
	}

	FAnimalOut AnimalShown(const FAnimalIn& In)
	{
		FAnimalOut O;
		const bool bFlyer = In.Family == EFamily::Bird || In.Family == EFamily::Poultry;
		if (In.bDead) { O.Shown = EShown::Dead; return O; }
		if (bFlyer && In.bAirborne) { O.Shown = EShown::Fly; return O; }
		if (In.Mode == ModeRest) { O.Shown = EShown::Rest; return O; }
		if (In.Mode == ModeGuard && In.bDangerous) { O.Shown = EShown::Attack; return O; }
		if (In.Speed < StandBelow) { O.Shown = EShown::Idle; return O; }
		const FGaitBands B = BandsOf(In.Family);
		// the walk clip covers walk and trot, the run clip canter and gallop: its rate follows the speed inside that range
		if (In.Speed <= B.Trot)
		{
			O.Shown = EShown::Walk;
			O.PlayRate = FMath::Clamp(In.Speed / FMath::Max(B.Walk * 0.75f, 0.1f), 0.5f, 2.f);
		}
		else
		{
			O.Shown = EShown::Run;
			O.PlayRate = FMath::Clamp(In.Speed / FMath::Max(B.Canter * 0.8f, 0.1f), 0.6f, 1.8f);
		}
		return O;
	}

	// ── FlockBirds, bit for bit (uint32 arithmetic wraps like C#'s unchecked uint) ──
	static uint32 H(uint32 Seed, int32 I, uint32 Salt)
	{
		uint32 X = Seed * 0x9E3779B1u ^ uint32(I) * 0x85EBCA6Bu ^ Salt * 0xC2B2AE35u;
		X ^= X >> 16; X *= 0x7feb352du; X ^= X >> 15; X *= 0x846ca68bu; X ^= X >> 16;
		return X;
	}
	static float U(uint32 Seed, int32 I, uint32 Salt) { return float(H(Seed, I, Salt) & 0xFFFFFF) / 16777216.f; }
	static float SinCycles(double Cycles) { return FMath::Sin(float(FMath::Fmod(Cycles, 1.0) * UE_DOUBLE_PI * 2.0)); }

	float FlockRadius(int32 Count) { return 2.5f + 0.5f * FMath::Sqrt(float(FMath::Max(Count, 1))); }

	FVector3f FlockOffset(uint32 Seed, int32 I, double T, uint8 Mode, int32 Count)
	{
		const float R = FlockRadius(Count);
		switch (Mode)
		{
		case FlockLanded:
		{
			const float Rad = R * 0.7f * FMath::Sqrt(U(Seed, I, 1)), Ang = U(Seed, I, 2) * UE_PI * 2.f;
			return FVector3f(Rad * FMath::Cos(Ang), Rad * FMath::Sin(Ang), 0.f);
		}
		case FlockPerched:
		{
			const float Rad = 1.6f * FMath::Sqrt(U(Seed, I, 1)), Ang = U(Seed, I, 2) * UE_PI * 2.f;
			return FVector3f(Rad * FMath::Cos(Ang), Rad * FMath::Sin(Ang), U(Seed, I, 3) * 1.5f);
		}
		default:
			return FVector3f((U(Seed, I, 1) - 0.5f) * R * 2.2f + SinCycles(T * 0.11 + U(Seed, I, 4)) * R * 0.25f,
			                 (U(Seed, I, 2) - 0.5f) * R * 2.f + SinCycles(T * 0.17 + U(Seed, I, 5)) * R * 0.2f,
			                 (U(Seed, I, 3) - 0.5f) * R * 0.5f + SinCycles(T * 0.23 + U(Seed, I, 6)) * 0.6f);
		}
	}

	float FlockYaw(uint32 Seed, int32 I, double T, uint8 Mode)
	{
		return Mode == FlockFlying ? 0.35f * SinCycles(T * 0.3 + U(Seed, I, 7)) : U(Seed, I, 8) * UE_PI * 2.f;
	}

	// ── TrackCurve ──
	FVector3d FKnot::Velocity() const
	{
		return FVector3d(Speed * FMath::Cos(Heading) * FMath::Cos(Pitch), Speed * FMath::Sin(Heading) * FMath::Cos(Pitch), Speed * FMath::Sin(Pitch));
	}

	double WrapPi(double A)
	{
		A = FMath::Fmod(A, 2.0 * UE_DOUBLE_PI);
		if (A > UE_DOUBLE_PI) A -= 2.0 * UE_DOUBLE_PI;
		else if (A <= -UE_DOUBLE_PI) A += 2.0 * UE_DOUBLE_PI;
		return A;
	}

	static FTrackPose PoseOf(const FKnot& K) { FTrackPose P; P.P = K.P; P.Heading = K.Heading; P.Pitch = K.Pitch; P.Speed = K.Speed; return P; }

	FTrackPose Between(const FKnot& A, const FKnot& B, double T)
	{
		const double Span = B.T - A.T;
		if (Span <= 1e-9) return PoseOf(B);
		const double Uu = FMath::Clamp((T - A.T) / Span, 0.0, 1.0), U2 = Uu * Uu, U3 = U2 * Uu;
		const double H00 = 2 * U3 - 3 * U2 + 1, H10 = U3 - 2 * U2 + Uu, H01 = -2 * U3 + 3 * U2, H11 = U3 - U2;
		FTrackPose P;
		P.P = H00 * A.P + H10 * Span * A.Velocity() + H01 * B.P + H11 * Span * B.Velocity();
		P.Heading = A.Heading + WrapPi(B.Heading - A.Heading) * Uu;
		P.Pitch = A.Pitch + (B.Pitch - A.Pitch) * Uu;
		P.Speed = A.Speed + (B.Speed - A.Speed) * Uu;
		return P;
	}

	FTrackPose Extrapolate(const FKnot& Last, double T)
	{
		FTrackPose P = PoseOf(Last);
		P.P += Last.Velocity() * (T - Last.T);
		return P;
	}

	bool PoseAt(const TArray<FKnot>& Knots, double T, FTrackPose& Out)
	{
		if (Knots.Num() == 0) return false;
		if (T <= Knots[0].T) { Out = PoseOf(Knots[0]); return true; }
		for (int32 I = 1; I < Knots.Num(); ++I)
			if (T <= Knots[I].T) { Out = Between(Knots[I - 1], Knots[I], T); return true; }
		Out = Extrapolate(Knots.Last(), T);
		return true;
	}

	void TrimBefore(TArray<FKnot>& Knots, double T)
	{
		int32 Drop = 0;
		while (Drop + 2 < Knots.Num() && Knots[Drop + 1].T < T - KeepBehind) ++Drop;
		if (Drop > 0) Knots.RemoveAt(0, Drop, EAllowShrinking::No);
	}

	// ── FlameLook ──
	float SpotSize(float Radius) { return FMath::Clamp(Radius * 0.7f, 0.3f, 8.f); }

	float SpotStrength(float Intensity, double Since, float Seconds, double Now)
	{
		if (Seconds <= 0) return Intensity;   // no burn time stated: burning while the spot is listed
		const double Phase = (Now - Since) / Seconds;
		if (Phase >= 1.0) return 0.f;
		const float Fade = Phase < 0.7 ? 1.f : FMath::Clamp(1.f - float(Phase - 0.7) / 0.3f, 0.f, 1.f);
		return Intensity * Fade;
	}

	float JetLength(float Distance, float RangeM) { return Distance > 0.5f ? FMath::Min(Distance, RangeM) : RangeM; }
}
