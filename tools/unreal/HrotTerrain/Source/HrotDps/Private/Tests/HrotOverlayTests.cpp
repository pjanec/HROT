// CE-4651 (U-19e) — rail for the overlay painter's maths (DESIGN_IG_Squad_Radio.md §7): no canvas.
// Run: UnrealEditor-Cmd <project> -ExecCmds="Automation RunTests Hrot.Dps.Overlay; Quit" -nullrhi -unattended
#include "Misc/AutomationTest.h"
#include "HrotDpsOverlay.h"

#if WITH_DEV_AUTOMATION_TESTS

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotOverlayTest, "Hrot.Dps.Overlay", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotOverlayTest::RunTest(const FString&)
{
	using namespace HrotDpsOverlay;
	TestEqual(TEXT("top"), SubtitleY(2), 0.08f);
	TestEqual(TEXT("centre"), SubtitleY(3), 0.5f);
	TestEqual(TEXT("bottom, as the render path draws it"), SubtitleY(1), 0.88f);
	TestEqual(TEXT("unknown = bottom"), SubtitleY(0), 0.88f);

	TestEqual(TEXT("alpha 0.4 applied"), SubtitleOpacity(0.4f), 0.4f);
	TestEqual(TEXT("alpha unset (0) = opaque"), SubtitleOpacity(0.f), 1.f);
	TestEqual(TEXT("alpha above 1 clamps"), SubtitleOpacity(3.f), 1.f);

	FSpec S;
	TestFalse(TEXT("empty subtitle: none"), HasSubtitle(S));
	S.Subtitle = TEXT("Phase two");
	TestTrue(TEXT("a subtitle with text"), HasSubtitle(S));

	S.Fade = 0.f;
	TestFalse(TEXT("fade 0: no rectangle"), HasFade(S));
	S.Fade = 0.004f;
	TestFalse(TEXT("fade below 0.5 %: no rectangle"), HasFade(S));
	S.Fade = 0.5f;
	TestTrue(TEXT("fade 0.5: a rectangle"), HasFade(S));
	TestEqual(TEXT("  at 0.5"), FadeOpacity(S.Fade), 0.5f);
	S.Fade = 1.f;
	TestEqual(TEXT("fade 1: opaque"), FadeOpacity(S.Fade), 1.f);
	TestEqual(TEXT("fade 7 clamps to opaque"), FadeOpacity(7.f), 1.f);
	TestEqual(TEXT("a negative fade clamps to clear"), FadeOpacity(-1.f), 0.f);

	TestFalse(TEXT("a null canvas paints nothing"), Paint(nullptr, FVector2D(100, 100), S, nullptr).bFade);
	return true;
}

#endif
