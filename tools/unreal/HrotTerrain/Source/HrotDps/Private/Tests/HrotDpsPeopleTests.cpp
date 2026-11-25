// CE-4651 (U-18d) — rails for HrotDpsPeople: the look from PersonState and HROT's aim rule.
// Run: UnrealEditor-Cmd <project> -ExecCmds="Automation RunTests Hrot.Dps.People; Quit" -nullrhi -unattended
#include "Misc/AutomationTest.h"
#include "HrotDpsPeople.h"
#include "HrotDpsWorld.h"
#include "HrotPersonKit.h"
#include "HrotPersonAnimInstance.h"
#include "HrotRuntimeLibrary.h"
#include "Engine/World.h"
#include "Engine/Engine.h"
#include "Components/SkeletalMeshComponent.h"

#if WITH_DEV_AUTOMATION_TESTS
using namespace HrotDpsPeople;

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotDpsPeopleLookTest, "Hrot.Dps.People.Look",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotDpsPeopleLookTest::RunTest(const FString&)
{
	FIn In; In.Held = HeldWeapon; In.Now = 100;
	TestEqual(TEXT("armed, still"), Look(In).Base, FName(TEXT("Rifle.Stand.Idle")));
	In.Speed = 1.4f; TestEqual(TEXT("walking"), Look(In).Base, FName(TEXT("Rifle.Stand.Walk")));
	In.Speed = 4.f; TestEqual(TEXT("running"), Look(In).Base, FName(TEXT("Rifle.Stand.Run")));
	In.Speed = 2.75f; In.LastGait = EGait::Run; TestEqual(TEXT("hysteresis keeps the run"), Look(In).Gait, EGait::Run);
	In.LastGait = EGait::Walk; TestEqual(TEXT("hysteresis keeps the walk"), Look(In).Gait, EGait::Walk);
	In.Speed = 4.f; In.Stance = In.TargetStance = Crouched; TestEqual(TEXT("a crouch has no run"), Look(In).Base, FName(TEXT("Rifle.Crouch.Walk")));
	In.Speed = 0;

	// posture on the upper body; prone holds the rifle in its own clips
	In.Stance = In.TargetStance = Standing; In.Posture = Aimed;
	TestEqual(TEXT("aimed upper"), Look(In).Upper, FName(TEXT("Upper.Aimed"))); TestTrue(TEXT("may aim"), Look(In).bCanAim);
	In.Posture = Ready; TestEqual(TEXT("ready upper"), Look(In).Upper, FName(TEXT("Upper.Ready"))); TestFalse(TEXT("ready does not aim"), Look(In).bCanAim);
	In.Posture = Aimed; In.Stance = In.TargetStance = Prone;
	TestTrue(TEXT("prone: no upper layer"), Look(In).Upper.IsNone()); TestFalse(TEXT("prone: no twist"), Look(In).bCanAim);

	// ⭐ a stated transition (DD-020, round 6): crouched -> prone over 1.2 s from 100.0
	In.Stance = Crouched; In.StanceFrom = Crouched; In.TargetStance = Prone; In.StanceChangedAt = 100.0; In.StanceSeconds = 1.2f;
	In.Now = 100.5; FLook L = Look(In);
	TestTrue(TEXT("inside the transition"), L.bTransition);
	TestEqual(TEXT("its own clip"), L.Base, FName(TEXT("Transition.CrouchToProne"))); TestFalse(TEXT("played once"), L.bLoops);
	TestEqual(TEXT("its time left"), L.TransitionLeft, 0.7, 1e-5);   // StanceSeconds is a float
	In.Now = 101.3; L = Look(In);
	TestFalse(TEXT("after it"), L.bTransition); TestEqual(TEXT("prone idle"), L.Base, FName(TEXT("Rifle.Prone.Idle")));
	// standing -> crouched: a crossfade as long as the transition
	In.StanceFrom = Standing; In.TargetStance = Crouched; In.Now = 100.2; L = Look(In);
	TestEqual(TEXT("crouch clip at once"), L.Base, FName(TEXT("Rifle.Crouch.Idle"))); TestEqual(TEXT("crossfade = the transition"), L.BlendTime, 1.2f);
	In.StanceChangedAt = 0; In.Stance = In.TargetStance = In.StanceFrom = Standing;

	// down
	In.Injury = InjuryDead; In.Stance = In.TargetStance = Crouched; L = Look(In);
	TestEqual(TEXT("dead from a crouch"), L.Base, FName(TEXT("Death.Crouch"))); TestFalse(TEXT("once, held"), L.bLoops); TestFalse(TEXT("dead: no twist"), L.bCanAim);
	In.Injury = InjuryIncapacitated; TestEqual(TEXT("U-19g: incapacitated from a crouch lies down crouched"), Look(In).Base, FName(TEXT("Death.Crouch")));
	In.Stance = In.TargetStance = Prone; TestEqual(TEXT("U-19g: incapacitated prone: the prone death, not the standing fall"), Look(In).Base, FName(TEXT("Death.Prone")));
	In.Injury = InjuryDead; TestEqual(TEXT("U-19g: dead after incapacitated, prone: the same base (no replay)"), Look(In).Base, FName(TEXT("Death.Prone")));
	In.Stance = In.TargetStance = Standing; In.Injury = InjuryIncapacitated; TestEqual(TEXT("incapacitated standing: dying"), Look(In).Base, FName(TEXT("Dying")));
	In.Injury = InjuryOk; In.Stance = In.TargetStance = Standing;

	// unarmed business; inside a vehicle
	In.Held = HeldPhone; In.Posture = AtEase; TestEqual(TEXT("the phone"), Look(In).Base, FName(TEXT("Phone"))); TestFalse(TEXT("no rifle"), Look(In).bWeapon);
	In.Held = HeldNone; In.Gesture = GestureWaving; TestEqual(TEXT("a wave"), Look(In).Base, FName(TEXT("Gesture.Wave")));
	In.Gesture = 0; In.bInSeat = true; TestTrue(TEXT("seated inside: hidden"), Look(In).bHidden);
	In.bSeatExposed = true; TestFalse(TEXT("head out: shown"), Look(In).bHidden);

	// U-19h 20: the gait rate = ground speed / the clip's own, so the feet match the ground
	TestEqual(TEXT("at the clip's own speed: 1"), GaitRate(3.0f, 3.0f), 1.f, 1e-6f);
	TestEqual(TEXT("half the speed, half the rate (no 0.6 floor)"), GaitRate(0.5f, 1.0f), 0.5f, 1e-6f);
	TestEqual(TEXT("a crawl-slow walker is still slow"), GaitRate(0.3f, 3.0f), 0.15f, 1e-6f);
	TestEqual(TEXT("a sprint is capped"), GaitRate(20.f, 3.0f), MaxGaitRate, 1e-6f);
	TestEqual(TEXT("a zero clip speed does not divide by zero"), GaitRate(1.f, 0.f), MaxGaitRate, 1e-6f);

	// fallbacks end at a clip every body has
	TestEqual(TEXT("run -> walk"), Fallback(TEXT("Rifle.Prone.Run")), FName(TEXT("Rifle.Prone.Walk")));
	TestEqual(TEXT("rifle -> unarmed"), Fallback(TEXT("Rifle.Prone.Walk")), FName(TEXT("Unarmed.Prone.Walk")));
	TestEqual(TEXT("a death -> the standing death"), Fallback(TEXT("Death.Prone")), FName(TEXT("Death.Stand")));
	TestEqual(TEXT("U-19h: no standing death -> dying"), Fallback(TEXT("Death.Stand")), FName(TEXT("Dying")));
	// U-19h: a civilian kit has ONLY "Dying": every death (and dying itself) resolves to a clip that exists, and a kit with nothing ends without looping
	{
		auto OnlyDying = [](FName R) { return R == FName(TEXT("Dying")); };
		for (const TCHAR* Role : { TEXT("Death.Stand"), TEXT("Death.Crouch"), TEXT("Death.Prone"), TEXT("Dying") })
			TestEqual(FString::Printf(TEXT("civilian kit: %s -> Dying"), Role), ResolveRole(FName(Role), OnlyDying), FName(TEXT("Dying")));
		auto OnlyStand = [](FName R) { return R == FName(TEXT("Death.Stand")); };   // the soldier kit has Death.* but no Dying
		TestEqual(TEXT("soldier kit: incapacitated standing -> Death.Stand"), ResolveRole(TEXT("Dying"), OnlyStand), FName(TEXT("Death.Stand")));
		TestEqual(TEXT("a kit with no death at all ends"), ResolveRole(TEXT("Death.Prone"), [](FName) { return false; }), NAME_None);
	}
	return true;
}

// U-19h: a soldier's body (the kit's Blueprint: a leader + 15 skinned parts) merged into ONE skinned mesh: one component renders, the weapon / head sockets and
// the side-colour slot names are still there. Skipped where the content is missing.
IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotDpsPeopleMergeTest, "Hrot.Dps.People.Merge",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotDpsPeopleMergeTest::RunTest(const FString&)
{
	UHrotPersonKit* K = LoadObject<UHrotPersonKit>(nullptr, TEXT("/Game/Hrot/People/DA_Person_USSoldier.DA_Person_USSoldier"), nullptr, LOAD_NoWarn | LOAD_Quiet);
	UClass* Cls = K ? K->BodyClass.LoadSynchronous() : nullptr;
	if (!Cls) { AddInfo(TEXT("the soldier kit is not in this project: skipped")); return true; }
	UWorld* W = UWorld::CreateWorld(EWorldType::Game, false);
	FWorldContext& Ctx = GEngine->CreateNewWorldContext(EWorldType::Game);
	Ctx.SetCurrentWorld(W);
	W->InitializeActorsForPlay(FURL());
	AActor* A = W->SpawnActor<AActor>(Cls, FTransform::Identity);
	if (TestNotNull(TEXT("the body spawns"), A))
	{
		UHrotDpsWorld::HideKitComponents(A, K);
		TArray<USkeletalMeshComponent*> Before; A->GetComponents(Before);
		int32 Visible = 0; for (const USkeletalMeshComponent* M : Before) if (M->GetSkinnedAsset() && M->IsVisible()) ++Visible;
		AddInfo(FString::Printf(TEXT("before: %d skinned components, %d visible"), Before.Num(), Visible));
		UHrotPersonState* St = UHrotRuntimeLibrary::AddPersonState(A);
		St->WeaponSocket = K->WeaponSocket;
		for (const TPair<FString, TObjectPtr<UMaterialInterface>>& O : K->SideMaterials) { for (const FName& S : K->SideSlots) St->MaterialOverrides.Add(S, O.Value); break; }
		const double T0 = FPlatformTime::Seconds();
		const int32 Merged = St->MergeBodyParts();
		AddInfo(FString::Printf(TEXT("merge took %.2f s (first, cold cache)"), FPlatformTime::Seconds() - T0));
		TestTrue(TEXT("parts were merged"), Merged > 1);
		TArray<USkeletalMeshComponent*> After; A->GetComponents(After);
		int32 Rendering = 0; for (const USkeletalMeshComponent* M : After) if (M->GetSkinnedAsset() && M->IsVisible()) ++Rendering;
		TestEqual(TEXT("exactly ONE skinned component renders"), Rendering, 1);
		for (const USkeletalMeshComponent* M : After) AddInfo(FString::Printf(TEXT("left: %s asset %s visible %d children %d parent %s"), *M->GetName(), M->GetSkinnedAsset() ? *M->GetSkinnedAsset()->GetName() : TEXT("none"), M->IsVisible() ? 1 : 0, M->GetAttachChildren().Num(), M->GetAttachParent() ? *M->GetAttachParent()->GetName() : TEXT("-")));
		int32 WithMesh = 0; for (const USkeletalMeshComponent* M : After) if (M->GetSkinnedAsset()) ++WithMesh;
		TestEqual(TEXT("the merged-in parts are gone, not hidden (the Character's empty default mesh has no asset: no render state)"), WithMesh, 1);
		if (USkeletalMeshComponent* L = St->FindLeader())
		{
			TestTrue(TEXT("the weapon socket is on the merged body"), L->DoesSocketExist(K->WeaponSocket));
			TestTrue(TEXT("the head socket is on the merged body"), L->DoesSocketExist(TEXT("head")));
			const TArray<FName> Slots = L->GetMaterialSlotNames();
			FString All; for (const FName& N : Slots) All += N.ToString() + TEXT(" ");
			AddInfo(FString::Printf(TEXT("merged: %d material slots: %s"), Slots.Num(), *All));
			for (const FName& S : K->SideSlots) TestTrue(FString::Printf(TEXT("side slot %s survives"), *S.ToString()), Slots.Contains(S));
		}
		else AddError(TEXT("no leader after the merge"));
		A->Destroy();
	}
	GEngine->DestroyWorldContext(W);
	W->DestroyWorld(false);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotDpsPeopleAimTest, "Hrot.Dps.People.Aim",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotDpsPeopleAimTest::RunTest(const FString&)
{
	const FQuat4d North(FVector3d::ZAxisVector, UE_DOUBLE_HALF_PI);   // facing +y (north)
	double Twist, Pitch;
	// a target 10 m to his left (west) at shoulder height: twist +45? no — straight left is 90°, clamped to 75
	TestTrue(TEXT("aims"), Aim(FVector3d(0, 0, 0), North, Standing, FVector3d(-10, 0, ShoulderStanding), Twist, Pitch));
	TestEqual(TEXT("left clamped to 75"), Twist, 75.0, 1e-9); TestEqual(TEXT("level"), Pitch, 0.0, 1e-9);
	// 10 m ahead, 10 m to the right: -45°; from the crouched shoulder down to the ground
	Aim(FVector3d(0, 0, 0), North, Crouched, FVector3d(10, 10, 0), Twist, Pitch);
	TestEqual(TEXT("right is negative"), Twist, -45.0, 1e-9);
	TestEqual(TEXT("down from the crouched shoulder"), Pitch, -FMath::RadiansToDegrees(FMath::Atan2(ShoulderCrouched, FMath::Sqrt(200.0))), 1e-9);
	TestFalse(TEXT("no aim straight above"), Aim(FVector3d(0, 0, 0), North, Standing, FVector3d(0.05, 0, 5), Twist, Pitch));
	TestFalse(TEXT("no aim prone"), Aim(FVector3d(0, 0, 0), North, Prone, FVector3d(0, 10, 0), Twist, Pitch));
	// the ease: 0.12 s time constant — after one tau 63 % of the way
	TestEqual(TEXT("one tau"), Ease(0, 10, AimTau), 10 * (1 - FMath::Exp(-1.0)), 1e-9);
	return true;
}
#endif
