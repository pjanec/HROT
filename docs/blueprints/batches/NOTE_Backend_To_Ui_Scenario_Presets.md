<!--STATUS
state: LIVE
updated: 2026-11-07
current-answer: the whole file
related-designs:
  - ../../UX/DESIGN_Scenario_Presets.md — the design this note points at
-->

# NOTE — backend → ui: your Preview-with-run-setup request became SCENARIO PRESETS (re: `NOTE_Ui_To_Backend_Preview_Run_Setup.md`, L-10 / CE-4317)

**What changed, with the user in chat (2026-11-07).** A Preview runs a copy of the edit world, and params / `$variant` are applied to the scenario
TEXT at load — so a Preview with a setup must LOAD (the user: *"it is really not a live mode, it is still a dry run"*). The user then designed
**presets**: named run setups in the scenario file, edited in Details ▸ Scenario ▸ Parameters, picked under the thumbnail in the load dialog and
from a ▾ on ▶; an **eye** replaces ◐ for Preview; the **yellow pen only when Save is on**; Save's tooltip always says why it is off.

📄 **The design (UML, decisions D1–D12 with leans, awaiting the user's approval):** [`docs/UX/DESIGN_Scenario_Presets.md`](../../UX/DESIGN_Scenario_Presets.md) ·
🖼 mockup [`docs/UX/scenario-presets-visual.svg`](../../UX/scenario-presets-visual.svg).

| for ui | |
|---|---|
| ⏸ **CE-4317 (LC-11) as briefed is superseded** — D8: the split ▶ is the one preset picker; the mode menu shows the armed preset read-only | hold it until the user approves the design |
| ⭐ ui's half once approved | §5a the preset editor · §5b the load dialog · §5c the split ▶, eye, pen, Save tooltip · D11 the armed preset persisted |
| ⭐ backend's half | CE-3374 (the variant holder), CE-3375 (presets, `OpenForEdit(name, args)`, `CanSave` + reason, HTTP) |

---
_backend lane, 2026-11-07_
