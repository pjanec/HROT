// CE-4651 (U-18a) — see HrotDpsSubsystem.h
#include "HrotDpsSubsystem.h"
#include "ProfilingDebugging/CsvProfiler.h"
#include "HrotDpsNative.h"
#include "Engine/Engine.h"
#include "HAL/IConsoleManager.h"
#include "HAL/PlatformProcess.h"
#include "HAL/PlatformTime.h"
#include "Math/RandomStream.h"
#include "Misc/DateTime.h"
#include "Misc/PackageName.h"
#include "Misc/CoreDelegates.h"
#include "ShaderCodeLibrary.h"
#include "Misc/CommandLine.h"
#include "Misc/Parse.h"
#include "Misc/Paths.h"
#include "HAL/FileManager.h"
#include "Misc/FileHelper.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"

void UHrotDpsSubsystem::Initialize(FSubsystemCollectionBase& Collection)
{
	Super::Initialize(Collection);
	Tick = FTSTicker::GetCoreTicker().AddTicker(FTickerDelegate::CreateWeakLambda(this, [this](float)
	{
		CSV_SCOPED_TIMING_STAT_EXCLUSIVE(HrotDpsDdsPoll);
		if (Dps.IsRunning()) Dps.Poll();
		if (bTraveling) SayTraveling();
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
		if (!S.bValid) { if (!S.bAlive) { ++FakeWorldLost; FakeWorldLog.Add(TEXT("LOST")); } return; }
		const auto& W = *static_cast<const SimNetGateway_Protocol_DdsPlayer_ClientWorld*>(S.Data);
		const TCHAR* St = W.State == SimNetGateway_Protocol_DdsPlayer_ClientWorldStateReady ? TEXT("Ready") :
			W.State == SimNetGateway_Protocol_DdsPlayer_ClientWorldStateLoading ? TEXT("Loading") :
			W.State == SimNetGateway_Protocol_DdsPlayer_ClientWorldStateFailed ? TEXT("Failed") : TEXT("Unknown");
		FakeWorld = FString::Printf(TEXT("%llx epoch %u %s '%s' wait %d"), W.ClientId, W.WorldEpoch, St, UTF8_TO_TCHAR(W.Step), W.WaitForMe ? 1 : 0);
		const FString Entry = FString::Printf(TEXT("%u %s '%s'%s"), W.WorldEpoch, St, UTF8_TO_TCHAR(W.Step), W.Reason[0] ? *FString::Printf(TEXT(" why '%s'"), UTF8_TO_TCHAR(W.Reason)) : TEXT(""));
		if (FakeWorldLog.Num() == 0 || FakeWorldLog.Last() != Entry) FakeWorldLog.Add(Entry);
	});
	// U-18q: what the render client answers
	Dps.Subscribe(TEXT("DPS_FrameDone"), [this](const FHrotDpsSample& S)
	{
		if (!S.bValid) return;
		const auto& D = *static_cast<const SimNetGateway_Protocol_DdsPlayer_FrameDone*>(S.Data);
		++FakeDoneCount;
		FakeDone = FString::Printf(TEXT("%d done, last: %d %s '%s' %.0f ms audio %d step %lld '%s'"), FakeDoneCount, D.FrameIndex,
			D.Status == SimNetGateway_Protocol_DdsPlayer_FrameStatusWritten ? TEXT("Written") : TEXT("Failed"), UTF8_TO_TCHAR(D.File), D.RenderMs, D.AudioSamples,
			D.StepSeq, UTF8_TO_TCHAR(D.Reason));
	});
	Dps.Subscribe(TEXT("DPS_StepAck"), [this](const FHrotDpsSample& S) { if (S.bValid) ++FakeAcks; });
	Dps.Subscribe(TEXT("DPS_RenderClient"), [this](const FHrotDpsSample& S)
	{
		if (!S.bValid) return;
		const auto& R = *static_cast<const SimNetGateway_Protocol_DdsPlayer_RenderClient*>(S.Data);
		FakeClient = FString::Printf(TEXT("'%s' ready %d max %dx%d x%d formats %u audio %u rate %d readyJob '%s'"), UTF8_TO_TCHAR(R.Name), R.Ready ? 1 : 0, R.MaxWidth,
			R.MaxHeight, int32(R.MaxSupersample), R.Formats, R.AudioFormats, R.MaxSampleRate, UTF8_TO_TCHAR(R.ReadyJobId));
	});
	Dps.Subscribe(TEXT("DPS_RenderJobResult"), [this](const FHrotDpsSample& S)
	{
		if (!S.bValid) return;
		const auto& R = *static_cast<const SimNetGateway_Protocol_DdsPlayer_RenderJobResult*>(S.Data);
		FakeResult = FString::Printf(TEXT("%s %s track '%s' %lld samples '%s'"), UTF8_TO_TCHAR(R.JobId),
			R.Status == SimNetGateway_Protocol_DdsPlayer_FrameStatusWritten ? TEXT("Written") : TEXT("Failed"), UTF8_TO_TCHAR(R.TrackFile), R.TrackSamples, UTF8_TO_TCHAR(R.Reason));
	});
	return true;
}

bool UHrotDpsSubsystem::FakeStationRenderJob(const FString& JobId, int32 State, int32 Fps, int32 Width, int32 Height, int32 Supersample, const FString& Dir,
	bool bAudio, int32 SampleRate)
{
	SimNetGateway_Protocol_DdsPlayer_RenderJob J{};
	FCStringAnsi::Strncpy(J.JobId, TCHAR_TO_UTF8(*JobId), sizeof(J.JobId));
	J.ClientId = GetClientId(); J.Kind = SimNetGateway_Protocol_DdsPlayer_RenderKindMovie;
	FCStringAnsi::Strncpy(J.What, "fake reel", sizeof(J.What));
	J.Fps = Fps; J.Width = Width; J.Height = Height; J.Supersample = uint8(Supersample);
	FCStringAnsi::Strncpy(J.Dir, TCHAR_TO_UTF8(*Dir), sizeof(J.Dir));
	J.Audio = bAudio; J.SampleRate = SampleRate; J.Channels = 2;
	J.State = SimNetGateway_Protocol_DdsPlayer_RenderState(State);
	return Dps.Write(TEXT("DPS_RenderJob"), &J);
}

bool UHrotDpsSubsystem::FakeStationRenderStep(int64 StepSeq, double TargetSimTime, int64 TimelineEpoch, int32 Samples)
{
	SimNetGateway_Protocol_DdsPlayer_ClockStep C{};
	C.StepSeq = StepSeq; C.FixedDelta = 1.f / 30.f; C.TargetSimTime = TargetSimTime; C.TimeScale = 1.f; C.TimelineEpoch = TimelineEpoch;
	C.AckRequired = true; C.Samples = uint32(Samples);
	return Dps.Write(TEXT("DPS_ClockStep"), &C);
}

bool UHrotDpsSubsystem::FakeStationFrame(const FString& JobId, int32 Index, int32 Kind, int64 StepSeq, double SimTime, FVector PositionM, float YawDeg, float PitchDeg,
	float FovDeg, int32 Width, int32 Height, const FString& File, const FString& Subtitle, FVector RingAt, float RingRadius)
{
	SimNetGateway_Protocol_DdsPlayer_FrameRequest F{};
	FCStringAnsi::Strncpy(F.JobId, TCHAR_TO_UTF8(*JobId), sizeof(F.JobId));
	F.FrameIndex = Index; F.Kind = SimNetGateway_Protocol_DdsPlayer_FrameKind(Kind); F.StepSeq = StepSeq; F.SimTime = SimTime; F.FilmRate = 1.f;
	F.Position = { PositionM.X, PositionM.Y, PositionM.Z };
	const FQuat4d Q = FQuat4d(FVector3d::ZAxisVector, FMath::DegreesToRadians(double(YawDeg))) * FQuat4d(FVector3d::YAxisVector, -FMath::DegreesToRadians(double(PitchDeg)));
	F.Orientation = { float(Q.X), float(Q.Y), float(Q.Z), float(Q.W) };
	F.FovDeg = FovDeg; F.Width = Width; F.Height = Height; F.Supersample = 1;
	FCStringAnsi::Strncpy(F.File, TCHAR_TO_UTF8(*File), sizeof(F.File));
	FCStringAnsi::Strncpy(F.Subtitle, TCHAR_TO_UTF8(*Subtitle), sizeof(F.Subtitle));
	F.SubtitlePosition = SimNetGateway_Protocol_DdsPlayer_SubtitlePositionBottom; F.SubtitleAlpha = 1.f;
	SimNetGateway_Protocol_DdsPlayer_FilmGraphic G{};
	SimNetGateway_Protocol_DdsPlayer_Vec3d P{ RingAt.X, RingAt.Y, RingAt.Z };
	if (RingRadius > 0)
	{
		G.Kind = SimNetGateway_Protocol_DdsPlayer_FilmGraphicKindRing; G.Radius = RingRadius; G.ColourRgb = 0xFFC020; G.Alpha = 1.f;
		FCStringAnsi::Strncpy(G.Text, "objective", sizeof(G.Text));
		G.Points._length = G.Points._maximum = 1; G.Points._buffer = &P; G.Points._release = false;
		F.Graphics._length = F.Graphics._maximum = 1; F.Graphics._buffer = &G; F.Graphics._release = false;
	}
	return Dps.Write(TEXT("DPS_FrameRequest"), &F);
}

bool UHrotDpsSubsystem::FakeStationFire(double SimTime, FVector OriginM, FVector VelocityMs)
{
	SimNetGateway_Protocol_DdsPlayer_Fire F{};
	F.SimTime = SimTime; F.ShooterId = 0; F.TargetId = 0;
	F.Origin = { OriginM.X, OriginM.Y, OriginM.Z };
	F.Velocity = { float(VelocityMs.X), float(VelocityMs.Y), float(VelocityMs.Z) };
	F.WeaponCategory = SimNetGateway_Protocol_DdsPlayer_WeaponCategorySmallArms;
	return Dps.Write(TEXT("DPS_Fire"), &F);
}

bool UHrotDpsSubsystem::FakeStationBurstRound(double SimTime, int32 ShooterId, FVector OriginM, FVector VelocityMs, int32 BurstId, int32 Rounds, int32 Index)
{
	return FakeStationBurstRoundAs(SimTime, ShooterId, OriginM, VelocityMs, BurstId, Rounds, Index, false);
}

bool UHrotDpsSubsystem::FakeStationBurstRoundAs(double SimTime, int32 ShooterId, FVector OriginM, FVector VelocityMs, int32 BurstId, int32 Rounds, int32 Index, bool bMachineGun)
{
	SimNetGateway_Protocol_DdsPlayer_Fire F{};
	F.SimTime = SimTime; F.ShooterId = ShooterId; F.TargetId = 0;
	F.Origin = { OriginM.X, OriginM.Y, OriginM.Z };
	F.Velocity = { float(VelocityMs.X), float(VelocityMs.Y), float(VelocityMs.Z) };
	F.WeaponCategory = bMachineGun ? SimNetGateway_Protocol_DdsPlayer_WeaponCategoryMachineGun : SimNetGateway_Protocol_DdsPlayer_WeaponCategorySmallArms;
	F.BurstId = uint32(BurstId); F.BurstRounds = uint8(Rounds); F.BurstIndex = uint8(Index);
	return Dps.Write(TEXT("DPS_Fire"), &F);
}

bool UHrotDpsSubsystem::FakeStationAmbience(int32 Id, const FString& Kind, float Intensity, bool bPositioned, FVector PositionM, float RadiusM, bool bDispose)
{
	SimNetGateway_Protocol_DdsPlayer_Ambience A{};
	A.Id = Id;
	if (bDispose) return Dps.Dispose(TEXT("DPS_Ambience"), &A);
	FCStringAnsi::Strncpy(A.Kind, TCHAR_TO_UTF8(*Kind), sizeof(A.Kind));
	A.Intensity = Intensity; A.HasPosition = bPositioned; A.Radius = RadiusM;
	A.Position = { PositionM.X, PositionM.Y, PositionM.Z };
	return Dps.Write(TEXT("DPS_Ambience"), &A);
}

bool UHrotDpsSubsystem::FakeStationWeather(int32 Id, const TArray<FVector2D>& OutlineM, float FeatherM, float WindSpeedMps, int32 Precipitation, float MmPerHour, bool bDispose)
{
	SimNetGateway_Protocol_DdsPlayer_Weather W{};
	W.Id = Id;
	if (bDispose) return Dps.Dispose(TEXT("DPS_Weather"), &W);
	TArray<SimNetGateway_Protocol_DdsPlayer_Vec3d> Pts;
	for (const FVector2D& P : OutlineM) Pts.Add({ P.X, P.Y, 0.0 });
	W.Outline._length = W.Outline._maximum = uint32(Pts.Num());
	W.Outline._buffer = Pts.Num() ? Pts.GetData() : nullptr;
	W.Outline._release = false;
	W.FeatherM = FeatherM; W.WindSpeedMps = WindSpeedMps;
	W.Precipitation = SimNetGateway_Protocol_DdsPlayer_PrecipitationKind(Precipitation); W.PrecipitationMmPerHour = MmPerHour;
	return Dps.Write(TEXT("DPS_Weather"), &W);
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

FString UHrotDpsSubsystem::LevelForTerrain(const FString& Terrain) const
{
	if (Terrain.IsEmpty()) return FString();
	if (const FString* L = TerrainLevels.Find(Terrain)) return FPackageName::DoesPackageExist(*L) ? *L : FString();
	const FString N = Terrain.Replace(TEXT("-"), TEXT("_"));
	for (const FString& L : { FString::Printf(TEXT("/Game/Maps/%s_look"), *N), FString::Printf(TEXT("/Game/Maps/%s"), *N) })
		if (FPackageName::DoesPackageExist(L)) return L;
	return FString();
}

FString UHrotDpsSubsystem::TerrainsDir()
{
	FString D;
	if (FParse::Value(FCommandLine::Get(), TEXT("HrotTerrains="), D)) return FPaths::ConvertRelativePathToFull(D);
	return FPaths::ConvertRelativePathToFull(FPaths::Combine(FPaths::ProjectDir(), TEXT("Terrains")));
}

FString UHrotDpsSubsystem::ResolveTerrainVersion(const FString& Terrain, const FString& BaseHash, const FString& OverlayHash, FString& OutWhy)
{
	OutWhy.Reset();
	if (Terrain.IsEmpty()) return FString();
	const FString Here = LevelForTerrain(Terrain);
	if (!Here.IsEmpty())
	{
		if (!BaseHash.IsEmpty() || !OverlayHash.IsEmpty())
			UE_LOG(LogTemp, Display, TEXT("HROT IG: terrain '%s' drawn by the working level %s - its version is not checked (base %s, overlay %s)"), *Terrain, *Here,
				*BaseHash.Left(12), *OverlayHash.Left(12));
		return Here;
	}
	// U-18t: the terrain's own cooked package (a content plugin cooked as DLC against the client's release)
	const FString N = Terrain.Replace(TEXT("-"), TEXT("_"));
	const FString Plugin = TEXT("HrotTerrain_") + N;
	const FString Level = FString::Printf(TEXT("/%s/Maps/%s"), *Plugin, *N);
	if (Mounted.Contains(Plugin) || FPackageName::DoesPackageExist(Level))
	{
		if (FPackageName::DoesPackageExist(Level)) return Level;
		OutWhy = FString::Printf(TEXT("package %s is mounted but holds no %s"), *Plugin, *Level);
		return FString();
	}
	const FString Dir = FPaths::Combine(TerrainsDir(), N);
	// U-18x: the package's identity - which HROT terrain (by content) it was built from, which overlays it carries
	if (!BaseHash.IsEmpty() || !OverlayHash.IsEmpty())
	{
		FString Text;
		TSharedPtr<FJsonObject> M;
		if (!FFileHelper::LoadFileToString(Text, *FPaths::Combine(Dir, TEXT("hrot-terrain.json"))) || !FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Text), M) || !M)
		{
			OutWhy = FString::Printf(TEXT("the station names terrain '%s' base %s, but the package in %s states no version (no hrot-terrain.json - re-run hrot_package_terrain.ps1)"),
				*Terrain, *BaseHash.Left(12), *Dir);
			return FString();
		}
		const FString Have = M->GetStringField(TEXT("baseHash"));
		if (!BaseHash.IsEmpty() && Have != BaseHash)
		{
			OutWhy = FString::Printf(TEXT("no bundle for terrain '%s' base %s (the package in %s is base %s)"), *Terrain, *BaseHash.Left(12), *Dir, *Have.Left(12));
			return FString();
		}
		if (!OverlayHash.IsEmpty())
		{
			bool bHas = false;
			const TArray<TSharedPtr<FJsonValue>>* O = nullptr;
			if (M->TryGetArrayField(TEXT("overlays"), O)) for (const TSharedPtr<FJsonValue>& V : *O) bHas |= V->AsString() == OverlayHash;
			if (!bHas)
			{
				OutWhy = FString::Printf(TEXT("no bundle for terrain '%s' with overlay %s (the package in %s carries the base only)"), *Terrain, *OverlayHash.Left(12), *Dir);
				return FString();
			}
		}
	}
	TArray<FString> Paks;
	IFileManager::Get().FindFiles(Paks, *FPaths::Combine(Dir, TEXT("*.pak")), true, false);
	if (Paks.Num() == 0)
	{
		OutWhy = FString::Printf(TEXT("no Unreal level for terrain '%s' (TerrainLevels, /Game/Maps/%s_look, /Game/Maps/%s, a package in %s)"), *Terrain, *N, *N, *Dir);
		return FString();
	}
	if (!FCoreDelegates::MountPak.IsBound())
	{
		OutWhy = FString::Printf(TEXT("terrain package %s found, but this build mounts no packages (the editor: enable the plugin %s instead)"), *Dir, *Plugin);
		return FString();
	}
	for (const FString& P : Paks)
	{
		const FString Path = FPaths::Combine(Dir, P);
		if (!FCoreDelegates::MountPak.Execute(Path, 100)) { OutWhy = FString::Printf(TEXT("could not mount %s"), *Path); return FString(); }
		MountLog.Add(FString::Printf(TEXT("%s <- %s"), *Plugin, *Path));
	}
	// the package's paths: /HrotTerrain_<n>/... = <ProjectPluginsDir>/HrotTerrain_<n>/Content/... (where the DLC cook put them)
	const FString ContentDir = FPaths::Combine(FPaths::ProjectPluginsDir(), Plugin, TEXT("Content/"));
	FPackageName::RegisterMountPoint(FString::Printf(TEXT("/%s/"), *Plugin), ContentDir);
	// its materials' shaders: a DLC carries its own shader library (named after the plugin); the plugin manager would open it on a plugin
	// mount — a package mounted by hand must open it, or the terrain's materials fall back to the default one
	const bool bShaders = FShaderCodeLibrary::OpenLibrary(Plugin, ContentDir);
	MountLog.Add(FString::Printf(TEXT("%s shader library %s"), *Plugin, bShaders ? TEXT("opened") : TEXT("NOT found (its materials may fall back)")));
	Mounted.Add(Plugin);
	UE_LOG(LogTemp, Display, TEXT("HROT IG: terrain package %s mounted (%d pak) from %s"), *Plugin, Paks.Num(), *Dir);
	if (FPackageName::DoesPackageExist(Level)) return Level;
	OutWhy = FString::Printf(TEXT("package %s mounted from %s but holds no %s"), *Plugin, *Dir, *Level);
	return FString();
}

void UHrotDpsSubsystem::BeginTravel(uint32 Epoch, const FString& Level, const FString& Step, bool bWaitForMe)
{
	bTraveling = true; TravelEpoch = Epoch; TravelLevel = Level; TravelStep = Step; bTravelWait = bWaitForMe; NextTravelSay = 0;
	SayTraveling();
}

void UHrotDpsSubsystem::SayTraveling()
{
	const double Now = FPlatformTime::Seconds();
	if (Now < NextTravelSay || !Dps.IsRunning()) return;
	NextTravelSay = Now + 1.0;
	SimNetGateway_Protocol_DdsPlayer_ClientWorld W{};
	W.ClientId = GetClientId(); W.WorldEpoch = TravelEpoch; W.State = SimNetGateway_Protocol_DdsPlayer_ClientWorldStateLoading;
	FCStringAnsi::Strncpy(W.Step, TCHAR_TO_UTF8(*TravelStep), sizeof(W.Step));
	W.WaitForMe = bTravelWait;
	Dps.Write(TEXT("DPS_ClientWorld"), &W);
}

bool UHrotDpsSubsystem::FakeStationSession(int32 WorldEpoch, double SimTime, const FString& Terrain)
{
	SimNetGateway_Protocol_DdsPlayer_Session S{};
	S.ProtocolMajor = 1; S.ProtocolMinor = 0; S.Mode = SimNetGateway_Protocol_DdsPlayer_SessionModeLive; S.SimTime = SimTime; S.TimeScale = 1.0;
	S.WorldEpoch = uint32(WorldEpoch);
	FCStringAnsi::Strncpy(S.Terrain, TCHAR_TO_UTF8(*Terrain), sizeof(S.Terrain)); FCStringAnsi::Strncpy(S.StationName, "fake station", sizeof(S.StationName));
	return Dps.Write(TEXT("DPS_Session"), &S);
}

bool UHrotDpsSubsystem::FakeStationEntity(int32 EntityId, double SimTime, FVector PositionM, FVector VelocityM, float HeadingDeg)
{
	return FakeStationEntityAs(EntityId, int32(SimNetGateway_Protocol_DdsPlayer_DomainGround), 1, TEXT("fake unit"), SimTime, PositionM, VelocityM, HeadingDeg);
}

bool UHrotDpsSubsystem::FakeStationEntityAs(int32 EntityId, int32 Domain, int32 InSide, const FString& Name, double SimTime, FVector PositionM, FVector VelocityM, float HeadingDeg)
{
	SimNetGateway_Protocol_DdsPlayer_Entity E{};
	E.EntityId = EntityId; E.TypeId = 0; E.Domain = SimNetGateway_Protocol_DdsPlayer_Domain(Domain); E.Side = uint8(InSide);
	FCStringAnsi::Strncpy(E.Name, TCHAR_TO_UTF8(*Name), sizeof(E.Name));
	SimNetGateway_Protocol_DdsPlayer_EntityState St{};
	St.EntityId = EntityId; St.SimTime = SimTime;
	St.Position = { PositionM.X, PositionM.Y, PositionM.Z };
	const FQuat4d Q(FVector3d::ZAxisVector, FMath::DegreesToRadians(double(HeadingDeg)));
	St.Orientation = { float(Q.X), float(Q.Y), float(Q.Z), float(Q.W) };
	St.LinearVelocity = { float(VelocityM.X), float(VelocityM.Y), float(VelocityM.Z) };
	return Dps.Write(TEXT("DPS_Entity"), &E) && Dps.Write(TEXT("DPS_EntityState"), &St);
}

bool UHrotDpsSubsystem::FakeStationUnit(int32 EntityId, int32 InSide, const FString& Name, double SimTime, FVector PositionM)
{
	SimNetGateway_Protocol_DdsPlayer_Entity E{};
	E.EntityId = EntityId; E.TypeId = 0; E.Domain = SimNetGateway_Protocol_DdsPlayer_DomainGround; E.Side = uint8(InSide); E.IsUnit = true;
	FCStringAnsi::Strncpy(E.Name, TCHAR_TO_UTF8(*Name), sizeof(E.Name));
	SimNetGateway_Protocol_DdsPlayer_EntityState St{};
	St.EntityId = EntityId; St.SimTime = SimTime;
	St.Position = { PositionM.X, PositionM.Y, PositionM.Z };
	St.Orientation = { 0.f, 0.f, 0.f, 1.f };
	return Dps.Write(TEXT("DPS_Entity"), &E) && Dps.Write(TEXT("DPS_EntityState"), &St);
}

bool UHrotDpsSubsystem::FakeStationStatus(int32 EntityId, const FString& Symbol, float HealthCurrent, float HealthMax, int32 StrengthPresent, int32 StrengthAuthorized,
	float AmmoShare, float MindFill, float MindBreak, int32 MindBand, const FString& Behaviour, const FString& Task)
{
	SimNetGateway_Protocol_DdsPlayer_EntityStatus S{};
	S.EntityId = EntityId;
	FCStringAnsi::Strncpy(S.Symbol, TCHAR_TO_UTF8(*Symbol), sizeof(S.Symbol));
	FCStringAnsi::Strncpy(S.Behaviour, TCHAR_TO_UTF8(*Behaviour), sizeof(S.Behaviour));
	FCStringAnsi::Strncpy(S.Task, TCHAR_TO_UTF8(*Task), sizeof(S.Task));
	S.HealthCurrent = HealthCurrent; S.HealthMax = HealthMax; S.StrengthPresent = StrengthPresent; S.StrengthAuthorized = StrengthAuthorized;
	S.AmmoShare = AmmoShare; S.MindFill = MindFill; S.MindBreak = MindBreak; S.MindBand = SimNetGateway_Protocol_DdsPlayer_MindBand(MindBand);
	return Dps.Write(TEXT("DPS_EntityStatus"), &S);
}

bool UHrotDpsSubsystem::FakeStationUtterance(int32 SpeakerId, const FString& Kind, double SimTime)
{
	SimNetGateway_Protocol_DdsPlayer_Utterance U{};
	U.SimTime = SimTime; U.SpeakerId = SpeakerId;
	FCStringAnsi::Strncpy(U.Kind, TCHAR_TO_UTF8(*Kind), sizeof(U.Kind));
	return Dps.Write(TEXT("DPS_Utterance"), &U);
}

bool UHrotDpsSubsystem::FakeStationReport(int32 FromId, int32 ToId, int32 Kind, int32 Sub, int32 Count, int32 Activity, int32 Strength, int32 Wia, int32 Kia,
	bool bSynthetic, double SimTime)
{
	SimNetGateway_Protocol_DdsPlayer_Report R{};
	R.SimTime = SimTime; R.FromId = FromId; R.ToId = ToId;
	R.Kind = SimNetGateway_Protocol_DdsPlayer_ReportKind(Kind); R.Sub = uint8(Sub); R.Count = uint8(Count);
	R.Activity = SimNetGateway_Protocol_DdsPlayer_ElementActivity(Activity); R.Strength = SimNetGateway_Protocol_DdsPlayer_StatusColour(Strength);
	R.Wia = uint8(Wia); R.Kia = uint8(Kia); R.Synthetic = bSynthetic;
	return Dps.Write(TEXT("DPS_Report"), &R);
}

bool UHrotDpsSubsystem::FakeStationOrder(int32 FromId, int32 ToId, const FString& Name, double SimTime)
{
	SimNetGateway_Protocol_DdsPlayer_Order O{};
	O.SimTime = SimTime; O.FromId = FromId; O.ToId = ToId;
	FCStringAnsi::Strncpy(O.Name, TCHAR_TO_UTF8(*Name), sizeof(O.Name));
	return Dps.Write(TEXT("DPS_Order"), &O);
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

bool UHrotDpsSubsystem::FakeStationViewOverlay(double SimTime, FVector PositionM, float YawDeg, float FovDeg, float Fade, const FString& Subtitle, int32 Pos, float Alpha)
{
	SimNetGateway_Protocol_DdsPlayer_View V{};
	V.Source = SimNetGateway_Protocol_DdsPlayer_ViewSourceDirector;
	V.Mode = SimNetGateway_Protocol_DdsPlayer_ViewMode(1);
	V.Side = 1;
	V.Position = { PositionM.X, PositionM.Y, PositionM.Z };
	const FQuat4d Q(FVector3d::ZAxisVector, FMath::DegreesToRadians(double(YawDeg)));
	V.Orientation = { float(Q.X), float(Q.Y), float(Q.Z), float(Q.W) };
	V.FovDeg = FovDeg; V.CutSeq = 1; V.SimTime = SimTime;
	FCStringAnsi::Strncpy(V.Shot, "fake overlay", sizeof(V.Shot));
	V.Fade = Fade;
	FCStringAnsi::Strncpy(V.Subtitle, TCHAR_TO_UTF8(*Subtitle), sizeof(V.Subtitle));
	V.SubtitlePosition = SimNetGateway_Protocol_DdsPlayer_SubtitlePosition(Pos); V.SubtitleAlpha = Alpha;
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

bool UHrotDpsSubsystem::FakeStationGizmoFrame(const FString& Layer, FVector C, int32 Count)
{
	SimNetGateway_Protocol_DdsPlayer_GizmoFrame F{};
	FCStringAnsi::Strncpy(F.Producer, "fake", sizeof(F.Producer));
	FCStringAnsi::Strncpy(F.Layer, TCHAR_TO_UTF8(*Layer), sizeof(F.Layer));
	using P = SimNetGateway_Protocol_DdsPlayer_GizmoPrimitive;
	using V = SimNetGateway_Protocol_DdsPlayer_Vec3d;
	TArray<P> Prims; TArray<TArray<V>> Pts;
	auto Add = [&](SimNetGateway_Protocol_DdsPlayer_GizmoShape Shape, std::initializer_list<V> Points, uint32 Rgba, float W, const char* Text)
	{
		Pts.Add(TArray<V>(Points));
		P& Pr = Prims.AddZeroed_GetRef(); Pr.Shape = Shape; Pr.ColourRgba = Rgba; Pr.Width = W;
		if (Text) FCStringAnsi::Strncpy(Pr.Text, Text, sizeof(Pr.Text));
	};
	Add(SimNetGateway_Protocol_DdsPlayer_GizmoShapeLineStrip, { { C.X, C.Y, C.Z + 0.5 }, { C.X + 10, C.Y + 5, C.Z + 0.5 } }, 0x00FF00FF, 3, nullptr);
	Add(SimNetGateway_Protocol_DdsPlayer_GizmoShapeArrow, { { C.X, C.Y - 5, C.Z + 1 }, { C.X + 8, C.Y - 5, C.Z + 1 } }, 0xFFFF00FF, 3, nullptr);
	Add(SimNetGateway_Protocol_DdsPlayer_GizmoShapeCircle, { { C.X + 12, C.Y, C.Z + 1 } }, 0xFF0000FF, 1.5f, nullptr);
	Add(SimNetGateway_Protocol_DdsPlayer_GizmoShapePolygon, { { C.X, C.Y + 8, 0 }, { C.X + 4, C.Y + 8, 0 }, { C.X + 2, C.Y + 12, 0 } }, 0x00FFFF80, 0, nullptr);
	Add(SimNetGateway_Protocol_DdsPlayer_GizmoShapeBox, { { C.X + 6, C.Y + 10, 0 }, { 2, 1, 30 } }, 0xFF00FFFF, 2, nullptr);
	Add(SimNetGateway_Protocol_DdsPlayer_GizmoShapeText, { { C.X + 2, C.Y - 8, 0 } }, 0xFFFFFFFF, 0, "fake gizmo text");
	while (Prims.Num() > FMath::Max(0, Count)) { Prims.Pop(); Pts.Pop(); }
	for (int32 I = 0; I < Prims.Num(); I++) { Prims[I].Points._length = Prims[I].Points._maximum = uint32(Pts[I].Num()); Prims[I].Points._buffer = Pts[I].GetData(); }
	F.Primitives._length = F.Primitives._maximum = uint32(Prims.Num()); F.Primitives._buffer = Prims.GetData();
	return Dps.Write(TEXT("DPS_GizmoFrame"), &F);
}
