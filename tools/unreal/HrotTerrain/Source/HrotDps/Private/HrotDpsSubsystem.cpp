// CE-4651 (U-18a) — see HrotDpsSubsystem.h
#include "HrotDpsSubsystem.h"
#include "HrotDpsNative.h"
#include "Engine/Engine.h"
#include "HAL/IConsoleManager.h"

void UHrotDpsSubsystem::Initialize(FSubsystemCollectionBase& Collection)
{
	Super::Initialize(Collection);
	Tick = FTSTicker::GetCoreTicker().AddTicker(FTickerDelegate::CreateWeakLambda(this, [this](float)
	{
		if (Dps.IsRunning()) Dps.Poll();
		return true;
	}));
}

void UHrotDpsSubsystem::Deinitialize()
{
	FTSTicker::GetCoreTicker().RemoveTicker(Tick);
	Dps.Stop();
	Super::Deinitialize();
}

bool UHrotDpsSubsystem::Start(int32 Domain, const FString& Partition)
{
	FString Why;
	if (Dps.Start(uint32(Domain), Partition, Why)) return true;
	UE_LOG(LogTemp, Error, TEXT("DPS start failed: %s"), *Why);
	return false;
}

void UHrotDpsSubsystem::Stop() { Dps.Stop(); Last.Reset(); }

namespace
{
	FString V(const SimNetGateway_Protocol_DdsPlayer_Vec3d& P) { return FString::Printf(TEXT("(%.3f, %.3f, %.3f)"), P.X, P.Y, P.Z); }
}

int32 UHrotDpsSubsystem::ProbeSubscribeAll()
{
	int32 N = 0;
	for (const FString& T : FHrotDpsClient::TopicNames())
	{
		if (T == TEXT("DPS_JoinRequest") || T == TEXT("DPS_ControlFrame") || T == TEXT("DPS_Leave") || T == TEXT("DPS_GizmoView")) continue;   // the client's own
		FHrotDpsClient::FHandler H = [](const FHrotDpsSample&) {};
		if (T == TEXT("DPS_Session"))
			H = [this](const FHrotDpsSample& S) { if (!S.bValid) return; const auto& D = *static_cast<const SimNetGateway_Protocol_DdsPlayer_Session*>(S.Data);
				Last.Add(TEXT("DPS_Session"), FString::Printf(TEXT("v%d.%d station '%hs' terrain '%hs' simTime %.3f epoch %u utm %d"),
					D.ProtocolMajor, D.ProtocolMinor, D.StationName, D.Terrain, D.SimTime, D.WorldEpoch, D.UtmZone)); };
		else if (T == TEXT("DPS_Entity"))
			H = [this](const FHrotDpsSample& S) { const auto& D = *static_cast<const SimNetGateway_Protocol_DdsPlayer_Entity*>(S.Data);
				Last.Add(TEXT("DPS_Entity"), S.bValid ? FString::Printf(TEXT("id %d type %lld domain %d side %d name '%hs' playable %d"),
					D.EntityId, D.TypeId, int32(D.Domain), D.Side, D.Name, D.Playable) : FString::Printf(TEXT("id %d disposed %d"), D.EntityId, S.bDisposed)); };
		else if (T == TEXT("DPS_EntityState"))
			H = [this](const FHrotDpsSample& S) { if (!S.bValid) return; const auto& D = *static_cast<const SimNetGateway_Protocol_DdsPlayer_EntityState*>(S.Data);
				Last.Add(TEXT("DPS_EntityState"), FString::Printf(TEXT("id %d t %.3f pos %s quat (%.3f, %.3f, %.3f, %.3f) vel (%.2f, %.2f, %.2f) bank %.3f"),
					D.EntityId, D.SimTime, *V(D.Position), D.Orientation.X, D.Orientation.Y, D.Orientation.Z, D.Orientation.W,
					D.LinearVelocity.X, D.LinearVelocity.Y, D.LinearVelocity.Z, D.Bank)); };
		else if (T == TEXT("DPS_Fire"))
			H = [this](const FHrotDpsSample& S) { if (!S.bValid) return; const auto& D = *static_cast<const SimNetGateway_Protocol_DdsPlayer_Fire*>(S.Data);
				Last.Add(TEXT("DPS_Fire"), FString::Printf(TEXT("t %.3f shooter %d target %d munition %lld origin %s"),
					D.SimTime, D.ShooterId, D.TargetId, D.MunitionTypeId, *V(D.Origin))); };
		if (Dps.Subscribe(T, MoveTemp(H))) ++N;
	}
	return N;
}

static FAutoConsoleCommand GDpsStart(TEXT("hrot.dps.start"), TEXT("hrot.dps.start <domain> <partition> — join a DPS station"),
	FConsoleCommandWithArgsDelegate::CreateLambda([](const TArray<FString>& A)
	{
		if (A.Num() < 2) { UE_LOG(LogTemp, Warning, TEXT("hrot.dps.start <domain> <partition>")); return; }
		GEngine->GetEngineSubsystem<UHrotDpsSubsystem>()->Start(FCString::Atoi(*A[0]), A[1]);
	}));
static FAutoConsoleCommand GDpsStop(TEXT("hrot.dps.stop"), TEXT("leave the DPS station"),
	FConsoleCommandDelegate::CreateLambda([] { GEngine->GetEngineSubsystem<UHrotDpsSubsystem>()->Stop(); }));
static FAutoConsoleCommand GDpsStats(TEXT("hrot.dps.stats"), TEXT("samples taken per DPS topic"),
	FConsoleCommandDelegate::CreateLambda([]
	{
		UHrotDpsSubsystem* S = GEngine->GetEngineSubsystem<UHrotDpsSubsystem>();
		UE_LOG(LogTemp, Display, TEXT("DPS %s, running %d"), *UHrotDpsSubsystem::Contract(), S->IsRunning());
		for (const TPair<FString, int64>& C : S->Counts()) UE_LOG(LogTemp, Display, TEXT("  %-22s %lld"), *C.Key, C.Value);
	}));
