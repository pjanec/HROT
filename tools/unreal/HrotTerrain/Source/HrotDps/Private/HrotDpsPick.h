// CE-4651 (U-19g) - the pure part of click-to-select: which entity does a click mean? (docs/designs/IG: the camera and unit menu, U-18m, as-built 19g)
// A line trace (ECC_Visibility) sees vehicles and buildings but NOT skeletal soldiers, so the camera also projects every drawn entity to the screen and picks
// the nearest to the cursor within MaxPx; that candidate wins over a trace hit unless the trace hit is NEARER the camera (something stands in front of him).
#pragma once
#include "CoreMinimal.h"

namespace HrotDpsPick
{
	constexpr float MaxPx = 24.f;
	/** a candidate: a point on the screen, or (bSeg) the segment Screen - B (a man: feet -> head) */
	struct FCand { int32 Id = 0; FVector2D Screen = FVector2D::ZeroVector; double Depth = 0; FVector2D B = FVector2D::ZeroVector; bool bSeg = false; };

	/** the distance from P to the segment A-B (a point when A == B) */
	inline float DistToSegment(const FVector2D& P, const FVector2D& A, const FVector2D& B)
	{
		const FVector2D AB = B - A;
		const double L2 = AB.SizeSquared();
		const double T = L2 > 1e-9 ? FMath::Clamp(FVector2D::DotProduct(P - A, AB) / L2, 0.0, 1.0) : 0.0;
		return float(FVector2D::Distance(P, A + AB * T));
	}

	/** CursorPx, the candidates (screen px, depth = distance from the camera), the trace's hit (0 = none) and its distance; 0 = nothing */
	inline int32 Choose(const TArray<FCand>& Cands, const FVector2D& CursorPx, int32 HitId, double HitDepth, float MaxDist = MaxPx)
	{
		const FCand* Best = nullptr; float BestD = MaxDist;
		for (const FCand& C : Cands)
		{
			const float D = C.bSeg ? DistToSegment(CursorPx, C.Screen, C.B) : float(FVector2D::Distance(C.Screen, CursorPx));
			if (D <= BestD) { BestD = D; Best = &C; }
		}
		if (!Best) return HitId;
		if (HitId != 0 && HitId != Best->Id && HitDepth < Best->Depth) return HitId;   // something nearer is under the cursor
		return Best->Id;
	}
}
