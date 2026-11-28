// CE-4651 (U-18m) — see HrotDpsGameMode.h
#include "HrotDpsGameMode.h"
#include "HrotDpsCameraPawn.h"
#include "HrotDpsHud.h"
#include "HrotDpsWorld.h"
#include "HrotDpsStation.h"
#include "HrotDpsSubsystem.h"
#include "Engine/Engine.h"
#include "Animation/AnimationAsset.h"
#include "Engine/SkeletalMesh.h"
#include "Misc/CommandLine.h"
#include "Misc/Parse.h"
#include "TimerManager.h"
#include "Kismet/GameplayStatics.h"
#include "Kismet/KismetSystemLibrary.h"
#include "HAL/IConsoleManager.h"
#include "Components/HierarchicalInstancedStaticMeshComponent.h"
#include "Engine/StaticMesh.h"
#include "EngineUtils.h"
#include "Materials/MaterialInterface.h"
#include "Materials/Material.h"

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
	// U-18r: a level loaded for the station's terrain rejoins the same station (the participant was kept across the load)
	UHrotDpsSubsystem* Dps = GEngine ? GEngine->GetEngineSubsystem<UHrotDpsSubsystem>() : nullptr;
	const bool bTravel = Dps && Dps->IsTraveling() && Dps->IsRunning();
	if (bTravel && !bD && !bP) { D = int32(Dps->Client().GetDomain()); P = Dps->Client().GetPartition(); }
	if (!bJoinOnPlay && !bD && !bP && !bTravel) return;
	if (W->Start(D, P)) { UE_LOG(LogHrotDpsGame, Display, TEXT("HROT IG: joined the station, domain %d partition '%s'"), D, *P); }
	else { UE_LOG(LogHrotDpsGame, Error, TEXT("HROT IG: could not join domain %d partition '%s'"), D, *P); }
	// U-18t: a terrain by name (a preview / a check without a station), and the camera
	int32 Labels = 0;
	if (FParse::Value(FCommandLine::Get(), TEXT("HrotIgLabels="), Labels)) W->LabelMode = FMath::Clamp(Labels, 1, 3);   // U-18y: 1 off, 2 commanders, 3 every unit (R-143)
	if (FParse::Param(FCommandLine::Get(), TEXT("HrotIgWireCards"))) W->bWireCards = true;   // HROT's own cards over gizmos (DD-024), optional
	FString Terrain;
	if (FParse::Value(FCommandLine::Get(), TEXT("HrotIgTerrain="), Terrain) && !Terrain.IsEmpty()) W->ShowTerrain(Terrain);
	FString View;
	if (FParse::Value(FCommandLine::Get(), TEXT("HrotIgView="), View, false))
	{
		TArray<FString> V; View.ParseIntoArray(V, TEXT(","));
		if (V.Num() == 5)
		{
			FTimerHandle H;
			const FVector At(FCString::Atod(*V[0]), FCString::Atod(*V[1]), FCString::Atod(*V[2]));
			const FRotator Rot(FCString::Atod(*V[3]), FCString::Atod(*V[4]), 0.0);
			GetWorldTimerManager().SetTimer(H, FTimerDelegate::CreateWeakLambda(this, [this, At, Rot]()
			{
				if (AHrotDpsCamera* Cam = Cast<AHrotDpsCamera>(UGameplayStatics::GetPlayerPawn(this, 0)))
				{
					Cam->PlaceAt(At, Rot.Quaternion(), 90.f);
					UE_LOG(LogHrotDpsGame, Display, TEXT("HROT IG: camera at %s looking %s"), *At.ToString(), *Rot.ToString());
				}
			}), 0.5f, false);
		}
	}
	// U-18s: the packaged client's own report (no Python there)
	float Every = 0, ShotAt = 0, QuitAt = 0;
	FTimerManager& T = GetWorldTimerManager();
	if (FParse::Value(FCommandLine::Get(), TEXT("HrotIgStatusEvery="), Every) && Every > 0) T.SetTimer(StatusTimer, this, &AHrotDpsGameMode::LogStatus, Every, true);
	if (FParse::Value(FCommandLine::Get(), TEXT("HrotIgShotAt="), ShotAt) && ShotAt > 0)
		T.SetTimer(ShotTimer, FTimerDelegate::CreateWeakLambda(this, [this]()
		{
			LogStatus();
			UKismetSystemLibrary::ExecuteConsoleCommand(this, TEXT("HighResShot 1"));
			UE_LOG(LogHrotDpsGame, Display, TEXT("HROT IG: screenshot taken (Saved/Screenshots)"));
		}), ShotAt, false);
	// U-18y/z: a live check — the gizmo layers to ask for, the station's suggested view, a screenshot every N s
	FString Gizmos;
	if (FParse::Value(FCommandLine::Get(), TEXT("HrotIgGizmos="), Gizmos, false) && !Gizmos.IsEmpty())
		if (UHrotDpsWorld* GW = UHrotDpsWorld::Get(this)) { TArray<FString> L; Gizmos.ParseIntoArray(L, TEXT(",")); GW->SetGizmoLayers(L); }
	if (FParse::Param(FCommandLine::Get(), TEXT("HrotIgDirector")))
	{
		FTimerHandle H;
		T.SetTimer(H, FTimerDelegate::CreateWeakLambda(this, [this]()
		{
			if (AHrotDpsCamera* Cam = Cast<AHrotDpsCamera>(UGameplayStatics::GetPlayerPawn(this, 0))) Cam->Director();
		}), 1.0f, false);
	}
	int32 FollowId = 0;
	if (FParse::Value(FCommandLine::Get(), TEXT("HrotIgFollow="), FollowId) && FollowId > 0)   // U-19g: follow THIS entity as soon as it exists
	{
		FTimerHandle H;
		TSharedRef<bool> bDone = MakeShared<bool>(false);
		T.SetTimer(H, FTimerDelegate::CreateWeakLambda(this, [this, bDone, FollowId]()
		{
			if (*bDone) return;
			AHrotDpsCamera* Cam = Cast<AHrotDpsCamera>(UGameplayStatics::GetPlayerPawn(this, 0));
			const UHrotDpsWorld* FW = UHrotDpsWorld::Get(this);
			// it must have a POSE: following an id before its first state frees the camera at once (measured live 2026-11-22: unit 1003 followed, then
			// free flight, so the listener was on no net and Brown's radio stayed in the world)
			FVector P; FQuat Q; int32 Dom = 0;
			if (!Cam || !FW || !FW->PoseNow(FollowId, P, Q, Dom)) return;
			Cam->Follow(FollowId, 40.f); *bDone = true;
			UE_LOG(LogHrotDpsGame, Display, TEXT("HROT IG: following entity %d (-HrotIgFollow)"), FollowId);
		}), 1.0f, true);
	}
	if (FParse::Param(FCommandLine::Get(), TEXT("HrotIgFollowFirst")))   // a live check without a station view: follow the first unit with a body
	{
		FTimerHandle H;
		TSharedRef<bool> bDone = MakeShared<bool>(false);
		T.SetTimer(H, FTimerDelegate::CreateWeakLambda(this, [this, bDone]()
		{
			if (*bDone) return;
			AHrotDpsCamera* Cam = Cast<AHrotDpsCamera>(UGameplayStatics::GetPlayerPawn(this, 0));
			const UHrotDpsWorld* FW = UHrotDpsWorld::Get(this);
			if (!Cam || !FW) return;
			for (int32 Id : FW->EntityIds()) if (FW->ActorOf(Id)) { Cam->Follow(Id, 40.f); *bDone = true; UE_LOG(LogHrotDpsGame, Display, TEXT("HROT IG: following entity %d"), Id); return; }
		}), 4.0f, true);   // again every 4 s until there is one (a level loads, entities arrive later)
	}
	float ShotEvery = 0;
	if (FParse::Value(FCommandLine::Get(), TEXT("HrotIgShotEvery="), ShotEvery) && ShotEvery > 0)
		T.SetTimer(ShotEveryTimer, FTimerDelegate::CreateWeakLambda(this, [this]()
		{
			LogStatus();
			UKismetSystemLibrary::ExecuteConsoleCommand(this, TEXT("HighResShot 1"));
		}), ShotEvery, true);
	if (FParse::Value(FCommandLine::Get(), TEXT("HrotIgQuitAt="), QuitAt) && QuitAt > 0)
		T.SetTimer(QuitTimer, FTimerDelegate::CreateWeakLambda(this, [this]() { LogStatus(); UKismetSystemLibrary::QuitGame(this, nullptr, EQuitPreference::Quit, false); }), QuitAt, false);
}

void AHrotDpsGameMode::EndPlay(const EEndPlayReason::Type Why)
{
	GetWorldTimerManager().ClearAllTimersForObject(this);
	Super::EndPlay(Why);
}

void AHrotDpsGameMode::LogStatus()
{
	const UHrotDpsWorld* W = UHrotDpsWorld::Get(this);
	if (!W) return;
	UE_LOG(LogHrotDpsGame, Display, TEXT("HROT IG status: level %s | %d entities, %d movers, %d spawned | terrain '%s' | ClientWorld %s | clock %s | %s | events %d kinds"),
		*GetWorld()->GetMapName(), W->EntityIds().Num(), W->MoversKnown(), W->SpawnedTotal(), *W->StationTerrain(), *W->DescribeClientWorld(), *W->DescribeClock(),
		*(W->DescribeLabels() + TEXT(" | ") + W->DescribeGizmos()), W->EventCounts().Num());
	// live 2026-11-26 (Stechovice, the packaged game): no grass and white windows though the editor drew both - say ONCE, on the terrain's level,
	// what the scattered plants and the house glass really are at run time
	if (!bSaidScene && W->StationTerrain().Len() && GetWorld()->GetMapName().Contains(W->StationTerrain()))
	{
		bSaidScene = true;
		TMap<FString, FIntPoint> ByMesh;   // mesh -> (components, instances)
		for (TObjectIterator<UHierarchicalInstancedStaticMeshComponent> It; It; ++It)
		{
			UHierarchicalInstancedStaticMeshComponent* H = *It;
			if (!H || H->GetWorld() != GetWorld()) continue;
			const UStaticMesh* M = H->GetStaticMesh();
			FString Key = M ? M->GetName() : TEXT("(no mesh)");
			if (M) Key += FString::Printf(TEXT(" lods %d%s%s"), M->GetNumLODs(), M->GetRenderData() ? TEXT("") : TEXT(" NO RENDER DATA"), H->IsVisible() ? TEXT("") : TEXT(" HIDDEN"));
			if (const UMaterialInterface* Mi = H->GetMaterial(0)) Key += TEXT(" mat ") + Mi->GetName();
			int32 S, E; H->GetCullDistances(S, E);
			Key += FString::Printf(TEXT(" cull %d"), E);
			FIntPoint& C = ByMesh.FindOrAdd(Key); C.X += 1; C.Y += H->GetInstanceCount();
		}
		for (const TPair<FString, FIntPoint>& It : ByMesh) UE_LOG(LogHrotDpsGame, Display, TEXT("scene: %s - %d components, %d instances"), *It.Key, It.Value.X, It.Value.Y);
		if (const UMaterialInterface* G = LoadObject<UMaterialInterface>(nullptr, TEXT("/Game/Hrot/M_HrotGlass.M_HrotGlass")))
			UE_LOG(LogHrotDpsGame, Display, TEXT("scene: glass %s blend %d (0 opaque, 2 translucent), two-sided %d"), *G->GetPathName(), int32(G->GetBlendMode()), G->IsTwoSided() ? 1 : 0);
	}
}
