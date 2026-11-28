// CE-4651 (U-19b) — THE LINE MAP: HROT's Utterance / Report / Order → the voice-line set to play (docs/designs/IG/DESIGN_IG_Squad_Radio.md §2).
// PURE: plain fields in, an FVoiceCue out (empty Set = say nothing) — no UObject, no world, railed alone (Hrot.Dps.Voice.Lines). The numbers
// below are HROT's own (Report.Sub is a ProgressKind or a RequestKind by Report.Kind); ReportKind / StatusColour / ElementActivity are the
// contract's enums, passed as their numbers so this header needs no generated type.
#pragma once

#include "CoreMinimal.h"
#include "HrotVoicePlayer.h"

namespace HrotVoiceLines
{
	// the contract's enum numbers (DpsTypes.h)
	namespace ReportKind { enum : int32 { None = 0, Contact, Spot, Sitrep, Lace, Progress, Request }; }
	namespace Colour { enum : int32 { Unknown = 0, Green, Amber, Red, Black }; }
	namespace Activity { enum : int32 { Unknown = 0, Firing, Moving, Static, Withdrawing, Surrendering }; }
	// HROT's ProgressKind / RequestKind numbers (Report.Sub)
	namespace Progress { enum : int32 { Set = 1, PhaseLine, Foothold, RoomClear, RoomBlocked, BuildingClear, ObjectiveSecure, Detained, Displacing, FallingBack, FireLifted, TaskFailed, TargetsDown }; }
	namespace Request { enum : int32 { Casevac = 1, FireSupport, Resupply, Reinforce, Permission, Engineers }; }

	struct FReportFields
	{
		int32 Kind = 0, Sub = 0, Count = 0, Activity = 0, Strength = 0, Wia = 0, Kia = 0;
	};

	/** a man's shout (Utterance.Kind): world; civil / animal kinds ⇒ an empty cue */
	HROTDPS_API FVoiceCue ForUtterance(const FString& Kind);
	/** a unit's report: radio */
	HROTDPS_API FVoiceCue ForReport(const FReportFields& R);
	/** an order (Order.Name) by its keyword, plus the receiver's answer: radio */
	HROTDPS_API FVoiceCue ForOrder(const FString& Name);
}

/** the sergeants' names: a unit's first transmission takes the next unused of the eleven, kept for the session; the 12th wraps to the first */
class HROTDPS_API FHrotCallsigns
{
public:
	static constexpr int32 Count = 11;
	/** "Brown" … "Taylor" */
	FString For(int32 UnitId);
	void Reset() { Assigned.Reset(); }
	static const TCHAR* NameAt(int32 Index);
private:
	TMap<int32, int32> Assigned;
};
