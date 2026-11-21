// CE-4651 (U-18a) — the IG's one DPS connection: an engine subsystem owning the FHrotDpsClient and polling it once per frame (core
// ticker: editor, Simulate and game alike). Console: hrot.dps.start <domain> <partition> · hrot.dps.stop · hrot.dps.stats
#pragma once

#include "CoreMinimal.h"
#include "Subsystems/EngineSubsystem.h"
#include "Containers/Ticker.h"
#include "HrotDpsClient.h"
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
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") bool FakeStationSession(int32 WorldEpoch, double SimTime);
	/** Entity (a ground unit) + its EntityState at sim time SimTime: position (m, ENU), velocity (m/s), heading (deg, counter-clockwise from east) */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") bool FakeStationEntity(int32 EntityId, double SimTime, FVector PositionM, FVector VelocityM, float HeadingDeg);
	/** View: Source director; Mode (ViewMode number: 1 free, 2 orbit …), bound to TargetId at Distance, or the pose: position (m), velocity
	 *  (m/s), yaw (deg, ENU) turning YawRateDeg/s (body frame z), pitch (deg), vertical fov */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") bool FakeStationView(double SimTime, int32 Mode, int32 TargetId, float Distance, FVector PositionM,
		FVector VelocityM, float YawDeg, float YawRateDeg, float PitchDeg, float FovDeg, int32 CutSeq);
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") int32 FakeStationEchoes() const { return FakeEchoes; }
	/** the last ClientWorld the fake station read: "<client hex> epoch <e> <Ready|Loading|Failed> '<step>' wait <0|1>", "" = none yet */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS|Test") FString FakeStationClientWorld() const { return FakeWorld; }

private:
	int64 FakeTicks() const;
	int64 FakeTickBase = 0;
	double FakeTickReal = 0;
	bool bFake = false;
	int32 FakeEchoes = 0;
	FString FakeWorld;
	FHrotDpsClient Dps;
	uint64 ClientId = 0;
	FTSTicker::FDelegateHandle Tick;
	TMap<FString, FString> Last;
};
