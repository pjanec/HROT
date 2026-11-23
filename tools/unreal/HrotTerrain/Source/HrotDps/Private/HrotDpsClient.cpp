// CE-4651 (U-18a) — see HrotDpsClient.h
#include "HrotDpsClient.h"
#include "HrotDpsNative.h"

DEFINE_LOG_CATEGORY_STATIC(LogHrotDps, Log, All);

#include "DpsTopicTable.gen.inl"

namespace
{
	const FHrotDpsTopicDef* FindDef(const FString& Name)
	{
		for (const FHrotDpsTopicDef& D : GHrotDpsTopics) if (Name == D.Name) return &D;
		return nullptr;
	}

	/** the topic's QoS exactly as the gateway's [DdsQos] (reader and writer alike) */
	dds_qos_t* QosOf(const FHrotDpsTopicDef& D)
	{
		dds_qos_t* Q = dds_create_qos();
		dds_qset_reliability(Q, D.bReliable ? DDS_RELIABILITY_RELIABLE : DDS_RELIABILITY_BEST_EFFORT, DDS_SECS(1));
		dds_qset_durability(Q, D.bTransientLocal ? DDS_DURABILITY_TRANSIENT_LOCAL : DDS_DURABILITY_VOLATILE);
		dds_qset_history(Q, DDS_HISTORY_KEEP_LAST, D.Depth);
		if (D.LeaseMs > 0) dds_qset_liveliness(Q, DDS_LIVELINESS_AUTOMATIC, DDS_MSECS(D.LeaseMs));
		return Q;
	}
}

FHrotDpsClient::FHrotDpsClient() {}

bool FHrotDpsClient::IsEventTopic(const FString& Topic)
{
	// the gateway's event topics (its answer, §5): counted per sample; every other world topic is state, counted per instance
	static const TSet<FString> Events = { TEXT("DPS_Fire"), TEXT("DPS_Detonation"), TEXT("DPS_Effect"), TEXT("DPS_Impact"), TEXT("DPS_ManDowned"),
		TEXT("DPS_PlayerMessage"), TEXT("DPS_MoverTracks") };
	return Events.Contains(Topic);
}

bool FHrotDpsClient::IsWorldTopic(const FString& Topic)
{
	// DD-022 D-B4: not SimClock, ClockStep, ClockEcho, the render topics or GizmoFrame; and the client-written topics are nobody's step
	static const TSet<FString> NotWorld = {
		TEXT("DPS_SimClock"), TEXT("DPS_ClockStep"), TEXT("DPS_ClockEcho"), TEXT("DPS_RenderJob"), TEXT("DPS_FrameRequest"), TEXT("DPS_GizmoFrame"),
		TEXT("DPS_JoinRequest"), TEXT("DPS_ControlFrame"), TEXT("DPS_Leave"), TEXT("DPS_GizmoView"), TEXT("DPS_ClockProbe"), TEXT("DPS_StepAck"),
		TEXT("DPS_ClientWorld"), TEXT("DPS_RenderClient"), TEXT("DPS_FrameDone"), TEXT("DPS_RenderJobResult") };
	return !NotWorld.Contains(Topic);
}
FHrotDpsClient::~FHrotDpsClient() { Stop(); }

TArray<FString> FHrotDpsClient::TopicNames()
{
	TArray<FString> Out;
	for (const FHrotDpsTopicDef& D : GHrotDpsTopics) Out.Add(D.Name);
	return Out;
}

FString FHrotDpsClient::Contract() { return HROT_DPS_CONTRACT; }

bool FHrotDpsClient::Start(uint32 InDomain, const FString& InPartition, FString& OutError)
{
	Stop();
	Participant = dds_create_participant(InDomain, nullptr, nullptr);
	if (Participant < 0)
	{
		OutError = FString::Printf(TEXT("dds_create_participant(%u): %hs"), InDomain, dds_strretcode(Participant));
		Participant = 0;
		return false;
	}
	Domain = InDomain;
	Partition = InPartition;
	dds_qos_t* Q = dds_create_qos();
	const FTCHARToUTF8 P(*InPartition);
	const char* Parts[] = { P.Get() };
	dds_qset_partition(Q, 1, Parts);
	Subscriber = dds_create_subscriber(Participant, Q, nullptr);
	Publisher = dds_create_publisher(Participant, Q, nullptr);
	dds_delete_qos(Q);
	if (Subscriber < 0 || Publisher < 0)
	{
		OutError = FString::Printf(TEXT("subscriber/publisher: %hs"), dds_strretcode(Subscriber < 0 ? Subscriber : Publisher));
		Stop();
		return false;
	}
	UE_LOG(LogHrotDps, Log, TEXT("DPS: domain %u, partition '%s', %s"), Domain, *Partition, HROT_DPS_CONTRACT);
	return true;
}

void FHrotDpsClient::Stop()
{
	if (Participant) dds_delete(Participant);   // deletes every reader, writer and topic under it
	Participant = Subscriber = Publisher = 0;
	Topics.Reset(); Readers.Reset(); Writers.Reset(); Counts.Reset();
}

int32 FHrotDpsClient::TopicOf(const FString& Name, const FHrotDpsTopicDef*& OutDef)
{
	OutDef = FindDef(Name);
	if (!OutDef || !Participant) return 0;
	if (const int32* T = Topics.Find(Name)) return *T;
	const int32 T = dds_create_topic(Participant, OutDef->Desc, TCHAR_TO_UTF8(OutDef->Name), nullptr, nullptr);
	if (T < 0) { UE_LOG(LogHrotDps, Error, TEXT("topic %s: %hs"), *Name, dds_strretcode(T)); return 0; }
	return Topics.Add(Name, T);
}

int32 FHrotDpsClient::Subscribe(const FString& Topic, FHandler Handler)
{
	if (FReader* R = Readers.Find(Topic)) { R->Handlers.Add({ ++LastHandlerId, MoveTemp(Handler) }); return LastHandlerId; }
	const FHrotDpsTopicDef* Def;
	const int32 T = TopicOf(Topic, Def);
	if (!T) return 0;
	dds_qos_t* Q = QosOf(*Def);
	const int32 Rd = dds_create_reader(Subscriber, T, Q, nullptr);
	dds_delete_qos(Q);
	if (Rd < 0) { UE_LOG(LogHrotDps, Error, TEXT("reader %s: %hs"), *Topic, dds_strretcode(Rd)); return 0; }
	FReader& R = Readers.Add(Topic);
	R.Handle = Rd;
	R.Handlers.Add({ ++LastHandlerId, MoveTemp(Handler) });
	Counts.FindOrAdd(Topic) = 0;
	return LastHandlerId;
}

void FHrotDpsClient::Refresh(const FString& Topic)
{
	FReader* R = Readers.Find(Topic);
	if (!R) return;
	const FHrotDpsTopicDef* Def;
	const int32 T = TopicOf(Topic, Def);
	if (!T) return;
	dds_delete(R->Handle);
	dds_qos_t* Q = QosOf(*Def);
	R->Handle = dds_create_reader(Subscriber, T, Q, nullptr);
	dds_delete_qos(Q);
	if (R->Handle < 0) { UE_LOG(LogHrotDps, Error, TEXT("reader %s (refresh): %hs"), *Topic, dds_strretcode(R->Handle)); Readers.Remove(Topic); }
}

void FHrotDpsClient::Unsubscribe(const FString& Topic, int32 Id)
{
	if (FReader* R = Readers.Find(Topic)) R->Handlers.RemoveAll([Id](const TPair<int32, FHandler>& H) { return H.Key == Id; });
}

int32 FHrotDpsClient::Poll()
{
	constexpr int32 Batch = 64;
	int32 Total = 0;
	for (TPair<FString, FReader>& It : Readers)
	{
		for (;;)
		{
			void* Buf[Batch] = { nullptr };   // nullptr: DDS loans the samples
			dds_sample_info_t Info[Batch];
			const int32 N = dds_take(It.Value.Handle, Buf, Info, Batch, Batch);
			if (N <= 0) break;
			for (int32 I = 0; I < N; ++I)
			{
				FHrotDpsSample S;
				S.Data = Buf[I];
				S.bValid = Info[I].valid_data;
				S.bAlive = Info[I].instance_state == DDS_IST_ALIVE;
				S.bDisposed = Info[I].instance_state == DDS_IST_NOT_ALIVE_DISPOSED;
				S.SourceTime = double(Info[I].source_timestamp) * 1e-9;
				S.Instance = Info[I].instance_handle;
				for (const TPair<int32, FHandler>& H : It.Value.Handlers) H.Value(S);
				if (bCountStep && IsWorldTopic(It.Key))   // U-18q: the barrier's count (an instance per state topic, a sample per event)
				{
					if (IsEventTopic(It.Key)) ++StepEvents; else StepInstances.Add({ It.Key, S.Instance });
				}
			}
			dds_return_loan(It.Value.Handle, Buf, N);
			Counts.FindOrAdd(It.Key) += N;
			if (IsWorldTopic(It.Key)) WorldTaken += N;   // U-18q: the barrier's count
			Total += N;
			if (N < Batch) break;
		}
	}
	return Total;
}

int32 FHrotDpsClient::MakeRawReader(const FString& Topic)
{
	const FHrotDpsTopicDef* Def;
	const int32 T = TopicOf(Topic, Def);
	if (!T) return 0;
	dds_qos_t* Q = QosOf(*Def);
	const int32 Rd = dds_create_reader(Subscriber, T, Q, nullptr);
	dds_delete_qos(Q);
	if (Rd < 0) { UE_LOG(LogHrotDps, Error, TEXT("raw reader %s: %hs"), *Topic, dds_strretcode(Rd)); return 0; }
	return Rd;
}

int32 FHrotDpsClient::MakeRawWriter(const FString& Topic)
{
	const FHrotDpsTopicDef* Def;
	const int32 T = TopicOf(Topic, Def);
	if (!T) return 0;
	dds_qos_t* Q = QosOf(*Def);
	const int32 W = dds_create_writer(Publisher, T, Q, nullptr);
	dds_delete_qos(Q);
	if (W < 0) { UE_LOG(LogHrotDps, Error, TEXT("raw writer %s: %hs"), *Topic, dds_strretcode(W)); return 0; }
	return W;
}

bool FHrotDpsClient::Dispose(const FString& Topic, const void* Sample)
{
	if (!Writers.Contains(Topic)) return false;   // nothing was ever written on this topic
	return dds_dispose(Writers.FindRef(Topic), Sample) == DDS_RETCODE_OK;
}

bool FHrotDpsClient::Write(const FString& Topic, const void* Sample)
{
	int32 W = Writers.FindRef(Topic);
	if (!W)
	{
		const FHrotDpsTopicDef* Def;
		const int32 T = TopicOf(Topic, Def);
		if (!T) return false;
		dds_qos_t* Q = QosOf(*Def);
		W = dds_create_writer(Publisher, T, Q, nullptr);
		dds_delete_qos(Q);
		if (W < 0) { UE_LOG(LogHrotDps, Error, TEXT("writer %s: %hs"), *Topic, dds_strretcode(W)); return false; }
		Writers.Add(Topic, W);
	}
	return dds_write(W, Sample) == DDS_RETCODE_OK;
}
