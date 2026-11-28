// CE-4651 (U-18m) — THE IG's HUD, as HROT's station draws it (PossessionStation.DrawOverlay / Hud, the DPS client guide "The player's HUD"):
//   playing   the crosshair (4 white ticks 3-10 px off the centre), the weapon's spread as a circle, the hit marker (diagonal ticks 7-14 px,
//             0.3 s, red when the man went down), the damage ARCS (red, ±22° on a ring of 0.38 × the smaller screen side, toward where the
//             hit came FROM, fading over 2 s, thicker with the damage), the use prompt under the crosshair, and bottom-left HEALTH, the
//             weapons (the selected one marked ">"), STANCE, the fire mode and the zoom
//   watching  the unit menu (right-click: Follow with the camera / Possess (FPS), greyed with the reason) and a status line
// One drawing (HrotDpsHud::Draw onto a UCanvas) for two hosts: AHrotDpsHud under a player controller (installed by AHrotDpsCamera::BeginPlay),
// and the debug-draw service when the camera has no controller (Simulate, a test, a render node) — the same picture either way.
#pragma once

#include "CoreMinimal.h"
#include "GameFramework/HUD.h"
#include "HrotDpsHud.generated.h"

class UCanvas;
class AHrotDpsCamera;
class APlayerController;

namespace HrotDpsHud
{
	/** draws the HUD of Cam onto Canvas; PC (optional) gives the mouse for the menu's hover */
	HROTDPS_API void Draw(UCanvas* Canvas, AHrotDpsCamera* Cam, APlayerController* PC);
}

UCLASS()
class HROTDPS_API AHrotDpsHud : public AHUD
{
	GENERATED_BODY()
public:
	virtual void DrawHUD() override;
};
