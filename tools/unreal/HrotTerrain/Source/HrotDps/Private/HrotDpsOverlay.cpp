// CE-4651 (U-19e) — see HrotDpsOverlay.h
#include "HrotDpsOverlay.h"
#include "CanvasItem.h"
#include "Engine/Canvas.h"
#include "Engine/Font.h"

namespace HrotDpsOverlay
{
	float SubtitleY(uint8 Pos) { return Pos == 2 ? 0.08f : Pos == 3 ? 0.5f : 0.88f; }
	float SubtitleOpacity(float Alpha) { return Alpha > 0.f ? FMath::Min(Alpha, 1.f) : 1.f; }
	float FadeOpacity(float Fade) { return FMath::Clamp(Fade, 0.f, 1.f); }
	bool HasSubtitle(const FSpec& S) { return !S.Subtitle.IsEmpty(); }
	bool HasFade(const FSpec& S) { return FadeOpacity(S.Fade) > 0.005f; }

	FPainted Paint(UCanvas* Canvas, FVector2D Size, const FSpec& S, UFont* Font)
	{
		FPainted Out;
		if (!Canvas) return Out;
		if (HasSubtitle(S) && Font)
		{
			const float Scale = float(Size.Y / 540.0);
			const FLinearColor Col = FLinearColor::White.CopyWithNewOpacity(SubtitleOpacity(S.Alpha));
			Canvas->K2_DrawText(Font, S.Subtitle, FVector2D(Size.X * 0.5, Size.Y * SubtitleY(S.Pos)), FVector2D(Scale * 1.6f, Scale * 1.6f), Col, 0, FLinearColor::Black, FVector2D(2, 2), true, true, true,
				FLinearColor::Black.CopyWithNewOpacity(Col.A));
			Out.bSubtitle = true;
		}
		if (HasFade(S))   // the director's dip to black: over the picture, the graphics and the subtitle
		{
			FCanvasTileItem T(FVector2D(0, 0), Size, FLinearColor(0, 0, 0, FadeOpacity(S.Fade)));
			T.BlendMode = SE_BLEND_Translucent;
			Canvas->DrawItem(T);
			Out.bFade = true;
		}
		return Out;
	}
}
