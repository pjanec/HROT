// CE-4651 (U-19c) — rails for the burst tracker (DESIGN_IG_Squad_Radio.md §5): no world, no audio.
// Run: UnrealEditor-Cmd <project> -ExecCmds="Automation RunTests Hrot.Dps.Burst; Quit" -nullrhi -unattended
#include "Misc/AutomationTest.h"
#include "HrotBurstTracker.h"

#if WITH_DEV_AUTOMATION_TESTS

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotBurstTrackerTest, "Hrot.Dps.Burst", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotBurstTrackerTest::RunTest(const FString&)
{
	using E = FHrotBurstTracker::EAct;
	const auto All = [](int32) { return true; };
	const auto None = [](int32) { return false; };

	{   // index 0 of N starts ONE clip of N; indices 1..N-1 play nothing
		FHrotBurstTracker T;
		FHrotBurstTracker::FResult R = T.OnRound(7, 3, 0, 10.0, All);
		TestTrue(TEXT("index 0 of 3 starts a clip"), R.Act == E::Start);
		TestEqual(TEXT("  of 3 rounds"), R.ClipRounds, 3);
		T.SetHandle(7, 42);
		TestTrue(TEXT("index 1 is covered"), T.OnRound(7, 3, 1, 10.08, All).Act == E::Covered);
		TestTrue(TEXT("index 2 is covered"), T.OnRound(7, 3, 2, 10.16, All).Act == E::Covered);
		TArray<int32> Cuts; T.PollCuts(10.5, Cuts);
		TestEqual(TEXT("a complete burst needs no cut"), Cuts.Num(), 0);
	}
	{   // BurstId 0 plays per round, whatever the rest says
		FHrotBurstTracker T;
		TestTrue(TEXT("id 0, index 0"), T.OnRound(0, 3, 0, 1.0, All).Act == E::PerRound);
		TestTrue(TEXT("id 0, index 1"), T.OnRound(0, 3, 1, 1.1, All).Act == E::PerRound);
		TestEqual(TEXT("nothing tracked"), T.Active(), 0);
	}
	{   // a gap fades the clip: round 2 of 5 never comes
		FHrotBurstTracker T;
		T.OnRound(9, 5, 0, 20.0, All); T.SetHandle(9, 11);
		T.OnRound(9, 5, 1, 20.1, All);   // spacing 100 ms
		TArray<int32> Cuts;
		T.PollCuts(20.25, Cuts);          // 150 ms after: not overdue (limit 2 x 0.1 + 0.08 = 0.28)
		TestEqual(TEXT("not overdue yet"), Cuts.Num(), 0);
		T.PollCuts(20.45, Cuts);          // 350 ms after
		TestEqual(TEXT("overdue: cut once"), Cuts.Num(), 1);
		TestEqual(TEXT("  its handle"), Cuts.Num() ? Cuts[0] : -1, 11);
		Cuts.Reset(); T.PollCuts(20.6, Cuts);
		TestEqual(TEXT("cut only once"), Cuts.Num(), 0);
		TestTrue(TEXT("a late round is still covered (silent)"), T.OnRound(9, 5, 2, 20.9, All).Act == E::Covered);
		T.PollCuts(23.0, Cuts);
		TestEqual(TEXT("forgotten 2 s after its last round"), T.Active(), 0);
	}
	{   // the first spacing is assumed 100 ms: a burst that dies after index 0
		FHrotBurstTracker T;
		T.OnRound(3, 4, 0, 5.0, All); T.SetHandle(3, 5);
		TArray<int32> Cuts; T.PollCuts(5.25, Cuts);
		TestEqual(TEXT("not overdue at 250 ms"), Cuts.Num(), 0);
		T.PollCuts(5.3, Cuts);
		TestEqual(TEXT("overdue past 280 ms"), Cuts.Num(), 1);
	}
	{   // no clip of N: the longest shorter one, the rest per round
		FHrotBurstTracker T;
		const auto UpTo4 = [](int32 N) { return N <= 4; };
		FHrotBurstTracker::FResult R = T.OnRound(5, 6, 0, 1.0, UpTo4);
		TestTrue(TEXT("6 rounds, clips up to 4: starts"), R.Act == E::Start);
		TestEqual(TEXT("  the 4-round clip"), R.ClipRounds, 4);
		TestTrue(TEXT("index 3 covered"), T.OnRound(5, 6, 3, 1.3, UpTo4).Act == E::Covered);
		TestTrue(TEXT("index 4 per round"), T.OnRound(5, 6, 4, 1.4, UpTo4).Act == E::PerRound);
		TestTrue(TEXT("index 5 per round"), T.OnRound(5, 6, 5, 1.5, UpTo4).Act == E::PerRound);
		// more rounds than the longest clip
		FHrotBurstTracker U;
		TestEqual(TEXT("12 rounds: the 8-round clip"), U.OnRound(6, 12, 0, 1.0, All).ClipRounds, 8);
	}
	{   // none at all, or a single round: per round
		FHrotBurstTracker T;
		TestTrue(TEXT("no clip: per round"), T.OnRound(1, 3, 0, 1.0, None).Act == E::PerRound);
		TestTrue(TEXT("  and its later rounds"), T.OnRound(1, 3, 1, 1.1, None).Act == E::PerRound);
		TestTrue(TEXT("a burst of 1: per round"), T.OnRound(2, 1, 0, 2.0, All).Act == E::PerRound);
		TestTrue(TEXT("a late join (index 2 first): per round"), T.OnRound(99, 3, 2, 3.0, All).Act == E::PerRound);
	}
	{   // two bursts at once are independent
		FHrotBurstTracker T;
		T.OnRound(1, 2, 0, 1.0, All); T.OnRound(2, 3, 0, 1.0, All);
		TestTrue(TEXT("burst 2 index 1"), T.OnRound(2, 3, 1, 1.1, All).Act == E::Covered);
		TestEqual(TEXT("two tracked"), T.Active(), 2);
	}
	return true;
}

#endif
