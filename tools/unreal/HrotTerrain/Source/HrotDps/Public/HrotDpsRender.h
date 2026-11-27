// CE-4651 (U-18q) — THE RENDER CLIENT: DPS round 7 §2 (SimNetGateway DD-022 rev 5 §3e / §3f, handoff §2). HROT's film director renders a
// reel through this client frame by frame, in lockstep:
//
//   RenderClient  ──►  we announce ourselves (limits, formats, Ready) and, per job, ReadyJobId after its warm-up
//   RenderJob     ◄──  Starting: validate against the limits, fixed time step, the soundtrack's recording opened; Done: the track closed,
//                      RenderJobResult; Failed / Cancelled: dropped
//   ClockStep     ◄──  AckRequired + Samples: the D-B4 BARRIER — the step is presented once that many world samples arrived since the last
//                      one (lockstep: nothing of the next step is written before we answer, so the count since our last presentation is
//                      exact even for topics with no SimTime, e.g. Entity); then StepAck
//   FrameRequest  ◄──  one step = one frame: the world advances ONE engine frame by FilmRate / Fps sim seconds (fixed time step; 0.1 ms
//                      while idle), a scene capture through the request's exact camera (+ warm-up captures after a cut), the overlay
//                      (subtitle + Graphics, world points projected) drawn INTO the image, the PNG written under the render root, one
//                      film frame of audio mixed by the NON-REAL-TIME mixer (Movie Render Queue's recipe: Update + RenderAudio(1 / Fps),
//                      the master submix recorded); FrameKindSilence = silence in the track, no image
//   FrameDone     ──►  Written / Failed + why, RenderMs
//
// ⭐ Only in a process started as a render client: -HrotDpsRender (and -deterministicaudio for the soundtrack — the non-real-time audio
// device; without it a job asking for audio fails before frame 0 with that reason). A render client is view-only (D-7).
// ⭐ CE-11064: a GAME-INSTANCE subsystem, so the job and its DDS readers live through the client's own level loads — a job begun on the start level
// (gateway T-086: the station films with a live client before any world) goes on in the terrain level; a world-bound subsystem lost the job and
// every FrameRequest written during the blocking LoadMap. What belongs to a world (the capture actor, the shot renderer's sim clock, the
// soundscape) is set up again when the world changes (ApplyWorld).
// ⭐ Sounds keep sim time here: UHrotShotRenderer's speed-of-sound delays read the sim clock during a job (DD-022 §3f rule 4).
#pragma once

#include "CoreMinimal.h"
#include "Subsystems/GameInstanceSubsystem.h"
#include "Tickable.h"
#include "HrotDpsRender.generated.h"

class ASceneCapture2D;
class UTextureRenderTarget2D;

UCLASS()
class HROTDPS_API UHrotDpsRender : public UGameInstanceSubsystem, public FTickableGameObject
{
	GENERATED_BODY()

public:
	static UHrotDpsRender* Get(const UObject* WorldContext);
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS", meta = (WorldContext = "WorldContext"))
	static UHrotDpsRender* ForWorld(UObject* WorldContext) { return Get(WorldContext); }

	/** be the station's render client (also -HrotDpsRender); the render root: -HrotDpsRenderRoot=<dir>, else <Saved>/HrotRender */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") void Enable(const FString& RenderRoot);
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") bool IsEnabled() const { return bEnabled; }
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") FString GetRenderRoot() const { return Root; }
	/** "<job> <state> frames <n> written <w> failed <f> silence <s> | warm-ups <n> | audio <samples> @ <rate> x <ch> (nrt|none) | barrier waits <n>, timeouts <n> | last: <why>" */
	UFUNCTION(BlueprintCallable, Category = "HROT|DPS") FString Describe() const;

	static constexpr int32 MaxWidth = 3840;
	static constexpr int32 MaxHeight = 2160;
	static constexpr int32 MaxSupersample = 2;
	static constexpr double IdleDelta = 1e-4;        // the world's step between frames (s): effects stand still while we wait
	static constexpr double BarrierGiveUp = 10.0;     // s: present anyway (and say so) — the station's own 30 s timeout is the real verdict

	virtual void Initialize(FSubsystemCollectionBase& Collection) override;
	virtual void Deinitialize() override;
	virtual void Tick(float DeltaTime) override;
	virtual ETickableTickType GetTickableTickType() const override { return HasAnyFlags(RF_ClassDefaultObject) ? ETickableTickType::Never : ETickableTickType::Always; }
	virtual bool IsTickableWhenPaused() const override { return true; }
	virtual TStatId GetStatId() const override { RETURN_QUICK_DECLARE_CYCLE_STAT(UHrotDpsRender, STATGROUP_Tickables); }

private:
	struct FGraphic { uint8 Kind = 0; TArray<FVector3d> Points; float Radius = 0; FString Text; uint32 Rgb = 0xFFFFFF; float Alpha = 1; };
	struct FRequest
	{
		FString JobId; int32 Index = 0; uint8 Kind = 0; int64 StepSeq = 0; double SimTime = 0; float FilmRate = 1;
		FVector3d P = FVector3d::ZeroVector; FQuat4d Q = FQuat4d::Identity; float Fov = 60;
		int32 Width = 0, Height = 0, Supersample = 1; FString File, Subtitle; uint8 SubPos = 0; float SubAlpha = 1, Fade = 0;
		TArray<FGraphic> Graphics;
		double SeenAt = 0;
	};
	struct FStepInfo { uint32 Samples = 0; bool bAck = false; };
	enum class EPhase : uint8 { Idle, Waiting, Advancing };

	bool bEnabled = false;
	FString Root;
	uint64 ClientId = 0;
	TArray<TPair<FString, int32>> Subs;
	double NextAnnounce = 0;
	// the job
	FString JobId, ReadyJobId, LastWhy;
	bool bJob = false, bAudio = false, bJobOk = true;
	// CE-11064: the job's instance went away (the gateway rebuilds its writers at every world boundary, the job kept): held this long for the
	// same JobId to come back from the new writer before the job is dropped; 0 = not orphaned
	static constexpr double OrphanGrace = 10.0;
	double OrphanSince = 0;
	int32 Fps = 30, Width = 0, Height = 0, Supersample = 1, SampleRate = 48000, Channels = 2;
	FString Dir;
	TArray<FRequest> Queue;
	TMap<int64, FStepInfo> StepsSeen;
	EPhase Phase = EPhase::Idle;
	uint64 AdvanceFrame = 0;
	double PhaseStart = 0;
	int32 Frames = 0, Written = 0, Failed = 0, Silences = 0, WarmUps = 0, BarrierWaits = 0, BarrierTimeouts = 0, Overcounts = 0;
	// CE-11064 (gateway T-086): after a world boundary inside a job the station writes the outstanding FrameRequest AGAIN, an exact repeat. A
	// repeat of a frame already presented is answered from its FrameDone, never rendered twice; a repeat of one still queued is dropped
	struct FDone { int64 StepSeq = 0; bool bOk = true; FString File, Why; float Ms = 0; int32 AudioSamples = 0; };
	TMap<int32, FDone> Presented;   // FrameIndex -> the FrameDone sent, this job
	int32 Repeats = 0, RepeatFrames = 0;   // repeated requests answered; FrameKindRepeat frames (slow motion)
	TWeakObjectPtr<UWorld> SetupWorld;   // the world the job's per-world setup was applied to
	bool bHadFrame = false; FVector3d LastP = FVector3d::ZeroVector; FQuat4d LastQ = FQuat4d::Identity; int64 LastEpoch = -1;
	// the soundtrack (segments: recorded mixer output, zeros for a silence frame)
	TArray<float> Track;   // interleaved, the device's channels
	int32 TrackChannels = 0, TrackRate = 0;
	bool bRecording = false;
	double AudioCarry = 0;
	int64 TrackFilmSamples = 0;
	TArray<TPair<int64, int32>> Silent;   // title cards: (first film sample, count)
	TMap<FString, int32> PrevCvars;   // the console variables a job sets, restored after it
	bool bPrevFixed = false; double PrevFixedDelta = 0;
	// the capture
	UPROPERTY(Transient) TObjectPtr<ASceneCapture2D> Capture;
	UPROPERTY(Transient) TObjectPtr<UTextureRenderTarget2D> Target;
	FDelegateHandle EndFrame;

	void Connect();
	void Announce(double Now);
	void OnJob(const struct FHrotDpsSample& S);
	void OnStep(const struct FHrotDpsSample& S);
	void OnFrame(const struct FHrotDpsSample& S);
	void BeginJob(const struct SimNetGateway_Protocol_DdsPlayer_RenderJob& J);
	void EndJob(bool bDone);
	void FailJob(const FString& Why);
	void OnEndFrame();
	bool BarrierMet(const FRequest& R, double Now);
	void Advance(const FRequest& R);
	void RenderFrame(const FRequest& R);
	void Present(const FRequest& R, bool bOk, const FString& File, const FString& Why, float Ms, int32 AudioSamples);
	UWorld* World() const;
	void ApplyWorld();
	void WriteDone(const FString& Job, int32 Index, const FDone& D);
	int32 MixFrame(bool bSilence);
	void StartRecording();
	void StopRecording();
	bool WriteTrack(const FString& Path, int64& OutSamples, FString& Why);
	bool SafePath(const FString& Rel, FString& OutAbs) const;
	void DrawOverlay(const FRequest& R, class UCanvas* Canvas, FVector2D Size, const FTransform& Cam, double HFovRad) const;
};
