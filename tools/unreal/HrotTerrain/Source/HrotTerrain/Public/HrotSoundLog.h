// CE-4651 (U-19f) - THE SOUND LOG: what was just played, on screen, so a listener can read what he hears (docs/designs/IG/DESIGN_IG_Squad_Radio.md section 8).
//
// ONE implementation, every play path feeds it (the choke points: UHrotVoicePlayer::PlayClip, UHrotShotRenderer::PlayNow, UHrotAmbiencePlayer::Start):
//   * a fixed ring of the last 6 entries (what the HUD draws, lower-left, when hrot.snd.Debug 1); an entry lives 6 s and fades over the last 1.5 s
//   * a transcript (the last 256 lines, no expiry) for the machine: a scene check reads it back, nothing is judged from a picture
//   * a CAPTION (hrot.snd.Caption <text>): one larger line, top-centre, saying what the listener should hear next (the sound demo)
// PURE: no world, no audio, no canvas - Now is injected (the overloads without it read the wall clock). Railed alone (Hrot.Dps.SoundLog).
// Numbers are printed as plain integers (invariant: no decimal comma on any machine).
#pragma once

#include "CoreMinimal.h"

enum class EHrotSndChannel : uint8 { Radio, World, Weapon, Ambience, Event, Fx };

struct FHrotSoundLogEntry
{
	double Time = 0;
	EHrotSndChannel Channel = EHrotSndChannel::Fx;
	FString Label;      // the clip / asset name
	FString Detail;     // who and why, in HROT's words
	float DistM = -1.f; // metres from the listener; < 0 = 2-D
	FString Text;       // the one screen line, made once on Add (the HUD draws it every frame without building a string)
};

class HROTTERRAIN_API FHrotSoundLog
{
public:
	static constexpr int32 Capacity = 6;
	static constexpr double Lifetime = 6.0;
	static constexpr double FadeSeconds = 1.5;
	static constexpr int32 TranscriptMax = 256;

	/** the process-wide log (game thread) */
	static FHrotSoundLog& Get();
	/** hrot.snd.Debug 1: the HUD draws the log */
	static bool IsOn();

	/** "WORLD 18 m . cmd_move_03 - man 31 Utterance CmdMove": channel name, " <n> m" when distance >= 0, " . earpiece" for the radio, label, " - detail" when any */
	static FString Format(EHrotSndChannel Channel, const FString& Label, const FString& Detail, float DistM);
	static const TCHAR* NameOf(EHrotSndChannel Channel);
	/** 1 until the last FadeSeconds of the lifetime, then down to 0 at Lifetime; 0 after */
	static float AlphaOfAge(double Age);

	void Add(EHrotSndChannel Channel, const FString& Label, const FString& Detail, float DistM, double Now);
	void Add(EHrotSndChannel Channel, const FString& Label, const FString& Detail, float DistM);

	/** the ring, oldest first (0 .. Num()-1); an entry past its lifetime is still held until pushed out - use Alpha */
	int32 Num() const { return Ring.Num(); }
	const FHrotSoundLogEntry& At(int32 I) const { return Ring[I]; }
	float Alpha(int32 I, double Now) const { return AlphaOfAge(Now - Ring[I].Time); }
	/** any entry still visible at Now */
	bool AnyLive(double Now) const;

	/** every line since the last Clear (capped at TranscriptMax), "<n> <text>" - the machine's view */
	const TArray<FString>& Transcript() const { return Lines; }

	void SetCaption(const FString& Text, double Seconds, double Now);
	void SetCaption(const FString& Text, double Seconds);
	/** the caption at Now ("" = none) and its opacity: it too fades over the last FadeSeconds */
	const FString& Caption(double Now, float& OutAlpha) const;

	void Clear();

private:
	TArray<FHrotSoundLogEntry> Ring;
	TArray<FString> Lines;
	int32 Counter = 0;
	FString CaptionText;
	double CaptionFrom = 0, CaptionUntil = 0;
};
