// CE-4651 (U-19h) - the pure part of the hold-F list: a TAP of F is the primary action (Verb None, sent on RELEASE); HOLDING F >= HoldSeconds opens HROT's
// offered-verb list (OwnUnit.Actions); the wheel / 1-8 move the highlight over the AVAILABLE rows; releasing sends ONE press naming the highlighted row's verb;
// Esc / right mouse while open cancels (no press). An empty list on hold = nothing happens. (HROT DESIGN_Entity_Control.md 8h; the client only sends the chosen verb.)
#pragma once
#include "CoreMinimal.h"

namespace HrotDpsUseList
{
	constexpr double HoldSeconds = 0.25;
	constexpr int32 MaxRows = 8;

	struct FRow { uint8 Verb = 0; bool bAvailable = false; };
	struct FState
	{
		bool bDown = false, bOpen = false, bCancelled = false;
		double DownAt = 0;
		int32 Highlight = -1;   // index into Rows, -1 = none
	};
	struct FOut { bool bPress = false; uint8 Verb = 0; };   // Verb 0 = None = the primary

	inline int32 FirstAvailable(const TArray<FRow>& Rows) { for (int32 I = 0; I < Rows.Num(); ++I) if (Rows[I].bAvailable) return I; return -1; }

	/** the next available row from `From` in direction Dir (+1 down the list, -1 up); stays where it is at the ends (no wrap) */
	inline int32 Move(const TArray<FRow>& Rows, int32 From, int32 Dir)
	{
		for (int32 I = From + Dir; I >= 0 && I < Rows.Num(); I += Dir) if (Rows[I].bAvailable) return I;
		return From;
	}

	/** one frame: the F key's state, the time, HROT's rows now, the wheel (+1 = next row, -1 = previous, 0 none), a number-key pick (0-based, -1 none), cancel pressed */
	inline FOut Step(FState& S, bool bFDown, double Now, const TArray<FRow>& Rows, int32 Wheel, int32 Pick, bool bCancel)
	{
		FOut Out;
		if (bFDown && !S.bDown) { S = FState(); S.bDown = true; S.DownAt = Now; }
		else if (bFDown && S.bDown)
		{
			if (!S.bOpen && !S.bCancelled && Now - S.DownAt >= HoldSeconds && Rows.Num() > 0) { S.bOpen = true; S.Highlight = FirstAvailable(Rows); }
			if (S.bOpen)
			{
				if (bCancel) { S.bOpen = false; S.bCancelled = true; S.Highlight = -1; }
				else
				{
					if (S.Highlight >= Rows.Num()) S.Highlight = FirstAvailable(Rows);
					if (Wheel != 0) S.Highlight = S.Highlight < 0 ? FirstAvailable(Rows) : Move(Rows, S.Highlight, Wheel > 0 ? 1 : -1);
					if (Pick >= 0 && Pick < Rows.Num() && Rows[Pick].bAvailable) S.Highlight = Pick;
				}
			}
		}
		else if (!bFDown && S.bDown)
		{
			const bool bHeldLong = Now - S.DownAt >= HoldSeconds;
			if (S.bOpen) { if (S.Highlight >= 0 && S.Highlight < Rows.Num() && Rows[S.Highlight].bAvailable) { Out.bPress = true; Out.Verb = Rows[S.Highlight].Verb; } }
			else if (!S.bCancelled && !bHeldLong) Out.bPress = true;   // a tap: the primary
			S = FState();
		}
		return Out;
	}
}
