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
	/** U-18r — a FRESH reader for `Topic`, its handlers kept: a TRANSIENT_LOCAL topic's late-join history is delivered again (what a new
	 *  world needs) WITHOUT restarting the participant — ⚠ a restart deletes our writers, which the station reads as the client gone
	 *  (ClientWorld's waiter lets a load go on without us). Not from a handler */
	void Refresh(const FString& Topic);
	/** takes everything waiting on every subscribed reader and calls its handlers (game thread). Returns the number of samples. */
	int32 Poll();
	/** writes one sample (the generated C struct) of `Topic` (the player topics: JoinRequest, ControlFrame, Leave, GizmoView) */
	bool Write(const FString& Topic, const void* Sample);
	/** disposes the instance `Sample`'s key names - a state row that ends (DPS_Ambience, DPS_Weather); the tests' fake station */
	bool Dispose(const FString& Topic, const void* Sample);

	/** U-18p — a reader / writer of `Topic` (its QoS, this partition) that Poll() never touches: a raw DDS handle for a thread of its own
	 *  (dds_* calls are thread-safe; the client's maps are not). Deleted with the client — stop the thread before Stop(). 0 = failed */
	int32 MakeRawReader(const FString& Topic);
	int32 MakeRawWriter(const FString& Topic);

	/** U-18q — samples taken so far on the WORLD topics (the D-B4 barrier's count: every topic the gateway writes in a step's gather but
	 *  the clock's, the render topics and GizmoFrame; and never the topics clients write) */
	int64 GetWorldTaken() const { return WorldTaken; }
	static bool IsWorldTopic(const FString& Topic);
	/** U-18q — the D-B4 step count as the gateway states it (its answer in NOTE-…-Round-7-Render §5): since the last reset, a STATE topic
	 *  counts once per INSTANCE taken (keep-last-1 can fold two writes of one instance into one sample; a keyless topic is one instance),
	 *  an EVENT topic once per sample. Counted only while on (a render job) */
	void ResetStepCount(bool bOn) { bCountStep = bOn; StepInstances.Reset(); StepEvents = 0; }
	int64 GetStepCount() const { return StepInstances.Num() + StepEvents; }
	static bool IsEventTopic(const FString& Topic);

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
	int64 WorldTaken = 0;
	bool bCountStep = false;
	TSet<TPair<FString, uint64>> StepInstances;
	int64 StepEvents = 0;
	int32 TopicOf(const FString& Name, const struct FHrotDpsTopicDef*& OutDef);
};
