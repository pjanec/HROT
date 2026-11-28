// CE-4651 (U-18b) — rails for HrotDpsMotion: the frame conversion, dead reckoning, the blend and the sim clock.
// Run: UnrealEditor-Cmd <project> -ExecCmds="Automation RunTests Hrot.Dps.Motion; Quit" -nullrhi -unattended
#include "Misc/AutomationTest.h"
#include "HrotDpsMotion.h"

#if WITH_DEV_AUTOMATION_TESTS

namespace
{
	FQuat4d Yaw(double Rad) { return FQuat4d(FVector3d::ZAxisVector, Rad); }   // ENU: counter-clockwise from east
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotDpsFrameTest, "Hrot.Dps.Motion.Frame",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotDpsFrameTest::RunTest(const FString&)
{
	// position: metres ENU -> cm, north = Unreal -Y (the terrain importer's +Y = south)
	TestEqual(TEXT("east"), HrotDpsMotion::ToUnreal(FVector3d(1, 0, 0)), FVector(100, 0, 0));
	TestEqual(TEXT("north"), HrotDpsMotion::ToUnreal(FVector3d(0, 1, 0)), FVector(0, -100, 0));
	TestEqual(TEXT("up"), HrotDpsMotion::ToUnreal(FVector3d(0, 0, 1)), FVector(0, 0, 100));
	TestEqual(TEXT("origin"), HrotDpsMotion::ToUnreal(FVector3d(1, 0, 0), FVector(5, 6, 7)), FVector(105, 6, 7));

	// orientation: the body's forward (+x) must point where the ENU rotation points it, converted the same way as a position
	const FQuat4d Cases[] = { Yaw(0), Yaw(UE_DOUBLE_HALF_PI), Yaw(-0.7), FQuat4d(FVector3d(0.3, -0.5, 0.8).GetSafeNormal(), 1.1) };
	for (const FQuat4d& Q : Cases)
	{
		const FVector3d FwdEnu = Q.RotateVector(FVector3d::XAxisVector), UpEnu = Q.RotateVector(FVector3d::ZAxisVector);
		const FQuat U = HrotDpsMotion::ToUnreal(Q);
		TestTrue(TEXT("forward maps like a position"), U.GetForwardVector().Equals(HrotDpsMotion::ToUnreal(FwdEnu) / 100.0, 1e-6));
		TestTrue(TEXT("up maps like a position"), U.GetUpVector().Equals(HrotDpsMotion::ToUnreal(UpEnu) / 100.0, 1e-6));
	}
	// facing north (yaw +90° ENU) is Unreal yaw -90°
	TestEqual(TEXT("north yaw"), HrotDpsMotion::ToUnreal(Yaw(UE_DOUBLE_HALF_PI)).Rotator().Yaw, -90.0, 1e-4);

	// bank: a right-hand roll about forward, top toward the right (for an eastbound body the right is south = Unreal +Y)
	const FQuat4d Banked = HrotDpsMotion::WithBank(Yaw(0), 0.3);
	const FVector Up = HrotDpsMotion::ToUnreal(Banked).GetUpVector();
	TestTrue(TEXT("bank tips the top to the right (south, +Y)"), Up.Y > 0.29 && Up.Y < 0.3);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotDpsReckonTest, "Hrot.Dps.Motion.DeadReckoning",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotDpsReckonTest::RunTest(const FString&)
{
	FHrotDpsTrackPoint K;
	K.T = 100; K.P = FVector3d(10, 20, 3); K.V = FVector3d(10, 0, 0); K.A = FVector3d(0, 2, 0); K.W = FVector3d(0, 0, 0.5);
	const FHrotDpsPose P = HrotDpsMotion::Extrapolate(K, 102);
	TestTrue(TEXT("p + v t + a t²/2"), P.P.Equals(FVector3d(30, 24, 3), 1e-9));
	TestEqual(TEXT("yaw advanced by w t"), P.Q.GetTwistAngle(FVector3d::ZAxisVector), 1.0, 1e-9);
	TestTrue(TEXT("never backwards"), HrotDpsMotion::Extrapolate(K, 90).P.Equals(K.P, 1e-12));
	TestTrue(TEXT("held past the limit"), HrotDpsMotion::Extrapolate(K, 1000).P.Equals(HrotDpsMotion::Extrapolate(K, 100 + HrotDpsMotion::MaxExtrapolation).P, 1e-9));

	// a correction: continuous at its start, gone at its end, half-way in between
	FHrotDpsPose A, B; A.P = FVector3d(0, 0, 0); B.P = FVector3d(4, 0, 0); B.Q = Yaw(1.0);
	const FHrotDpsCorrection C = HrotDpsMotion::Correction(A, B);
	TestTrue(TEXT("correction start is the old pose"), HrotDpsMotion::Corrected(B, C, 0).P.Equals(A.P, 1e-12));
	TestEqual(TEXT("correction start yaw"), HrotDpsMotion::Corrected(B, C, 0).Q.GetTwistAngle(FVector3d::ZAxisVector), 0.0, 1e-9);
	TestTrue(TEXT("correction half"), HrotDpsMotion::Corrected(B, C, 0.5).P.Equals(FVector3d(2, 0, 0), 1e-12));
	TestEqual(TEXT("correction half yaw"), HrotDpsMotion::Corrected(B, C, 0.5).Q.GetTwistAngle(FVector3d::ZAxisVector), 0.5, 1e-9);
	TestTrue(TEXT("correction end is the new curve"), HrotDpsMotion::Corrected(B, C, 2).P.Equals(B.P, 1e-12));

	// ⭐ the U-18b defect: a sample that agrees with the old curve must change NOTHING while the entity moves (12 m/s) — a blend from
	// a fixed start pose trailed it by up to 0.75 m
	FHrotDpsTrackPoint Old; Old.T = 0; Old.V = FVector3d(12, 0, 0);
	FHrotDpsTrackPoint New = Old; New.T = 0.5; New.P = FVector3d(6, 0, 0);
	const FHrotDpsCorrection None = HrotDpsMotion::Correction(HrotDpsMotion::Extrapolate(Old, 0.5), HrotDpsMotion::Extrapolate(New, 0.5));
	for (double T = 0.5; T <= 0.8; T += 0.05)
	{
		const FHrotDpsPose Shown = HrotDpsMotion::Corrected(HrotDpsMotion::Extrapolate(New, T), None, (T - 0.5) / HrotDpsMotion::BlendSeconds);
		TestTrue(*FString::Printf(TEXT("no lag at %.2f s"), T), Shown.P.Equals(FVector3d(12 * T, 0, 0), 1e-9));
	}
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotDpsClockTest, "Hrot.Dps.Motion.Clock",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotDpsClockTest::RunTest(const FString&)
{
	FHrotDpsClock C;
	TestFalse(TEXT("no time before a sample"), C.IsValid());
	C.OnSample(50, 1000);
	TestEqual(TEXT("runs at scale 1"), C.Now(1002), 52.0, 1e-9);
	C.OnSample(40, 1003);   // an old late-join sample
	TestEqual(TEXT("an old sample never pulls it back"), C.Now(1003), 53.0, 1e-9);
	C.OnSample(60, 1004);   // the producer is ahead
	TestEqual(TEXT("a newer sample catches up"), C.Now(1004), 60.0, 1e-9);
	C.OnRate(0, 1005);      // paused
	TestEqual(TEXT("paused holds"), C.Now(1010), 61.0, 1e-9);
	C.OnRate(2, 1010);
	TestEqual(TEXT("scale 2"), C.Now(1011), 63.0, 1e-9);
	return true;
}

// U-18p (DD-022 §3b/§3c) — the station's anchor: HROT's slave formula, steps, the ease, the epoch jump, the probe's offset
IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotDpsAnchorTest, "Hrot.Dps.Motion.Anchor",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotDpsAnchorTest::RunTest(const FString&)
{
	constexpr int64 Base = 638000000000000000ll;   // some UTC tick
	constexpr int64 S = 10000000;                  // ticks per second
	FHrotDpsClock C;
	C.SetTickBase(Base, 100.0);
	TestEqual(TEXT("local ticks follow the monotonic clock"), C.LocalTicks(102.5), Base + 25000000);
	C.OnSample(5, 100);
	// the station is 3 s ahead of this machine's tick (another process start): the echo says so
	const int64 Off = 3 * S;
	TestTrue(TEXT("echo taken"), C.OnEcho(Base, Base + Off + 20000, Base + Off + 30000, Base + 50000));   // 5 ms there and back, 1 ms inside
	TestEqual(TEXT("offset = 3 s (+ the symmetric half)"), C.GetOffsetTicks(), double(Off), 1.0);
	TestEqual(TEXT("rtt 4 ms"), C.GetLastRttMs(), 4.0, 1e-9);
	TestFalse(TEXT("a 250 ms round trip is dropped"), C.OnEcho(Base, Base + Off, Base + Off, Base + 2500000));
	TestEqual(TEXT("dropped counted"), C.GetEchoesDropped(), 1);

	// Continuous, scale 2, anchored at the station's tick for real 101 s: sim = 40 + (stationNow - anchor) * 2
	const int64 AnchorWall = Base + S + Off;
	TestTrue(TEXT("the first anchor is a jump"), C.OnAnchor(false, AnchorWall, 40.0, 2.f, 1, 0, 101.0));
	TestEqual(TEXT("formula at the anchor"), C.Now(101.0), 40.0, 1e-6);
	TestEqual(TEXT("formula 1.5 s later"), C.Now(102.5), 43.0, 1e-6);
	C.OnSample(500, 102.5);
	TestEqual(TEXT("samples no longer move it"), C.Now(102.5), 43.0, 1e-6);
	TestEqual(TEXT("scale"), C.GetScale(), 2.0, 1e-9);

	// a re-anchor in the same epoch 0.1 s off: eased out over 0.25 s, never a jump
	TestFalse(TEXT("same epoch: not a jump"), C.OnAnchor(false, Base + 2 * S + Off, 41.9, 2.f, 1, 0, 103.0));   // 41.9 at real 102 s, where the old said 42.0
	TestEqual(TEXT("ease starts where it was"), C.Now(103.0), 44.0, 1e-6);   // the old curve at 103 s (the new one says 43.9)
	TestEqual(TEXT("ease is done after 0.25 s"), C.Now(103.25), 41.9 + 1.25 * 2, 1e-6);

	// pause = Stepped: frozen at once at AnchorSimTime (D-A2); a step presents its target exactly
	C.OnAnchor(true, Base + 4 * S + Off, 50.0, 2.f, 1, 7, 105.0);
	TestEqual(TEXT("frozen"), C.Now(105.0), 50.0, 1e-12);
	TestEqual(TEXT("still frozen"), C.Now(109.0), 50.0, 1e-12);
	TestEqual(TEXT("stepped: scale 0"), C.GetScale(), 0.0, 1e-12);
	TestFalse(TEXT("a step of the epoch"), C.OnStep(8, 50.0 + 1.0 / 60, 1, 109.5));
	TestEqual(TEXT("the step's target, exactly"), C.Now(120.0), 50.0 + 1.0 / 60, 1e-12);
	TestEqual(TEXT("step seq"), C.GetStepSeq(), int64(8));

	// a seek: a new TimelineEpoch jumps (no ease), backwards too
	TestTrue(TEXT("new epoch is a jump"), C.OnAnchor(false, Base + 21 * S + Off, 10.0, 1.f, 2, 0, 121.0));   // the station's tick at real 121 s
	TestEqual(TEXT("jumped, no ease"), C.Now(121.0), 10.0, 1e-6);
	TestEqual(TEXT("then runs"), C.Now(122.0), 11.0, 1e-6);
	C.Reset();   // a new world: the anchor stays
	TestEqual(TEXT("Reset keeps the anchor"), C.Now(122.0), 11.0, 1e-6);

	// the offset steers 10 % for a small change, snaps for one over 1 s
	const double O0 = C.GetOffsetTicks();
	C.OnEcho(Base, Base + Off + 1000000 + 20000, Base + Off + 1000000 + 30000, Base + 50000);   // +100 ms
	TestEqual(TEXT("steered 10 %"), C.GetOffsetTicks(), O0 + 100000.0, 1.0);
	C.OnEcho(Base, Base + Off + 30000000, Base + Off + 30000000, Base);
	TestEqual(TEXT("> 1 s: snapped"), C.GetOffsetTicks(), double(Off + 30000000), 1.0);
	return true;
}

// U-18p — the clock equals HROT's SlaveSyncController formula over a seeded random run of play / pause / step / scale / seek (DD-022 §7)
IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotDpsAnchorPropertyTest, "Hrot.Dps.Motion.AnchorProperty",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotDpsAnchorPropertyTest::RunTest(const FString&)
{
	FRandomStream R(4242);
	constexpr int64 Base = 638000000000000000ll;
	FHrotDpsClock C; C.SetTickBase(Base, 0.0);
	// no offset: the station's tick is this machine's (the probe's own rails above)
	double Real = 0, Sim = 0, Scale = 1; bool bStepped = false; int64 Epoch = 1, Seq = 0;
	int32 Jumps = 0, EpochChanges = 0, Fails = 0;
	auto Truth = [&](double At) { return bStepped ? Sim : Sim + (At - Real) * Scale; };   // the slave formula, anchored at (Real, Sim)
	for (int32 I = 0; I < 400; ++I)
	{
		const double Next = Real + R.FRandRange(0.01, 0.7);
		for (double T = Real; T < Next; T += 0.05)   // between events: the clock is the formula (eases excluded: none here, see below)
			if (FMath::Abs(C.Now(T) - Truth(T)) > 1e-6 && C.IsAnchored()) ++Fails;
		const double Anchor = Truth(Next);
		Real = Next; Sim = Anchor;
		const int32 Op = R.RandRange(0, 4);
		if (Op == 0) bStepped = true;                                       // pause
		else if (Op == 1) bStepped = false;                                 // play
		else if (Op == 2 && bStepped) { Sim += 1.0 / 60; ++Seq; if (C.OnStep(Seq, Sim, Epoch, Real)) ++Jumps; continue; }
		else if (Op == 3) Scale = R.FRandRange(0.1, 4.0);                   // a scale change: the gateway re-anchors
		else { Epoch++; EpochChanges++; Sim = R.FRandRange(0, 600); }       // a seek
		Scale = double(float(Scale));   // the wire's float
		if (C.OnAnchor(bStepped, C.LocalTicks(Real), Sim, float(Scale), Epoch, Seq, Real)) ++Jumps;
	}
	TestEqual(TEXT("the formula everywhere (re-anchors are continuous, so nothing eased)"), Fails, 0);
	TestEqual(TEXT("one jump per epoch change, plus the first anchor"), Jumps, EpochChanges + 1);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotDpsNeedsPlaceTest, "Hrot.Dps.Motion.NeedsPlace",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotDpsNeedsPlaceTest::RunTest(const FString&)
{
	// U-19h ⑱ B: a body standing still is not re-placed; any visible move or turn is
	const FTransform A(FQuat(FVector::UpVector, 0.5), FVector(1000, 2000, 30));
	TestFalse(TEXT("the same transform"), HrotDpsMotion::NeedsPlace(A, A));
	TestFalse(TEXT("0.05 cm off"), HrotDpsMotion::NeedsPlace(A, FTransform(A.GetRotation(), A.GetLocation() + FVector(0.05, 0, 0))));
	TestTrue(TEXT("1 cm off"), HrotDpsMotion::NeedsPlace(A, FTransform(A.GetRotation(), A.GetLocation() + FVector(0, 1, 0))));
	TestFalse(TEXT("turned 0.01 deg"), HrotDpsMotion::NeedsPlace(A, FTransform(FQuat(FVector::UpVector, 0.5 + FMath::DegreesToRadians(0.01)), A.GetLocation())));
	TestTrue(TEXT("turned 1 deg"), HrotDpsMotion::NeedsPlace(A, FTransform(FQuat(FVector::UpVector, 0.5 + FMath::DegreesToRadians(1.0)), A.GetLocation())));
	TestTrue(TEXT("a new scale"), HrotDpsMotion::NeedsPlace(A, FTransform(A.GetRotation(), A.GetLocation(), FVector(2))));
	return true;
}

#endif
