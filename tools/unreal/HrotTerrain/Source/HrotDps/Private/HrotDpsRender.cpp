// CE-4651 (U-18q) — see HrotDpsRender.h
#include "HrotDpsRender.h"
#include "ProfilingDebugging/CsvProfiler.h"
#include "HrotDpsOverlay.h"
#include "HrotDpsNative.h"
#include "HrotDpsSubsystem.h"
#include "HrotDpsWorld.h"
#include "HrotDpsMotion.h"
#include "HrotShotRenderer.h"
#include "HrotSoundscape.h"
#include "HrotDpsCameraPawn.h"
#include "Kismet/GameplayStatics.h"
#include "Engine/Engine.h"
#include "Engine/World.h"
#include "Engine/GameInstance.h"
#include "Engine/Canvas.h"
#include "Engine/Font.h"
#include "Engine/SceneCapture2D.h"
#include "Components/SceneCaptureComponent2D.h"
#include "Engine/TextureRenderTarget2D.h"
#include "Kismet/KismetRenderingLibrary.h"
#include "ImageUtils.h"
#include "AudioDevice.h"
#include "AudioMixerDevice.h"
#include "AudioMixerSubmix.h"
#include "AudioMixerPlatformNonRealtime.h"
#include "ContentStreaming.h"
#if WITH_EDITOR
#include "ShaderCompiler.h"   // the warm-up flushes shader compiles (a packaged client has them cooked)
#endif
#include "HAL/IConsoleManager.h"
#include "HAL/PlatformTime.h"
#include "HAL/FileManager.h"
#include "Misc/App.h"
#include "Misc/CoreDelegates.h"
#include "Misc/CommandLine.h"
#include "Misc/FileHelper.h"
#include "Misc/Parse.h"
#include "Misc/Paths.h"

DEFINE_LOG_CATEGORY_STATIC(LogHrotDpsRender, Log, All);

static TAutoConsoleVariable<int32> CVarAudioLatencyFrames(TEXT("hrot.render.AudioLatencyFrames"), 2,
	TEXT("U-18q: film frames of mixer latency dropped from the head of a job's track (and drained at its end). Measured at 30 fps in PIE: a shot 29 m away (85 ms of air) started 159-186 ms after its frame (sync decodes; the spread is one 1024-sample mixer buffer) -> 2 frames"));
static TAutoConsoleVariable<int32> CVarAudioDiag(TEXT("hrot.render.AudioDiag"), 0,
	TEXT("U-18q: 1 = log the mixer's state every 10 frames of a render job"));
static TAutoConsoleVariable<int32> CVarCutWarmUps(TEXT("hrot.render.CutWarmUps"), 4,
	TEXT("U-18q: extra captures at a cut (anti-aliasing history, exposure, streaming) before the frame's own"));

namespace
{
	using FRenderJob = SimNetGateway_Protocol_DdsPlayer_RenderJob;
	using FFrameReq = SimNetGateway_Protocol_DdsPlayer_FrameRequest;
	using FClockStep = SimNetGateway_Protocol_DdsPlayer_ClockStep;
	double RenderReal() { return FPlatformTime::Seconds(); }
	void RenderCopy(char* To, int32 Cap, const FString& S) { FCStringAnsi::Strncpy(To, TCHAR_TO_UTF8(*S), Cap); }
	Audio::FMixerDevice* MixerOf(UWorld* W)
	{
		if (!W) return nullptr;
		FAudioDeviceHandle H = W->GetAudioDevice();
		FAudioDevice* D = H.GetAudioDevice();
		return D ? static_cast<Audio::FMixerDevice*>(D) : nullptr;   // UE 5.8: every device is the audio mixer
	}
	Audio::FMixerPlatformNonRealtime* NrtOf(Audio::FMixerDevice* M)
	{
		Audio::IAudioMixerPlatformInterface* P = M ? M->GetAudioMixerPlatform() : nullptr;
		return P && P->IsNonRealtime() ? static_cast<Audio::FMixerPlatformNonRealtime*>(P) : nullptr;
	}
	const TCHAR* RenderStateName(uint8 S)
	{
		switch (S)
		{
		case SimNetGateway_Protocol_DdsPlayer_RenderStateStarting: return TEXT("Starting");
		case SimNetGateway_Protocol_DdsPlayer_RenderStateRunning: return TEXT("Running");
		case SimNetGateway_Protocol_DdsPlayer_RenderStateDone: return TEXT("Done");
		case SimNetGateway_Protocol_DdsPlayer_RenderStateFailed: return TEXT("Failed");
		case SimNetGateway_Protocol_DdsPlayer_RenderStateCancelled: return TEXT("Cancelled");
		default: return TEXT("Unknown");
		}
	}
}

UHrotDpsRender* UHrotDpsRender::Get(const UObject* WorldContext)
{
	const UWorld* W = GEngine ? GEngine->GetWorldFromContextObject(WorldContext, EGetWorldErrorMode::ReturnNull) : nullptr;
	UGameInstance* GI = W ? W->GetGameInstance() : nullptr;
	return GI ? GI->GetSubsystem<UHrotDpsRender>() : nullptr;
}

UWorld* UHrotDpsRender::World() const
{
	const UGameInstance* GI = GetGameInstance();
	return GI ? GI->GetWorld() : nullptr;
}

void UHrotDpsRender::ApplyWorld()
{
	// the job's per-world setup, applied at the job's start and again in each world the client opens during it (CE-11064)
	UWorld* W = World();
	SetupWorld = W;
	if (!W) return;
	Capture = nullptr; bHadFrame = false;   // the capture actor belonged to the old world; the first frame here is a cut (warm-ups)
	if (bAudio)
	{
		if (UHrotShotRenderer* SR = UHrotShotRenderer::Get(W)) SR->ActivateWorldAudio();
		if (UHrotSoundscape* Snd = UHrotSoundscape::Get(W)) Snd->SetSpatialize(false);   // UE-381932: see UHrotSoundscape::SetSpatialize
	}
	// sounds keep sim time during the job (§3f rule 4): the speed-of-sound delays read the sim clock
	if (UHrotShotRenderer* SR = UHrotShotRenderer::Get(W))
		SR->TimeSource = [WW = TWeakObjectPtr<UWorld>(W)]() { UHrotDpsWorld* DW = WW.IsValid() ? UHrotDpsWorld::Get(WW.Get()) : nullptr; return DW ? DW->SimNow() : 0.0; };
}

void UHrotDpsRender::Initialize(FSubsystemCollectionBase& Collection)
{
	Super::Initialize(Collection);
	FString R;
	if (FParse::Param(FCommandLine::Get(), TEXT("HrotDpsRender")))
	{
		if (!FParse::Value(FCommandLine::Get(), TEXT("HrotDpsRenderRoot="), R)) R.Reset();
		Enable(R);
	}
	EndFrame = FCoreDelegates::OnEndFrame.AddUObject(this, &UHrotDpsRender::OnEndFrame);
}

void UHrotDpsRender::Deinitialize()
{
	FCoreDelegates::OnEndFrame.Remove(EndFrame);
	if (bJob) EndJob(false);
	if (GEngine)
		if (UHrotDpsSubsystem* Dps = GEngine->GetEngineSubsystem<UHrotDpsSubsystem>())
			for (const TPair<FString, int32>& S : Subs) Dps->Client().Unsubscribe(S.Key, S.Value);
	Subs.Reset();
	Super::Deinitialize();
}

void UHrotDpsRender::Enable(const FString& RenderRoot)
{
	bEnabled = true;
	Root = RenderRoot.IsEmpty() ? FPaths::Combine(FPaths::ProjectSavedDir(), TEXT("HrotRender")) : RenderRoot;
	Root = FPaths::ConvertRelativePathToFull(Root);
	IFileManager::Get().MakeDirectory(*Root, true);
	UE_LOG(LogHrotDpsRender, Display, TEXT("DPS render client: on, render root '%s'"), *Root);
}

void UHrotDpsRender::Connect()
{
	UHrotDpsSubsystem* Dps = GEngine ? GEngine->GetEngineSubsystem<UHrotDpsSubsystem>() : nullptr;
	if (!Dps || !Dps->IsRunning()) { Subs.Reset(); return; }
	if (Subs.Num() && Dps->Client().GetCounts().Contains(TEXT("DPS_RenderJob"))) return;   // still the same client
	Subs.Reset();
	ClientId = Dps->GetClientId();
	FHrotDpsClient& C = Dps->Client();
	auto Sub = [this, &C](const TCHAR* Topic, void (UHrotDpsRender::*Fn)(const FHrotDpsSample&))
	{
		Subs.Add({ Topic, C.Subscribe(Topic, [this, Fn](const FHrotDpsSample& S) { (this->*Fn)(S); }) });
	};
	Sub(TEXT("DPS_RenderJob"), &UHrotDpsRender::OnJob);
	Sub(TEXT("DPS_ClockStep"), &UHrotDpsRender::OnStep);
	Sub(TEXT("DPS_FrameRequest"), &UHrotDpsRender::OnFrame);
	// the barrier counts EVERY world topic the gateway writes: a topic nobody here reads would never be counted — read them all
	for (const FString& T : FHrotDpsClient::TopicNames())
		if (FHrotDpsClient::IsWorldTopic(T)) Subs.Add({ T, C.Subscribe(T, [](const FHrotDpsSample&) {}) });
	UE_LOG(LogHrotDpsRender, Display, TEXT("DPS render client: client %llx, %d readers"), ClientId, Subs.Num());
}

void UHrotDpsRender::Announce(double Now)
{
	if (Now < NextAnnounce) return;
	UHrotDpsSubsystem* Dps = GEngine->GetEngineSubsystem<UHrotDpsSubsystem>();
	NextAnnounce = Now + 2.0;   // ⚠ player QoS is VOLATILE: a gateway that starts later must hear it again
	SimNetGateway_Protocol_DdsPlayer_RenderClient R{};
	R.ClientId = ClientId;
	RenderCopy(R.Name, sizeof(R.Name), FString::Printf(TEXT("unreal-ig %s"), FPlatformProcess::ComputerName()));
	R.Ready = true;
	R.MaxWidth = MaxWidth; R.MaxHeight = MaxHeight; R.MaxSupersample = MaxSupersample;
	R.Formats = 1;   // PNG
	Audio::FMixerDevice* M = MixerOf(World());
	R.AudioFormats = NrtOf(M) ? 1u : 0u;   // WAV PCM16, only with the non-real-time device
	R.MaxSampleRate = M ? int32(M->GetSampleRate()) : 0;
	RenderCopy(R.ReadyJobId, sizeof(R.ReadyJobId), ReadyJobId);
	Dps->Client().Write(TEXT("DPS_RenderClient"), &R);
}

CSV_DECLARE_CATEGORY_EXTERN(HrotDps);

void UHrotDpsRender::Tick(float)
{
	CSV_SCOPED_TIMING_STAT(HrotDps, Render);
	if (!bEnabled) return;
	Connect();
	if (!Subs.Num()) return;
	const double Now = RenderReal();
	if (bJob && World() != SetupWorld.Get())
	{
		UE_LOG(LogHrotDpsRender, Display, TEXT("DPS render job %s goes on in the new world %s (%d frames so far, queue %d)"), *JobId, World() ? *World()->GetMapName() : TEXT("-"), Frames, Queue.Num());
		ApplyWorld();
		IStreamingManager::Get().StreamAllResources(5.0f);
	}
	if (bJob && OrphanSince > 0 && Now - OrphanSince >= OrphanGrace)
	{
		LastWhy = FString::Printf(TEXT("the job's instance went away and did not come back within %.0f s"), OrphanGrace);
		EndJob(false);
	}
	Announce(Now);
}

// ── the job ──

void UHrotDpsRender::OnJob(const FHrotDpsSample& S)
{
	const FRenderJob& J = *static_cast<const FRenderJob*>(S.Data);
	const FString Id = UTF8_TO_TCHAR(J.JobId);
	// CE-11064: a world boundary rebuilds the gateway's writers and the job's instance goes away with the old one, while the gateway keeps the
	// job and writes it again (Running) from the new writer — measured on the director's reel and on a take's load. So the instance going away
	// is NOT the end: hold the job (frames, track, clock) for OrphanGrace; the station's Done / Failed / Cancelled is what ends it
	if (!S.bValid)
	{
		if (!S.bAlive && bJob && Id == JobId && OrphanSince == 0)
		{
			OrphanSince = RenderReal();
			UE_LOG(LogHrotDpsRender, Display, TEXT("DPS RenderJob %s: its instance went away - held %.0f s for it to come back"), *Id, OrphanGrace);
		}
		return;
	}
	if (J.ClientId != ClientId) return;
	UE_LOG(LogHrotDpsRender, Display, TEXT("DPS RenderJob %s %s (%s) %d fps %dx%d x%d audio %d"), *Id, RenderStateName(uint8(J.State)), UTF8_TO_TCHAR(J.What), J.Fps,
		J.Width, J.Height, int32(J.Supersample), J.Audio ? 1 : 0);
	if (bJob && Id == JobId && OrphanSince > 0)
	{
		UE_LOG(LogHrotDpsRender, Display, TEXT("DPS RenderJob %s: back after %.2f s - the job goes on"), *Id, RenderReal() - OrphanSince);
		OrphanSince = 0;
	}
	switch (J.State)
	{
	case SimNetGateway_Protocol_DdsPlayer_RenderStateStarting: if (!bJob || Id != JobId) BeginJob(J); break;
	case SimNetGateway_Protocol_DdsPlayer_RenderStateDone: if (bJob && Id == JobId) EndJob(true); break;
	case SimNetGateway_Protocol_DdsPlayer_RenderStateFailed:
	case SimNetGateway_Protocol_DdsPlayer_RenderStateCancelled:
		if (bJob && Id == JobId) { LastWhy = FString::Printf(TEXT("job %s by the station: %s"), RenderStateName(uint8(J.State)), UTF8_TO_TCHAR(J.Reason)); EndJob(false); }
		break;
	default: break;
	}
}

bool UHrotDpsRender::SafePath(const FString& Rel, FString& OutAbs) const
{
	// relative to the render root; ⛔ absolute paths and ".." (DD-022 §6: "the client refuses it")
	if (Rel.IsEmpty() || !FPaths::IsRelative(Rel) || Rel.Contains(TEXT("..")) || Rel.StartsWith(TEXT("/")) || Rel.StartsWith(TEXT("\\"))) return false;
	OutAbs = FPaths::ConvertRelativePathToFull(FPaths::Combine(Root, Rel));
	return OutAbs.StartsWith(Root);
}

void UHrotDpsRender::FailJob(const FString& Why)
{
	LastWhy = Why; bJobOk = false;
	UE_LOG(LogHrotDpsRender, Warning, TEXT("DPS render job %s fails: %s"), *JobId, *Why);
	UHrotDpsSubsystem* Dps = GEngine->GetEngineSubsystem<UHrotDpsSubsystem>();
	SimNetGateway_Protocol_DdsPlayer_RenderJobResult R{};
	R.ClientId = ClientId; RenderCopy(R.JobId, sizeof(R.JobId), JobId);
	R.Status = SimNetGateway_Protocol_DdsPlayer_FrameStatusFailed;
	RenderCopy(R.Reason, sizeof(R.Reason), Why);
	Dps->Client().Write(TEXT("DPS_RenderJobResult"), &R);
}

void UHrotDpsRender::BeginJob(const FRenderJob& J)
{
	if (bJob) EndJob(false);
	JobId = UTF8_TO_TCHAR(J.JobId);
	bJob = true; bJobOk = true; LastWhy.Reset(); OrphanSince = 0;
	Fps = FMath::Max(1, J.Fps); Width = J.Width; Height = J.Height; Supersample = FMath::Max(1, int32(J.Supersample));
	bAudio = J.Audio; SampleRate = J.SampleRate; Channels = FMath::Max(1, int32(J.Channels));
	Dir = UTF8_TO_TCHAR(J.Dir);
	Queue.Reset(); StepsSeen.Reset();
	Frames = Written = Failed = Silences = WarmUps = BarrierWaits = BarrierTimeouts = Overcounts = Repeats = RepeatFrames = 0;
	Presented.Reset();
	bHadFrame = false; LastEpoch = -1;
	Track.Reset(); TrackChannels = TrackRate = 0; AudioCarry = 0; Silent.Reset(); TrackFilmSamples = 0;
	// the limits first: a job larger than we can is refused before frame 0, with the limit named
	FString DirAbs;
	if (Width <= 0 || Height <= 0 || Width > MaxWidth || Height > MaxHeight) { FailJob(FString::Printf(TEXT("size %dx%d outside 1..%dx%d"), Width, Height, MaxWidth, MaxHeight)); return; }
	if (Supersample > MaxSupersample) { FailJob(FString::Printf(TEXT("supersample %d over the limit %d"), Supersample, MaxSupersample)); return; }
	if (!SafePath(Dir.IsEmpty() ? FString(TEXT(".")) : Dir, DirAbs) && !Dir.IsEmpty()) { FailJob(FString::Printf(TEXT("Dir '%s' is not a path under the render root"), *Dir)); return; }
	Audio::FMixerDevice* M = MixerOf(World());
	if (bAudio)
	{
		if (!NrtOf(M)) { FailJob(TEXT("audio asked, but this process has the real-time audio device (start the render client with -deterministicaudio)")); return; }
		if (SampleRate != 0 && SampleRate != int32(M->GetSampleRate())) { FailJob(FString::Printf(TEXT("sample rate %d, the mixer runs at %d"), SampleRate, int32(M->GetSampleRate()))); return; }
		if (Channels > 2) { FailJob(FString::Printf(TEXT("%d channels, at most 2"), Channels)); return; }
	}
	// the job's clock: a FIXED time step — FilmRate / Fps per frame, IdleDelta while we wait (MRQ runs the engine the same way)
	bPrevFixed = FApp::UseFixedTimeStep(); PrevFixedDelta = FApp::GetFixedDeltaTime();
	FApp::SetUseFixedTimeStep(true); FApp::SetFixedDeltaTime(IdleDelta);
	// MRQ's settings for the non-real-time device, set the way it sets them (SetWithCurrentPriority: a SetByCode write loses to a higher
	// priority already there). ⚠ measured (U-18q): without them the track came out 574 s long (au.nrt.RenderEveryTick renders FApp's
	// delta at start, ~1 s, on EVERY engine tick) and digital silence with 3 sounds active (au.NeverMuteNonRealtimeAudioDevices)
	PrevCvars.Reset();
	for (const TPair<const TCHAR*, int32>& C : { TPair<const TCHAR*, int32>(TEXT("au.nrt.RenderEveryTick"), 0), TPair<const TCHAR*, int32>(TEXT("au.NeverMuteNonRealtimeAudioDevices"), 1),
	                                            TPair<const TCHAR*, int32>(TEXT("au.NeverDisableSubmixes"), 1),
	                                            // ⚠ measured (U-18q): async decodes finish in WALL time while the mixer runs in FILM time — the same shot
	                                            // landed 59 ms and 181 ms after its frame in two identical runs; decoding inline makes the film's sound repeatable
	                                            TPair<const TCHAR*, int32>(TEXT("au.ForceSyncAudioDecodes"), 1) })
		if (IConsoleVariable* V = IConsoleManager::Get().FindConsoleVariable(C.Key))
		{
			PrevCvars.Add(C.Key, V->GetInt());
			V->SetWithCurrentPriority(C.Value, NAME_None, ECVF_SetByConsole, ECVF_SetByScalability);
		}
		else UE_LOG(LogHrotDpsRender, Warning, TEXT("no console variable %s"), C.Key);
	ApplyWorld();
	// warm-up: shaders and streaming (the first frame after a rebuild hitched 28 s — DD-022 Q-4)
	const double T0 = RenderReal();
#if WITH_EDITOR
	if (GShaderCompilingManager) GShaderCompilingManager->FinishAllCompilation();
#endif
	IStreamingManager::Get().StreamAllResources(5.0f);
	if (bAudio) StartRecording();
	ReadyJobId = JobId; NextAnnounce = 0;   // said at once
	Phase = EPhase::Waiting;
	if (UHrotDpsSubsystem* Dps = GEngine->GetEngineSubsystem<UHrotDpsSubsystem>()) Dps->Client().ResetStepCount(true);   // step 0 counts from ReadyJobId
	UE_LOG(LogHrotDpsRender, Display, TEXT("DPS render job %s ready after %.1f s warm-up (%dx%d x%d, %d fps, audio %s) -> '%s'"), *JobId, RenderReal() - T0,
		Width, Height, Supersample, Fps, bAudio ? *FString::Printf(TEXT("%d Hz x %d"), int32(M->GetSampleRate()), Channels) : TEXT("off"), *DirAbs);
}

void UHrotDpsRender::EndJob(bool bDone)
{
	if (!bJob) return;
	if (bRecording && bDone)   // drain the mixer's latency so the tail is not lost (MRQ does the same on its last shot)
		if (const int32 Lat = CVarAudioLatencyFrames.GetValueOnGameThread(); Lat > 0)
			if (Audio::FMixerDevice* M = MixerOf(World())) if (Audio::FMixerPlatformNonRealtime* Nrt = NrtOf(M)) { M->Update(true); Nrt->RenderAudio(double(Lat) / Fps); }
	if (bRecording) StopRecording();
	if (bDone && bJobOk)
	{
		SimNetGateway_Protocol_DdsPlayer_RenderJobResult R{};
		R.ClientId = ClientId; RenderCopy(R.JobId, sizeof(R.JobId), JobId);
		R.Status = SimNetGateway_Protocol_DdsPlayer_FrameStatusWritten;
		if (bAudio)
		{
			const FString Rel = Dir.IsEmpty() ? JobId + TEXT(".wav") : FPaths::Combine(Dir, JobId + TEXT(".wav"));
			FString Abs, Why; int64 N = 0;
			if (SafePath(Rel, Abs) && WriteTrack(Abs, N, Why)) { RenderCopy(R.TrackFile, sizeof(R.TrackFile), Rel); R.TrackSamples = N; }
			else { R.Status = SimNetGateway_Protocol_DdsPlayer_FrameStatusFailed; RenderCopy(R.Reason, sizeof(R.Reason), Why.IsEmpty() ? TEXT("track path") : Why); LastWhy = Why; }
			UE_LOG(LogHrotDpsRender, Display, TEXT("DPS render job %s: track '%s' %lld samples (%d frames x %d Hz / %d fps = %lld)"), *JobId, *Rel, N, Frames, TrackRate, Fps,
				int64(FMath::RoundToDouble(double(Frames) * TrackRate / Fps)));
		}
		GEngine->GetEngineSubsystem<UHrotDpsSubsystem>()->Client().Write(TEXT("DPS_RenderJobResult"), &R);
	}
	UE_LOG(LogHrotDpsRender, Display, TEXT("DPS render job %s ended (%s): %s"), *JobId, bDone ? TEXT("done") : TEXT("dropped"), *Describe());
	FApp::SetUseFixedTimeStep(bPrevFixed); FApp::SetFixedDeltaTime(PrevFixedDelta);
	for (const TPair<FString, int32>& C : PrevCvars)
		if (IConsoleVariable* V = IConsoleManager::Get().FindConsoleVariable(*C.Key)) V->SetWithCurrentPriority(C.Value, NAME_None, ECVF_SetByConsole, ECVF_SetByScalability);
	PrevCvars.Reset();
	if (UHrotShotRenderer* SR = UHrotShotRenderer::Get(World())) SR->TimeSource = nullptr;
	if (UHrotSoundscape* Snd = UHrotSoundscape::Get(World())) Snd->SetSpatialize(true);
	if (UHrotDpsSubsystem* Dps = GEngine->GetEngineSubsystem<UHrotDpsSubsystem>()) Dps->Client().ResetStepCount(false);
	SetupWorld = nullptr;
	bJob = false; Phase = EPhase::Idle; ReadyJobId.Reset(); NextAnnounce = 0; Queue.Reset(); OrphanSince = 0;
}

// ── steps and frames ──

void UHrotDpsRender::WriteDone(const FString& Job, int32 Index, const FDone& Done)
{
	SimNetGateway_Protocol_DdsPlayer_FrameDone D{};
	D.ClientId = ClientId; RenderCopy(D.JobId, sizeof(D.JobId), Job);
	D.FrameIndex = Index; D.StepSeq = Done.StepSeq;
	D.Status = Done.bOk ? SimNetGateway_Protocol_DdsPlayer_FrameStatusWritten : SimNetGateway_Protocol_DdsPlayer_FrameStatusFailed;
	RenderCopy(D.File, sizeof(D.File), Done.File); D.AudioSamples = Done.AudioSamples; RenderCopy(D.Reason, sizeof(D.Reason), Done.Why); D.RenderMs = Done.Ms;
	GEngine->GetEngineSubsystem<UHrotDpsSubsystem>()->Client().Write(TEXT("DPS_FrameDone"), &D);
}

void UHrotDpsRender::OnStep(const FHrotDpsSample& S)
{
	if (!S.bValid || !bJob) return;
	const FClockStep& D = *static_cast<const FClockStep*>(S.Data);
	StepsSeen.Add(D.StepSeq, { D.Samples, D.AckRequired });
}

void UHrotDpsRender::OnFrame(const FHrotDpsSample& S)
{
	if (!S.bValid || !bJob) return;
	const FFrameReq& D = *static_cast<const FFrameReq*>(S.Data);
	if (JobId != UTF8_TO_TCHAR(D.JobId)) return;
	// a repeat (T-086): answered from the frame already presented, or dropped while the first copy is still queued — never rendered twice
	if (const FDone* Was = Presented.Find(D.FrameIndex))
	{
		++Repeats;
		UE_LOG(LogHrotDpsRender, Display, TEXT("frame %d repeated by the station (step %lld) - answered from the frame already presented"), D.FrameIndex, D.StepSeq);
		WriteDone(JobId, D.FrameIndex, *Was);
		return;
	}
	for (const FRequest& Q : Queue)
		if (Q.Index == D.FrameIndex)
		{
			++Repeats;
			UE_LOG(LogHrotDpsRender, Display, TEXT("frame %d repeated by the station while still queued - answered once, when presented"), D.FrameIndex);
			return;
		}
	FRequest R;
	R.JobId = JobId; R.Index = D.FrameIndex; R.Kind = uint8(D.Kind); R.StepSeq = D.StepSeq; R.SimTime = D.SimTime;
	R.FilmRate = D.FilmRate > 0 ? D.FilmRate : 1.f;
	R.P = FVector3d(D.Position.X, D.Position.Y, D.Position.Z);
	R.Q = FQuat4d(D.Orientation.X, D.Orientation.Y, D.Orientation.Z, D.Orientation.W).GetNormalized();
	R.Fov = D.FovDeg > 1.f ? D.FovDeg : 60.f;
	R.Width = D.Width > 0 ? D.Width : Width; R.Height = D.Height > 0 ? D.Height : Height; R.Supersample = D.Supersample > 0 ? int32(D.Supersample) : Supersample;
	R.File = UTF8_TO_TCHAR(D.File); R.Subtitle = UTF8_TO_TCHAR(D.Subtitle); R.SubPos = uint8(D.SubtitlePosition); R.SubAlpha = D.SubtitleAlpha; R.Fade = D.Fade;   // U-19e (P-7)
	for (uint32 I = 0; I < D.Graphics._length; ++I)
	{
		const auto& G = D.Graphics._buffer[I];
		FGraphic Gr; Gr.Kind = uint8(G.Kind); Gr.Radius = G.Radius; Gr.Text = UTF8_TO_TCHAR(G.Text); Gr.Rgb = G.ColourRgb; Gr.Alpha = G.Alpha;
		for (uint32 K = 0; K < G.Points._length; ++K) Gr.Points.Add(FVector3d(G.Points._buffer[K].X, G.Points._buffer[K].Y, G.Points._buffer[K].Z));
		R.Graphics.Add(MoveTemp(Gr));
	}
	R.SeenAt = RenderReal();
	if (D.LayersOn._length || D.LayersOff._length) UE_LOG(LogHrotDpsRender, Verbose, TEXT("frame %d: layers on/off not drawn by this client"), R.Index);
	Queue.Add(MoveTemp(R));
}

bool UHrotDpsRender::BarrierMet(const FRequest& R, double Now)
{
	// D-B4: the step's world samples, counted since our last presentation (see the header). A frame with no stated step needs none.
	if (R.Kind != SimNetGateway_Protocol_DdsPlayer_FrameKindScene) return true;   // owns no step (the client guide): no barrier
	const FStepInfo* St = StepsSeen.Find(R.StepSeq);
	if (!St && R.StepSeq != 0 && Now - R.SeenAt < BarrierGiveUp) return false;   // its ClockStep is not here yet
	if (!St || St->Samples == 0) return true;
	const int64 Got = GEngine->GetEngineSubsystem<UHrotDpsSubsystem>()->Client().GetStepCount();
	if (Got == int64(St->Samples)) return true;
	if (Got > int64(St->Samples))
	{
		// the gateway: "a count above it is a gateway defect - log it and present"
		++Overcounts;
		UE_LOG(LogHrotDpsRender, Warning, TEXT("frame %d: step %lld counted %lld world instances/events, the station said %u - presented"), R.Index, R.StepSeq, Got, St->Samples);
		return true;
	}
	if (Now - R.SeenAt >= BarrierGiveUp)
	{
		++BarrierTimeouts;
		UE_LOG(LogHrotDpsRender, Warning, TEXT("frame %d: step %lld barrier %lld of %u world samples after %.0f s - presented anyway"), R.Index, R.StepSeq, Got, St->Samples, BarrierGiveUp);
		return true;
	}
	return false;
}

void UHrotDpsRender::OnEndFrame()
{
	if (!bJob || !bJobOk || World() != SetupWorld.Get()) return;
	const double Now = RenderReal();
	if (Phase == EPhase::Advancing)
	{
		if (GFrameCounter < AdvanceFrame) return;   // the advancing frame has not been ticked yet
		const FRequest R = Queue[0];
		Queue.RemoveAt(0);
		FApp::SetFixedDeltaTime(IdleDelta);
		RenderFrame(R);
		Phase = EPhase::Waiting;
		return;
	}
	if (Phase != EPhase::Waiting || Queue.Num() == 0) return;
	const FRequest& R = Queue[0];
	if (!BarrierMet(R, Now)) { if (Now - R.SeenAt > 0.05 && R.Index != -1) ++BarrierWaits; return; }
	if (R.Kind == SimNetGateway_Protocol_DdsPlayer_FrameKindSilence)
	{
		const FRequest RenderCopy = R; Queue.RemoveAt(0);
		const int32 N = bAudio ? MixFrame(true) : 0;
		++Silences;
		Present(RenderCopy, true, FString(), FString(), 0.f, N);
		return;
	}
	if (R.Kind == SimNetGateway_Protocol_DdsPlayer_FrameKindRepeat)
	{
		// slow motion past one frame per step (the client guide): no image (HROT copies the previous scene's file), the world does not step,
		// the soundtrack advances by one film frame
		const FRequest RepeatCopy = R; Queue.RemoveAt(0);
		const int32 N = bAudio ? MixFrame(false) : 0;
		++RepeatFrames;
		Present(RepeatCopy, true, FString(), FString(), 0.f, N);
		return;
	}
	Advance(R);
}

void UHrotDpsRender::Advance(const FRequest& R)
{
	// the NEXT engine frame is the frame's: the world steps FilmRate / Fps sim seconds (effects, animation) at the presented sim time
	FApp::SetFixedDeltaTime(double(R.FilmRate) / double(Fps));
	// the listener is the film camera (§3f): the IG camera (the player's view, so the audio listener) stands at the frame's camera
	if (AHrotDpsCamera* Pawn = Cast<AHrotDpsCamera>(UGameplayStatics::GetPlayerPawn(World(), 0)))
	{
		const UHrotDpsWorld* DW = UHrotDpsWorld::Get(World());
		const double Aspect = double(FMath::Max(1, R.Width)) / double(FMath::Max(1, R.Height));
		Pawn->PlaceAt(DW ? DW->EnuMToUnreal(FVector(R.P)) : FVector(R.P * 100.0), HrotDpsMotion::ToUnreal(R.Q),
			float(FMath::RadiansToDegrees(2.0 * FMath::Atan(FMath::Tan(FMath::DegreesToRadians(double(R.Fov)) * 0.5) * Aspect))));
	}
	AdvanceFrame = GFrameCounter + 1;
	Phase = EPhase::Advancing;
	PhaseStart = RenderReal();
}

void UHrotDpsRender::RenderFrame(const FRequest& R)
{
	const double T0 = RenderReal();
	UWorld* W = World();
	UHrotDpsWorld* DW = UHrotDpsWorld::Get(W);
	FString Abs;
	if (!SafePath(R.File, Abs)) { ++Failed; Present(R, false, R.File, FString::Printf(TEXT("File '%s' is not a path under the render root"), *R.File), 0.f, 0); return; }
	const int32 SS = FMath::Clamp(R.Supersample, 1, MaxSupersample);
	const int32 CW = R.Width * SS, CH = R.Height * SS;
	if (R.Width <= 0 || R.Height <= 0 || R.Width > MaxWidth || R.Height > MaxHeight) { ++Failed; Present(R, false, R.File, TEXT("size outside the limits"), 0.f, 0); return; }
	if (!Capture || !IsValid(Capture))
	{
		FActorSpawnParameters P; P.ObjectFlags |= RF_Transient;
		Capture = W->SpawnActor<ASceneCapture2D>(ASceneCapture2D::StaticClass(), FTransform::Identity, P);
		USceneCaptureComponent2D* C = Capture->GetCaptureComponent2D();
		C->bCaptureEveryFrame = false; C->bCaptureOnMovement = false;
		C->bAlwaysPersistRenderingState = true;   // the anti-aliasing history and the exposure carry from frame to frame
		C->CaptureSource = ESceneCaptureSource::SCS_FinalColorLDR;
	}
	if (!Target || Target->SizeX != CW || Target->SizeY != CH)
	{
		Target = NewObject<UTextureRenderTarget2D>(this);
		Target->RenderTargetFormat = RTF_RGBA8;
		Target->InitCustomFormat(CW, CH, PF_B8G8R8A8, false);
		Target->UpdateResourceImmediate(true);
		Capture->GetCaptureComponent2D()->TextureTarget = Target;
		bHadFrame = false;
	}
	// the request's exact camera; its FovDeg is VERTICAL (HROT's) -> the capture's horizontal
	const FVector At = DW ? DW->EnuMToUnreal(FVector(R.P)) : FVector(R.P * 100.0);
	const FQuat Rot = HrotDpsMotion::ToUnreal(R.Q);
	const double Aspect = double(R.Width) / double(R.Height);
	const double HFov = 2.0 * FMath::Atan(FMath::Tan(FMath::DegreesToRadians(double(R.Fov)) * 0.5) * Aspect);
	USceneCaptureComponent2D* C = Capture->GetCaptureComponent2D();
	C->SetWorldLocationAndRotation(At, Rot);
	C->FOVAngle = float(FMath::RadiansToDegrees(HFov));
	// a cut (the first frame, a jump, a turn, a new timeline): warm-up captures at this camera first, unrequested and unstepped (Q-4)
	const int64 Ep = DW ? DW->TimelineEpochNow() : 0;
	const bool bCut = !bHadFrame || FVector3d::Dist(R.P, LastP) > 30.0 || FMath::RadiansToDegrees(R.Q.AngularDistance(LastQ)) > 25.0 || Ep != LastEpoch;
	if (bCut) for (int32 I = 0, N = CVarCutWarmUps.GetValueOnGameThread(); I < N; ++I) { C->CaptureScene(); ++WarmUps; }
	bHadFrame = true; LastP = R.P; LastQ = R.Q; LastEpoch = Ep;
	C->CaptureScene();
	// the overlay INTO the image (a canvas on the target, after the capture)
	if (!R.Subtitle.IsEmpty() || R.Graphics.Num() || R.Fade > 0.005f)
	{
		UCanvas* Canvas = nullptr; FVector2D Size; FDrawToRenderTargetContext Ctx;
		UKismetRenderingLibrary::BeginDrawCanvasToRenderTarget(W, Target, Canvas, Size, Ctx);
		if (Canvas) DrawOverlay(R, Canvas, Size, FTransform(Rot, At), HFov);
		UKismetRenderingLibrary::EndDrawCanvasToRenderTarget(W, Ctx);
	}
	TArray<FColor> Px;
	FTextureRenderTargetResource* Res = Target->GameThread_GetRenderTargetResource();
	if (!Res || !Res->ReadPixels(Px) || Px.Num() != CW * CH) { ++Failed; Present(R, false, R.File, TEXT("the capture could not be read"), 0.f, 0); return; }
	TArray<FColor> Out;
	if (SS == 1) { Out = MoveTemp(Px); for (FColor& X : Out) X.A = 255; }
	else
	{
		Out.SetNumUninitialized(R.Width * R.Height);
		for (int32 Y = 0; Y < R.Height; ++Y)
			for (int32 X = 0; X < R.Width; ++X)
			{
				int32 Rs = 0, Gs = 0, Bs = 0;
				for (int32 J = 0; J < SS; ++J) for (int32 I = 0; I < SS; ++I) { const FColor& P = Px[(Y * SS + J) * CW + X * SS + I]; Rs += P.R; Gs += P.G; Bs += P.B; }
				const int32 D = SS * SS;
				Out[Y * R.Width + X] = FColor(uint8(Rs / D), uint8(Gs / D), uint8(Bs / D), 255);
			}
	}
	TArray64<uint8> Png;
	FImageUtils::PNGCompressImageArray(R.Width, R.Height, TArrayView64<const FColor>(Out.GetData(), Out.Num()), Png);
	IFileManager::Get().MakeDirectory(*FPaths::GetPath(Abs), true);
	const bool bSaved = FFileHelper::SaveArrayToFile(TArrayView64<const uint8>(Png.GetData(), Png.Num()), *Abs);
	const int32 N = bAudio ? MixFrame(false) : 0;
	const float Ms = float((RenderReal() - T0) * 1000.0);
	if (bSaved) ++Written; else ++Failed;
	Present(R, bSaved, R.File, bSaved ? FString() : FString::Printf(TEXT("could not write '%s'"), *Abs), Ms, N);
}

void UHrotDpsRender::Present(const FRequest& R, bool bOk, const FString& File, const FString& Why, float Ms, int32 AudioSamples)
{
	++Frames;
	UHrotDpsSubsystem* Dps = GEngine->GetEngineSubsystem<UHrotDpsSubsystem>();
	FHrotDpsClient& C = Dps->Client();
	C.ResetStepCount(true);   // the next step's samples count from here
	if (const FStepInfo* St = StepsSeen.Find(R.StepSeq); St && St->bAck)
	{
		SimNetGateway_Protocol_DdsPlayer_StepAck A{}; A.ClientId = ClientId; A.StepSeq = R.StepSeq; A.Checksum = 0;
		C.Write(TEXT("DPS_StepAck"), &A);
	}
	StepsSeen.Remove(R.StepSeq);
	FDone Done; Done.StepSeq = R.StepSeq; Done.bOk = bOk; Done.File = File; Done.Why = Why; Done.Ms = Ms; Done.AudioSamples = AudioSamples;
	WriteDone(R.JobId, R.Index, Done);
	Presented.Add(R.Index, MoveTemp(Done));
	if (!bOk) { LastWhy = Why; UE_LOG(LogHrotDpsRender, Warning, TEXT("frame %d failed: %s"), R.Index, *Why); }
}

// ── the soundtrack: the non-real-time mixer, one film frame at a time ──

void UHrotDpsRender::StartRecording()
{
	Audio::FMixerDevice* M = MixerOf(World());
	if (!M) return;
	if (TSharedPtr<Audio::FMixerSubmix> Main = M->GetMasterSubmix().Pin()) { Main->OnStartRecordingOutput(60.f); bRecording = true; }
}

void UHrotDpsRender::StopRecording()
{
	bRecording = false;
	Audio::FMixerDevice* M = MixerOf(World());
	TSharedPtr<Audio::FMixerSubmix> Main = M ? M->GetMasterSubmix().Pin() : nullptr;
	if (!Main) return;
	float Ch = 0, Rate = 0;
	const Audio::FAlignedFloatBuffer& Buf = Main->OnStopRecordingOutput(Ch, Rate);
	if (Ch > 0) { TrackChannels = int32(Ch); TrackRate = int32(Rate); }
	Track.Append(Buf.GetData(), Buf.Num());
}

int32 UHrotDpsRender::MixFrame(bool bSilence)
{
	Audio::FMixerDevice* M = MixerOf(World());
	Audio::FMixerPlatformNonRealtime* Nrt = NrtOf(M);
	if (!Nrt) return 0;
	// one film frame of samples, the remainder carried (48000 / 30 = 1600 exactly; 44100 / 24 is not)
	const double Want = double(M->GetSampleRate()) / double(Fps) + AudioCarry;
	const int32 N = int32(Want); AudioCarry = Want - N;
	// a title card (HROT's) is silence in the track (§3f rule 2): the mixer runs on (its latency carries the scene's sound across a cut), and
	// the card's film range is zeroed in the FINISHED track — ⚠ measured (U-18q): zeros inserted mid-stream landed 2 frames early once the
	// latency was dropped from the head
	if (bSilence) Silent.Add({ TrackFilmSamples, N });
	TrackFilmSamples += N;
	M->Update(true);
	Nrt->RenderAudio(1.0 / double(Fps));
	if (Frames % 10 == 0 && CVarAudioDiag.GetValueOnGameThread())
	{
		FTransform L; const bool bL = M->GetListenerTransform(0, L);
		UE_LOG(LogHrotDpsRender, Display, TEXT("audio frame %d: device %u muted %d active sounds %d sources %d volume %.2f/%.2f/%.2f listener %d at %s"),
			Frames, M->DeviceID, M->IsAudioDeviceMuted() ? 1 : 0, M->GetActiveSounds().Num(), M->GetNumActiveSources(), M->GetTransientPrimaryVolume(),
			FApp::GetVolumeMultiplier(), FApp::GetUnfocusedVolumeMultiplier(), bL ? 1 : 0, *L.GetLocation().ToString());
	}
	return N;
}

bool UHrotDpsRender::WriteTrack(const FString& Path, int64& OutSamples, FString& Why)
{
	const int32 SrcCh = TrackChannels > 0 ? TrackChannels : 2;
	const int32 Rate = TrackRate > 0 ? TrackRate : 48000;
	if (TrackRate <= 0) TrackRate = Rate;
	int64 Frames64 = Track.Num() / SrcCh;
	// the mixer's latency (the submix chain holds a few frames): drop its head (MRQ's AudioLatencyCompensationFrames)
	const int64 Drop = int64(CVarAudioLatencyFrames.GetValueOnGameThread()) * Rate / Fps;
	const int64 Start = FMath::Min(Drop, Frames64);
	Frames64 -= Start;
	const int32 Ch = Channels;
	for (const TPair<int64, int32>& Z : Silent)   // the title cards, in film samples (after the latency's head is dropped)
		for (int64 F = FMath::Max<int64>(0, Z.Key); F < FMath::Min<int64>(Z.Key + Z.Value, Frames64); ++F)
			for (int32 C = 0; C < SrcCh; ++C) Track[(Start + F) * SrcCh + C] = 0.f;
	TArray<uint8> Wav;
	const uint32 DataBytes = uint32(Frames64 * Ch * 2);
	auto U32 = [&Wav](uint32 V) { Wav.Append(reinterpret_cast<uint8*>(&V), 4); };
	auto U16 = [&Wav](uint16 V) { Wav.Append(reinterpret_cast<uint8*>(&V), 2); };
	Wav.Append(reinterpret_cast<const uint8*>("RIFF"), 4); U32(36 + DataBytes); Wav.Append(reinterpret_cast<const uint8*>("WAVEfmt "), 8);
	U32(16); U16(1); U16(uint16(Ch)); U32(uint32(Rate)); U32(uint32(Rate * Ch * 2)); U16(uint16(Ch * 2)); U16(16);
	Wav.Append(reinterpret_cast<const uint8*>("data"), 4); U32(DataBytes);
	Wav.Reserve(Wav.Num() + DataBytes);
	for (int64 F = Start; F < Start + Frames64; ++F)
		for (int32 C = 0; C < Ch; ++C)
		{
			// the device's channels to the job's: mono = the mean, stereo from mono = both
			float S;
			if (Ch == SrcCh) S = Track[F * SrcCh + C];
			else if (Ch == 1) { S = 0; for (int32 K = 0; K < SrcCh; ++K) S += Track[F * SrcCh + K]; S /= SrcCh; }
			else S = Track[F * SrcCh + FMath::Min(C, SrcCh - 1)];
			U16(uint16(int16(FMath::Clamp(FMath::RoundToInt(S * 32767.f), -32768, 32767))));
		}
	IFileManager::Get().MakeDirectory(*FPaths::GetPath(Path), true);
	if (!FFileHelper::SaveArrayToFile(Wav, *Path)) { Why = FString::Printf(TEXT("could not write '%s'"), *Path); return false; }
	OutSamples = Frames64;
	return true;
}

// ── the overlay: subtitle and the director's graphics, projected through the frame's camera ──

void UHrotDpsRender::DrawOverlay(const FRequest& R, UCanvas* Canvas, FVector2D Size, const FTransform& Cam, double HFovRad) const
{
	const UHrotDpsWorld* DW = UHrotDpsWorld::Get(World());
	const double F = (Size.X * 0.5) / FMath::Tan(HFovRad * 0.5);
	auto Project = [&](const FVector3d& Enu, FVector2D& Out)
	{
		const FVector U = DW ? DW->EnuMToUnreal(FVector(Enu)) : FVector(Enu * 100.0);
		const FVector L = Cam.InverseTransformPositionNoScale(U);   // x forward, y right, z up
		if (L.X < 1.0) return false;
		Out = FVector2D(Size.X * 0.5 + L.Y / L.X * F, Size.Y * 0.5 - L.Z / L.X * F);
		return true;
	};
	const float Scale = float(Size.Y / 540.0);
	UFont* Font = GEngine->GetLargeFont();
	for (const FGraphic& G : R.Graphics)
	{
		const FLinearColor Col = FLinearColor(FColor((G.Rgb >> 16) & 0xFF, (G.Rgb >> 8) & 0xFF, G.Rgb & 0xFF)).CopyWithNewOpacity(G.Alpha > 0 ? G.Alpha : 1.f);
		TArray<FVector3d> Line = G.Points;
		if (G.Kind == SimNetGateway_Protocol_DdsPlayer_FilmGraphicKindRing && G.Points.Num())
		{
			Line.Reset();
			for (int32 I = 0; I <= 48; ++I) { const double A = UE_DOUBLE_TWO_PI * I / 48; Line.Add(G.Points[0] + FVector3d(FMath::Cos(A), FMath::Sin(A), 0) * G.Radius); }
		}
		if (G.Kind == SimNetGateway_Protocol_DdsPlayer_FilmGraphicKindArea && Line.Num() > 2) Line.Add(Line[0]);
		FVector2D A2, B2;
		for (int32 I = 1; I < Line.Num(); ++I)
			if (Project(Line[I - 1], A2) && Project(Line[I], B2)) Canvas->K2_DrawLine(A2, B2, 3.f * Scale, Col);
		if (G.Kind == SimNetGateway_Protocol_DdsPlayer_FilmGraphicKindArrow && Line.Num() >= 2 && Project(Line.Last(1), A2) && Project(Line.Last(), B2))
		{
			const FVector2D D = (B2 - A2).GetSafeNormal() * 18.f * Scale, Nrm(-D.Y, D.X);
			Canvas->K2_DrawLine(B2, B2 - D + Nrm * 0.6f, 3.f * Scale, Col);
			Canvas->K2_DrawLine(B2, B2 - D - Nrm * 0.6f, 3.f * Scale, Col);
		}
		if (!G.Text.IsEmpty() && G.Points.Num() && Project(G.Points[0], A2))
			Canvas->K2_DrawText(Font, G.Text, A2 + FVector2D(8, -8) * Scale, FVector2D(Scale, Scale), Col, 0, FLinearColor::Black, FVector2D(1, 1), false, false, true, FLinearColor::Black);
	}
	// the subtitle and the director's fade (P-7) through the ONE painter the live HUD uses (HrotDpsOverlay): the fade covers the graphics too
	HrotDpsOverlay::FSpec Sp; Sp.Subtitle = R.Subtitle; Sp.Pos = R.SubPos; Sp.Alpha = R.SubAlpha; Sp.Fade = R.Fade;
	HrotDpsOverlay::Paint(Canvas, Size, Sp, Font);
}

FString UHrotDpsRender::Describe() const
{
	Audio::FMixerDevice* M = MixerOf(World());
	return FString::Printf(TEXT("%s %s frames %d written %d failed %d silence %d repeat %d | warm-ups %d | audio %d samples @ %d x %d (%s) | barrier waits %d, timeouts %d, over %d | repeats %d | queue %d | last: %s"),
		bJob ? *JobId : TEXT("no job"), bJob ? (bJobOk ? TEXT("ok") : TEXT("failed")) : TEXT("-"), Frames, Written, Failed, Silences, RepeatFrames, WarmUps,
		TrackChannels > 0 ? Track.Num() / TrackChannels : 0, TrackRate, TrackChannels, NrtOf(M) ? TEXT("nrt") : TEXT("real-time"), BarrierWaits, BarrierTimeouts, Overcounts,
		Repeats, Queue.Num(), LastWhy.IsEmpty() ? TEXT("-") : *LastWhy);
}
