// CE-4651 (U-19b) — see HrotVoiceLines.h
#include "HrotVoiceLines.h"

namespace
{
	FVoiceCue World(const TCHAR* Set) { FVoiceCue C; C.Set = FString(TEXT("vox/")) + Set; return C; }
	FVoiceCue Radio(const TCHAR* Set) { FVoiceCue C; C.Set = FString(TEXT("vox/")) + Set; C.bRadio = true; return C; }

	/** a word of the order's name starts with `Stem` (the words split at anything that is not a letter / digit, lower case) */
	bool HasWord(const TArray<FString>& Words, const TCHAR* Stem, bool bExact = false)
	{
		for (const FString& W : Words) if (bExact ? W == Stem : W.StartsWith(Stem)) return true;
		return false;
	}
}

namespace HrotVoiceLines
{
	FVoiceCue ForUtterance(const FString& K)
	{
		struct FRow { const TCHAR* Kind; const TCHAR* Set; };
		static const FRow Rows[] = {
			{ TEXT("CmdMove"), TEXT("cmd_move") }, { TEXT("CmdHold"), TEXT("cmd_hold") }, { TEXT("CmdFallBack"), TEXT("cmd_fallback") },
			{ TEXT("CmdDown"), TEXT("cmd_down") }, { TEXT("CallCovering"), TEXT("call_cover") }, { TEXT("CallSuppress"), TEXT("call_suppress") },
			{ TEXT("CallReloading"), TEXT("call_reload") }, { TEXT("CallFragOut"), TEXT("call_fragout") }, { TEXT("CallContact"), TEXT("call_contact") },
			{ TEXT("CallGrenade"), TEXT("call_grenade") }, { TEXT("CallAck"), TEXT("ack") }, { TEXT("CallClear"), TEXT("call_clear") },
			{ TEXT("CallManDown"), TEXT("call_medic") },
		};
		for (const FRow& R : Rows) if (K.Equals(R.Kind, ESearchCase::IgnoreCase)) return World(R.Set);
		return FVoiceCue();   // civil, animal, anything else: not this file's
	}

	FVoiceCue ForReport(const FReportFields& R)
	{
		switch (R.Kind)
		{
		case ReportKind::Contact: return Radio(TEXT("rep_contact"));
		case ReportKind::Spot: return (R.Count <= 3 && R.Activity != Activity::Moving) ? Radio(TEXT("rep_spot")) : Radio(TEXT("rep_spot_big"));
		case ReportKind::Lace: return (int32(R.Kia) + int32(R.Wia) >= 2) ? Radio(TEXT("rep_losses")) : Radio(TEXT("rep_status_ok"));
		case ReportKind::Sitrep:
			switch (R.Strength)
			{
			case Colour::Green: return Radio(TEXT("rep_advantage"));
			case Colour::Amber: return Radio(TEXT("rep_status_ok"));
			case Colour::Red: return Radio(TEXT("rep_tight"));
			default: return FVoiceCue();
			}
		case ReportKind::Progress:
			switch (R.Sub)
			{
			case Progress::Set: return Radio(TEXT("rep_set"));
			case Progress::ObjectiveSecure: case Progress::BuildingClear: case Progress::RoomClear: return Radio(TEXT("rep_done"));
			case Progress::FallingBack: case Progress::Displacing: return Radio(TEXT("rep_fallback"));
			case Progress::TaskFailed: return Radio(TEXT("rep_failed"));
			case Progress::TargetsDown: return Radio(TEXT("rep_targetsdown"));
			default: return FVoiceCue();   // PhaseLine, Foothold, RoomBlocked, Detained, FireLifted: no line
			}
		case ReportKind::Request:
			switch (R.Sub)
			{
			case Request::Casevac: return Radio(TEXT("rep_casevac"));
			case Request::FireSupport: return Radio(TEXT("rep_fires"));
			case Request::Permission: return Radio(TEXT("rep_permission"));
			default: return Radio(TEXT("rep_backup"));   // Reinforce and every other request
			}
		default: return FVoiceCue();
		}
	}

	FVoiceCue ForOrder(const FString& Name)
	{
		TArray<FString> Words;
		FString Cur;
		for (const TCHAR Ch : Name.ToLower())
		{
			if (FChar::IsAlnum(Ch)) Cur.AppendChar(Ch);
			else if (!Cur.IsEmpty()) { Words.Add(Cur); Cur.Reset(); }
		}
		if (!Cur.IsEmpty()) Words.Add(Cur);
		FVoiceCue C;
		if (HasWord(Words, TEXT("attack")) || HasWord(Words, TEXT("assault"))) C = Radio(TEXT("ord_attack"));
		else if (HasWord(Words, TEXT("defend")) || HasWord(Words, TEXT("defen")) || HasWord(Words, TEXT("hold"))) C = Radio(TEXT("ord_defend"));
		else if (HasWord(Words, TEXT("flank"))) C = Radio(TEXT("ord_flank"));
		else if (HasWord(Words, TEXT("move")) || HasWord(Words, TEXT("advance")) || HasWord(Words, TEXT("go"), true)) C = Radio(TEXT("ord_move"));
		else C = Radio(TEXT("ord_standby"));
		C.AnswerSet = TEXT("vox/ack_order");
		return C;
	}
}

const TCHAR* FHrotCallsigns::NameAt(int32 I)
{
	static const TCHAR* N[Count] = { TEXT("Brown"), TEXT("Cheen"), TEXT("Cooper"), TEXT("Garcia"), TEXT("Johnson"), TEXT("Lee"), TEXT("Martinez"),
		TEXT("Nguyen"), TEXT("Patel"), TEXT("Ramirez"), TEXT("Taylor") };
	return N[((I % Count) + Count) % Count];
}

FString FHrotCallsigns::For(int32 UnitId)
{
	if (const int32* I = Assigned.Find(UnitId)) return NameAt(*I);
	const int32 I = Assigned.Num();   // the next unused; beyond Count the names repeat
	Assigned.Add(UnitId, I);
	return NameAt(I);
}
