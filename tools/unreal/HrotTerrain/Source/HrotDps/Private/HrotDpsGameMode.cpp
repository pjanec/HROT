// CE-4651 (U-18m) — see HrotDpsGameMode.h
#include "HrotDpsGameMode.h"
#include "HrotDpsCameraPawn.h"
#include "HrotDpsHud.h"
#include "HrotDpsWorld.h"
#include "HrotDpsStation.h"
#include "Animation/AnimationAsset.h"
#include "Engine/SkeletalMesh.h"
#include "Misc/CommandLine.h"
#include "Misc/Parse.h"

DEFINE_LOG_CATEGORY_STATIC(LogHrotDpsGame, Log, All);

AHrotDpsGameMode::AHrotDpsGameMode()
{
	DefaultPawnClass = AHrotDpsCamera::StaticClass();
	HUDClass = AHrotDpsHud::StaticClass();
	PersonKit = TSoftObjectPtr<UHrotPersonKit>(FSoftObjectPath(TEXT("/Game/Hrot/People/DA_Person_USSoldier.DA_Person_USSoldier")));
	for (int32 I = 0; I <= 28; I += 4)
	{
		const FString N = FString::Printf(TEXT("DA_Person_Civ_%02d"), I);
		CivilianKits.Add(TSoftObjectPtr<UHrotPersonKit>(FSoftObjectPath(TEXT("/Game/Hrot/People/") + N + TEXT(".") + N)));
	}
	DefaultMissileKind = TSoftObjectPtr<UHrotMissileKind>(FSoftObjectPath(TEXT("/Game/Hrot/Missiles/DA_Missile_RPG7.DA_Missile_RPG7")));
	PersonMesh = TSoftObjectPtr<USkeletalMesh>(FSoftObjectPath(TEXT("/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny.SKM_Manny")));
	PersonClip = TSoftObjectPtr<UAnimationAsset>(FSoftObjectPath(TEXT("/Game/Characters/Heroes/Mannequin/Animations/Locomotion/Rifle/MF_Rifle_Walk_Fwd.MF_Rifle_Walk_Fwd")));
}

void AHrotDpsGameMode::StartPlay()
{
	Super::StartPlay();
	UHrotDpsWorld* W = UHrotDpsWorld::Get(this);
	if (!W) return;
	// the look, where the world has none of its own
	if (W->PersonKit.IsNull()) W->PersonKit = PersonKit;
	if (W->CivilianKits.Num() == 0) W->CivilianKits = CivilianKits;
	if (W->DefaultMissileKind.IsNull()) W->DefaultMissileKind = DefaultMissileKind;
	if (W->PersonMesh.IsNull()) W->PersonMesh = PersonMesh;
	if (W->PersonClip.IsNull()) W->PersonClip = PersonClip;
	int32 D = Domain; FString P = Partition;
	const bool bD = FParse::Value(FCommandLine::Get(), TEXT("HrotDpsDomain="), D);
	const bool bP = FParse::Value(FCommandLine::Get(), TEXT("HrotDpsPartition="), P);
	if (!bJoinOnPlay && !bD && !bP) return;
	if (W->Start(D, P)) { UE_LOG(LogHrotDpsGame, Display, TEXT("HROT IG: joined the station, domain %d partition '%s'"), D, *P); }
	else { UE_LOG(LogHrotDpsGame, Error, TEXT("HROT IG: could not join domain %d partition '%s'"), D, *P); }
}
