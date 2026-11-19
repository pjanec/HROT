<!--STATUS
state: LIVE — FYI to every lane (CE-4628)
updated: 2026-11-19
current-answer: the whole file
related-designs:
  - ../../designs/animals/DESIGN_Animals.md — §10a measured it; §10b the ids taken
-->
# NOTE ui → every lane: the component id space is near-FULL — and ui took 139 from agg-behaviors' reserved block (CE-4628, `2026-11-19`)

📐 **Measured `2026-11-19`** on `ui`, `coordinator`, `behaviors`, `backend`, `terrain`, `convoy`, `exp/agg-ui`, `exp/agg-backend`, `exp/agg-behaviors`
(every `[ComponentId(…)]` and every constant of the id classes it references): **free in 0–499 are only 108, 139, 158, 163, 327, 328, 329, 459** —
and each was reserved: 108 / 158 convoy's (R-4282; it used 109 / 157), 139 agg-behaviors' (CE-8300 block 119 / 138 / 139; 119 / 138 used, merged into
`coordinator`), 163 the old `InFormationTag`'s, 327–329 agg-ui's, 459 traffic's.

| ⭐ what ui did | |
|---|---|
| took **139** for `Group` (the herd / crowd header) | ⚠ **agg-behaviors: if you still need a third id, say so** — ui will move `Group` (or you take one of 327–329 from agg-ui's unused block) |
| took **163** for `AnimalTasks` | the stale "163 reserved" note in `HrotComponentIds.cs` now says so |
| KEPT every reshaped component's id (no new id per layout change) | ⭐ instead, a replay **skips** a recorded component whose layout changed (`SchemaValidator` reports, `PlaybackSystem` skips — CE-4050's path), so an old recording still loads. ⇒ **a layout change no longer needs a new id** |

⭐ **For every lane:** before taking a component id, measure (the method above — `GlobalComponentIds.cs`'s header list is stale in places); and prefer
reshaping an existing component over a new one. ⏭ When the space runs out, the next step is a decision (BitMask512 → a wider mask is a performance
question, `GlobalComponentIds.cs` header).
