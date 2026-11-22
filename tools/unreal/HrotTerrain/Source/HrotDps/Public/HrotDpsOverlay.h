// CE-4651 (U-19e) — THE ONE OVERLAY PAINTER (DESIGN_IG_Squad_Radio.md §7, F-1): the director's subtitle and fade, drawn the same way into a render job's
// image (UHrotDpsRender::DrawOverlay, FrameRequest.Subtitle / SubtitlePosition / SubtitleAlpha / Fade) and onto the live screen (AHrotDpsHud, the station's
// View, only in director mode). The maths is pure (railed: Hrot.Dps.Overlay); Paint only issues the canvas calls.
#pragma once

#include "CoreMinimal.h"

class UCanvas;
class UFont;

namespace HrotDpsOverlay
{
	/** what to draw: Pos is the contract's SubtitlePosition (0 unknown, 1 bottom, 2 top, 3 centre); Alpha 0 = unset = opaque; Fade 0 clear .. 1 black */
	struct FSpec
	{
		FString Subtitle;
		uint8 Pos = 0;
		float Alpha = 1.f, Fade = 0.f;
	};
	/** what was drawn (the HUD's probes) */
	struct FPainted { bool bSubtitle = false, bFade = false; };

	/** the subtitle's baseline as a fraction of the height: top 0.08, centre 0.5, bottom (and unknown) 0.88 — as the render path always drew it */
	HROTDPS_API float SubtitleY(uint8 Pos);
	/** the subtitle's opacity: an unset (<= 0) alpha is opaque, else clamped to 1 */
	HROTDPS_API float SubtitleOpacity(float Alpha);
	/** the fade rectangle's opacity, clamped to 0..1 */
	HROTDPS_API float FadeOpacity(float Fade);
	/** a subtitle is drawn iff it has text; the fade rectangle iff it is visible (> 0.5 %) */
	HROTDPS_API bool HasSubtitle(const FSpec& S);
	HROTDPS_API bool HasFade(const FSpec& S);

	/** the subtitle (centred at Size.X / 2, shadowed, scaled by Size.Y / 540), then a black rectangle at the fade's opacity OVER everything already on the canvas */
	HROTDPS_API FPainted Paint(UCanvas* Canvas, FVector2D Size, const FSpec& S, UFont* Font);
}
