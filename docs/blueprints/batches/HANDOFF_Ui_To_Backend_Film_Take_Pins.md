# HANDOFF ui → backend — a film take's per-unit gizmo pins *(CE-1329 BV-73)*

Dispatched at: (the ui commit carrying this file)

**Why.** Since CE-1329 ([Brain View §12r–§12s](../../DESIGN_Entity_Brain_View.md)) a BRAIN gizmo family (Perception, Contacts, EQS,
Utility, Squad, Actions, Command picture, My units' contacts) draws only for a unit it is PINNED on — selecting a unit draws none, and
the Layers panel's rows are global on / off only. ⇒ a take's `"layers": {"on": ["Ai.VisionCones"]}` ([Film Director §4b](../../DESIGN_Film_Director.md))
turns the layer on but draws no cone until some unit is pinned.

**Frame (the receiver designs the details).**

| | |
|---|---|
| goal | a take (top level and per shot) may pin families on named units: `"pins": {"Sentry 2": ["Perception"], "1004": ["Contacts"]}` |
| fence | a TEMPORARY override, like the take's layers: dropped when the take (or the shot) ends; ⛔ never written to the store's remembered entries (the user's per-scenario file) |
| design basis | the store: `Fdp.Toolkit.Behavior.Diagnostics.UnitDebugSettings` (keyed by network id); a temporary request set beside `viewed` / `armed` would fit — or the director restores what it changed |
| HTTP | `POST /entities/{id}/debug-settings {pins}` exists (ui) — ⚠ it is REMEMBERED; a take must not use it for a temporary pin |
| acceptance | a take pinning Perception on one sentry shows its cone in the capture; after the take the user's pins are as before |
