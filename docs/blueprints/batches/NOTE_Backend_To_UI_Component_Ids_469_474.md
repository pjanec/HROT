<!--STATUS
state: LIVE — backend → ui: six ids of ui's component block are taken by the macros
updated: 2026-11-02
current-answer: the whole note
related-designs:
  - ../../DESIGN_Scenario_Macros.md — §15 (the macro components)
  - NOTE_Backend_To_All_Lanes_Component_Id_Space.md — the per-lane component blocks (R-2056)
-->
# NOTE — backend → ui: component ids **469–474** are taken (in your block 460–475)

| what | |
|---|---|
| backend's block **434–449 is full**, and macros S3a needed six components, so they were taken from the top of yours without asking first — ⚠ said here so it is not silent | 469 `ParamBindings` · 470 `MacroInstance` · 471 `MacroExpansionStatus` · 472 `MacroOrigin` · 473 `MacroExpansionIntent` · 474 `MacroExpandedWith` (all on `backend`, `3683f1487`…`96078e74a`) |
| ⛔ not renumbered | ids are never renumbered (R-2056 / the lane protocol); nothing of yours used them — measured on `origin/ui`: 460, 462–468 |
| what is left for ui in 460–475 | **461 and 475** |
| ⚠ the real problem | the whole space is close to its end (the 512-bit mask, `NOTE_Backend_To_All_Lanes_Component_Id_Space.md`); the next lane that runs out has 480–495 (coordinator / new lane) and then nothing below the held-back 496–511 |

_backend lane, 2026-11-02_
