// CE-4651 (U-19f) - see HrotSoundLog.h
#include "HrotSoundLog.h"
#include "HAL/IConsoleManager.h"
#include "HAL/PlatformTime.h"

DEFINE_LOG_CATEGORY_STATIC(LogHrotSound, Log, All);

namespace
{
	int32 CDebug = 0;
	FAutoConsoleVariableRef VDebug(TEXT("hrot.snd.Debug"), CDebug, TEXT("1 = the HUD draws the sound log (what was just played) in every camera mode; 0 = off (default)"));

	FAutoConsoleCommand CCaption(TEXT("hrot.snd.Caption"), TEXT("hrot.snd.Caption <text>: show <text> as the sound caption (top-centre) for 8 s; empty = clear"),
		FConsoleCommandWithArgsDelegate::CreateLambda([](const TArray<FString>& Args)
		{
			FHrotSoundLog::Get().SetCaption(FString::Join(Args, TEXT(" ")), 8.0);
		}));
}

FHrotSoundLog& FHrotSoundLog::Get()
{
	static FHrotSoundLog Instance;
	return Instance;
}

bool FHrotSoundLog::IsOn() { return CDebug != 0; }

const TCHAR* FHrotSoundLog::NameOf(EHrotSndChannel C)
{
	switch (C)
	{
	case EHrotSndChannel::Radio: return TEXT("RADIO");
	case EHrotSndChannel::World: return TEXT("WORLD");
	case EHrotSndChannel::Weapon: return TEXT("WEAPON");
	case EHrotSndChannel::Ambience: return TEXT("AMBIENCE");
	case EHrotSndChannel::Event: return TEXT("EVENT");
	default: return TEXT("FX");
	}
}

FString FHrotSoundLog::Format(EHrotSndChannel Channel, const FString& Label, const FString& Detail, float DistM)
{
	FString S = NameOf(Channel);
	if (DistM >= 0.f) S += FString::Printf(TEXT(" %d m"), FMath::RoundToInt(DistM));
	if (Channel == EHrotSndChannel::Radio) S += TEXT(" · earpiece");
	S += TEXT(" · ");
	S += Label;
	if (!Detail.IsEmpty()) { S += TEXT(" — "); S += Detail; }
	return S;
}

float FHrotSoundLog::AlphaOfAge(double Age)
{
	if (Age < 0) return 1.f;
	if (Age >= Lifetime) return 0.f;
	if (Age <= Lifetime - FadeSeconds) return 1.f;
	return float((Lifetime - Age) / FadeSeconds);
}

void FHrotSoundLog::Add(EHrotSndChannel Channel, const FString& Label, const FString& Detail, float DistM, double Now)
{
	FHrotSoundLogEntry E;
	E.Time = Now; E.Channel = Channel; E.Label = Label; E.Detail = Detail; E.DistM = DistM;
	E.Text = Format(Channel, Label, Detail, DistM);
	Lines.Add(FString::Printf(TEXT("%d %s"), ++Counter, *E.Text));
	UE_LOG(LogHrotSound, Display, TEXT("%s"), *E.Text);   // U-19g: a live run is checkable after the fact
	if (Lines.Num() > TranscriptMax) Lines.RemoveAt(0, Lines.Num() - TranscriptMax);
	Ring.Add(MoveTemp(E));
	if (Ring.Num() > Capacity) Ring.RemoveAt(0, Ring.Num() - Capacity);
}

void FHrotSoundLog::Add(EHrotSndChannel Channel, const FString& Label, const FString& Detail, float DistM)
{
	Add(Channel, Label, Detail, DistM, FPlatformTime::Seconds());
}

bool FHrotSoundLog::AnyLive(double Now) const
{
	for (const FHrotSoundLogEntry& E : Ring) if (AlphaOfAge(Now - E.Time) > 0.f) return true;
	return false;
}

void FHrotSoundLog::SetCaption(const FString& Text, double Seconds, double Now)
{
	CaptionText = Text; CaptionFrom = Now; CaptionUntil = Now + Seconds;
}

void FHrotSoundLog::SetCaption(const FString& Text, double Seconds) { SetCaption(Text, Seconds, FPlatformTime::Seconds()); }

const FString& FHrotSoundLog::Caption(double Now, float& OutAlpha) const
{
	static const FString None;
	OutAlpha = 0.f;
	if (CaptionText.IsEmpty() || Now >= CaptionUntil || Now < CaptionFrom) return None;
	OutAlpha = FMath::Clamp(float((CaptionUntil - Now) / FadeSeconds), 0.f, 1.f);
	return CaptionText;
}

void FHrotSoundLog::Clear()
{
	Ring.Reset(); Lines.Reset(); Counter = 0; CaptionText.Reset(); CaptionFrom = CaptionUntil = 0;
}
