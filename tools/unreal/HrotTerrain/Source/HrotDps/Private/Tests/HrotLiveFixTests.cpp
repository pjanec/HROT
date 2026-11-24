// CE-4651 (U-19g) - rails for the live-run fixes: the click pick (HrotDpsPick), the empty-vehicle engine rule (UHrotSoundscape::EngineWanted).
// Run: UnrealEditor-Cmd <project> -ExecCmds="Automation RunTests Hrot.Dps.LiveFix; Quit" -nullrhi -unattended
#include "Misc/AutomationTest.h"
#include "HrotDpsPick.h"
#include "HrotSoundscape.h"
#include "Misc/Paths.h"
#include "HrotDpsCamera.h"
#include "HrotDpsTrafficKit.h"
#include "HrotDpsWorld.h"
#include "HrotVehicle.h"

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
IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotLiveFixTrafficKitTest, "Hrot.Dps.LiveFix.TrafficKit", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotLiveFixTrafficKitTest::RunTest(const FString&)
{
	namespace TK = HrotDpsTrafficKit;
	const FString Dis = TEXT("1.1.0.81.0.0.0");
	for (const int32 Car : { 1002, 1050, 1054, 1055, 1056, 7777 }) TestTrue(FString::Printf(TEXT("TKB %d -> a car"), Car), TK::FamilyOf(Dis, Car) == TK::EFamily::Car);
	for (const int32 Van : { 1051, 1053 }) TestTrue(FString::Printf(TEXT("TKB %d -> a van"), Van), TK::FamilyOf(Dis, Van) == TK::EFamily::Van);
	TestTrue(TEXT("1052 CityBus -> the bus"), TK::FamilyOf(Dis, 1052) == TK::EFamily::Bus);
	// HROT CE-11042: each kind its own SISO-REF-010 DIS - the category decides, whatever the TypeId
	TestTrue(TEXT("1.1.0.81.43 Sedan -> a car"), TK::FamilyOf(TEXT("1.1.0.81.43.0.0"), 1054) == TK::EFamily::Car);
	TestTrue(TEXT("1.1.0.81.113 Suv -> a car"), TK::FamilyOf(TEXT("1.1.0.81.113.0.0"), 1056) == TK::EFamily::Car);
	TestTrue(TEXT("1.1.0.83.74 DeliveryVan -> a van"), TK::FamilyOf(TEXT("1.1.0.83.74.0.0"), 1051) == TK::EFamily::Van);
	TestTrue(TEXT("1.1.0.83.101 BoxTruck -> a van"), TK::FamilyOf(TEXT("1.1.0.83.101.0.0"), 1053) == TK::EFamily::Van);
	TestTrue(TEXT("1.1.0.82.1 CityBus -> the bus"), TK::FamilyOf(TEXT("1.1.0.82.1.0.0"), 1052) == TK::EFamily::Bus);
	TestTrue(TEXT("a new-form car with a van's TypeId stays a car (the DIS decides)"), TK::FamilyOf(TEXT("1.1.0.81.10.0.0"), 1051) == TK::EFamily::Car);
	TestTrue(TEXT("a HMMWV is not traffic"), TK::FamilyOf(TEXT("1.1.225.6"), 1052) == TK::EFamily::None);
	TestTrue(TEXT("a motorcycle (cat 80) is not a car"), TK::FamilyOf(TEXT("1.1.0.80.1.0.0"), 115) == TK::EFamily::None);
	TestTrue(TEXT("a look-alike prefix is not traffic"), TK::FamilyOf(TEXT("1.1.0.810"), 1050) == TK::EFamily::None);
	// the mover's own pick: id % count, the same car on every channel
	TestEqual(TEXT("pick = id % 4"), TK::Pick(4102, 4), 2); TestEqual(TEXT("no kits"), TK::Pick(5, 0), -1);
	return true;
}
IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotLiveFixPreloadTest, "Hrot.Dps.LiveFix.SoundPreload", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotLiveFixPreloadTest::RunTest(const FString&)
{
	// U-19h: PreloadAll loads every sound of both libraries once, at join (no world needed)
	if (!FPaths::DirectoryExists(FPaths::ProjectContentDir() / TEXT("Hrot/Audio")) && !FPaths::DirectoryExists(FPaths::ProjectContentDir() / TEXT("Hrot/AudioPro")))
	{ AddInfo(TEXT("no sound content here: skipped")); return true; }
	UHrotSoundscape* Snd = NewObject<UHrotSoundscape>(GetTransientPackage());
	const int32 N = Snd->PreloadAll();
	TestTrue(TEXT("loads sounds"), N > 0);
	TestEqual(TEXT("one-shot: a second call finds everything cached"), Snd->PreloadAll(), 0);
	if (N > 0) TestNotNull(TEXT("a set picks from the cache"), Snd->Pick(TEXT("wpn/rifle")));
	return true;
}
// U-19h 20: aircraft are drawn (a kit per HROT aircraft DIS + an Air fallback) and sound (the engine class of each kit)
IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotLiveFixAircraftTest, "Hrot.Dps.LiveFix.Aircraft", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotLiveFixAircraftTest::RunTest(const FString&)
{
	UHrotDpsWorld* W = NewObject<UHrotDpsWorld>(GetTransientPackage());
	W->DefaultLife();
	const TMap<FString, FString> Want = { { TEXT("1.2.225.20"), TEXT("DA_Air_Apache") }, { TEXT("1.2.225.2"), TEXT("DA_Air_F16") }, { TEXT("1.2.225.1"), TEXT("DA_Air_F16") },
		{ TEXT("1.2.222.1"), TEXT("DA_Air_Typhoon") }, { TEXT("1.2.225.4"), TEXT("DA_Air_AC130") }, { TEXT("1.2.225.21"), TEXT("DA_Air_OH1") } };
	for (const TPair<FString, FString>& P : Want)
	{
		const TSoftObjectPtr<UHrotVehicleKit>* K = W->KitsByDis.Find(P.Key);
		if (!TestNotNull(*FString::Printf(TEXT("DIS %s has a kit"), *P.Key), K)) continue;
		TestTrue(*FString::Printf(TEXT("DIS %s -> %s"), *P.Key, *P.Value), K->ToSoftObjectPath().GetAssetName() == P.Value);
		if (FPaths::FileExists(FPaths::ProjectContentDir() / TEXT("Hrot/Vehicles") / (P.Value + TEXT(".uasset"))))
			TestNotNull(*FString::Printf(TEXT("%s loads as a vehicle kit"), *P.Value), K->LoadSynchronous());
	}
	for (const int64 Technical : { 104, 105, 106, 108, 109, 110 })   // U-19h 21: the technicals / pickup are not grey boxes
	{
		const TSoftObjectPtr<UHrotVehicleKit>* K = W->KitsByType.Find(Technical);
		TestTrue(*FString::Printf(TEXT("TKB %lld (technical / pickup) has the truck kit"), Technical), K && K->ToSoftObjectPath().GetAssetName() == TEXT("DA_City_vehicle04_Truck"));
	}
	const TSoftObjectPtr<UHrotVehicleKit>* Air = W->KitsByDomain.Find(TEXT("Air"));
	TestTrue(TEXT("any other aircraft: the F-16 kit"), Air && Air->ToSoftObjectPath().GetAssetName() == TEXT("DA_Air_F16"));
	// the engine voice by kit: rotors, a prop, jets
	TestEqual(TEXT("Apache: rotor"), FString(UHrotSoundscape::EngineName(TEXT("DA_Air_Apache"))), FString(TEXT("Helicopter")));
	TestEqual(TEXT("OH1: rotor"), FString(UHrotSoundscape::EngineName(TEXT("DA_Air_OH1"))), FString(TEXT("Helicopter")));
	TestEqual(TEXT("AC130: prop"), FString(UHrotSoundscape::EngineName(TEXT("DA_Air_AC130"))), FString(TEXT("Propeller")));
	TestEqual(TEXT("F16: jet"), FString(UHrotSoundscape::EngineName(TEXT("DA_Air_F16"))), FString(TEXT("Jet")));
	TestEqual(TEXT("Typhoon: jet"), FString(UHrotSoundscape::EngineName(TEXT("DA_Air_Typhoon"))), FString(TEXT("Jet")));
	TestEqual(TEXT("a Leclerc is still a tracked vehicle"), FString(UHrotSoundscape::EngineName(TEXT("DA_FR_Leclerc"))), FString(TEXT("Tracked")));
	// U-19h 21: the entity's DIS decides before the kit's name - the F-16 kit stands in for the A-10 AND the F-16 (jets), only the C-130 is a prop
	auto Eng = [](const TCHAR* Kit, const TCHAR* Dis) { return FString(UHrotSoundscape::EngineName(Kit, Dis)); };
	TestEqual(TEXT("A-10 on the F-16 kit: jet"), Eng(TEXT("DA_Air_F16"), TEXT("1.2.225.2.0.0.0")), FString(TEXT("Jet")));
	TestEqual(TEXT("F-16: jet"), Eng(TEXT("DA_Air_F16"), TEXT("1.2.225.1.0.0.0")), FString(TEXT("Jet")));
	TestEqual(TEXT("MiG-29 on the Typhoon kit: jet"), Eng(TEXT("DA_Air_Typhoon"), TEXT("1.2.222.1.0.0.0")), FString(TEXT("Jet")));
	TestEqual(TEXT("C-130: prop"), Eng(TEXT("DA_Air_AC130"), TEXT("1.2.225.4.0.0.0")), FString(TEXT("Propeller")));
	TestEqual(TEXT("AH-64: rotor"), Eng(TEXT("DA_Air_Apache"), TEXT("1.2.225.20.0.0.0")), FString(TEXT("Helicopter")));
	TestEqual(TEXT("UH-60 on the OH-1 kit: rotor"), Eng(TEXT("DA_Air_OH1"), TEXT("1.2.225.21.0.0.0")), FString(TEXT("Helicopter")));
	TestEqual(TEXT("an unknown aircraft on any kit: jet, not prop"), Eng(TEXT("DA_Air_AC130"), TEXT("1.2.225.99.0.0.0")), FString(TEXT("Jet")));
	for (const TCHAR* CarDis : { TEXT("1.1.0.81.0.0.0"), TEXT("1.1.0.82.0.0.0"), TEXT("1.1.0.83.0.0.0") })
		TestEqual(*FString::Printf(TEXT("civilian road vehicle %s: car (never a truck / tank), whatever the kit"), CarDis), Eng(TEXT("DA_City_vehicle04_Truck"), CarDis), FString(TEXT("Light")));
	TestEqual(TEXT("HMMWV / technical pickup: jeep"), Eng(TEXT("DA_City_vehicle04_Truck"), TEXT("1.1.225.6.0.0.0")), FString(TEXT("Jeep")));
	TestEqual(TEXT("a T-72 stays tracked (by kit)"), Eng(TEXT("DA_FR_Leclerc"), TEXT("1.1.222.1.0.0.0")), FString(TEXT("Tracked")));
	TestEqual(TEXT("a mover (no DIS): its city car kit is a car"), Eng(TEXT("DA_City_vehicle02_Car"), TEXT("")), FString(TEXT("Light")));
	TestEqual(TEXT("a mover's truck kit is a truck"), Eng(TEXT("DA_City_vehicle04_Truck"), TEXT("")), FString(TEXT("Heavy")));
	TestEqual(TEXT("a mover's bus kit is a truck-class engine"), Eng(TEXT("DA_City_vehicle10_Bus"), TEXT("")), FString(TEXT("Heavy")));
	return true;
}

// U-19h 21: every air kit's NOSE points +X (the way HROT flies). The nose of each hull mesh was measured by its silhouette (scripts/unreal/hrot_air_nose.py: tail fin / canopy /
// tail rotor end); the rail pins the part yaw that puts it on +X, so a kit reset or a re-import is caught.
IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotLiveFixAirNoseTest, "Hrot.Dps.LiveFix.AirNose", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotLiveFixAirNoseTest::RunTest(const FString&)
{
	const TMap<FString, float> Yaw = { { TEXT("DA_Air_Apache"), 180.f }, { TEXT("DA_Air_F16"), 180.f }, { TEXT("DA_Air_AC130"), 0.f }, { TEXT("DA_Air_OH1"), 90.f },
		{ TEXT("DA_Air_F22"), 180.f }, { TEXT("DA_Air_Typhoon"), 0.f } };
	for (const TPair<FString, float>& K : Yaw)
	{
		if (!FPaths::FileExists(FPaths::ProjectContentDir() / TEXT("Hrot/Vehicles") / (K.Key + TEXT(".uasset")))) continue;
		const UHrotVehicleKit* Kit = LoadObject<UHrotVehicleKit>(nullptr, *FString::Printf(TEXT("/Game/Hrot/Vehicles/%s.%s"), *K.Key, *K.Key), nullptr, LOAD_NoWarn | LOAD_Quiet);
		if (!TestNotNull(*K.Key, Kit) || Kit->Parts.Num() == 0) continue;
		const float Got = FRotator::NormalizeAxis(Kit->Parts[0].Transform.Rotator().Yaw);
		TestEqual(*FString::Printf(TEXT("%s: hull part yaw puts the nose on +X"), *K.Key), FMath::Abs(FRotator::NormalizeAxis(Got - K.Value)), 0.0, 0.5);
	}
	return true;
}
IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotLiveFixFalloffTest, "Hrot.Dps.LiveFix.Falloff", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotLiveFixFalloffTest::RunTest(const FString&)
{
	// U-19h (live: a shot across the river sounded as loud as a close one): weapons fall off by inverse distance, -6 dB per doubling
	TestEqual(TEXT("full within the inner radius"), UHrotSoundscape::InverseDistanceGain(5.f, 10.f, 1500.f), 1.f);
	TestEqual(TEXT("half at twice the inner radius"), UHrotSoundscape::InverseDistanceGain(20.f, 10.f, 1500.f), 0.5f);
	TestEqual(TEXT("1/20 at 200 m (-26 dB)"), UHrotSoundscape::InverseDistanceGain(200.f, 10.f, 1500.f), 0.05f);
	TestEqual(TEXT("silent at the reach"), UHrotSoundscape::InverseDistanceGain(1500.f, 10.f, 1500.f), 0.f);
	UHrotSoundscape* Snd = NewObject<UHrotSoundscape>(GetTransientPackage());
	USoundAttenuation* A = Snd->Falloff(10.f, 1500.f);
	TestEqual(TEXT("a custom curve"), A->Attenuation.DistanceAlgorithm, EAttenuationDistanceModel::Custom);
	const float X200 = (200.f - 10.f) / (1500.f - 10.f);
	const float G = A->Attenuation.CustomAttenuationCurve.GetRichCurveConst()->Eval(X200);
	TestTrue(FString::Printf(TEXT("the built curve gives ~0.05 at 200 m (got %.3f)"), G), FMath::Abs(G - 0.05f) < 0.01f);
	USoundAttenuation* Speech = Snd->Falloff(5.f, 40.f, -30.f, true);
	TestEqual(TEXT("speech keeps the natural curve"), Speech->Attenuation.DistanceAlgorithm, EAttenuationDistanceModel::NaturalSound);
	return true;
}
#endif
