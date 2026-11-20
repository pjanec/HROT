// CE-4651 (U-18a) — the DDS PLUMBING: one DDS participant in the station's domain and partition (SimNetGateway DD-017 §3.2a), a reader
// or writer per DPS topic made on first use with the topic's QoS from the GENERATED table (scripts/unreal/dps_gen.py, from the gateway's
// [DdsQos] — a mismatch fails the match silently), samples taken on the GAME THREAD by Poll() and handed to the handlers.
//
// Nothing here knows a field of the contract: a handler casts FHrotDpsSample::Data to the idlc type of its topic
// (Private/Generated/DpsTopics.h, e.g. SimNetGateway_Protocol_DdsPlayer_EntityState). The mapping onto actors comes later.
#pragma once

#include "CoreMinimal.h"

/** one sample as taken from a reader */
struct FHrotDpsSample
{
	/** the generated C struct of the topic; valid only during the handler call. With !bValid only its KEY fields are set. */
	const void* Data = nullptr;
	/** false = no data, only the instance's new state (disposed by the writer, or no writer left) */
	bool bValid = false;
	/** the instance is alive (not disposed, a writer is there) */
	bool bAlive = false;
	/** the writer disposed the instance (an entity that is gone) */
	bool bDisposed = false;
	/** the writer's timestamp, seconds since 1970 */
	double SourceTime = 0;
	/** the DDS instance handle (one per key) */
	uint64 Instance = 0;
};

class HROTDPS_API FHrotDpsClient
{
public:
	using FHandler = TFunction<void(const FHrotDpsSample&)>;

	FHrotDpsClient();
	~FHrotDpsClient();

	/** joins `Domain` / `Partition` (the gateway's `dps` section). False + why when DDS refuses. */
	bool Start(uint32 Domain, const FString& Partition, FString& OutError);
	void Stop();
	bool IsRunning() const { return Participant != 0; }
	uint32 GetDomain() const { return Domain; }
	const FString& GetPartition() const { return Partition; }

	/** calls `Handler` for every sample of `Topic` ("DPS_EntityState") taken by Poll(); the reader is made now. ⚠ A TRANSIENT_LOCAL
	 *  topic's late-join history goes to the handlers present when the reader takes it: a handler added later sees only newer samples
	 *  (UHrotDpsWorld restarts the client for that reason). Returns the handler's id for Unsubscribe, 0 = unknown topic. Not from a handler. */
	int32 Subscribe(const FString& Topic, FHandler Handler);
	void Unsubscribe(const FString& Topic, int32 Id);
	/** takes everything waiting on every subscribed reader and calls its handlers (game thread). Returns the number of samples. */
	int32 Poll();
	/** writes one sample (the generated C struct) of `Topic` (the player topics: JoinRequest, ControlFrame, Leave, GizmoView) */
	bool Write(const FString& Topic, const void* Sample);

	/** samples taken per topic since Start */
	const TMap<FString, int64>& GetCounts() const { return Counts; }

	/** every topic of the generated contract, and the contract's version line */
	static TArray<FString> TopicNames();
	static FString Contract();

private:
	struct FReader { int32 Handle = 0; TArray<TPair<int32, FHandler>> Handlers; };
	int32 LastHandlerId = 0;
	int32 Participant = 0, Subscriber = 0, Publisher = 0;
	uint32 Domain = 0;
	FString Partition;
	TMap<FString, int32> Topics;
	TMap<FString, FReader> Readers;
	TMap<FString, int32> Writers;
	TMap<FString, int64> Counts;
	int32 TopicOf(const FString& Name, const struct FHrotDpsTopicDef*& OutDef);
};
