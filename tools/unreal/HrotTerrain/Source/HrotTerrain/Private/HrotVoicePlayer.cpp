// CE-4651 (U-19b) — see HrotVoicePlayer.h
#include "HrotVoicePlayer.h"
#include "HrotSoundscape.h"
#include "HrotShotRenderer.h"
#include "HrotSoundLog.h"
#include "Engine/World.h"
#include "HAL/PlatformTime.h"
#include "Kismet/GameplayStatics.h"
#include "Sound/SoundBase.h"

DEFINE_LOG_CATEGORY_STATIC(LogHrotVoice, Log, All);

// ---- the pure rules ---------------------------------------------------------------------------------------------------------------
int32 FHrotVoiceSched::Offer(int32 Net, int32 Token)
{
	FNet& N = Nets.FindOrAdd(Net);
	N.Waiting.Add(Token);
	if (N.Waiting.Num() <= MaxWaiting) return -1;
	const int32 Old = N.Waiting[0];
	N.Waiting.RemoveAt(0);
	++Dropped;
	return Old;
}

bool FHrotVoiceSched::PopStartable(int32 ActiveVoices, int32& OutNet, int32& OutToken)
{
	if (ActiveVoices >= MaxVoices) return false;
	for (TPair<int32, FNet>& KV : Nets)
	{
		FNet& N = KV.Value;
		if (N.bBusy || N.Waiting.IsEmpty()) continue;
		OutNet = KV.Key; OutToken = N.Waiting[0];
		N.Waiting.RemoveAt(0);
		N.bBusy = true;
		return true;
	}
	return false;
}

void FHrotVoiceSched::Finish(int32 Net) { if (FNet* N = Nets.Find(Net)) N->bBusy = false; }
int32 FHrotVoiceSched::Waiting(int32 Net) const { const FNet* N = Nets.Find(Net); return N ? N->Waiting.Num() : 0; }
bool FHrotVoiceSched::Busy(int32 Net) const { const FNet* N = Nets.Find(Net); return N && N->bBusy; }

bool FHrotVoiceSched::AllowShout(int32 Speaker, double Now, int32 ActiveVoices)
{
	if (ActiveVoices >= MaxVoices) { ++Dropped; return false; }
	if (const double* Last = LastShout.Find(Speaker)) if (Now - *Last < ShoutGap) { ++Dropped; return false; }
	LastShout.Add(Speaker, Now);
	return true;
}

TArray<FString> FHrotVoiceSched::BuildChain(const FVoiceCue& Cue, bool bOver)
{
	TArray<FString> C;
	if (Cue.IsEmpty()) return C;
	if (!Cue.bRadio) { C.Add(Cue.Set); return C; }
	C.Add(TEXT("radio/beep"));
	if (!Cue.Callsign.IsEmpty()) C.Add(TEXT("vox/sgt_") + Cue.Callsign.ToLower());
	C.Add(Cue.Set);
	if (bOver) C.Add(TEXT("vox/over"));
	C.Add(TEXT("radio/squelch"));
	if (!Cue.AnswerSet.IsEmpty())
	{
		C.Add(TEXT("+1.5/radio/beep"));
		C.Add(Cue.AnswerSet);
		C.Add(TEXT("radio/squelch"));
	}
	return C;
}

bool FHrotVoiceSched::IsEarpiece(const FVoiceCue& Cue, int32 NetId, int32 ListenerNet)
{
	return Cue.bRadio && ListenerNet != 0 && (NetId == ListenerNet || Cue.ToId == ListenerNet);
}

// ---- the subsystem ----------------------------------------------------------------------------------------------------------------
UHrotVoicePlayer* UHrotVoicePlayer::Get(const UObject* WorldContext)
{
	UWorld* W = WorldContext ? WorldContext->GetWorld() : nullptr;
	return W ? W->GetSubsystem<UHrotVoicePlayer>() : nullptr;
}

TStatId UHrotVoicePlayer::GetStatId() const { RETURN_QUICK_DECLARE_CYCLE_STAT(UHrotVoicePlayer, STATGROUP_Tickables); }

FString UHrotVoicePlayer::Describe() const
{
	return FString::Printf(TEXT("voices: %d clips, %d transmissions (%d earpiece), %d dropped, %d playing; last %s"), Played, Transmissions, Earpiece, Dropped, Active.Num(), *LastChain);
}

void UHrotVoicePlayer::Play(const FVoiceCue& Cue, int32 NetId, int32 SpeakerId, const FVector& At, bool bEarpiece)
{
	UHrotSoundscape* Snd = UHrotSoundscape::Get(this);
	if (!Snd || !Snd->bEnabled || Cue.IsEmpty()) return;
	const double Now = FPlatformTime::Seconds();
	FTx T;
	T.Cue = Cue; T.NetId = NetId; T.SpeakerId = SpeakerId; T.At = At; T.bEarpiece = bEarpiece;
	const int32 Token = NextToken++;
	if (!Cue.bRadio)
	{
		if (!Sched.AllowShout(SpeakerId, Now, Active.Num())) { Sync(); return; }
		T.Chain = FHrotVoiceSched::BuildChain(Cue, false);
		T.NextAt = Now;
		Pending.Add(Token, MoveTemp(T));
		Active.Add(Token);
		StartTx(Token, Now);
		return;
	}
	T.bOver = (RadioCount++ % 2) == 1;   // "over" on every second transmission
	T.Chain = FHrotVoiceSched::BuildChain(Cue, T.bOver);
	Pending.Add(Token, MoveTemp(T));
	const int32 Dropped1 = Sched.Offer(NetId, Token);
	if (Dropped1 >= 0) Pending.Remove(Dropped1);
	Sync();
	Pump(Now);
}

void UHrotVoicePlayer::Pump(double Now)
{
	int32 Net, Token;
	while (Sched.PopStartable(Active.Num(), Net, Token))
	{
		if (!Pending.Contains(Token)) { Sched.Finish(Net); continue; }
		Pending[Token].NextAt = Now;
		Active.Add(Token);
		StartTx(Token, Now);
	}
}

void UHrotVoicePlayer::StartTx(int32 Token, double Now)
{
	FTx& T = Pending[Token];
	++Transmissions;
	if (T.bEarpiece) ++Earpiece;
	TArray<FString> Shown;
	for (const FString& S : T.Chain) { int32 I; Shown.Add(S.StartsWith(TEXT("+")) && S.FindChar('/', I) ? S.Mid(I + 1) : S); }
	LastChain = FString::Join(Shown, TEXT(" > "));
	UE_LOG(LogHrotVoice, Display, TEXT("voice: %s [%s, net %d]"), *LastChain, T.bEarpiece ? TEXT("earpiece") : TEXT("world"), T.NetId);
}

void UHrotVoicePlayer::PlayClip(const FTx& T, const FString& Set, double& OutSeconds)
{
	OutSeconds = 0;
	UHrotSoundscape* Snd = UHrotSoundscape::Get(this);
	USoundBase* S = Snd ? Snd->Pick(Set) : nullptr;   // a miss is counted by the soundscape (LibraryMissing)
	if (!S) return;
	// world speech: full to 5 m, -30 dB at 40 m (HROT's VoiceRange), held there by the natural-sound curve, CUT at 50 m
	// (heard 2026-11-22: "the faint at 50 was ok but so weak that we can cut it there")
	static constexpr float VoiceCutM = 50.f;
	float DistM = -1.f;
	if (!T.bEarpiece) { const UHrotShotRenderer* R = UHrotShotRenderer::Get(GetWorld()); if (R) DistM = FVector::Dist(T.At, R->ViewLocation()) / 100.f; }
	const bool bCut = DistM > VoiceCutM;
	if (T.bEarpiece) UGameplayStatics::PlaySound2D(GetWorld(), S);
	else if (!bCut) UGameplayStatics::PlaySoundAtLocation(GetWorld(), S, T.At, 1.f, 1.f, 0.f, Snd->Falloff(5.f, 40.f, -30.f, true));
	OutSeconds = S->GetDuration();
	{   // U-19f: the sound log - the line (and the answer) carry the HROT meaning, the framing clips (beep, sgt, over, squelch) their set
		const bool bAnswer = !T.Cue.AnswerSet.IsEmpty() && Set == T.Cue.AnswerSet && Set != T.Cue.Set;
		const bool bLine = Set == T.Cue.Set || bAnswer;
		FString Detail = bLine ? (bAnswer ? TEXT("answer: ") + T.Cue.Why : T.Cue.Why) : Set;
		if (bCut) Detail += TEXT(" (not played, beyond 50 m)");
		else if (DistM > 40.f) Detail += TEXT(" (faint, beyond 40 m)");
		FHrotSoundLog::Get().Add(T.bEarpiece ? EHrotSndChannel::Radio : EHrotSndChannel::World, S->GetName(), Detail, DistM);
	}
	++Played;
	PlayedLog.Add(Set);
	if (PlayedLog.Num() > 64) PlayedLog.RemoveAt(0, PlayedLog.Num() - 64);
}

void UHrotVoicePlayer::Tick(float)
{
	UWorld* W = GetWorld();
	if (!W || W->IsNetMode(NM_DedicatedServer)) return;
	if (!(W->WorldType == EWorldType::PIE || W->WorldType == EWorldType::Game)) return;
	const double Now = FPlatformTime::Seconds();
	for (int32 i = Active.Num() - 1; i >= 0; --i)
	{
		const int32 Token = Active[i];
		FTx* T = Pending.Find(Token);
		if (!T) { Active.RemoveAt(i); continue; }
		while (T->Next < T->Chain.Num() && Now >= T->NextAt)
		{
			FString Step = T->Chain[T->Next];
			if (Step.StartsWith(TEXT("+")))
			{
				int32 Slash = INDEX_NONE; Step.FindChar('/', Slash);
				if (T->DelayDone != T->Next) { T->DelayDone = T->Next; T->NextAt += FCString::Atod(*Step.Mid(1, Slash - 1)); continue; }
				Step = Step.Mid(Slash + 1);
			}
			double Secs = 0;
			PlayClip(*T, Step, Secs);
			T->NextAt = Now + Secs + 0.05;
			++T->Next;
		}
		if (T->Next >= T->Chain.Num() && Now >= T->NextAt)
		{
			if (T->Cue.bRadio) Sched.Finish(T->NetId);
			Pending.Remove(Token);
			Active.RemoveAt(i);
		}
	}
	Pump(Now);
}
