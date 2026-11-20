// CE-4651 (U-18m) — THE IG AS A GAME: Play (PIE, standalone) puts you behind AHrotDpsCamera (free flight, select, the unit menu, follow,
// possess) with AHrotDpsHud, and joins the DPS station. Set it as a level's GameMode Override (World Settings) or pass
// ?game=/Script/HrotDps.HrotDpsGameMode. The station: Domain / Partition below (the gateway's `dps` section; its defaults are domain 0 and
// the empty partition), overridden by -HrotDpsDomain=<n> -HrotDpsPartition=<name> on the command line.
// The look: the content the IG draws with (person kit, civilian kits, the default missile, the stand-in body) applied to UHrotDpsWorld where
// the world has none of its own — the same set the scene harness uses.
#pragma once

#include "CoreMinimal.h"
#include "GameFramework/GameModeBase.h"
#include "HrotDpsGameMode.generated.h"

UCLASS()
class HROTDPS_API AHrotDpsGameMode : public AGameModeBase
{
	GENERATED_BODY()
public:
	AHrotDpsGameMode();
	virtual void StartPlay() override;

	/** join the station when play starts */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") bool bJoinOnPlay = true;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") int32 Domain = 0;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") FString Partition;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") TSoftObjectPtr<class UHrotPersonKit> PersonKit;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") TArray<TSoftObjectPtr<class UHrotPersonKit>> CivilianKits;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") TSoftObjectPtr<class UHrotMissileKind> DefaultMissileKind;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") TSoftObjectPtr<USkeletalMesh> PersonMesh;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") TSoftObjectPtr<UAnimationAsset> PersonClip;
};
