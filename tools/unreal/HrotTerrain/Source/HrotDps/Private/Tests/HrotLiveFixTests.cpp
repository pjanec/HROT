// CE-4651 (U-19g) - rails for the live-run fixes: the click pick (HrotDpsPick), the empty-vehicle engine rule (UHrotSoundscape::EngineWanted).
// Run: UnrealEditor-Cmd <project> -ExecCmds="Automation RunTests Hrot.Dps.LiveFix; Quit" -nullrhi -unattended
#include "Misc/AutomationTest.h"
#include "HrotDpsPick.h"
#include "HrotSoundscape.h"

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
#endif
