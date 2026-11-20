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

private:
	FHrotDpsClient Dps;
	FTSTicker::FDelegateHandle Tick;
	TMap<FString, FString> Last;
};
