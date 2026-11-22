// CE-4651 (U-18a) — the IG's one DPS connection: an engine subsystem owning the FHrotDpsClient and polling it once per frame (core
// ticker: editor, Simulate and game alike). Console: hrot.dps.start <domain> <partition> · hrot.dps.stop · hrot.dps.stats
#pragma once

#include "CoreMinimal.h"
#include "Subsystems/EngineSubsystem.h"
#include "Containers/Ticker.h"
#include "HrotDpsClient.h"
#include "HrotSoundLog.h"
#include "HrotDpsSubsystem.generated.h"

UCLASS()
class HROTDPS_API UHrotDpsSubsystem : public UEngineSubsystem
{
	GENERATED_BODY()

public:
	virtual void Initialize(FSubsystemCollectionBase& Collection) override;
	virtual void Deinitialize() override;

	/** the connection, for the code that maps topics onto actors (Subscribe with a typed handler) */
	FHrotDpsClient& Client() { return Dps; }

	/** joins the station (the gateway's `dps` domain and partition); polled every frame from then on */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS")
	bool Start(int32 Domain, const FString& Partition);
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS")
	void Stop();
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS")
	bool IsRunning() const { return Dps.IsRunning(); }
	/** takes what is waiting now (the per-frame ticker does this anyway; for a commandlet, which has no frames) */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS")
	int32 Poll() { return Dps.Poll(); }
	/** samples taken per topic since Start */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS")
	TMap<FString, int64> Counts() const { return Dps.GetCounts(); }
	/** U-18p — this process's DPS client id (the guide: random, nonzero; one per client): the player station's slot, the clock probe and
	 *  ClientWorld all say the same one */
	uint64 GetClientId();

	// ── U-18r: the world follows the station's TERRAIN (Session.Terrain): this subsystem outlives a level load, so it holds what must ──
	/** a terrain name -> the level that draws it ("/Game/Maps/demo_city_look"); unlisted: the convention "-" -> "_", "<name>_look" else "<name>" */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|DPS") TMap<FString, FString> TerrainLevels;
	/** the level for a terrain, "" = none (no package mounted — ResolveTerrain mounts) */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") FString LevelForTerrain(const FString& Terrain) const;
	/** U-18t — the level for a terrain, MOUNTING its cooked package when needed: TerrainLevels, the /Game/Maps convention, then the terrain
	 *  package HrotTerrain_<name> (its level /HrotTerrain_<name>/Maps/<name>): present already (the editor) or mounted from
	 *  <TerrainsDir>/<name>/*.pak (+ its IoStore container) with its content path registered. "" = none; OutWhy says where it looked */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") FString ResolveTerrain(const FString& Terrain, FString& OutWhy) { return ResolveTerrainVersion(Terrain, FString(), FString(), OutWhy); }
	/** U-18x — the same, by name AND content (Session.TerrainBaseHash / TerrainOverlayHash): a package's hrot-terrain.json must name the base
	 *  hash the station states, and carry the overlay it states; else "" and OutWhy says which differs. The editor's /Game/Maps levels are not
	 *  checked (a working copy), only logged */
	FString ResolveTerrainVersion(const FString& Terrain, const FString& BaseHash, const FString& OverlayHash, FString& OutWhy);
	/** where terrain packages are looked for: -HrotTerrains=<dir>, else <ProjectDir>/Terrains (a packaged client: <App>/HrotUnreal/Terrains) */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") static FString TerrainsDir();
	/** the terrain packages mounted so far ("HrotTerrain_farm <- <pak>") */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") TArray<FString> MountedTerrains() const { return MountLog; }
	/** a level load for world epoch `Epoch` is under way: ClientWorld keeps saying Loading (`Step`) from HERE every second until the new
	 *  world's UHrotDpsWorld takes over — the participant (our ClientWorld writer) stays alive across the load */
	void BeginTravel(uint32 Epoch, const FString& Level, const FString& Step, bool bWaitForMe);
	void EndTravel() { bTraveling = false; }
	bool IsTraveling() const { return bTraveling; }
	uint32 GetTravelEpoch() const { return TravelEpoch; }
	const FString& GetTravelStep() const { return TravelStep; }
	const FString& GetTravelLevel() const { return TravelLevel; }
	/** the generated contract's version line and topic names */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS")
	static FString Contract() { return FHrotDpsClient::Contract(); }
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS")
	static TArray<FString> Topics() { return FHrotDpsClient::TopicNames(); }

	/** ⚙ TEST PROBE (the plumbing proof, not the mapping): subscribes every topic the gateway writes and keeps the last sample of a few
	 *  as text, decoded through the generated types — so a test can check the bytes really arrive as fields */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS")
	int32 ProbeSubscribeAll();
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS")
	TMap<FString, FString> ProbeLast() const { return Last; }

	/** ⚙ TEST PROBE (U-18p) — a FAKE STATION on this client's own participant (a local reader matches a local writer), so round 7 runs end
	 *  to end before the gateway publishes it: its wall = this machine's tick + OffsetSeconds; it answers every ClockProbe with a ClockEcho
	 *  (stamped at the poll: up to a frame late on the probe's leg) and keeps the last ClientWorld it read. ⛔ Never on a real station's
	 *  domain: its SimClock / View / Session would fight the gateway's */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") bool FakeStationStart(double OffsetSeconds);
	/** SimClock anchored at the fake station's NOW */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") bool FakeStationClock(bool bStepped, double AnchorSimTime, float TimeScale, int64 TimelineEpoch, int64 StepSeq);
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") bool FakeStationStep(int64 StepSeq, double TargetSimTime, int64 TimelineEpoch);
	/** Session (playing, scale 1) of this world epoch at sim time SimTime */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") bool FakeStationSession(int32 WorldEpoch, double SimTime, const FString& Terrain = TEXT(""));
	/** every ClientWorld the fake read, in order ("<epoch> <state> '<step>'"), and how often the IG's ClientWorld writer went away */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") TArray<FString> FakeStationClientWorldLog() const { return FakeWorldLog; }
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") int32 FakeStationClientWorldLost() const { return FakeWorldLost; }
	/** Entity (a ground unit) + its EntityState at sim time SimTime: position (m, ENU), velocity (m/s), heading (deg, counter-clockwise from east) */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") bool FakeStationEntity(int32 EntityId, double SimTime, FVector PositionM, FVector VelocityM, float HeadingDeg);
	/** U-18z — a GizmoFrame for Layer: a test pattern around CentreM (ENU m): a line strip, an arrow, a sphere, a filled triangle, a box and a
	 *  text (one of each shape the gateway sends), the first Count of them (0 = an empty layer) */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") bool FakeStationGizmoFrame(const FString& Layer, FVector CentreM, int32 Count);
	/** U-18y — the same with its Entity's Domain (the contract's number: 1 person, 2 ground, 3 air …), Side (0 neutral, 1 friend, 2 hostile) and Name */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") bool FakeStationEntityAs(int32 EntityId, int32 Domain, int32 InSide, const FString& Name, double SimTime,
		FVector PositionM, FVector VelocityM, float HeadingDeg);
	/** View: Source director; Mode (ViewMode number: 1 free, 2 orbit …), bound to TargetId at Distance, or the pose: position (m), velocity
	 *  (m/s), yaw (deg, ENU) turning YawRateDeg/s (body frame z), pitch (deg), vertical fov */
	/** U-18y (DD-025) — a body-less unit (Entity.IsUnit) at PositionM, and its EntityStatus facts (one call per topic's sample) */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") bool FakeStationUnit(int32 EntityId, int32 InSide, const FString& Name, double SimTime, FVector PositionM);
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") bool FakeStationStatus(int32 EntityId, const FString& Symbol, float HealthCurrent, float HealthMax, int32 StrengthPresent,
		int32 StrengthAuthorized, float AmmoShare, float MindFill, float MindBreak, int32 MindBand, const FString& Behaviour, const FString& Task);
	/** U-19b — the squad's radio (DD-026): an Utterance (Kind text, e.g. "CmdMove"), a Report (ReportKind number, Sub, Count, Activity, Strength colour,
	 *  Wia, Kia, Synthetic) from FromId to ToId (0 = HQ), an Order (Name) */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") bool FakeStationUtterance(int32 SpeakerId, const FString& Kind, double SimTime);
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") bool FakeStationReport(int32 FromId, int32 ToId, int32 Kind, int32 Sub, int32 Count, int32 Activity,
		int32 Strength, int32 Wia, int32 Kia, bool bSynthetic, double SimTime);
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") bool FakeStationOrder(int32 FromId, int32 ToId, const FString& Name, double SimTime);
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") bool FakeStationView(double SimTime, int32 Mode, int32 TargetId, float Distance, FVector PositionM,
		FVector VelocityM, float YawDeg, float YawRateDeg, float PitchDeg, float FovDeg, int32 CutSeq);
	/** U-19e (P-8): the View of FakeStationView with the director's Fade (0..1) and a subtitle (Pos: 1 bottom, 2 top, 3 centre; Alpha) */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") bool FakeStationViewOverlay(double SimTime, FVector PositionM, float YawDeg, float FovDeg, float Fade, const FString& Subtitle, int32 Pos, float Alpha);
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") int32 FakeStationEchoes() const { return FakeEchoes; }
	// U-18q — the render side: a job, a step with its barrier, a frame, a shot; what the render client answered
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") bool FakeStationRenderJob(const FString& JobId, int32 State, int32 Fps, int32 Width, int32 Height,
		int32 Supersample, const FString& Dir, bool bAudio, int32 SampleRate);
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") bool FakeStationRenderStep(int64 StepSeq, double TargetSimTime, int64 TimelineEpoch, int32 Samples);
	/** a FrameRequest: Kind 1 scene / 2 silence; the camera as FakeStationView's; a subtitle and one ring (RingAt, radius m) */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") bool FakeStationFrame(const FString& JobId, int32 Index, int32 Kind, int64 StepSeq, double SimTime,
		FVector PositionM, float YawDeg, float PitchDeg, float FovDeg, int32 Width, int32 Height, const FString& File, const FString& Subtitle, FVector RingAt, float RingRadius);
	/** a rifle shot (Fire, small arms) from OriginM at SimTime */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") bool FakeStationFire(double SimTime, FVector OriginM, FVector VelocityMs);
	/** "<n> done, last: <index> <Written|Failed> '<file>' <ms> ms audio <n> '<why>'" */
	/** U-19c - one round of a burst (DD-027): a rifle shot (SmallArms) with BurstId / Rounds / Index; the side comes from ShooterId's Entity */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") bool FakeStationBurstRound(double SimTime, int32 ShooterId, FVector OriginM, FVector VelocityMs, int32 BurstId, int32 Rounds, int32 Index);
	/** U-19f: the same from a machine gun (the mg_ clips) when bMachineGun, a rifle otherwise */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") bool FakeStationBurstRoundAs(double SimTime, int32 ShooterId, FVector OriginM, FVector VelocityMs, int32 BurstId, int32 Rounds, int32 Index, bool bMachineGun);
	/** U-19d (DD-028) - an ambience row (Kind, Intensity; a place in ENU metres and a radius when bPositioned) and a weather row (a global one when Outline is
	 *  empty; else a region with its outline, ENU metres); bDispose = the row ends (the writer disposes it) */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") bool FakeStationAmbience(int32 Id, const FString& Kind, float Intensity, bool bPositioned, FVector PositionM, float RadiusM, bool bDispose);
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") bool FakeStationWeather(int32 Id, const TArray<FVector2D>& OutlineM, float FeatherM, float WindSpeedMps, int32 Precipitation, float MmPerHour, bool bDispose);
	/** U-19f - the sound log (HrotSoundLog.h): every line since the last ClearSoundLog, "<n> WORLD 18 m . cmd_move_03 - man 31 Utterance CmdMove" - what a scene check / the sound demo reads back */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") static TArray<FString> GetSoundLogLines() { return FHrotSoundLog::Get().Transcript(); }
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") static void ClearSoundLog() { FHrotSoundLog::Get().Clear(); }
	/** the caption (top-centre, while hrot.snd.Debug is 1) for Seconds */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") static void SetSoundCaption(const FString& Text, float Seconds) { FHrotSoundLog::Get().SetCaption(Text, Seconds); }
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") FString FakeStationFrameDone() const { return FakeDone; }
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") int32 FakeStationFramesDone() const { return FakeDoneCount; }
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") int32 FakeStationStepAcks() const { return FakeAcks; }
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") FString FakeStationRenderClient() const { return FakeClient; }
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") FString FakeStationJobResult() const { return FakeResult; }
	/** the last ClientWorld the fake station read: "<client hex> epoch <e> <Ready|Loading|Failed> '<step>' wait <0|1>", "" = none yet */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") FString FakeStationClientWorld() const { return FakeWorld; }

private:
	int64 FakeTicks() const;
	int64 FakeTickBase = 0;
	double FakeTickReal = 0;
	bool bFake = false;
	int32 FakeEchoes = 0;
	FString FakeWorld, FakeDone, FakeClient, FakeResult;
	TArray<FString> FakeWorldLog;
	TSet<FString> Mounted;
	TArray<FString> MountLog;
	int32 FakeWorldLost = 0;
	bool bTraveling = false, bTravelWait = true;
	uint32 TravelEpoch = 0;
	FString TravelStep, TravelLevel;
	double NextTravelSay = 0;
	void SayTraveling();
	int32 FakeDoneCount = 0, FakeAcks = 0;
	FHrotDpsClient Dps;
	uint64 ClientId = 0;
	FTSTicker::FDelegateHandle Tick;
	TMap<FString, FString> Last;
};
