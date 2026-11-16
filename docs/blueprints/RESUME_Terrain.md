<!--STATUS
state: LIVE
updated: 2026-11-16
current-answer: §1 — the task in flight, the last green sha, what is next
stale-below: §3 is the dated log (history)
related-designs:
  - ../PLAN_Dynamic_Terrain.md — the programme's task list and order (this lane runs all of it)
  - ../DESIGN_Vehicle_Breaching.md — B-1…B-6; §7 wins
  - ../DESIGN_Ground_Patches_And_Road_Blockage.md — P-0…P-6; §9 wins
  - batches/HANDOFF_Coordinator_To_Terrain.md — the dispatch (scope frozen at a4afa4e2a; re-stamped 8e3a8000)
  - STATUS_Terrain.md — the morning report (decisions first)
-->

# RESUME — the `terrain` lane (dynamic terrain programme, CE-4604)

## 1. Now

| | |
|---|---|
| branch | `terrain` (pushed also to the session branch) — started at `8e3a8000` (coordinator merged) |
| order | B-1 → P-0 → B-2 → P-1 → P-2 → B-3 → P-3 → P-4 → B-6 → B-4 → B-5 → P-5 → P-6 ([handoff §2](batches/HANDOFF_Coordinator_To_Terrain.md)) |
| done | **B-1** (CE-4598), **CE-6014** (found by B-1), **P-0** (CE-4596), **B-2** (CE-4599), **P-1** (CE-4403, 🟡), **P-2** (CE-4590), **B-3 core** (CE-4600), **P-3** (CE-4597), **P-4** (CE-4591, ✅ every item incl. V-11 decal, V-12 strand, stepped hash), **B-6** (CE-4603), **B-4** (CE-4601), **B-5** (CE-4602), **P-5 parts 1 + 2a** (CE-4592, 🟡 2b open), **P-6 part 1** (CE-4593, 🟡) |
| last green | `dd028cc93` (the batch gate: every red pre-existing) — see STATUS §3 |
| next | every task has a first build. Left: **CE-5002 part 2** (the first bake's start and commit, ~21 ms each once per session — profile first) · **CE-5000 part 3** (BuildingDamageTable on the shared cache — reverted, a stale table in one test order, cause not found) · **D-4** (Architect Question 112 awaits the user: versions by hash) · **CE-4402** (needs the scale terrains re-imported — their input sets are not in this checkout) · **V-29**'s behaviour half is the behaviors lane's (R-5031, handoff) |
| open | B-3b (V-17 feedback consumers), P-1's V-15/D-4/CE-4402 |
| open from P-1 | V-15, D-4, CE-4402 (re-import) — see STATUS |
| open from P-0 | stepped-hash run 1/4/8: equal (bt-mortar-roof, roadblock-blast; STATUS §3) |

## 2. Decisions taken (lane rulings, for the user's review)

See [STATUS_Terrain.md](STATUS_Terrain.md) §1 — the list is kept there, once.

## 3. Log

- `2026-11-16` — ⭐ **CE-4594 terrain editor UX BUILT** (all seven slices, [DESIGN_Terrain_Editor_UX.md](../DESIGN_Terrain_Editor_UX.md) §6 as-built notes): CE-5006 toolbars + the Terrain lock · CE-5007 tree Row / Group · CE-5008 obstacle rows · CE-5009 snapping (+ panel toggle, road offset, hint `d899e0248`) · CE-5010 the Terrain Details tab + obstacle types 8820–8822 · CE-5011 feature gizmo, ghost, 3-D tree pick (`e6033cbff`) · CE-5012 plot wall, Add gate here, berm, breach remap by geometry (`78a2b2a0b`, `5145f9cea`). CE-5013 toolbar content decided (R-5039/5040). Open: a ditch / berm line snaps but shows no hint text (the hint rides the ghost underlay); `Map3DFrameRail.CE4582` red only in a wide run (green alone); `EntityTypeCatalog` CE2138 golden updated for 8820–8822.
- `2026-11-15` — **close-out** ([STATUS_Terrain_Closeout.md](batches/STATUS_Terrain_Closeout.md)): CE-4403 / 4592 / 4593 / 4600 / 4604 closed, the plan BUILT; CE-4588's three rails and the geo rail (CE-5003) re-pointed — stale rails, no defect; CE-5004 (D-4, awaits AQ112) and CE-5005 (ui: the hint's glyphs) filed. ⭐ + CE-4594 DESIGNED ([DESIGN_Terrain_Editor_UX.md](../DESIGN_Terrain_Editor_UX.md), leans TE-1…TE-12 await the user). Next: the user's answers on TE-1…TE-12 and AQ112, CE-5002 part 2.
- `2026-11-15` — the batch gate: two of mine found and fixed (icons not in the font; CE-5000 part 3 reverted), every other red pre-existing (measured at `3c9c991d`). CE-5002 part 1 (the load warms the view tables).
- `2026-11-14` (night) — the visual pass (`da1f877b`: crater soot drew as grass, the 2-D map drew no ditches / craters — both fixed), CE-5001 the contact cue across nodes (`437e529c`), V-29 handed to behaviors (R-5031), CE-5000 closed (`42aed17b`), V-15 bridges / tunnels (`a89b6220`), AQ112 for D-4, the barrage measured on city-battle + a bake latency defect fixed (`c086ce191`), CE-5002 filed.
- `2026-11-14` — P-5 part 2b built (`4f1d7020`): draw panel rows, the Ground tab, the effect read-out, the tint, G-37; a drawn-shape birth defect fixed.
- `2026-11-14` — G-16 built (`6485b7a1`), G-24 deferred (R-5028), V-12 built (`7914c37c`, R-5029).
- `2026-11-14` — lane started; B-1, CE-6014, P-0 built (`d09510a6`); B-2 built; P-1, P-2, B-3 core, P-3 built; P-4 parts 1–2 + wire built.
