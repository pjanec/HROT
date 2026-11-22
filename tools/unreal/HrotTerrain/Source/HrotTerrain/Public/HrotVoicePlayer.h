// CE-4651 (U-19b) — THE SQUAD'S RADIO PLAYER: HROT's shouts, reports and orders as recorded voice lines (docs/designs/IG/DESIGN_IG_Squad_Radio.md).
//
// FVoiceCue is WHAT to say (made by HrotDps's pure HrotVoiceLines from an Utterance / Report / Order); this subsystem owns WHO HEARS and WHEN:
//   * a radio cue plays the chain  radio/beep → vox/sgt_<callsign> → <the line> → vox/over (every second transmission) → radio/squelch,
//     each clip started when the previous one's duration ends (the WALL clock, as the soundscape); an order adds the receiver's answer
//     (vox/ack_order) 1.5 s after, framed the same way
//   * one transmission at a time per net, ≤ 2 waiting (the oldest dropped); a speaker's world shouts ≥ 2 s apart; ≤ 3 voices at once
//   * WORLD = at the speaker, UHrotSoundscape::Falloff(5, 40) (HROT's VoiceRange); EARPIECE = 2-D at full level (the listener's own net)
// The clips come from UHrotSoundscape::Pick (the private pack in /Game/Hrot/AudioPro) — no second loader. FHrotVoiceSched is the pure
// queue / spacing logic (no audio, no world), railed alone (Hrot.Dps.Voice.Player).
#pragma once

#include "CoreMinimal.h"
#include "Subsystems/WorldSubsystem.h"
#include "HrotVoicePlayer.generated.h"

class UHrotSoundscape;

/** what to say; an empty Set = say nothing */
struct FVoiceCue
{
	FString Set;          // "vox/rep_losses"
	bool bRadio = false;  // false = a shout in the world
	FString AnswerSet;    // an order's receiver answers with this, 1.5 s after
	FString Callsign;     // "Cooper" (radio: the "this is Sergeant X" clip); empty = none
	int32 ToId = 0;       // the addressee (a unit / net); 0 = HQ / nobody
	FString Why;          // U-19f: the HROT meaning, for the sound log ("Report Contact from unit 12", "man 31 Utterance CmdMove")
	bool IsEmpty() const { return Set.IsEmpty(); }
};

/** the pure queue / spacing rules — Now is injected */
struct HROTTERRAIN_API FHrotVoiceSched
{
	static constexpr int32 MaxWaiting = 2;
	static constexpr int32 MaxVoices = 3;
	static constexpr double ShoutGap = 2.0;

	int32 Dropped = 0;   // transmissions dropped from a full queue + shouts refused (spacing / the voice cap)

	/** queue a transmission on `Net`; when the queue overflows the OLDEST waiting one is dropped: its token is returned (else -1) */
	int32 Offer(int32 Net, int32 Token);
	/** the next transmission that may start now: a net that is not busy with something waiting, while fewer than MaxVoices are active */
	bool PopStartable(int32 ActiveVoices, int32& OutNet, int32& OutToken);
	/** the net's transmission ended */
	void Finish(int32 Net);
	int32 Waiting(int32 Net) const;
	bool Busy(int32 Net) const;
	/** a world shout of `Speaker` at `Now`: false (and Dropped++) when he shouted < ShoutGap ago or MaxVoices are active; else it is recorded */
	bool AllowShout(int32 Speaker, double Now, int32 ActiveVoices);

	/** the clip sets of a transmission, in order (a Set with a leading "+<seconds>/" is delayed that long first): radio = beep, sgt, line, over?, squelch
	 *  (+ for an order the answer: +1.5/beep, ack, squelch); a world shout = the line alone. `bOver` = this is a "say over" transmission */
	static TArray<FString> BuildChain(const FVoiceCue& Cue, bool bOver);
	/** the listener's earpiece: a radio cue from or to his own net (0 = he has none) */
	static bool IsEarpiece(const FVoiceCue& Cue, int32 NetId, int32 ListenerNet);

private:
	struct FNet { bool bBusy = false; TArray<int32> Waiting; };
	TMap<int32, FNet> Nets;
	TMap<int32, double> LastShout;
};

UCLASS()
class HROTTERRAIN_API UHrotVoicePlayer : public UTickableWorldSubsystem
{
	GENERATED_BODY()

public:
	static UHrotVoicePlayer* Get(const UObject* WorldContext);
	UFUNCTION(BlueprintCallable, Category = "HROT|Sound", meta = (WorldContext = "WorldContext"))
	static UHrotVoicePlayer* ForWorld(UObject* WorldContext) { return Get(WorldContext); }

	/** a cue heard from `SpeakerId` (a world shout's spacing key) on net `NetId` (the sender's unit: the queue key), at `At` (Unreal cm); `bEarpiece` = the
	 *  listener is on this net (2-D, full level) */
	void Play(const FVoiceCue& Cue, int32 NetId, int32 SpeakerId, const FVector& At, bool bEarpiece);

	/** ⭐ probes — the rails and the scene read these */
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "HROT|Sound") int32 Played = 0;          // clips started
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "HROT|Sound") int32 Transmissions = 0;   // transmissions / shouts started
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "HROT|Sound") int32 Dropped = 0;         // queue overflow, shout spacing, the voice cap
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "HROT|Sound") int32 Earpiece = 0;        // transmissions heard 2-D
	/** the last transmission's chain, "radio/beep > vox/sgt_cooper > vox/rep_losses > radio/squelch" */
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "HROT|Sound") FString LastChain;
	/** every clip started, in order (capped at 64) — a scene check reads the whole sequence */
	TArray<FString> PlayedLog;

	UFUNCTION(BlueprintCallable, Category = "HROT|Sound") FString Describe() const;
	/** every clip started, in order, "radio/beep > vox/sgt_brown > ..." (the last 64) */
	UFUNCTION(BlueprintCallable, Category = "HROT|Sound") FString PlayedText() const { return FString::Join(PlayedLog, TEXT(" > ")); }

	virtual void Tick(float DeltaTime) override;
	virtual TStatId GetStatId() const override;
	virtual bool IsTickableInEditor() const override { return true; }

private:
	struct FTx
	{
		FVoiceCue Cue; int32 NetId = 0, SpeakerId = 0; FVector At = FVector::ZeroVector; bool bEarpiece = false, bOver = false;
		TArray<FString> Chain; int32 Next = 0, DelayDone = -1; double NextAt = 0;
	};
	FHrotVoiceSched Sched;
	TMap<int32, FTx> Pending;   // by token: waiting for its net, or playing
	TArray<int32> Active;       // tokens playing
	int32 NextToken = 1, RadioCount = 0;
	void StartTx(int32 Token, double Now);
	void Pump(double Now);
	void PlayClip(const FTx& T, const FString& Set, double& OutSeconds);
	/** the sched's Dropped mirrored into the probe */
	void Sync() { Dropped = Sched.Dropped; }
};
