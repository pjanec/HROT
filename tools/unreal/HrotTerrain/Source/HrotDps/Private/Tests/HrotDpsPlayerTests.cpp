// CE-4651 (U-18m) — rails for the player rules (ports of HROT's FpsCameraDriver, FpsZoom, HitFeedback).
// Run: UnrealEditor-Cmd <project> -ExecCmds="Automation RunTests Hrot.Dps.Player; Quit" -nullrhi -unattended
#include "Misc/AutomationTest.h"
#include "HrotDpsCamera.h"
#include "HrotDpsUseList.h"

#if WITH_DEV_AUTOMATION_TESTS
using namespace HrotPlayer;
using HrotCamera::EScheme;

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotDpsPlayerBodyTest, "Hrot.Dps.Player.Body",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotDpsPlayerBodyTest::RunTest(const FString&)
{
	const double D = UE_DOUBLE_PI / 180.0;
	TestEqual(TEXT("zoomed looks slower"), SensitivityFor(30.f), 0.0025f * 30.f / 45.f, 1e-7f);
	TestEqual(TEXT("wrap"), Wrap(3.5 * UE_DOUBLE_PI), -0.5 * UE_DOUBLE_PI, 1e-9);
	// standing over the shoulder: the body stays until the look is 45° off, then is dragged
	TestEqual(TEXT("within the lag: stays"), ShoulderBodyYaw(0.0, 30 * D, false, 0.016f), 0.0, 1e-6);
	TestEqual(TEXT("past the lag: dragged to 45° behind"), ShoulderBodyYaw(0.0, 90 * D, false, 0.016f), 45 * D, 1e-6);
	// moving: turns toward the look at 540°/s
	TestEqual(TEXT("moving: 540°/s"), ShoulderBodyYaw(0.0, 90 * D, true, 0.1f), 54 * D, 1e-6);
	TestEqual(TEXT("moving: reaches it"), ShoulderBodyYaw(0.0, 10 * D, true, 0.1f), 10 * D, 1e-6);
	// W with the camera 90° left of the body = the body's LEFT (side +1, ENU +left)
	float F = 1.f, S = 0.f; CameraRelative(F, S, 90 * D);
	TestEqual(TEXT("camera-relative W: no forward"), F, 0.f, 1e-6f);
	TestEqual(TEXT("camera-relative W: left"), S, 1.f, 1e-6f);
	TestEqual(TEXT("a flyer's heading led by A"), FlyerHeading(1.0, 1.f), 1.8, 1e-6);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotDpsPlayerDistanceTest, "Hrot.Dps.Player.Distance",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotDpsPlayerDistanceTest::RunTest(const FString&)
{
	TestEqual(TEXT("a person: 2.2 m in one notch → 1.7 m"), WheelDistance(2.2f, 1.f, EScheme::Person), 1.7f, 1e-5f);
	TestEqual(TEXT("in to the eyes"), WheelDistance(0.5f, 1.f, EScheme::Person), 0.f);
	TestEqual(TEXT("past the eyes: ahead (< 0)"), WheelDistance(0.f, 1.f, EScheme::Person), -0.5f, 1e-6f);
	TestEqual(TEXT("ahead is capped at 0.75 m"), WheelDistance(-0.5f, 2.f, EScheme::Person), -0.75f, 1e-6f);
	TestEqual(TEXT("out of ahead: the eyes first"), WheelDistance(-0.25f, -1.f, EScheme::Person), 0.f);
	TestEqual(TEXT("a person's play maximum 5 m"), WheelDistance(4.8f, -1.f, EScheme::Person), 5.f, 1e-6f);
	TestEqual(TEXT("a vehicle steps 2 m"), WheelDistance(9.f, -1.f, EScheme::Vehicle), 11.f, 1e-5f);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotDpsPlayerHudTest, "Hrot.Dps.Player.Hud",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotDpsPlayerHudTest::RunTest(const FString&)
{
	float A = 0;
	TestTrue(TEXT("ahead"), ScreenAngle(FVector3d(1, 0, 0), FVector3d(1, 0, 0), A)); TestEqual(TEXT("ahead = up (−90)"), A, -90.f, 1e-4f);
	ScreenAngle(FVector3d(1, 0, 0), FVector3d(0, -1, 0), A); TestEqual(TEXT("to the right (ENU −y) = 0"), A, 0.f, 1e-4f);
	ScreenAngle(FVector3d(1, 0, 0), FVector3d(-1, 0, 0), A);   // ±180 off forward: -270 or 90, both straight DOWN on the screen
	TestEqual(TEXT("behind = down: x"), FMath::Cos(FMath::DegreesToRadians(A)), 0.f, 1e-4f);
	TestEqual(TEXT("behind = down: y"), FMath::Sin(FMath::DegreesToRadians(A)), 1.f, 1e-4f);
	TestFalse(TEXT("straight down: no angle"), ScreenAngle(FVector3d(0, 0, -1), FVector3d(1, 0, 0), A));
	TestEqual(TEXT("arc without damage"), ArcThickness(0.f), 6.f);
	TestEqual(TEXT("arc thickest 18"), ArcThickness(500.f), 18.f);
	TestEqual(TEXT("spread at half the fov = half the screen"), SpreadRadiusPx(FMath::DegreesToRadians(22.5f), 45.f, 900.f), 450.f, 0.01f);
	return true;
}
IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotDpsUseListTest, "Hrot.Dps.Player.UseList",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotDpsUseListTest::RunTest(const FString&)
{
	namespace UL = HrotDpsUseList;
	TArray<UL::FRow> Rows = { { 9, true }, { 11, false }, { 12, true }, { 13, true } };   // verb 11 is refused
	UL::FState S; double T = 10.0;
	// a tap: nothing on the press, one primary press (Verb None) on the release
	TestFalse(TEXT("press: nothing yet"), UL::Step(S, true, T, Rows, 0, -1, false).bPress);
	UL::FOut O = UL::Step(S, false, T + 0.1, Rows, 0, -1, false);
	TestTrue(TEXT("tap releases one press"), O.bPress); TestEqual(TEXT("tap = the primary (None)"), int32(O.Verb), 0);
	// a hold: opens on the first available row; the wheel skips the refused one; the release sends that verb
	S = UL::FState(); UL::Step(S, true, T, Rows, 0, -1, false);
	UL::Step(S, true, T + 0.3, Rows, 0, -1, false);
	TestTrue(TEXT("held: open"), S.bOpen); TestEqual(TEXT("highlight starts on the first available row"), S.Highlight, 0);
	UL::Step(S, true, T + 0.35, Rows, 1, -1, false);
	TestEqual(TEXT("the wheel skips the refused row"), S.Highlight, 2);
	UL::Step(S, true, T + 0.4, Rows, 1, -1, false); UL::Step(S, true, T + 0.45, Rows, 1, -1, false);
	TestEqual(TEXT("no wrap at the end"), S.Highlight, 3);
	UL::Step(S, true, T + 0.5, Rows, 0, 1, false);
	TestEqual(TEXT("a refused row cannot be picked by number"), S.Highlight, 3);
	UL::Step(S, true, T + 0.5, Rows, 0, 2, false);
	TestEqual(TEXT("number key picks"), S.Highlight, 2);
	O = UL::Step(S, false, T + 0.6, Rows, 0, -1, false);
	TestTrue(TEXT("release sends"), O.bPress); TestEqual(TEXT("the highlighted row's verb"), int32(O.Verb), 12);
	// cancel: no press at all, not even a tap on the release
	S = UL::FState(); UL::Step(S, true, T, Rows, 0, -1, false); UL::Step(S, true, T + 0.3, Rows, 0, -1, false);
	UL::Step(S, true, T + 0.4, Rows, 0, -1, true);
	TestFalse(TEXT("cancelled: closed"), S.bOpen);
	TestFalse(TEXT("cancelled: release sends nothing"), UL::Step(S, false, T + 0.5, Rows, 0, -1, false).bPress);
	// an empty list on hold = nothing happens
	TArray<UL::FRow> None; S = UL::FState(); UL::Step(S, true, T, None, 0, -1, false); UL::Step(S, true, T + 0.5, None, 0, -1, false);
	TestFalse(TEXT("empty list: not open"), S.bOpen);
	TestFalse(TEXT("empty list: release sends nothing"), UL::Step(S, false, T + 0.6, None, 0, -1, false).bPress);
	return true;
}
#endif
