<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-11
current-answer: the whole file
related-designs:
  - ../../DESIGN_Terrain_Authoring.md — OWNS this work: §2 feature kinds, §7 clutter, §8 rivers and bridges, §11 blocks (R-4199), §12–§13 the random terrain and the workflow (R-4200), §18 the compiled overlay as you built it (R-4204)
  - ../../DESIGN_Terrain_Synth.md — hrot-terrain-synth, the code T-1 turns into a library
  - HANDOFF_Coordinator_To_Ui_Terrain_Authoring.md — ui's parallel batch: its U-3 (Unpack), U-7 (feature tools, scatter) and U-8 (the wizard) sit on T-4, T-2 and T-1
  - STATUS_Backend_Terrain_Overlay.md — your previous batch (O-0…O-8)
-->

# HANDOFF — coordinator → backend: terrain authoring, part 2 — the review fixes, the generator library, more feature kinds, richer blocks

**For:** backend. **From:** coordinator. **Dispatched at `21392507c`**, scope frozen there; later documents are FYI (one that INVALIDATES an item
⇒ stop THAT item and report it). Merge the lanes you build on; push `chore: started HANDOFF_Coordinator_To_Backend_Terrain_Authoring_2 at <sha>`;
status file `STATUS_Backend_Terrain_Authoring_2.md`, per item. ⛔⛔ **Autonomous — never stop for a question** (R-4156); a failed premise stops THAT
item only (R-106); a design choice ⇒ the lean here or your own, with its claim table in the status file. ⭐ **You design the details** — UML in the
owning design first, then build, then fold the as-built back.

**Why:** your O-0…O-8 batch built the engine and it holds up (the overlay hash is deterministic, the merge's index offsets are right, the per-frame
paths allocate nothing — checked). What the user's approved mockups need next is behind it: ui builds the editor side in parallel
([its handoff](HANDOFF_Coordinator_To_Ui_Terrain_Authoring.md)) and waits on T-1, T-2 and T-4 for three of its items. 🔒 User, `2026-11-11`:
*"The generator should become a library, very likely at the beginning."* (R-4202) · *"approved, mockups too"* (R-4203).

## Items, in order

| # | id | what | done when |
|---|---|---|---|
| **T-0** | CE-4427 | ⭐ **the review findings of O-0…O-8** (an independent review of `67b5fcb8d..backend@e9a0b213b`; ✔ = re-checked by the coordinator): **R-1 ✔** a compile that throws before `.part` leaves the OLD `terrain-overlay/` and it is distributed (`StorageProcessManager.cs:244-246` only logs) — delete it on failure; **R-2 ✔** an unresolved key stores the sender's WITHIN-building panel as a GLOBAL index (`EntityBuildingDamageTranslators.cs:186-198`) — say on the wire that panels are local, and defer / drop what cannot resolve, never fall back to the raw index; **R-3** `BuildingPanelIndex.ToLocal` scans panels per breach per egress tick — cache per world or compare before converting; **R-4** the editor's live re-expansion re-sorts overlay parts, so `BuildingDamage`'s global indices drift — re-key by stable key when the merged world changes; **R-5** the unbounded construction wait applies whenever the obstacle ack is pending, even to other modules' acks, and hangs where the bake returns before acking (`StaticObstacle` unregistered, a missing `SimTransform` / `ObstacleShape`) — unbounded only when the terrain module is the ONLY ack left, plus a loud log after N seconds; **R-6** `RecastNavmeshFactory`'s previous keys / serial are shared between overlapping bakes (a staged load and an obstacle bake) — lock `Build` and return serial + keys; **R-7** the live overlay cache ignores recipe / template file edits — key it on their (length, mtime) too | a red-proved rail per finding you confirm (R-2: a breach whose key the receiver cannot resolve never breaches another house); a finding you refute, with its trace, in the status file |
| **T-1** | CE-4428 | ⭐ **the generator as a library, and the wizard's engine** (§12, §13): the synth's heights / road net / rivers / ground cover as a library the editor calls (the CLI stays on it); presets (Plain terrain · Open countryside · Villages · Small town · Town with suburbs · Mixed) × Size · Relief · Vegetation · Water · Seed → ① a GROUND terrain folder (through the importer's path, prefill included) and ② a LAYOUT of recipe blocks as `TerrainBlock` entities in a scenario DOM; fast PREVIEW data for the 2-D plan (relief, roads, water, the blocks' outlines, the counts, the budget verdict); *Shuffle layout* (new seeds, same ground — seconds) and *Shuffle ground*; *Open in editor* writes both and returns what to open (V-1). Plain terrain = no layout | rails: same inputs → byte-identical ground and layout; shuffle layout keeps the ground; a 3 km *Town with suburbs* generates, imports and loads in budget (R-4192) |
| **T-2** | CE-4429 | ⭐ **more feature kinds + the area scatter** (§2, §7, §8): wall / fence / hedge LINES, wood AREAS, lone TREES, BUSH / scrub patches, CLUTTER items (the §7 list — a material prism; reuse the obstacle TKB types where they exist), WATER areas, BRIDGES (your O-2 `structure`), and the AREA SCATTER (an area + a recipe + a seed + a density — ONE entity, R-4199 B-2). Shapes as the managed immutable `EditablePolyline` (G-15: the 1 024-byte component cap rules out a fixed list; CLAUDE.md allows a rarely changed managed record). Each compiled, merged and drawn by the existing layers; state each kind's destructibility (baked clutter: not — G-15) | rails per kind: a hedge blocks movement, a wood conceals, water is impassable, a unit crosses a bridge, a scatter of 140 items compiles deterministically |
| **T-3** | CE-4430 | **a recipe vocabulary for the mockup's blocks** (§11 B-8): lanes / streets inside a block, house rows along a road, a street grid of 3–6-storey UNIQUE houses (the synth's building generator — R-4202 L-2), yards and barns, walls and hedges, clutter inside a block, prisms (sandbags, barriers, a booth), orchard rows, hedgerow fields — and ship the TEN blocks of mockup page 2 (farmstead, hamlet, village street, town block, checkpoint, roadblock, defensive position, copse, orchard, hedgerow fields) | each shipped recipe expands deterministically; a real frame of each beside its mockup tile, reasonably matching (R-4146) |
| **T-4** | CE-4431 | **block operations** (§11 B-6, B-Q1): engine commands the editor calls, ONE undo step each — *Re-roll* (a new seed), *Unpack* (the block becomes its own `TerrainFeature` entities on T-2's kinds, frozen — no longer follows the recipe), *Change recipe* (keeps place and heading) | rails: unpack → recompile gives the SAME parts as before the unpack; undo restores the block |
| **T-5** | CE-3431 | **a block aligns to the nearest road** (the Fit bit is stored and ignored today) | a rail: a farmstead placed near a track turns to face it |
| **T-6** | CE-3432 · CE-3433 · CE-3434 · CE-3435 | your own findings: prune staged overlay folders; the compile off the save's answer; a recorded obstacle born during 1× playback; stale base navtiles in *Save terrain as* | each row's done-when |
| **T-7** | CE-4423 | **roads and bridges in the road net** (G-12): a placed bridge carries vehicles and SUMO traffic; a destroyed one stops them; netconvert on *Save terrain* | a vehicle routes over a new bridge and around a fallen one |
| **T-8** | CE-4424 | **the missions check for a re-saved terrain** (G-13): an API ui's overwrite dialog calls — for each scenario on the terrain, every unit / route / area a new or removed part intersects | a rail lists the squad a new house would stand on |

**Not in this batch:** the external engine (CE-4421, needs the gateway session); the UI (ui's batch).

## Rules

- **Proof proportionate to risk:** T-0, T-2, T-4, T-7 touch sim / wire / determinism — red-prove each new rail and gate the integration suites that
  exercise the bake, the load and the damage wire (name them, CLAUDE.md row 8). T-1 / T-3 are generation — determinism rails + frames.
- **0 B garbage per steady frame** for anything per-frame.
- **Shared, never duplicated:** ONE expander, ONE merge, ONE compile path — the generator's layout produces the same `TerrainBlock` entities a user
  places; the generator library is the synth's code moved, not copied.
- Fold every as-built into Terrain Authoring (a §18f… sub-section) or the design it touched; new findings ⇒ rows in your block.
- Gates per the gate-report contract; `tracker-counts.py --check`.

**Done when:** every item closed with its evidence, or the status file names exactly what blocks it and what was measured.

---
_coordinator lane, 2026-11-11_
