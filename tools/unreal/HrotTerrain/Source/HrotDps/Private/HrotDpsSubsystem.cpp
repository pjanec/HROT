// CE-4651 (U-18a) — see HrotDpsSubsystem.h
#include "HrotDpsSubsystem.h"
#include "HrotDpsNative.h"
#include "Engine/Engine.h"
#include "HAL/IConsoleManager.h"
#include "HAL/PlatformProcess.h"
#include "HAL/PlatformTime.h"
#include "Math/RandomStream.h"
#include "Misc/DateTime.h"

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

void UHrotDpsSubsystem::Stop() { Dps.Stop(); Last.Reset(); bFake = false; }

// ── ⚙ the fake station (U-18p) ──

int64 UHrotDpsSubsystem::FakeTicks() const { return FakeTickBase + int64(FMath::RoundToDouble((FPlatformTime::Seconds() - FakeTickReal) * 1e7)); }

bool UHrotDpsSubsystem::FakeStationStart(double OffsetSeconds)
{
	if (!Dps.IsRunning()) return false;
	FakeTickReal = FPlatformTime::Seconds();
	FakeTickBase = FDateTime::UtcNow().GetTicks() + int64(OffsetSeconds * 1e7);
	if (bFake) return true;
	bFake = true;
	Dps.Subscribe(TEXT("DPS_ClockProbe"), [this](const FHrotDpsSample& S)
	{
		if (!S.bValid) return;
		const auto& P = *static_cast<const SimNetGateway_Protocol_DdsPlayer_ClockProbe*>(S.Data);
		SimNetGateway_Protocol_DdsPlayer_ClockEcho E{};
		E.ClientId = P.ClientId; E.ClientSendTicks = P.ClientSendTicks;
		E.StationReceiveTicks = FakeTicks(); E.StationTransmitTicks = FakeTicks();
		if (Dps.Write(TEXT("DPS_ClockEcho"), &E)) ++FakeEchoes;
	});
	Dps.Subscribe(TEXT("DPS_ClientWorld"), [this](const FHrotDpsSample& S)
	{
		if (!S.bValid) return;
		const auto& W = *static_cast<const SimNetGateway_Protocol_DdsPlayer_ClientWorld*>(S.Data);
		const TCHAR* St = W.State == SimNetGateway_Protocol_DdsPlayer_ClientWorldStateReady ? TEXT("Ready") :
			W.State == SimNetGateway_Protocol_DdsPlayer_ClientWorldStateLoading ? TEXT("Loading") :
			W.State == SimNetGateway_Protocol_DdsPlayer_ClientWorldStateFailed ? TEXT("Failed") : TEXT("Unknown");
		FakeWorld = FString::Printf(TEXT("%llx epoch %u %s '%s' wait %d"), W.ClientId, W.WorldEpoch, St, UTF8_TO_TCHAR(W.Step), W.WaitForMe ? 1 : 0);
	});
	return true;
}

bool UHrotDpsSubsystem::FakeStationClock(bool bStepped, double AnchorSimTime, float TimeScale, int64 TimelineEpoch, int64 StepSeq)
{
	static uint32 Seq = 0;
	SimNetGateway_Protocol_DdsPlayer_SimClock C{};
	C.Mode = bStepped ? SimNetGateway_Protocol_DdsPlayer_ClockModeStepped : SimNetGateway_Protocol_DdsPlayer_ClockModeContinuous;
	C.AnchorWallTicks = FakeTicks(); C.AnchorSimTime = AnchorSimTime; C.TimeScale = TimeScale; C.FixedDelta = 1.f / 60.f;
	C.StepSeq = StepSeq; C.TimelineEpoch = TimelineEpoch; C.Seq = ++Seq;
	return Dps.Write(TEXT("DPS_SimClock"), &C);
}

bool UHrotDpsSubsystem::FakeStationStep(int64 StepSeq, double TargetSimTime, int64 TimelineEpoch)
{
	SimNetGateway_Protocol_DdsPlayer_ClockStep C{};
	C.StepSeq = StepSeq; C.FixedDelta = 1.f / 60.f; C.TargetSimTime = TargetSimTime; C.TimeScale = 1.f; C.TimelineEpoch = TimelineEpoch;
	C.AckRequired = false; C.Samples = 0;
	return Dps.Write(TEXT("DPS_ClockStep"), &C);
}

bool UHrotDpsSubsystem::FakeStationSession(int32 WorldEpoch, double SimTime)
{
	SimNetGateway_Protocol_DdsPlayer_Session S{};
	S.ProtocolMajor = 1; S.ProtocolMinor = 0; S.Mode = SimNetGateway_Protocol_DdsPlayer_SessionModeLive; S.SimTime = SimTime; S.TimeScale = 1.0;
	S.WorldEpoch = uint32(WorldEpoch);
	FCStringAnsi::Strncpy(S.Terrain, "fake", sizeof(S.Terrain)); FCStringAnsi::Strncpy(S.StationName, "fake station", sizeof(S.StationName));
	return Dps.Write(TEXT("DPS_Session"), &S);
}

bool UHrotDpsSubsystem::FakeStationEntity(int32 EntityId, double SimTime, FVector PositionM, FVector VelocityM, float HeadingDeg)
{
	SimNetGateway_Protocol_DdsPlayer_Entity E{};
	E.EntityId = EntityId; E.TypeId = 0; E.Domain = SimNetGateway_Protocol_DdsPlayer_DomainGround; E.Side = 1;
	FCStringAnsi::Strncpy(E.Name, "fake unit", sizeof(E.Name));
	SimNetGateway_Protocol_DdsPlayer_EntityState St{};
	St.EntityId = EntityId; St.SimTime = SimTime;
	St.Position = { PositionM.X, PositionM.Y, PositionM.Z };
	const FQuat4d Q(FVector3d::ZAxisVector, FMath::DegreesToRadians(double(HeadingDeg)));
	St.Orientation = { float(Q.X), float(Q.Y), float(Q.Z), float(Q.W) };
	St.LinearVelocity = { float(VelocityM.X), float(VelocityM.Y), float(VelocityM.Z) };
	return Dps.Write(TEXT("DPS_Entity"), &E) && Dps.Write(TEXT("DPS_EntityState"), &St);
}

bool UHrotDpsSubsystem::FakeStationView(double SimTime, int32 Mode, int32 TargetId, float Distance, FVector PositionM, FVector VelocityM, float YawDeg,
	float YawRateDeg, float PitchDeg, float FovDeg, int32 CutSeq)
{
	SimNetGateway_Protocol_DdsPlayer_View V{};
	V.Source = SimNetGateway_Protocol_DdsPlayer_ViewSourceDirector;
	V.Mode = SimNetGateway_Protocol_DdsPlayer_ViewMode(Mode);
	V.TargetId = TargetId; V.Distance = Distance; V.Side = 1;
	V.Position = { PositionM.X, PositionM.Y, PositionM.Z };
	// body -> ENU: yaw about up, then pitch about the body's y (nose up = a NEGATIVE rotation about +y in a right-handed frame)
	const FQuat4d Q = FQuat4d(FVector3d::ZAxisVector, FMath::DegreesToRadians(double(YawDeg))) * FQuat4d(FVector3d::YAxisVector, -FMath::DegreesToRadians(double(PitchDeg)));
	V.Orientation = { float(Q.X), float(Q.Y), float(Q.Z), float(Q.W) };
	V.FovDeg = FovDeg; V.CutSeq = uint32(CutSeq); V.SimTime = SimTime;
	FCStringAnsi::Strncpy(V.Shot, "fake shot", sizeof(V.Shot));
	V.Velocity = { float(VelocityM.X), float(VelocityM.Y), float(VelocityM.Z) };
	V.AngularVelocity = { 0.f, 0.f, float(FMath::DegreesToRadians(double(YawRateDeg))) };   // body frame
	return Dps.Write(TEXT("DPS_View"), &V);
}

uint64 UHrotDpsSubsystem::GetClientId()
{
	if (ClientId == 0)
	{
		FRandomStream R(int32(FPlatformTime::Cycles64() ^ (uint64(FPlatformProcess::GetCurrentProcessId()) << 20)));
		ClientId = (uint64(uint32(R.GetUnsignedInt())) << 32) | uint32(R.GetUnsignedInt()) | 1ull;   // random, nonzero (the guide)
	}
	return ClientId;
}

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
