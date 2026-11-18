<!--STATUS
state: LIVE — FYI, a cross-lane edit in your file
updated: 2026-11-18
current-answer: the whole file
related-designs:
  - ../../designs/minefields/DESIGN_Minefields.md — MF-2 (the pattern, now with the edge inset)
-->

# NOTE — terrain → behaviors: mines keep off the field's edge (CE-4655); your recon's mine test now uses the shared one

| what | why | your side |
|---|---|---|
| a laid field keeps every mine `max(spacing/2, 1 m)` INSIDE its outline — `MinePattern.Laid` is now the ONE "does this lattice point hold a mine" test (pattern, trigger, clearing pass) | the user saw a squad skirting a struck field blow up on mines laid right on its edge; a route round a known field hugs the edge (the ×20 / ×200 pricing is unchanged) | nothing to change in the avoid / back-out |
| ⚠ **cross-lane edit in `ReconMinefieldBehavior.Mined`**: it calls `MinePattern.Laid(in f, xs, ys, p)` (a span overload over your fixed `OX` / `OY` buffers); its private `InOutline` copy is removed | otherwise recon would report edge cells "mined" that hold no mine | `Hrot.SimHost.Tests` ReconMinefield + MinefieldBake 3 / 3 after the edit; `mf-recon-and-mark` re-run after it: see STATUS_Terrain_Queue §5 |
| new scenario **`mf-way-round`** (a march with `DangerAreas` off routed round a known field): 4/4; with the inset off a mine goes off at 56.5 s | the regression for CE-4655 | — |
| `mf-no-way-round` "nobody enters the field" is red at 49.5 s WITH AND WITHOUT the inset | ⇒ your CE-2447, untouched by this | — |
