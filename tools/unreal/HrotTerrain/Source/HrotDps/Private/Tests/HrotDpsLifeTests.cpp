// CE-4651 (U-18h) — rails for HrotDpsLife: animals, the flock, the traffic track, the flame (ports of HROT's / the gateway's rules).
// Run: UnrealEditor-Cmd <project> -ExecCmds="Automation RunTests Hrot.Dps.Life; Quit" -nullrhi -unattended
#include "Misc/AutomationTest.h"
#include "HrotDpsLife.h"

#if WITH_DEV_AUTOMATION_TESTS
using namespace HrotDpsLife;

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotDpsLifeAnimalTest, "Hrot.Dps.Life.Animal",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotDpsLifeAnimalTest::RunTest(const FString&)
{
	// the family from the DIS type, as the showcase sends it (measured live: cattle 3.1.0.200.1.0.0, horse 3.1.0.202.2.0.0, birds 3.2.0.205.2.0.0)
	TestEqual(TEXT("cattle"), FamilyOfDis(TEXT("3.1.0.200.1.0.0")), EFamily::Cattle);
	TestEqual(TEXT("horse"), FamilyOfDis(TEXT("3.1.0.202.2.0.0")), EFamily::Horse);
	TestEqual(TEXT("dog"), FamilyOfDis(TEXT("3.1.0.203.1.0.0")), EFamily::Dog);
	TestEqual(TEXT("birds"), FamilyOfDis(TEXT("3.2.0.205.2.0.0")), EFamily::Bird);
	TestEqual(TEXT("rabbit"), FamilyOfDis(TEXT("3.1.0.209.0.0.0")), EFamily::Rabbit);
	TestEqual(TEXT("not an animal"), FamilyOfDis(TEXT("1.1.225.1.0.0.0")), EFamily::Unknown);

	FAnimalIn In; In.Family = EFamily::Horse; In.Mode = ModeCalm;
	TestEqual(TEXT("standing"), AnimalShown(In).Shown, EShown::Idle);
	In.Speed = 0.14f; TestEqual(TEXT("below 0.15 m/s stands"), AnimalShown(In).Shown, EShown::Idle);
	In.Speed = 1.5f; TestEqual(TEXT("a horse walks at 1.5"), AnimalShown(In).Shown, EShown::Walk);
	In.Speed = 4.8f; TestEqual(TEXT("still the walk clip at the trot top"), AnimalShown(In).Shown, EShown::Walk);
	In.Speed = 6.f; TestEqual(TEXT("canters: the run clip"), AnimalShown(In).Shown, EShown::Run);
	In.Family = EFamily::Sheep; In.Speed = 4.f; TestEqual(TEXT("a sheep runs at 4 (its trot top is 3.0)"), AnimalShown(In).Shown, EShown::Run);
	In.Mode = ModeRest; In.Speed = 0; TestEqual(TEXT("rest lies down"), AnimalShown(In).Shown, EShown::Rest);
	In.Family = EFamily::Dog; In.Mode = ModeGuard; TestEqual(TEXT("guarding, not attacking"), AnimalShown(In).Shown, EShown::Idle);
	In.bDangerous = true; TestEqual(TEXT("attacking"), AnimalShown(In).Shown, EShown::Attack);
	In.bDead = true; TestEqual(TEXT("dead beats everything"), AnimalShown(In).Shown, EShown::Dead);
	FAnimalIn Bird; Bird.Family = EFamily::Bird; Bird.bAirborne = true; Bird.Speed = 8;
	TestEqual(TEXT("a bird in the air flies"), AnimalShown(Bird).Shown, EShown::Fly);
	Bird.bAirborne = false; Bird.Speed = 0; TestEqual(TEXT("a landed bird stands"), AnimalShown(Bird).Shown, EShown::Idle);
	FAnimalIn Cow; Cow.Family = EFamily::Cattle; Cow.bAirborne = true; Cow.Speed = 2;
	TestEqual(TEXT("only flyers fly"), AnimalShown(Cow).Shown, EShown::Walk);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotDpsLifeFlockTest, "Hrot.Dps.Life.Flock",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotDpsLifeFlockTest::RunTest(const FString&)
{
	TestEqual(TEXT("radius of 16"), FlockRadius(16), 4.5f);
	// closed form: the same inputs, the same bird (a seek, a replay, a second channel)
	TestEqual(TEXT("deterministic"), FlockOffset(1234, 7, 10.0, FlockFlying, 30), FlockOffset(1234, 7, 10.0, FlockFlying, 30));
	TestNotEqual(TEXT("birds differ"), FlockOffset(1234, 7, 10.0, FlockFlying, 30), FlockOffset(1234, 8, 10.0, FlockFlying, 30));
	// landed: on the ground, inside 0.7 R
	for (int32 I = 0; I < 50; ++I)
	{
		const FVector3f O = FlockOffset(99, I, 3.0, FlockLanded, 30);
		TestEqual(TEXT("landed on the ground"), O.Z, 0.f);
		TestTrue(TEXT("landed inside 0.7 R"), FVector2f(O.X, O.Y).Size() <= FlockRadius(30) * 0.7f + 1e-4f);
	}
	// flying: a loose cloud a few metres across, drifting in time
	const FVector3f A = FlockOffset(5, 3, 0.0, FlockFlying, 30), B = FlockOffset(5, 3, 2.0, FlockFlying, 30);
	TestTrue(TEXT("drifts"), (A - B).Size() > 1e-3f);
	TestTrue(TEXT("inside the cloud"), FMath::Abs(A.X) < FlockRadius(30) * 1.4f && FMath::Abs(A.Y) < FlockRadius(30) * 1.3f);
	TestTrue(TEXT("a flyer's yaw is a few degrees"), FMath::Abs(FlockYaw(5, 3, 1.0, FlockFlying)) <= 0.35f + 1e-5f);
	TestEqual(TEXT("perched crown height in 0..1.5"), FMath::IsWithinInclusive(FlockOffset(1, 0, 0.0, FlockPerched, 10).Z, 0.f, 1.5f), true);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotDpsLifeTrackTest, "Hrot.Dps.Life.Track",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotDpsLifeTrackTest::RunTest(const FString&)
{
	// a car going east at 10 m/s, knots 1 s apart: the Hermite reproduces the straight line exactly
	FKnot A; A.T = 10; A.P = FVector3d(0, 0, 0); A.Heading = 0; A.Speed = 10;
	FKnot B = A; B.T = 11; B.P = FVector3d(10, 0, 0);
	TestTrue(TEXT("at a knot"), Between(A, B, 10.0).P.Equals(A.P, 1e-9));
	TestTrue(TEXT("midway on the line"), Between(A, B, 10.5).P.Equals(FVector3d(5, 0, 0), 1e-9));
	TestTrue(TEXT("at b"), Between(A, B, 11.0).P.Equals(B.P, 1e-9));
	// past the newest knot: straight on along its velocity
	TArray<FKnot> K = { A, B };
	FTrackPose P;
	TestTrue(TEXT("pose"), PoseAt(K, 11.5, P));
	TestTrue(TEXT("extrapolated"), P.P.Equals(FVector3d(15, 0, 0), 1e-9));
	TestTrue(TEXT("before the first: the first"), PoseAt(K, 0.0, P) && P.P.Equals(A.P, 1e-9));
	// the heading turns the short way across ±pi
	FKnot C = A; C.Heading = FMath::DegreesToRadians(170.0);
	FKnot D = A; D.T = 11; D.Heading = FMath::DegreesToRadians(-170.0);
	TestEqual(TEXT("short way"), FMath::RadiansToDegrees(WrapPi(Between(C, D, 10.5).Heading)), 180.0, 1e-6);
	// a north-going car (heading 90°): its velocity is +y
	FKnot N = A; N.Heading = UE_DOUBLE_PI / 2;
	TestTrue(TEXT("heading 90 = north"), N.Velocity().Equals(FVector3d(0, 10, 0), 1e-9));
	// trimming keeps two
	TArray<FKnot> Long; for (int32 I = 0; I < 20; ++I) { FKnot X = A; X.T = I; Long.Add(X); }
	TrimBefore(Long, 19.0);
	TestEqual(TEXT("trimmed to 5 s behind"), Long[0].T, 13.0);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotDpsLifeFlameTest, "Hrot.Dps.Life.Flame",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotDpsLifeFlameTest::RunTest(const FString&)
{
	TestEqual(TEXT("small spot clamps to 0.3"), SpotSize(0.1f), 0.3f);
	TestEqual(TEXT("2 m spot = 1.4 m flames"), SpotSize(2.f), 1.4f, 1e-6f);
	TestEqual(TEXT("big spot clamps to 8"), SpotSize(30.f), 8.f);
	TestEqual(TEXT("full through 70 %"), SpotStrength(0.8f, 100.0, 10.f, 106.9), 0.8f);
	TestEqual(TEXT("half at 85 %"), SpotStrength(1.f, 100.0, 10.f, 108.5), 0.5f, 1e-4f);
	TestEqual(TEXT("burnt out"), SpotStrength(1.f, 100.0, 10.f, 110.0), 0.f);
	TestEqual(TEXT("no burn time: listed = burning"), SpotStrength(0.6f, 100.0, 0.f, 500.0), 0.6f);
	TestEqual(TEXT("jet to a near aim"), JetLength(12.f, 30.f), 12.f);
	TestEqual(TEXT("jet capped by its range"), JetLength(60.f, 30.f), 30.f);
	TestEqual(TEXT("no aim: full range"), JetLength(0.2f, 30.f), 30.f);
	return true;
}
#endif
