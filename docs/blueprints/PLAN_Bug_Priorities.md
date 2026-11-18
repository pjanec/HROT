<!--STATUS
state: LIVE
updated: 2026-11-18
current-answer: the whole file — P1…P5 first (the user's priority), then the second tier by area
stale-below: none
known-rot: none — ⚠ every row cites its tracker id; the TRACKER row is the truth when they differ
-->

# PLAN — the open bugs, by priority *(coordinator, `2026-11-18`)*

🔒 User, `2026-11-18`: *"I would like to focus on the most serious bugs pls suggest"* · *"List is good. Maybe we can extend it by other bugs, stating the one
above as the priority ones."* Ranked by: does it make the product SILENTLY wrong, break the core of every fight, or make the simulation disagree with itself.

## ⭐ Priority — P1…P5

| # | the bug | rows | owner |
|---|---|---|---|
| **P1** | **a move that cannot finish never says so** — the unit freezes forever, silently; a disabled APC "runs" its route forever (a false-green check); a bike reports blocked while moving | CE-3458 · CE-9023 · CE-3452 | backend (navigation) |
| **P2** | **soldiers do not react to fire** — a near-missed mover does not drop; a badly hurt man does not lie down; militia never break | CE-4652 · CE-4653 · CE-3461 (likely one cause — the suppression / stress chain) | behaviors |
| **P3** | **breaching differs between the cluster and the editor** — a bulldozer's bump makes no breach in a cluster; a possessed soldier cannot enter a bumped hole; a charge's crater blocks its own breach | CE-5018 · CE-4630 · CE-5019 | terrain |
| **P4** | **minefields hurt the wrong people** — an AP burst under a man hurts nobody, and a mine strike leaves the man walking (user); an AT mine does not hurt the tank but a man 35 m away; skirting a field walks the mines laid at its edge (user); a man enters the field before the squad reacts | CE-2446 · CE-4654 · CE-2444 · CE-4655 · CE-2447 | terrain (the effects, the clearance) + behaviors (the injury, the reaction) |
| **P5** | **vehicles never slow or yield for animals on their road** | CE-3457 | backend |

## Second tier — by area

| area | the bug | rows | owner |
|---|---|---|---|
| fights (with P2) | the waverer never breaks or surrenders · the guard does not fire first · a pinned man never fires and walks instead of crawling to cover · defenders never throw a grenade at a man hugging their wall · a unit thrashes between cover points · no HQ answers an Engineers request | CE-3462 · CE-3463 · CE-3464 · CE-3465 · CE-3444 · CE-2442 | behaviors |
| ⭐ the threaded editor (the default) | in the live editor with the parallel kernel a `Points` / `Best` scoring root sees NO scored children (the archery contest's score) · every mounted-rider scenario aborts under the strict parallel checks (blocks the war party) · six SimHost rails red only under the parallel env | CE-3468 · CE-3470 · CE-3469 | backend / behaviors |
| determinism | `cc-battalion` differs between 1 and 8 threads in 1 of 5 runs · a rare stepped divergence at 4 threads · the paused-preview "run to" does not repeat · `cp-vbied`'s golden does not repeat · five goldens differ from t = 0 while every check passes | CE-2440 · CE-3445 · CE-2438 · CE-2448 · CE-3449 | backend |
| scale | at 1 000 men only 1 of 12 sampled movers moved · the CGF frame 70 ms (budget 10) · the brain's cost · the gizmo buffer drops primitives silently under load · roads at height 0 on the scale rig | CE-3424 · CE-3425 · CE-4395 · CE-1470 · CE-4401 | backend · ui · terrain |
| rules | the root objective never aggregates its children | CE-3460 | backend |
| animals | a pig walked through its pen's brick wall | CE-3471 | backend |
| visuals | a static car obstacle gets a grey solid block over it (its baked terrain box drawn as terrain, likely after the navmesh re-bake) | CE-4656 | terrain (+ ui) |
| editor | right-click move also orders units no longer selected · a press on an unselected unit drags it on the threaded editor | CE-1133 · CE-1954 | ui |
| tests and tools | the SimHost suite runs a different number of tests per run · a replay test fails under load · `Hrot.ClusterRunner.Tests` did not build · rails red on a non-English Windows (decimal commas) · three pre-existing presentation reds | CE-1038 · CE-1046 · CE-1473 / CE-3451 · CE-1475 · CE-1955 | backend · ui |
| ⚠ to re-check before work | a cluster move stops ~21 m short (September — may be stale) | CE-167 | backend |
