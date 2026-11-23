// CE-4651 (U-18p) — round 7 of the DPS contract (SimNetGateway DD-022 rev 5, IDL at gateway ff6b413), the client half the gateway publishes
// without any HROT change:
//
//   SimClock            the station's anchor -> FHrotDpsClock becomes HROT's slave formula (§3b); a new TimelineEpoch = no blend across it
//   ClockProbe / Echo   the offset to the station's wall tick, every second, on a THREAD of its own (below: why)
//   ClockStep           while Stepped, the step's TargetSimTime is presented exactly (§3c); ⏳ StepAck + the D-B4 barrier come with render jobs
//   View (with rates)   the station's suggested camera, dead-reckoned like an entity on the sim clock (§3d); a new CutSeq jumps
//   ClientWorld         the world we present, for the station's load step (§3g): Ready for Session.WorldEpoch, WaitForMe
//
// ⭐ WHY A PROBE THREAD (measured from the plumbing, not assumed): CycloneDDS's sample info has no reception time, and the client takes
// samples once a frame (UHrotDpsSubsystem's ticker). An echo stamped at the next poll is late by up to a frame (~33 ms at 30 fps) on the
// RETURN leg only — NTP's symmetric-delay assumption then biases the offset by about half of that, i.e. ~16 ms of sim time at scale 1.
// The thread writes the probe and waits on the echo reader (a waitset), stamping the echo when it lands.
#include "HrotDpsWorld.h"
#include "HrotDpsNative.h"
#include "HrotDpsSubsystem.h"
#include "Engine/Engine.h"
#include "Engine/World.h"
#include "HAL/PlatformTime.h"
#include "HAL/Runnable.h"
#include "HAL/RunnableThread.h"
#include "Containers/Queue.h"
#include "Misc/DateTime.h"

DEFINE_LOG_CATEGORY_STATIC(LogHrotDpsClock, Log, All);

namespace
{
	using FSimClock = SimNetGateway_Protocol_DdsPlayer_SimClock;
	using FClockProbe = SimNetGateway_Protocol_DdsPlayer_ClockProbe;
	using FClockEcho = SimNetGateway_Protocol_DdsPlayer_ClockEcho;
	using FClockStep = SimNetGateway_Protocol_DdsPlayer_ClockStep;
	using FViewS = SimNetGateway_Protocol_DdsPlayer_View;
	using FClientWorld = SimNetGateway_Protocol_DdsPlayer_ClientWorld;
	double ClockReal() { return FPlatformTime::Seconds(); }
	FVector3d ClockV3(const SimNetGateway_Protocol_DdsPlayer_Vec3d& V) { return FVector3d(V.X, V.Y, V.Z); }
	FVector3d ClockV3(const SimNetGateway_Protocol_DdsPlayer_Vec3f& V) { return FVector3d(V.X, V.Y, V.Z); }
	void ClockCopy(char* To, int32 Cap, const FString& S) { FCStringAnsi::Strncpy(To, TCHAR_TO_UTF8(*S), Cap); }

	const TCHAR* SourceName(uint8 S)
	{
		switch (S)
		{
		case SimNetGateway_Protocol_DdsPlayer_ViewSourceDirector: return TEXT("director");
		case SimNetGateway_Protocol_DdsPlayer_ViewSourceFollow: return TEXT("follow");
		case SimNetGateway_Protocol_DdsPlayer_ViewSourceOperator: return TEXT("operator");
		default: return TEXT("none");
		}
	}
	const TCHAR* ModeName(uint8 M)
	{
		switch (M)
		{
		case SimNetGateway_Protocol_DdsPlayer_ViewModeFree: return TEXT("free");
		case SimNetGateway_Protocol_DdsPlayer_ViewModeOrbit: return TEXT("orbit");
		case SimNetGateway_Protocol_DdsPlayer_ViewModeEyes: return TEXT("eyes");
		case SimNetGateway_Protocol_DdsPlayer_ViewModeShoulder: return TEXT("shoulder");
		case SimNetGateway_Protocol_DdsPlayer_ViewModeChase: return TEXT("chase");
		case SimNetGateway_Protocol_DdsPlayer_ViewModeFixed: return TEXT("fixed");
		default: return TEXT("unknown");
		}
	}
}

/** the probe thread: every second a ClockProbe, then the echo timed as it lands (see the file header) */
class FHrotDpsProbeThread : public FRunnable
{
public:
	struct FEcho { int64 Send, Receive, Transmit, Back; };
	TQueue<FEcho, EQueueMode::Spsc> Echoes;

	FHrotDpsProbeThread(int32 InWriter, int32 InReader, uint64 InClientId, int64 InTickBase, double InTickReal)
		: Writer(InWriter), Reader(InReader), ClientId(InClientId), TickBase(InTickBase), TickReal(InTickReal)
	{
		Thread = FRunnableThread::Create(this, TEXT("HrotDpsClockProbe"), 64 * 1024, TPri_AboveNormal);
	}
	virtual ~FHrotDpsProbeThread() override
	{
		bStop = true;
		if (Thread) { Thread->WaitForCompletion(); delete Thread; }
		if (Waitset > 0) dds_delete(Waitset);
		if (Reader > 0) dds_delete(Reader);
		if (Writer > 0) dds_delete(Writer);
	}
	virtual uint32 Run() override
	{
		Waitset = dds_create_waitset(dds_get_participant(Reader));
		dds_set_status_mask(Reader, DDS_DATA_AVAILABLE_STATUS);
		dds_waitset_attach(Waitset, Reader, Reader);
		while (!bStop)
		{
			FClockProbe P{};
			P.ClientId = ClientId;
			P.ClientSendTicks = Ticks();
			dds_write(Writer, &P);
			// wait for our echo up to 300 ms (an RTT over 200 ms is dropped anyway), then rest out the second
			const double Until = ClockReal() + 0.3;
			bool bGot = false;
			while (!bStop && !bGot && ClockReal() < Until)
			{
				dds_attach_t Fired;
				if (dds_waitset_wait(Waitset, &Fired, 1, DDS_MSECS(50)) <= 0) continue;
				const int64 Back = Ticks();
				void* Buf[8] = { nullptr }; dds_sample_info_t Info[8];
				const int32 N = dds_take(Reader, Buf, Info, 8, 8);
				for (int32 I = 0; I < N; ++I)
				{
					if (!Info[I].valid_data) continue;
					const FClockEcho& E = *static_cast<const FClockEcho*>(Buf[I]);
					if (E.ClientId != ClientId || E.ClientSendTicks != P.ClientSendTicks) continue;   // another client's, or a stale one
					Echoes.Enqueue({ E.ClientSendTicks, E.StationReceiveTicks, E.StationTransmitTicks, Back });
					bGot = true;
				}
				if (N > 0) dds_return_loan(Reader, Buf, N);
			}
			Sent.IncrementExchange();
			for (double Rest = Until + 0.7; !bStop && ClockReal() < Rest;) FPlatformProcess::Sleep(0.02f);
		}
		return 0;
	}
	int32 ProbesSent() const { return Sent.Load(); }

private:
	int64 Ticks() const { return TickBase + int64(FMath::RoundToDouble((ClockReal() - TickReal) * 1e7)); }   // FHrotDpsClock::LocalTicks
	int32 Writer = 0, Reader = 0, Waitset = 0;
	uint64 ClientId = 0;
	int64 TickBase = 0;
	double TickReal = 0;
	TAtomic<bool> bStop{ false };
	TAtomic<int32> Sent{ 0 };
	FRunnableThread* Thread = nullptr;
};

// ── start / stop (called from UHrotDpsWorld::Start / Stop) ──

void UHrotDpsWorld::StartClock(FHrotDpsClient& C, uint64 ClientId)
{
	Clock = FHrotDpsClock();
	const double R = ClockReal();
	Clock.SetTickBase(FDateTime::UtcNow().GetTicks(), R);   // this machine's tick: UTC once, then the monotonic clock (as HROT's own)
	View = FView();
	TimelineCut = -1; NextWorldReport = 0; WorldSaid.Reset(); WorldSaidEpoch = 0; WorldSaidState = 0;
	Anchors = Steps = AckWanted = ViewSamples = 0;
	MyClientId = ClientId;
	const int32 W = C.MakeRawWriter(TEXT("DPS_ClockProbe")), Rd = C.MakeRawReader(TEXT("DPS_ClockEcho"));
	if (W && Rd) Probe = MakeShared<FHrotDpsProbeThread>(W, Rd, ClientId, Clock.LocalTicks(R), R);
	else UE_LOG(LogHrotDpsClock, Warning, TEXT("DPS clock: no probe (writer %d, reader %d) - the offset stays 0"), W, Rd);
}

void UHrotDpsWorld::StopClock() { Probe.Reset(); }   // joins the thread before the client's participant goes

// ── samples ──

void UHrotDpsWorld::OnSimClock(const FHrotDpsSample& S)
{
	if (!S.bValid) return;
	const FSimClock& D = *static_cast<const FSimClock*>(S.Data);
	const double R = ClockReal();
	const bool bStepped = D.Mode == SimNetGateway_Protocol_DdsPlayer_ClockModeStepped;
	if (Clock.OnAnchor(bStepped, D.AnchorWallTicks, D.AnchorSimTime, D.TimeScale, D.TimelineEpoch, D.StepSeq, R)) OnTimelineJump(R);
	if (++Anchors <= 3 || bStepped != bLoggedStepped)
		UE_LOG(LogHrotDpsClock, Display, TEXT("DPS clock: anchor #%d %s sim %.3f scale %.2f epoch %lld step %lld (offset %s)"), Anchors,
			bStepped ? TEXT("stepped") : TEXT("continuous"), D.AnchorSimTime, D.TimeScale, D.TimelineEpoch, D.StepSeq,
			Clock.HasOffset() ? *FString::Printf(TEXT("%.2f ms"), Clock.GetOffsetTicks() * 1e-4) : TEXT("not measured yet"));
	bLoggedStepped = bStepped;
}

void UHrotDpsWorld::OnClockStep(const FHrotDpsSample& S)
{
	if (!S.bValid) return;
	const FClockStep& D = *static_cast<const FClockStep*>(S.Data);
	const double R = ClockReal();
	if (Clock.OnStep(D.StepSeq, D.TargetSimTime, D.TimelineEpoch, R)) OnTimelineJump(R);
	++Steps;
	if (D.AckRequired) ++AckWanted;   // ⏳ a render job's step: StepAck after the D-B4 barrier (round 7 §2, with FrameRequest)
}

void UHrotDpsWorld::OnClockEcho(const FHrotDpsSample&) {}   // taken by the probe thread (its own reader); nothing reaches Poll

void UHrotDpsWorld::OnTimelineJump(double R)
{
	TimelineCut = R;
	for (TPair<int32, FEnt>& It : Ents) It.Value.BlendStart = -1;   // no correction across a seek / a load
	View.BlendStart = -1;
	UE_LOG(LogHrotDpsClock, Display, TEXT("DPS clock: timeline epoch %lld - sim %.3f, curves restart"), Clock.GetTimelineEpoch(), Clock.Now(R));
}

void UHrotDpsWorld::OnView(const FHrotDpsSample& S)
{
	if (!S.bValid) { if (!S.bAlive) View.bHas = false; return; }
	const FViewS& D = *static_cast<const FViewS*>(S.Data);
	const double R = ClockReal();
	Clock.OnSample(D.SimTime, R);
	FHrotDpsTrackPoint K;
	K.T = D.SimTime;
	K.P = ClockV3(D.Position);
	K.Q = FQuat4d(D.Orientation.X, D.Orientation.Y, D.Orientation.Z, D.Orientation.W).GetNormalized();
	K.V = ClockV3(D.Velocity); K.A = ClockV3(D.Acceleration);
	K.W = K.Q.RotateVector(ClockV3(D.AngularVelocity));   // the contract states it in the BODY frame; the curve turns by a world-frame rate
	// one CutSeq blends like an entity (from where the OLD curve puts it now); a new cut, a new source or a timeline jump: a hard cut
	const bool bSameShot = View.bHas && View.CutSeq == D.CutSeq && View.Source == uint8(D.Source) && View.SampleReal >= TimelineCut;
	if (bSameShot) { View.Blend = HrotDpsMotion::Correction(ViewPoseAt(R), HrotDpsMotion::Extrapolate(K, Clock.Now(R))); View.BlendStart = R; }
	else View.BlendStart = -1;
	View.bHas = D.Source != SimNetGateway_Protocol_DdsPlayer_ViewSourceNone;
	View.Source = uint8(D.Source); View.Mode = uint8(D.Mode);
	View.TargetId = D.TargetId; View.Distance = D.Distance; View.Side = D.Side == 0 ? 1.f : D.Side;
	View.Fov = D.FovDeg > 1.f ? D.FovDeg : 60.f; View.FovRate = D.FovRate;
	View.CutSeq = D.CutSeq;
	View.Fade = D.Fade; View.Subtitle = UTF8_TO_TCHAR(D.Subtitle); View.SubPos = uint8(D.SubtitlePosition); View.SubAlpha = D.SubtitleAlpha;   // U-19e
	View.Track = K;
	View.SampleReal = R;
	++ViewSamples;
}

UHrotDpsWorld::FOverlayNow UHrotDpsWorld::OverlayNow() const
{
	FOverlayNow O;
	O.bHas = View.bHas; O.Subtitle = View.Subtitle; O.Pos = View.SubPos; O.Alpha = View.SubAlpha; O.Fade = View.Fade;
	return O;
}

FHrotDpsPose UHrotDpsWorld::ViewPoseAt(double R) const
{
	const FHrotDpsPose Curve = HrotDpsMotion::Extrapolate(View.Track, Clock.Now(R));
	if (View.BlendStart < 0) return Curve;
	return HrotDpsMotion::Corrected(Curve, View.Blend, (R - View.BlendStart) / HrotDpsMotion::BlendSeconds);
}

UHrotDpsWorld::FViewNow UHrotDpsWorld::ViewNow() const
{
	FViewNow Out;
	if (!View.bHas) return Out;
	const double R = ClockReal();
	const FHrotDpsPose Pose = ViewPoseAt(R);
	Out.bHas = true;
	Out.Source = View.Source; Out.Mode = View.Mode; Out.TargetId = View.TargetId;
	Out.Distance = View.Distance; Out.Side = View.Side; Out.CutSeq = View.CutSeq;
	Out.P = Pose.P; Out.Q = Pose.Q;
	const double Ahead = FMath::Clamp(Clock.Now(R) - View.Track.T, 0.0, HrotDpsMotion::MaxExtrapolation);
	Out.FovDeg = FMath::Clamp(View.Fov + View.FovRate * float(Ahead), 5.f, 150.f);
	return Out;
}

// ── every frame: the echoes the thread measured, ClientWorld ──

void UHrotDpsWorld::TickClock(double R)
{
	if (Probe)
	{
		FHrotDpsProbeThread::FEcho E;
		while (Probe->Echoes.Dequeue(E))
			if (!Clock.OnEcho(E.Send, E.Receive, E.Transmit, E.Back) && Clock.GetEchoesDropped() <= 3)
				UE_LOG(LogHrotDpsClock, Display, TEXT("DPS clock: echo dropped, rtt %.1f ms"), Clock.GetLastRttMs());
		if (Clock.GetEchoes() == 1 && !bLoggedOffset)
		{
			bLoggedOffset = true;
			UE_LOG(LogHrotDpsClock, Display, TEXT("DPS clock: station offset %.3f ms (rtt %.2f ms)"), Clock.GetOffsetTicks() * 1e-4, Clock.GetLastRttMs());
		}
	}
	// ClientWorld (§3g): Ready for the Session's world epoch — this IG presents the level it was started in, so a new epoch is ready once
	// its entities were dropped (Clear, in OnSession); before any Session: Loading. ⚠ VOLATILE QoS: a gateway that starts after us would
	// never see a one-off sample, so it is re-said every 2 s as well as on a change
	if (bLeaving) return;   // U-18r: loading another level — the subsystem says Loading until the new world takes over
	UHrotDpsSubsystem* Dps = GEngine ? GEngine->GetEngineSubsystem<UHrotDpsSubsystem>() : nullptr;
	if (!Dps || !Dps->IsRunning()) return;
	uint8 State; uint32 Ep = Epoch; FString Step, Reason;
	if (!WorldFail.IsEmpty()) { State = SimNetGateway_Protocol_DdsPlayer_ClientWorldStateFailed; Step = FString::Printf(TEXT("terrain %s"), *ShownTerrain); Reason = WorldFail; }
	else if (bEpoch) { State = SimNetGateway_Protocol_DdsPlayer_ClientWorldStateReady; Step = FString::Printf(TEXT("level %s"), *GetWorld()->GetMapName()); }
	else if (Dps->IsTraveling()) { State = SimNetGateway_Protocol_DdsPlayer_ClientWorldStateLoading; Ep = Dps->GetTravelEpoch(); Step = Dps->GetTravelStep(); }
	else { State = SimNetGateway_Protocol_DdsPlayer_ClientWorldStateLoading; Step = TEXT("waiting for the station's Session"); }
	if (R < NextWorldReport && State == WorldSaidState && Ep == WorldSaidEpoch) return;
	NextWorldReport = R + 2.0;
	FClientWorld W{};
	W.ClientId = MyClientId;
	W.WorldEpoch = Ep;
	W.State = SimNetGateway_Protocol_DdsPlayer_ClientWorldState(State);
	ClockCopy(W.Step, sizeof(W.Step), Step);
	ClockCopy(W.Reason, sizeof(W.Reason), Reason);
	W.WaitForMe = bWaitForMe;
	const TCHAR* Name = State == SimNetGateway_Protocol_DdsPlayer_ClientWorldStateReady ? TEXT("Ready") : State == SimNetGateway_Protocol_DdsPlayer_ClientWorldStateFailed ? TEXT("Failed") : TEXT("Loading");
	if (Dps->Client().Write(TEXT("DPS_ClientWorld"), &W) && (State != WorldSaidState || Ep != WorldSaidEpoch))
		UE_LOG(LogHrotDpsClock, Display, TEXT("DPS ClientWorld: epoch %u %s '%s'%s wait-for-me %d"), Ep, Name, *Step, Reason.IsEmpty() ? TEXT("") : *FString::Printf(TEXT(" (%s)"), *Reason), bWaitForMe);
	WorldSaid = FString::Printf(TEXT("%u %s %s"), Ep, Name, *Step);
	WorldSaidState = State; WorldSaidEpoch = Ep;
}

// ── read back ──

FString UHrotDpsWorld::DescribeClock() const
{
	const double R = ClockReal();
	const TCHAR* Kind = Clock.IsAnchored() ? (Clock.IsStepped() ? TEXT("anchored stepped") : TEXT("anchored continuous")) : TEXT("samples");
	return FString::Printf(TEXT("%s sim %.3f scale %.2f epoch %lld step %lld | offset %s rtt %.2f ms echoes %d/%d dropped, probes %d | steps %d (ack wanted %d) anchors %d views %d"),
		Kind, Clock.Now(R), Clock.GetScale(), Clock.GetTimelineEpoch(), Clock.GetStepSeq(),
		Clock.HasOffset() ? *FString::Printf(TEXT("%.3f ms"), Clock.GetOffsetTicks() * 1e-4) : TEXT("-"), Clock.GetLastRttMs(),
		Clock.GetEchoes(), Clock.GetEchoesDropped(), Probe ? Probe->ProbesSent() : 0, Steps, AckWanted, Anchors, ViewSamples);
}

FString UHrotDpsWorld::DescribeView() const
{
	const FViewNow V = ViewNow();
	if (!V.bHas) return TEXT("none");
	return FString::Printf(TEXT("%s %s target %d d %.2f side %.0f cut %u at (%.2f, %.2f, %.2f) fov %.1f"), SourceName(V.Source), ModeName(V.Mode),
		V.TargetId, V.Distance, V.Side, V.CutSeq, V.P.X, V.P.Y, V.P.Z, V.FovDeg);
}
