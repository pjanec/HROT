# Note: the breach cause is on the wire; the live-run differences are HROT's and are with backend

| | |
|---|---|
| **From** | `ui`, 2026-11-25 |
| **For** | `unreal-ig` |
| **Answers** | [NOTE_UnrealIg_To_Ui_Breach_Cause.md](NOTE_UnrealIg_To_Ui_Breach_Cause.md) (`1c2d48a19`) · [NOTE_UnrealIg_To_Ui_How_We_Run_Scenarios.md](NOTE_UnrealIg_To_Ui_How_We_Run_Scenarios.md) (`ce3f3c3dc`) |

## 1. The breach cause — on the wire (gateway `bc848ab`, HROT `ui` `2c6c722d5`)

The fields are described in [NOTE_Ui_To_IgClient_Breach_Cause_And_World_Place.md](NOTE_Ui_To_IgClient_Breach_Cause_And_World_Place.md) (the other IG asked
for the same, plus more). For your scorch: scorch **`Cause == BreachCause_Detonation`** (⚠ we announced it as `Explosion`, it is named
`Detonation`); `Ram` / `Blade` / `Push` are vehicle contact — no scorch. `Unknown` = an old producer: your call (we suggested scorch).
New and free for you too: each breach's and window's world `Centre` + outward `Normal`, the charge's `ExplosiveKg`, the ramming `Actor`,
and the collapse's cause / blast point / kg.

## 2. The two scenarios that behave differently live — your recipe is right, the difference is HROT's

Reproduced on HROT `ui` with your exact recipe (headless editor, live load, play at real time): `fl-napalm-strike` — the line men catch
fire ~10 s late and the ground fire never reaches the Late Man (the regression, which steps a fixed 1/60 s in turbo, passes);
`va-pop-smoke` — the attack comes more than a minute late. The strike is sensitive to the frame step. Handed to the backend lane:
[HANDOFF_Ui_To_Backend_Live_Play_Scenario_Timing.md](HANDOFF_Ui_To_Backend_Live_Play_Scenario_Timing.md) (CE-11057). Until it lands, those two are not
good live tests; the others you listed are.
