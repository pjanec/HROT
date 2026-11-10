<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-10
current-answer: the whole file
-->

# HANDOFF — coordinator → ui (Windows, hidden): RENDERING the large terrain UNDER LOAD — 1 000 men moving and fighting

**For:** ui, on the user's Windows machine, **every run hidden** (`HROT_HIDDEN_WINDOW=1`, L-0 of the running batch — ⛔ no window may appear;
the user works on this machine). **From:** coordinator. **Dispatched at `bdccd3d86`**, scope frozen there. ⭐ **Start it when
[HANDOFF_Coordinator_To_Ui_Windows_Live_Terrain](HANDOFF_Coordinator_To_Ui_Windows_Live_Terrain.md) is done** (it needs L-0 and L-3's empty-terrain
numbers as the baseline). Push `chore: started HANDOFF_Coordinator_To_Ui_Windows_Render_Under_Load at <sha>`; status file
`STATUS_Ui_Windows_Render_Under_Load.md`. Same [protocol](PROTOCOL_Two_Lanes_Self_Coordinating.md). ⛔⛔ **Never stop for a question** (R-4156).

**Why:** 🔒 user, `2026-11-10`: *"what about performance of rendering 3d maps on large terrain? is it already known?"* — ⛔ **not known**: the
cloud VM measured only the CPU side of the EMPTY terrain (1.9–2.4 M triangles, ≈ 2.1 GB, 0 B garbage — Map 3D §6ah); the running batch's L-3
measures the empty terrain on this GPU. ⭐ **What users will actually see is the terrain WITH the units** — bodies, vehicles, aircraft, labels,
cards, gizmos, effects, the panels — and nobody has measured that at 1 000 men.

**The load:** backend's scale rig scenario — `python scripts/scale-rig.py --terrain <t> --men 1000 --vehicles 50 --aircraft 10 --write-only`
writes `scenarios/scale-rig-<t>-1000/` (the men patrol and fight; [DESIGN_Scale_Rig.md](../../DESIGN_Scale_Rig.md) §2, §7 for its Windows
launch: `scripts/cluster_launch.py`, `--runner-dll`, `--pin`). Run the cluster (orchestrator + cgf + simhost) and the **editor as a live viewer**
(hidden). Terrains: `scale-1` (the synthetic 5.5 km) and `real-1` (the real one; ⛔ never in git). Release builds. Budget **3-D ≥ 30 fps**,
0 B garbage per steady frame (R-4192, CLAUDE.md hot path).

| # | id | what | done when |
|---|---|---|---|
| **R-1** | CE-4387 | ⭐ **the editor's 3-D map under load** on both terrains: cameras near (40 m over a fight), mid (400 m), far (3 km, the whole fight in view), a flight along a street at 15 m/s; per view fps (min / avg over 10 s) and the frame SPLIT by layer — terrain chunks · unit bodies / vehicles / aircraft · labels and cards · gizmos and effects · ImGui panels — from `HROT_FRAME_STATS` (Evaluation Cost §0e) and your D-1 counters; garbage per frame; memory | a table in Map 3D §6ah ("under load, on Windows") beside L-3's empty-terrain row |
| **R-2** | CE-4388 | **fix what is under budget, biggest cost first** — e.g. bodies not instanced, a label per unit rebuilt per frame, gizmos drawn for off-screen units, per-frame allocation in a layer; each fix re-measured | every view ≥ 30 fps and 0 B / frame, or a row naming the remaining cost, its owner and the number |
| **R-3** | CE-4389 | **the 2-D map under load**: whole-terrain zoom with 1 000 symbols + labels, and close in; the same split | the numbers in Terrain World §10a's measured block; fixes as R-2 |
| **R-4** | CE-4390 | **the Player** (`Hrot.Player`) in first person among the fighting units on `real-1` (a street, a house interior, a rooftop): fps and the split | the numbers in the status file; fixes as R-2 |

**Rules:** the look does not change (performance only, except what R-4195 already lets you design); a hidden-window number is GPU-honest but has
no vsync / compositor — say so; a VISIBLE confirmation run only when the user says the machine is free. Gates: the touched test classes + each
new rail red-proved once; any new zero-garbage rail per fixed layer; `tracker-counts.py --check`.

---
_coordinator lane, 2026-11-10_
