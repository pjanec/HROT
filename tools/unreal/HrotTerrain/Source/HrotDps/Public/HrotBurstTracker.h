// CE-4651 (U-19c) — THE BURST TRACKER (DESIGN_IG_Squad_Radio.md §5, DD-027): which rounds of a Fire stream are one burst, and what each one's
// SOUND does. PURE (no audio, no world; `Now` is injected, the clip lookup is a predicate) — railed alone (Hrot.Dps.Burst).
//   BurstId 0                         ⇒ PerRound   (an aimed single, a player's round: today's path)
//   index 0 of N                      ⇒ Start      the clip of the LONGEST available count <= min(N, 8) (B-2); none ⇒ PerRound
//   index 1.. inside the clip         ⇒ Covered    (the clip plays them; tracers and impacts stay per round)
//   index 1.. beyond the clip's count ⇒ PerRound   (a shorter clip: the rest of the burst plays per round)
//   a burst whose next round is overdue (> 2 × the last spacing + 80 ms; the first spacing is assumed 100 ms) ⇒ PollCuts gives its handle
//   a burst is forgotten 2 s after its last round
#pragma once

#include "CoreMinimal.h"

struct HROTDPS_API FHrotBurstTracker
{
	enum class EAct : uint8 { PerRound, Start, Covered };
	struct FResult { EAct Act = EAct::PerRound; int32 ClipRounds = 0; };

	static constexpr int32 MinClip = 2;
	static constexpr int32 MaxClip = 8;
	static constexpr double ForgetAfter = 2.0;
	static constexpr double FirstSpacing = 0.1;
	static constexpr double Slack = 0.08;

	/** one round of a Fire stream. `HasClip(n)` = a clip of exactly n rounds exists */
	FResult OnRound(uint32 BurstId, int32 Rounds, int32 Index, double Now, TFunctionRef<bool(int32)> HasClip);
	/** the sound the caller started for a burst's Start */
	void SetHandle(uint32 BurstId, int32 Handle);
	/** the handles of bursts whose clip must be cut now (each once); also forgets the old */
	void PollCuts(double Now, TArray<int32>& OutHandles);
	int32 Active() const { return Bursts.Num(); }

private:
	struct FBurst { int32 ClipRounds = 0, LastIndex = 0, Handle = 0; double LastAt = 0, Spacing = FirstSpacing; bool bCut = false; };
	TMap<uint32, FBurst> Bursts;
};
