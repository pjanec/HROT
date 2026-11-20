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

#endif
