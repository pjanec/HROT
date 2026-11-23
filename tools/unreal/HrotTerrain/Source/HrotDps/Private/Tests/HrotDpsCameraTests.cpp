// CE-4651 (U-18l) — rails for the camera rules (ports of HROT's UnitCameraRig, FollowCameraDriver.Step, MapCamera3D's fly speed).
// Run: UnrealEditor-Cmd <project> -ExecCmds="Automation RunTests Hrot.Dps.Camera; Quit" -nullrhi -unattended
#include "Misc/AutomationTest.h"
#include "HrotDpsCamera.h"

#if WITH_DEV_AUTOMATION_TESTS
using namespace HrotCamera;

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotDpsCameraRigTest, "Hrot.Dps.Camera.Rig",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotDpsCameraRigTest::RunTest(const FString&)
{
	// the continuum: orbit beyond 12 m, shoulder under 3 m (a person); a vehicle's ×4
	TestEqual(TEXT("far: orbit"), NearBlend(12.f, EScheme::Person), 0.f);
	TestEqual(TEXT("near: shoulder"), NearBlend(3.f, EScheme::Person), 1.f);
	TestEqual(TEXT("halfway: smoothstep 0.5"), NearBlend(7.5f, EScheme::Person), 0.5f, 1e-5f);
	TestEqual(TEXT("a vehicle at 12 m is still near"), NearBlend(12.f, EScheme::Vehicle), 1.f);
	TestEqual(TEXT("a vehicle orbits beyond 48 m"), NearBlend(48.f, EScheme::Vehicle), 0.f);

	// the pivot: a person looking east (+x) at 2 m is over his RIGHT shoulder (−y, ENU) and 0.2 m up, at his eye height
	const FVector3d Eye(0, 0, 1.6), Centre(0, 0, 0.9);
	const FVector3d P = Pivot(Centre, Eye, FVector3d(1, 0, 0), 2.f, EScheme::Person, +1);
	TestTrue(TEXT("shoulder pivot"), P.Equals(FVector3d(0, -0.55, 1.8), 1e-6));
	TestTrue(TEXT("far pivot = the centre"), Pivot(Centre, Eye, FVector3d(1, 0, 0), 20.f, EScheme::Person, +1).Equals(Centre, 1e-9));
	TestTrue(TEXT("a vehicle's near pivot rises 2.5 m at 9 m"), Pivot(Centre, Eye, FVector3d(1, 0, 0), 9.f, EScheme::Vehicle, +1).Equals(Eye + FVector3d(0, 0, 2.5), 1e-6));

	// the camera: playing goes back along the LEVEL look; watching orbits with the pitch
	const FVector3d Down = Forward(0, -0.5);
	TestTrue(TEXT("playing: level"), Camera(P, Down, 2.f, false).Equals(P - FVector3d(2, 0, 0), 1e-9));
	TestTrue(TEXT("watching: lifted when looking down"), Camera(P, Down, 2.f, true).Z > P.Z + 0.9);

	// the spring arm: 0.25 m in front of a hit at half the arm; nothing in the way = wanted
	const FVector3d A(0, 0, 0), W(-4, 0, 0);
	TestTrue(TEXT("in front of the wall"), SpringArm(A, W, 0.5, ArmMargin).Equals(FVector3d(-1.75, 0, 0), 1e-9));
	TestTrue(TEXT("no wall"), SpringArm(A, W, -1.0, ArmMargin).Equals(W, 1e-9));
	TestEqual(TEXT("arm in at once"), EaseArm(4.f, 1.f, 0.1f), 1.f);
	TestEqual(TEXT("arm out at 4 m/s"), EaseArm(1.f, 4.f, 0.1f), 1.4f, 1e-5f);
	TestEqual(TEXT("a vehicle's arm out ×4"), EaseArm(1.f, 10.f, 0.1f, 4.f), 2.6f, 1e-5f);
	// squeezed: lifted toward 0.6 m over the eyes, never lowered
	const FVector3d Low(-0.5, 0, 1.0);
	TestEqual(TEXT("fully squeezed: 0.6 m over his eyes"), AboveHeadWhenSqueezed(Low, Eye, 0.f, 2.f).Z, 2.2, 1e-5);   // SqueezedAboveEyes is a float
	TestEqual(TEXT("no squeeze: unchanged"), AboveHeadWhenSqueezed(Low, Eye, 2.f, 2.f).Z, 1.0, 1e-9);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotDpsCameraFollowTest, "Hrot.Dps.Camera.Follow",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotDpsCameraFollowTest::RunTest(const FString&)
{
	TestEqual(TEXT("in: ÷1.2"), Step(14.f, 1.f), 14.f / 1.2f, 1e-4f);
	TestEqual(TEXT("out: ×1.2"), Step(14.f, -1.f), 14.f * 1.2f, 1e-4f);
	TestEqual(TEXT("in below 0.8 m: his eyes"), Step(0.9f, 1.f), 0.f);
	TestEqual(TEXT("out of his eyes: the shoulder"), Step(0.f, -1.f), 1.5f);
	TestEqual(TEXT("in at his eyes: stays"), Step(0.f, 1.f), 0.f);
	TestEqual(TEXT("capped at 400 m"), Step(390.f, -1.f), 400.f);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotDpsCameraFlyTest, "Hrot.Dps.Camera.Fly",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotDpsCameraFlyTest::RunTest(const FString&)
{
	TestEqual(TEXT("on the ground: the minimum"), FlyBasisSpeed(0.f), 1.2f);
	TestEqual(TEXT("100 m up: 40 m/s"), FlyBasisSpeed(100.f), 40.f, 1e-4f);
	TestEqual(TEXT("3 m up: doubled near the ground"), FlyBasisSpeed(3.f), 2.4f, 1e-4f);
	TestEqual(TEXT("a press starts at 25 %"), FlySpeed(100.f, 0.f, false, false), 10.f, 1e-4f);
	TestEqual(TEXT("full after 1 s"), FlySpeed(100.f, 1.f, false, false), 40.f, 1e-4f);
	TestEqual(TEXT("×5 after 3 s"), FlySpeed(100.f, 3.f, false, false), 200.f, 1e-3f);
	TestEqual(TEXT("Shift ×4"), FlySpeed(100.f, 1.f, true, false), 160.f, 1e-3f);
	TestEqual(TEXT("Ctrl ×20"), FlySpeed(100.f, 1.f, false, true), 800.f, 1e-3f);
	TestEqual(TEXT("both ×100"), FlySpeed(100.f, 1.f, true, true), 4000.f, 1e-2f);
	TestEqual(TEXT("a lone wheel click: 0.1 s of flight"), WheelStep(100.f, 0), 4.f, 1e-4f);
	TestEqual(TEXT("a streak grows it, to ×8"), WheelStepGrowth(20), 8.f);
	return true;
}
#endif
