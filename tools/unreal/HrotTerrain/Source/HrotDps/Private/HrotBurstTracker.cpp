// CE-4651 (U-19c) — see HrotBurstTracker.h
#include "HrotBurstTracker.h"

FHrotBurstTracker::FResult FHrotBurstTracker::OnRound(uint32 Id, int32 Rounds, int32 Index, double Now, TFunctionRef<bool(int32)> HasClip)
{
	FResult R;
	if (Id == 0) return R;
	if (Index == 0)
	{
		int32 N = FMath::Min(Rounds, MaxClip);
		while (N >= MinClip && !HasClip(N)) --N;
		FBurst B; B.LastAt = Now;
		B.ClipRounds = N >= MinClip ? N : 0;
		Bursts.Add(Id, B);
		if (B.ClipRounds) { R.Act = EAct::Start; R.ClipRounds = B.ClipRounds; }
		return R;
	}
	FBurst* B = Bursts.Find(Id);
	if (!B) return R;   // its first round was never seen (a late join): per round
	if (Index > B->LastIndex) { B->Spacing = FMath::Max(Now - B->LastAt, 0.01); B->LastIndex = Index; }
	B->LastAt = Now;
	if (Index < B->ClipRounds) { R.Act = EAct::Covered; R.ClipRounds = B->ClipRounds; }
	return R;
}

void FHrotBurstTracker::SetHandle(uint32 Id, int32 Handle) { if (FBurst* B = Bursts.Find(Id)) B->Handle = Handle; }

void FHrotBurstTracker::PollCuts(double Now, TArray<int32>& Out)
{
	for (auto It = Bursts.CreateIterator(); It; ++It)
	{
		FBurst& B = It.Value();
		if (!B.bCut && B.Handle != 0 && B.LastIndex + 1 < B.ClipRounds && Now - B.LastAt > 2.0 * B.Spacing + Slack)
		{
			B.bCut = true;
			Out.Add(B.Handle);
		}
		if (Now - B.LastAt > ForgetAfter) It.RemoveCurrent();
	}
}
