// CE-4651 (U-19g) - rails for the live-run fixes: the click pick (HrotDpsPick), the empty-vehicle engine rule (UHrotSoundscape::EngineWanted).
// Run: UnrealEditor-Cmd <project> -ExecCmds="Automation RunTests Hrot.Dps.LiveFix; Quit" -nullrhi -unattended
#include "Misc/AutomationTest.h"
#include "HrotDpsPick.h"
#include "HrotSoundscape.h"
#include "HrotDpsCamera.h"

#if WITH_DEV_AUTOMATION_TESTS

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotLiveFixPickTest, "Hrot.Dps.LiveFix.Pick", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotLiveFixPickTest::RunTest(const FString&)
{
	using namespace HrotDpsPick;
	TArray<FCand> C;
	C.Add({ 5, FVector2D(400, 300), 30.0 });
	C.Add({ 6, FVector2D(420, 310), 50.0 });
	TestEqual(TEXT("nothing near: nothing"), Choose(C, FVector2D(100, 100), 0, 0), 0);
	TestEqual(TEXT("the nearest within 24 px"), Choose(C, FVector2D(415, 308), 0, 0), 6);
	TestEqual(TEXT("  or the other"), Choose(C, FVector2D(398, 296), 0, 0), 5);
	TestEqual(TEXT("25 px away is too far"), Choose(C, FVector2D(400, 325.5), 0, 0), 0);
	TestEqual(TEXT("a trace hit alone is kept"), Choose(C, FVector2D(100, 100), 9, 20.0), 9);
	TestEqual(TEXT("the soldier wins over a FARTHER trace hit (a wall behind him)"), Choose(C, FVector2D(400, 300), 9, 80.0), 5);
	TestEqual(TEXT("a NEARER trace hit wins (something stands in front)"), Choose(C, FVector2D(400, 300), 9, 10.0), 9);
	TestEqual(TEXT("the same entity both ways"), Choose(C, FVector2D(400, 300), 5, 10.0), 5);

	// U-19g: a man is a SEGMENT feet -> head (a click on his head worked, on his body did not)
	TArray<FCand> M;
	FCand Man; Man.Id = 7; Man.Screen = FVector2D(500, 400); Man.B = FVector2D(500, 300); Man.bSeg = true; Man.Depth = 20.0;   // feet (500, 400), head (500, 300)
	M.Add(Man);
	TestEqual(TEXT("a click on his head picks him"), Choose(M, FVector2D(500, 300), 0, 0), 7);
	TestEqual(TEXT("a click on his BODY mid-segment picks him"), Choose(M, FVector2D(510, 350), 0, 0), 7);
	TestEqual(TEXT("a click 30 px beside him misses"), Choose(M, FVector2D(530, 350), 0, 0), 0);
	TestEqual(TEXT("a click 30 px below his feet misses"), Choose(M, FVector2D(500, 430), 0, 0), 0);
	TestEqual(TEXT("23 px beside the segment still picks"), Choose(M, FVector2D(523, 350), 0, 0), 7);
	TestEqual(TEXT("distance to the segment: beside"), DistToSegment(FVector2D(530, 350), FVector2D(500, 400), FVector2D(500, 300)), 30.f, 1e-3f);
	TestEqual(TEXT("distance to the segment: past the head end"), DistToSegment(FVector2D(500, 270), FVector2D(500, 400), FVector2D(500, 300)), 30.f, 1e-3f);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotLiveFixEngineTest, "Hrot.Dps.LiveFix.Engine", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotLiveFixEngineTest::RunTest(const FString&)
{
	TestFalse(TEXT("an empty ground vehicle, still for 2 s: no engine"), UHrotSoundscape::EngineWanted(true, false, 2.f));
	TestTrue(TEXT("an empty one, still only 1.5 s: still on"), UHrotSoundscape::EngineWanted(true, false, 1.5f));
	TestTrue(TEXT("crewed and parked: idles"), UHrotSoundscape::EngineWanted(true, true, 60.f));
	TestTrue(TEXT("empty but moving (still 0): engine"), UHrotSoundscape::EngineWanted(true, false, 0.f));
	TestTrue(TEXT("an aircraft is unchanged"), UHrotSoundscape::EngineWanted(false, false, 60.f));
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotLiveFixEyesTest, "Hrot.Dps.LiveFix.Eyes", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotLiveFixEyesTest::RunTest(const FString&)
{
	// entering his eyes keeps the orbit's look: the gaze the eyes then compute IS the orbit's yaw / pitch (no jump), from any heading
	for (const double BodyYaw : { 0.0, 1.0, -2.5, 3.0 })
		for (const double Yaw : { 0.3, -1.2, 3.0, -3.0 })
		{
			const double Pitch = -0.35, BodyPitch = 0.1;
			double OY, OP, GY, GP;
			HrotCamera::EyesEntry(Yaw, Pitch, BodyYaw, BodyPitch, OY, OP);
			HrotCamera::EyesLook(BodyYaw, BodyPitch, OY, OP, GY, GP);
			TestEqual(TEXT("the gaze pitch = the orbit's"), GP, Pitch, 1e-9);
			TestEqual(TEXT("the gaze yaw = the orbit's (mod 2 pi)"), FMath::UnwindRadians(GY - Yaw), 0.0, 1e-9);
			TestTrue(TEXT("the held yaw offset is the short way round"), FMath::Abs(OY) <= UE_DOUBLE_PI + 1e-9);
		}
	return true;
}
#endif
