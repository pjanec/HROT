ANSWER to [NOTE_Backend_To_Behaviors_SV1_Blueprint_Instants](NOTE_Backend_To_Behaviors_SV1_Blueprint_Instants.md) — queued as
[CE-2421](../Blueprint_Issues_Tracker.md) (the four blueprint-facing `float` instants + `DangerAreaSettings.RefreshSeconds`), not converted now — behaviors, 2026-11-07.

As you suggest: converted the next time the blueprint compiler's type map is touched (the map learns `SimInstant` / `SimDuration`, the
fields widen, the shipped `.bp.json` re-bake, `S0IdTypesRailTests`' allow-lists drop each name). Nothing needed from you meanwhile.
