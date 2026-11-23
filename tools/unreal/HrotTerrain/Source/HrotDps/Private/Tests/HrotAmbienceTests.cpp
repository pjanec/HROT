// CE-4651 (U-19d) — rails for the ambience mix and the weather pick (DESIGN_IG_Squad_Radio.md §6): no world, no audio.
// Run: UnrealEditor-Cmd <project> -ExecCmds="Automation RunTests Hrot.Dps.Ambience; Automation RunTests Hrot.Dps.Weather; Quit" -nullrhi -unattended
#include "Misc/AutomationTest.h"
#include "HrotAmbience.h"

#if WITH_DEV_AUTOMATION_TESTS

using FMix = FHrotAmbienceMix;

static FMix::FRow Row(int32 Id, const TCHAR* Kind, float I = 1.f, const FVector* At = nullptr, float RadiusCm = 0.f)
{
	FMix::FRow R; R.Id = Id; R.Kind = Kind; R.Intensity = I;
	if (At) { R.bHasPosition = true; R.PositionCm = *At; R.RadiusCm = RadiusCm; }
	return R;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotAmbienceTest, "Hrot.Dps.Ambience", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotAmbienceTest::RunTest(const FString&)
{
	// A-1: the kind -> set table, the last dotted part dropped until known, an unknown family says nothing
	TestEqual(TEXT("battle.city"), FMix::KindToSet(TEXT("battle.city")), FString(TEXT("amb/war_city")));
	TestEqual(TEXT("alarm.airraid.preliminary (its own set)"), FMix::KindToSet(TEXT("alarm.airraid.preliminary")), FString(TEXT("amb/al_airraid_pre")));
	TestEqual(TEXT("alarm.airraid.final -> alarm.airraid"), FMix::KindToSet(TEXT("alarm.airraid.final")), FString(TEXT("amb/al_airraid")));
	TestEqual(TEXT("alarm.klaxon -> alarm"), FMix::KindToSet(TEXT("alarm.klaxon")), FString(TEXT("amb/al_emergency")));
	TestEqual(TEXT("event.evacuation.city"), FMix::KindToSet(TEXT("event.evacuation.city")), FString(TEXT("amb/ev_evacuation_city")));
	TestEqual(TEXT("event.evacuation.night -> event.evacuation"), FMix::KindToSet(TEXT("event.evacuation.night")), FString(TEXT("amb/ev_evacuation")));
	TestEqual(TEXT("naval.warship"), FMix::KindToSet(TEXT("naval.warship")), FString(TEXT("amb/nv_warship")));
	TestEqual(TEXT("case does not matter"), FMix::KindToSet(TEXT("Battle.Heavy")), FString(TEXT("amb/war_heavy")));
	TestTrue(TEXT("an unknown family: none"), FMix::KindToSet(TEXT("weird.thing")).IsEmpty());
	TestTrue(TEXT("a bare event: none"), FMix::KindToSet(TEXT("event")).IsEmpty());
	TestTrue(TEXT("empty: none"), FMix::KindToSet(TEXT("")).IsEmpty());
	TestTrue(TEXT("categories"), FMix::CategoryOf(TEXT("amb/war_city")) == FMix::ECategory::Bed && FMix::CategoryOf(TEXT("amb/nv_ship")) == FMix::ECategory::Bed &&
		FMix::CategoryOf(TEXT("amb/ev_drone")) == FMix::ECategory::Event && FMix::CategoryOf(TEXT("amb/al_bell")) == FMix::ECategory::Alarm);

	// A-2: gains
	const FVector Origin = FVector::ZeroVector;
	TestEqual(TEXT("positionless = Intensity"), FMix::GainOf(Row(1, TEXT("battle.city"), 0.7f), FVector(99999, 0, 0)), 0.7f);
	const FVector Town(10000, 0, 0);   // 100 m
	const FMix::FRow Siren = Row(2, TEXT("alarm.airraid"), 1.f, &Town, 40000.f);   // radius 400 m
	TestEqual(TEXT("within half the radius: full (listener 150 m from it)"), FMix::GainOf(Siren, FVector(25000, 0, 0)), 1.f);
	TestEqual(TEXT("at 300 m from it: half"), FMix::GainOf(Siren, FVector(40000, 0, 0)), 0.5f);
	TestEqual(TEXT("at the radius and beyond: silent"), FMix::GainOf(Siren, FVector(50000, 0, 0)), 0.f);

	// A-3: <= 2 battle beds (the loudest), <= 3 alarms, events one at a time and once per row
	{
		FMix M;
		M.Set(Row(1, TEXT("battle.city"), 0.4f)); M.Set(Row(2, TEXT("battle.heavy"), 0.9f)); M.Set(Row(3, TEXT("battle.night"), 0.6f)); M.Set(Row(4, TEXT("naval.ship"), 0.1f));
		FMix::FPlan P = M.Plan(Origin);
		TArray<int32> Ids; for (const auto& L : P.Loops) Ids.Add(L.Id);
		TestEqual(TEXT("two beds: the loudest (heavy 0.9, night 0.6)"), FString::JoinBy(Ids, TEXT(","), [](int32 I) { return FString::FromInt(I); }), FString(TEXT("2,3")));
		for (int32 I = 10; I < 15; ++I) M.Set(Row(I, TEXT("alarm.bell"), 0.1f * (I - 9)));
		P = M.Plan(Origin);
		int32 Alarms = 0; for (const auto& L : P.Loops) Alarms += L.Cat == FMix::ECategory::Alarm;
		TestEqual(TEXT("three alarms of five"), Alarms, 3);
		M.Set(Row(20, TEXT("event.airstrike"))); M.Set(Row(21, TEXT("event.drone")));
		P = M.Plan(Origin);
		TestTrue(TEXT("one event: the first row"), P.bEvent && P.Event.Id == 20 && P.Event.Set == TEXT("amb/ev_airstrike"));
		M.MarkPlayed(20);
		P = M.Plan(Origin);
		TestTrue(TEXT("once per row: the next event"), P.bEvent && P.Event.Id == 21);
		M.MarkPlayed(21);
		TestFalse(TEXT("both played: no event"), M.Plan(Origin).bEvent);
		M.Set(Row(20, TEXT("event.airstrike")));   // the same row again (a state refresh): still played
		TestFalse(TEXT("a refreshed row does not replay"), M.Plan(Origin).bEvent);
		M.Remove(20); M.Set(Row(20, TEXT("event.airstrike")));
		TestTrue(TEXT("a removed then new row plays again"), M.Plan(Origin).bEvent);
		// a silent positioned row (out of its radius) is not in the plan
		M.Set(Row(30, TEXT("alarm.intruder"), 1.f, &Town, 1000.f));
		bool bIn = false; for (const auto& L : M.Plan(FVector(500000, 0, 0)).Loops) bIn |= L.Id == 30;
		TestFalse(TEXT("out of its radius: not looped"), bIn);
	}

	// A-4: a disposed row is listed (the player fades it); an unknown kind is reported once
	{
		FMix M;
		M.Set(Row(1, TEXT("battle.city")));
		M.Remove(1);
		TArray<int32> D = M.TakeDisposed();
		TestTrue(TEXT("the disposed row is listed once"), D.Num() == 1 && D[0] == 1 && M.TakeDisposed().Num() == 0);
		TestEqual(TEXT("and gone from the plan"), M.Plan(Origin).Loops.Num(), 0);
		M.Remove(77);
		TestEqual(TEXT("removing an unknown row lists nothing"), M.TakeDisposed().Num(), 0);
		M.Set(Row(5, TEXT("zzz.unknown"))); M.Set(Row(6, TEXT("zzz.unknown")));
		TestEqual(TEXT("an unknown kind is reported once"), M.TakeUnknown().Num(), 1);
		TestEqual(TEXT("and plays nothing"), M.Plan(Origin).Loops.Num(), 0);
	}

	// A-5: the battle beds 6 dB lower while simulated shots were heard in the last 10 s
	TestEqual(TEXT("no shot ever: full"), FMix::ShotDuck(100.0, -1.0), 1.f);
	TestEqual(TEXT("a shot 3 s ago: -6 dB"), FMix::ShotDuck(100.0, 97.0), 0.5f);
	TestEqual(TEXT("a shot 10.5 s ago: full again"), FMix::ShotDuck(100.0, 89.5), 1.f);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotWeatherTest, "Hrot.Dps.Weather", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotWeatherTest::RunTest(const FString&)
{
	using FW = FHrotWeatherPick;
	const auto Global = [](int32 Id, float Wind, float Mm, bool bRain) { FW::FRow R; R.Id = Id; R.WindSpeedMps = Wind; R.PrecipMmPerHour = Mm; R.bRain = bRain; return R; };
	const auto Square = [](int32 Id, float Half, float Feather, float Wind, float Mm) {
		FW::FRow R; R.Id = Id; R.FeatherM = Feather; R.WindSpeedMps = Wind; R.PrecipMmPerHour = Mm; R.bRain = true;
		R.Outline = { FVector2D(-Half, -Half), FVector2D(Half, -Half), FVector2D(Half, Half), FVector2D(-Half, Half) }; return R; };

	{
		FW P;
		TestFalse(TEXT("no rows: nothing to say (the hand values stay)"), P.At(FVector2D(0, 0)).bAny);
		P.Set(Global(1, 7.5f, 4.f, true));
		FW::FOut O = P.At(FVector2D(1000, 1000));
		TestTrue(TEXT("a global row"), O.bAny);
		TestEqual(TEXT("rain = 4 mm/h / 8"), O.Rain, 0.5f);
		TestEqual(TEXT("wind = 7.5 m/s / 15"), O.Wind, 0.5f);
		P.Set(Global(1, 30.f, 40.f, true));
		O = P.At(FVector2D(0, 0));
		TestTrue(TEXT("clamped to 1"), O.Rain == 1.f && O.Wind == 1.f);
		P.Set(Global(1, 3.f, 40.f, false));
		TestEqual(TEXT("snow (not rain-like): no rain bed"), P.At(FVector2D(0, 0)).Rain, 0.f);
	}
	{   // a region: the one holding the camera wins over the global row; the feather blends toward it
		FW P;
		P.Set(Global(1, 0.f, 0.f, true));                 // clear everywhere ...
		P.Set(Square(2, 1000.f, 200.f, 15.f, 8.f));      // ... a storm cell 2 km wide with a 200 m feather
		FW::FOut In = P.At(FVector2D(0, 0));
		TestTrue(TEXT("deep inside the cell: the cell (rain 1, wind 1)"), In.Rain == 1.f && In.Wind == 1.f);
		FW::FOut Out = P.At(FVector2D(5000, 0));
		TestTrue(TEXT("outside: the global row (calm)"), Out.Rain == 0.f && Out.Wind == 0.f);
		FW::FOut Edge = P.At(FVector2D(900, 0));   // 100 m inside the edge: half way through the 200 m feather
		TestEqual(TEXT("100 m inside a 200 m feather: half"), Edge.Rain, 0.5f);
		TestEqual(TEXT("  wind too"), Edge.Wind, 0.5f);
		TestEqual(TEXT("at the edge: the global row"), P.At(FVector2D(999.9f, 0)).Rain < 0.01f, true);
		// two regions overlap: the lower id; a region without a global row blends toward calm
		P.Set(Square(3, 1000.f, 0.f, 0.f, 0.f));
		TestEqual(TEXT("overlap: the lowest id wins"), P.At(FVector2D(0, 0)).Rain, 1.f);
		FW Q;
		Q.Set(Square(2, 1000.f, 200.f, 15.f, 8.f));
		TestEqual(TEXT("no global row: the feather blends toward calm"), Q.At(FVector2D(900, 0)).Rain, 0.5f);
		TestEqual(TEXT("no global row, outside: calm (rows exist)"), Q.At(FVector2D(9000, 0)).Rain, 0.f);
		TestTrue(TEXT("  and bAny stays true"), Q.At(FVector2D(9000, 0)).bAny);
		Q.Remove(2);
		TestFalse(TEXT("a removed region: no rows again"), Q.At(FVector2D(0, 0)).bAny);
	}
	{   // geometry
		const TArray<FVector2D> Tri = { FVector2D(0, 0), FVector2D(100, 0), FVector2D(0, 100) };
		TestTrue(TEXT("inside a triangle"), FW::Inside(Tri, FVector2D(10, 10)));
		TestFalse(TEXT("outside it"), FW::Inside(Tri, FVector2D(80, 80)));
		TestEqual(TEXT("distance to the nearest edge"), FW::DistanceToEdge(Tri, FVector2D(10, 10)), 10.f);
	}
	return true;
}

#endif
