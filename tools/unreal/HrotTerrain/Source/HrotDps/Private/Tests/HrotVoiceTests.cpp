// CE-4651 (U-19b) — rails for the squad's radio: the line map (HrotVoiceLines, every row of DESIGN_IG_Squad_Radio.md §2) and the player's pure
// queue / spacing / earpiece rules (FHrotVoiceSched) — no world, no audio.
// Run: UnrealEditor-Cmd <project> -ExecCmds="Automation RunTests Hrot.Dps.Voice; Quit" -nullrhi -unattended
#include "Misc/AutomationTest.h"
#include "HrotVoiceLines.h"
#include "HrotVoicePlayer.h"

#if WITH_DEV_AUTOMATION_TESTS

using namespace HrotVoiceLines;

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotVoiceLinesTest, "Hrot.Dps.Voice.Lines", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotVoiceLinesTest::RunTest(const FString&)
{
	auto Rep = [](int32 Kind, int32 Sub = 0, int32 Count = 0, int32 Act = 0, int32 Str = 0, int32 Wia = 0, int32 Kia = 0)
	{ FReportFields R; R.Kind = Kind; R.Sub = Sub; R.Count = Count; R.Activity = Act; R.Strength = Str; R.Wia = Wia; R.Kia = Kia; return ForReport(R); };

	// utterances: shouts in the world
	const TCHAR* Utt[][2] = { { TEXT("CmdMove"), TEXT("vox/cmd_move") }, { TEXT("CmdHold"), TEXT("vox/cmd_hold") }, { TEXT("CmdFallBack"), TEXT("vox/cmd_fallback") },
		{ TEXT("CmdDown"), TEXT("vox/cmd_down") }, { TEXT("CallCovering"), TEXT("vox/call_cover") }, { TEXT("CallSuppress"), TEXT("vox/call_suppress") },
		{ TEXT("CallReloading"), TEXT("vox/call_reload") }, { TEXT("CallFragOut"), TEXT("vox/call_fragout") }, { TEXT("CallContact"), TEXT("vox/call_contact") },
		{ TEXT("CallGrenade"), TEXT("vox/call_grenade") }, { TEXT("CallAck"), TEXT("vox/ack") }, { TEXT("CallClear"), TEXT("vox/call_clear") },
		{ TEXT("CallManDown"), TEXT("vox/call_medic") } };
	for (const auto& U : Utt)
	{
		const FVoiceCue C = ForUtterance(U[0]);
		TestEqual(FString(U[0]), C.Set, FString(U[1]));
		TestFalse(FString(U[0]) + TEXT(" is heard in the world"), C.bRadio);
	}
	TestTrue(TEXT("a civil kind says nothing"), ForUtterance(TEXT("CivilScream")).IsEmpty());
	TestTrue(TEXT("an animal kind says nothing"), ForUtterance(TEXT("AnimalBark")).IsEmpty());
	TestTrue(TEXT("an empty kind says nothing"), ForUtterance(TEXT("")).IsEmpty());

	// reports: all on the radio
	TestEqual(TEXT("Contact"), Rep(ReportKind::Contact).Set, FString(TEXT("vox/rep_contact")));
	TestEqual(TEXT("Spot, 3 not moving"), Rep(ReportKind::Spot, 0, 3, Activity::Static).Set, FString(TEXT("vox/rep_spot")));
	TestEqual(TEXT("Spot, 4"), Rep(ReportKind::Spot, 0, 4, Activity::Static).Set, FString(TEXT("vox/rep_spot_big")));
	TestEqual(TEXT("Spot, 2 moving"), Rep(ReportKind::Spot, 0, 2, Activity::Moving).Set, FString(TEXT("vox/rep_spot_big")));
	TestEqual(TEXT("Progress Set"), Rep(ReportKind::Progress, Progress::Set).Set, FString(TEXT("vox/rep_set")));
	for (int32 Sub : { int32(Progress::ObjectiveSecure), int32(Progress::BuildingClear), int32(Progress::RoomClear) })
		TestEqual(FString::Printf(TEXT("Progress %d done"), Sub), Rep(ReportKind::Progress, Sub).Set, FString(TEXT("vox/rep_done")));
	for (int32 Sub : { int32(Progress::FallingBack), int32(Progress::Displacing) })
		TestEqual(FString::Printf(TEXT("Progress %d fallback"), Sub), Rep(ReportKind::Progress, Sub).Set, FString(TEXT("vox/rep_fallback")));
	TestEqual(TEXT("Progress TaskFailed"), Rep(ReportKind::Progress, Progress::TaskFailed).Set, FString(TEXT("vox/rep_failed")));
	TestEqual(TEXT("Progress TargetsDown"), Rep(ReportKind::Progress, Progress::TargetsDown).Set, FString(TEXT("vox/rep_targetsdown")));
	TestTrue(TEXT("Progress PhaseLine: no line"), Rep(ReportKind::Progress, Progress::PhaseLine).IsEmpty());
	TestEqual(TEXT("Lace, 2 lost (kia 2)"), Rep(ReportKind::Lace, 0, 0, 0, 0, 0, 2).Set, FString(TEXT("vox/rep_losses")));
	TestEqual(TEXT("Lace, 1 wia + 1 kia"), Rep(ReportKind::Lace, 0, 0, 0, 0, 1, 1).Set, FString(TEXT("vox/rep_losses")));
	TestEqual(TEXT("Lace, 1 lost"), Rep(ReportKind::Lace, 0, 0, 0, 0, 0, 1).Set, FString(TEXT("vox/rep_status_ok")));
	TestEqual(TEXT("Lace, none lost"), Rep(ReportKind::Lace).Set, FString(TEXT("vox/rep_status_ok")));
	TestEqual(TEXT("Sitrep green"), Rep(ReportKind::Sitrep, 0, 0, 0, Colour::Green).Set, FString(TEXT("vox/rep_advantage")));
	TestEqual(TEXT("Sitrep amber"), Rep(ReportKind::Sitrep, 0, 0, 0, Colour::Amber).Set, FString(TEXT("vox/rep_status_ok")));
	TestEqual(TEXT("Sitrep red"), Rep(ReportKind::Sitrep, 0, 0, 0, Colour::Red).Set, FString(TEXT("vox/rep_tight")));
	TestEqual(TEXT("Request Casevac"), Rep(ReportKind::Request, Request::Casevac).Set, FString(TEXT("vox/rep_casevac")));
	TestEqual(TEXT("Request FireSupport"), Rep(ReportKind::Request, Request::FireSupport).Set, FString(TEXT("vox/rep_fires")));
	TestEqual(TEXT("Request Reinforce"), Rep(ReportKind::Request, Request::Reinforce).Set, FString(TEXT("vox/rep_backup")));
	TestEqual(TEXT("Request Permission"), Rep(ReportKind::Request, Request::Permission).Set, FString(TEXT("vox/rep_permission")));
	TestEqual(TEXT("Request Resupply (other)"), Rep(ReportKind::Request, Request::Resupply).Set, FString(TEXT("vox/rep_backup")));
	TestTrue(TEXT("every report is on the radio"), Rep(ReportKind::Lace, 0, 0, 0, 0, 0, 2).bRadio);
	TestTrue(TEXT("ReportKind None: no line"), Rep(ReportKind::None).IsEmpty());

	// orders: by the name's keyword, with the receiver's answer
	const TCHAR* Ord[][2] = { { TEXT("Attack the ridge"), TEXT("vox/ord_attack") }, { TEXT("assault"), TEXT("vox/ord_attack") }, { TEXT("Defend"), TEXT("vox/ord_defend") },
		{ TEXT("Hold position"), TEXT("vox/ord_defend") }, { TEXT("Flank left"), TEXT("vox/ord_flank") }, { TEXT("Move to OBJ"), TEXT("vox/ord_move") },
		{ TEXT("Advance"), TEXT("vox/ord_move") }, { TEXT("go"), TEXT("vox/ord_move") }, { TEXT("Orbit"), TEXT("vox/ord_standby") }, { TEXT("cargo"), TEXT("vox/ord_standby") },
		{ TEXT(""), TEXT("vox/ord_standby") } };
	for (const auto& O : Ord)
	{
		const FVoiceCue C = ForOrder(O[0]);
		TestEqual(FString::Printf(TEXT("order '%s'"), O[0]), C.Set, FString(O[1]));
		TestTrue(TEXT("  on the radio"), C.bRadio);
		TestEqual(TEXT("  the receiver answers"), C.AnswerSet, FString(TEXT("vox/ack_order")));
	}

	// callsigns: stable, distinct, the 12th wraps
	FHrotCallsigns Cs;
	TArray<FString> Names;
	for (int32 U = 100; U < 111; ++U) Names.Add(Cs.For(U));
	TestEqual(TEXT("the first unit is Brown"), Names[0], FString(TEXT("Brown")));
	TestEqual(TEXT("the eleventh is Taylor"), Names[10], FString(TEXT("Taylor")));
	TestEqual(TEXT("eleven distinct names"), TSet<FString>(Names).Num(), 11);
	TestEqual(TEXT("stable: unit 103 again"), Cs.For(103), Names[3]);
	TestEqual(TEXT("the twelfth unit wraps to the first name"), Cs.For(999), Names[0]);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotVoicePlayerTest, "Hrot.Dps.Voice.Player", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotVoicePlayerTest::RunTest(const FString&)
{
	// one transmission at a time per net, <= 2 waiting, the oldest dropped
	{
		FHrotVoiceSched S;
		TestEqual(TEXT("A offered on an idle net: nothing dropped"), S.Offer(7, 1), -1);
		int32 Net = 0, Tok = 0;
		TestTrue(TEXT("A starts"), S.PopStartable(0, Net, Tok));
		TestTrue(TEXT("  on net 7 as token 1"), Net == 7 && Tok == 1);
		TestTrue(TEXT("the net is busy"), S.Busy(7));
		TestEqual(TEXT("B waits"), S.Offer(7, 2), -1);
		TestEqual(TEXT("C waits"), S.Offer(7, 3), -1);
		TestEqual(TEXT("the third waiting one drops the OLDEST (B)"), S.Offer(7, 4), 2);
		TestEqual(TEXT("two waiting"), S.Waiting(7), 2);
		TestEqual(TEXT("one dropped"), S.Dropped, 1);
		TestFalse(TEXT("nothing starts while A talks"), S.PopStartable(0, Net, Tok));
		S.Finish(7);
		TestTrue(TEXT("A ends: the older survivor (C) starts"), S.PopStartable(0, Net, Tok) && Tok == 3);
		S.Finish(7);
		TestTrue(TEXT("then D"), S.PopStartable(0, Net, Tok) && Tok == 4);
		// another net is independent
		S.Offer(8, 10);
		TestTrue(TEXT("net 8 talks while 7 does"), S.PopStartable(1, Net, Tok) && Net == 8);
		// the voice cap holds a transmission back
		FHrotVoiceSched V;
		V.Offer(1, 1);
		TestFalse(TEXT("3 voices active: it waits"), V.PopStartable(FHrotVoiceSched::MaxVoices, Net, Tok));
		TestTrue(TEXT("a slot frees: it starts"), V.PopStartable(FHrotVoiceSched::MaxVoices - 1, Net, Tok));
	}

	// a speaker's world shouts are >= 2 s apart; <= 3 voices overall
	{
		FHrotVoiceSched S;
		TestTrue(TEXT("first shout"), S.AllowShout(5, 10.0, 0));
		TestFalse(TEXT("1 s later: refused"), S.AllowShout(5, 11.0, 0));
		TestTrue(TEXT("another speaker is free"), S.AllowShout(6, 11.0, 0));
		TestTrue(TEXT("2 s after the first: allowed"), S.AllowShout(5, 12.0, 0));
		TestFalse(TEXT("3 voices playing: refused"), S.AllowShout(7, 20.0, 3));
		TestEqual(TEXT("two refusals counted"), S.Dropped, 2);
	}

	// earpiece vs world by the listener's net
	{
		FVoiceCue Rad; Rad.Set = TEXT("vox/rep_losses"); Rad.bRadio = true; Rad.ToId = 40;
		TestTrue(TEXT("his own unit's report: earpiece"), FHrotVoiceSched::IsEarpiece(Rad, 12, 12));
		TestTrue(TEXT("a report TO his unit: earpiece"), FHrotVoiceSched::IsEarpiece(Rad, 12, 40));
		TestFalse(TEXT("another unit's: world"), FHrotVoiceSched::IsEarpiece(Rad, 12, 13));
		TestFalse(TEXT("he follows nobody: world"), FHrotVoiceSched::IsEarpiece(Rad, 12, 0));
		FVoiceCue Shout; Shout.Set = TEXT("vox/cmd_move");
		TestFalse(TEXT("a shout is always the world"), FHrotVoiceSched::IsEarpiece(Shout, 12, 12));
	}

	// the chain: beep, sgt, line, over (every second), squelch; an order adds the answer 1.5 s later
	{
		FVoiceCue C; C.Set = TEXT("vox/rep_losses"); C.bRadio = true; C.Callsign = TEXT("Cooper");
		TestEqual(TEXT("chain"), FString::Join(FHrotVoiceSched::BuildChain(C, false), TEXT(",")), FString(TEXT("radio/beep,vox/sgt_cooper,vox/rep_losses,radio/squelch")));
		TestEqual(TEXT("chain with over"), FString::Join(FHrotVoiceSched::BuildChain(C, true), TEXT(",")), FString(TEXT("radio/beep,vox/sgt_cooper,vox/rep_losses,vox/over,radio/squelch")));
		C.AnswerSet = TEXT("vox/ack_order");
		TestEqual(TEXT("an order's answer"), FString::Join(FHrotVoiceSched::BuildChain(C, false), TEXT(",")),
			FString(TEXT("radio/beep,vox/sgt_cooper,vox/rep_losses,radio/squelch,+1.5/radio/beep,vox/ack_order,radio/squelch")));
		FVoiceCue W; W.Set = TEXT("vox/cmd_move");
		TestEqual(TEXT("a shout is the line alone"), FString::Join(FHrotVoiceSched::BuildChain(W, true), TEXT(",")), FString(TEXT("vox/cmd_move")));
	}
	return true;
}

#endif
