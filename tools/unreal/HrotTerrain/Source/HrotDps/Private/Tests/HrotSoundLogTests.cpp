// CE-4651 (U-19f) - rail for the sound log (HrotSoundLog.h, DESIGN_IG_Squad_Radio.md section 8): the ring, the expiry and fade, the format strings, the caption. No world, no audio.
// Run: UnrealEditor-Cmd <project> -ExecCmds="Automation RunTests Hrot.Dps.SoundLog; Quit" -nullrhi -unattended
#include "Misc/AutomationTest.h"
#include "HrotSoundLog.h"
#include "Internationalization/Internationalization.h"
#include "Internationalization/Culture.h"

#if WITH_DEV_AUTOMATION_TESTS

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotSoundLogTest, "Hrot.Dps.SoundLog", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotSoundLogTest::RunTest(const FString&)
{
	// the machine is Czech: a decimal comma must never reach a line (the log prints plain integers / one-decimal invariant numbers)
	const FString Culture = FInternationalization::Get().GetCurrentCulture()->GetName();
	FInternationalization::Get().SetCurrentCulture(TEXT("cs-CZ"));

	// the format strings, one per channel
	TestEqual(TEXT("radio"), FHrotSoundLog::Format(EHrotSndChannel::Radio, TEXT("rep_contact_02"), TEXT("Report Contact from unit 12"), -1.f),
		FString(TEXT("RADIO · earpiece · rep_contact_02 — Report Contact from unit 12")));
	TestEqual(TEXT("world: distance rounded to metres"), FHrotSoundLog::Format(EHrotSndChannel::World, TEXT("cmd_move_03"), TEXT("man 31 Utterance CmdMove"), 18.4f),
		FString(TEXT("WORLD 18 m · cmd_move_03 — man 31 Utterance CmdMove")));
	TestEqual(TEXT("weapon: no detail, no dash"), FHrotSoundLog::Format(EHrotSndChannel::Weapon, TEXT("mg_b7_01"), FString(), 42.f), FString(TEXT("WEAPON 42 m · mg_b7_01")));
	TestEqual(TEXT("ambience: 2-D"), FHrotSoundLog::Format(EHrotSndChannel::Ambience, TEXT("war_city_01"), TEXT("battle.city 0.8"), -1.f),
		FString(TEXT("AMBIENCE · war_city_01 — battle.city 0.8")));
	TestEqual(TEXT("event"), FHrotSoundLog::Format(EHrotSndChannel::Event, TEXT("ev_airstrike_01"), FString(), -1.f), FString(TEXT("EVENT · ev_airstrike_01")));

	// the ring keeps the last 6, oldest first; the transcript keeps them all
	FHrotSoundLog L;
	for (int32 I = 0; I < 9; ++I) L.Add(EHrotSndChannel::Fx, FString::Printf(TEXT("c%d"), I), FString(), -1.f, 100.0 + I);
	TestEqual(TEXT("ring holds 6"), L.Num(), 6);
	TestEqual(TEXT("  the oldest kept is the 4th added"), L.At(0).Label, FString(TEXT("c3")));
	TestEqual(TEXT("  the newest is last"), L.At(5).Label, FString(TEXT("c8")));
	TestEqual(TEXT("transcript holds all 9"), L.Transcript().Num(), 9);
	TestTrue(TEXT("  numbered"), L.Transcript()[0].StartsWith(TEXT("1 FX")));

	// expiry: opaque for 4.5 s, fading to 0 at 6 s
	TestEqual(TEXT("age 0 opaque"), FHrotSoundLog::AlphaOfAge(0.0), 1.f);
	TestEqual(TEXT("age 4.5 still opaque"), FHrotSoundLog::AlphaOfAge(4.5), 1.f);
	TestEqual(TEXT("age 5.25 half"), FHrotSoundLog::AlphaOfAge(5.25), 0.5f);
	TestEqual(TEXT("age 6 gone"), FHrotSoundLog::AlphaOfAge(6.0), 0.f);
	TestEqual(TEXT("age 9 gone"), FHrotSoundLog::AlphaOfAge(9.0), 0.f);
	TestTrue(TEXT("an entry just added is live"), L.AnyLive(108.5));
	TestFalse(TEXT("all gone 6 s after the last"), L.AnyLive(114.5));

	// the caption
	float A = -1.f;
	L.SetCaption(TEXT("You should hear him"), 4.0, 200.0);
	TestEqual(TEXT("caption shown"), L.Caption(201.0, A), FString(TEXT("You should hear him")));
	TestEqual(TEXT("  opaque mid-way"), A, 1.f);
	TestEqual(TEXT("  fading at the end"), (L.Caption(203.25, A), A), 0.5f);
	TestTrue(TEXT("caption gone after its time"), L.Caption(204.5, A).IsEmpty());
	L.Clear();
	TestEqual(TEXT("clear empties the ring"), L.Num(), 0);
	TestEqual(TEXT("clear empties the transcript"), L.Transcript().Num(), 0);

	FInternationalization::Get().SetCurrentCulture(Culture);
	return true;
}

#endif
