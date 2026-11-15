<!--STATUS
state: ⛔ ON HOLD — the review (design §6) must be ruled first; this file will be re-stamped (rule 1a) — do NOT start
updated: 2026-11-15
current-answer: the whole file
related-designs:
  - ../../designs/air-attack/DESIGN_Radar_SAM.md — ⭐ THE design (RS-1 the vehicle, RS-9 who builds; §5 U-1…U-3)
  - ../../DESIGN_Visual_Effects.md — the launch / motor effects this extends
-->

# HANDOFF — coordinator → `ui`: the radar SAM's visuals (CE-4618)

**For:** the `ui` lane. **From:** coordinator. **Dispatched at `87fc746f3`**; ⛔ scope FROZEN there.

🔒 User, `2026-11-15`: *"The UI lane can provide the visuals for the carrier and launcher and the missile and the effect of launch."*

| # | builds | done when |
|---|---|---|
| **U-1** | the carrier (a wheeled truck; RS-1), the launcher (4 missile canisters that elevate to fire) and the radar mast — ⭐ the antenna TURNS while the vehicle's `RadarEmitter.On` is set (backend's B-1 component), still while Off | drawn on the 2-D map symbol and in 3-D; a frame rail run under `xvfb-run -a` (display-only rails skip silently otherwise) |
| **U-2** | the missile model + the launch effect (a canister flash, a smoke puff, the motor trail) through the existing effect sets — the same path the Stinger / Javelin launch uses | the launch shows on the frame of the shot |
| **U-3** | ⏸ optional: the radar's coverage on the map when the vehicle is selected (On / Off colour) | — |

⚠ backend builds the type with a placeholder asset key; bind your model to it. If backend's B-1 / B-2 are not merged when you start, build
against a test type and rebind. ⛔ No screenshots in git — side-by-sides go to the report and chat. Report: `STATUS_Ui_Radar_SAM_Visuals.md`.
