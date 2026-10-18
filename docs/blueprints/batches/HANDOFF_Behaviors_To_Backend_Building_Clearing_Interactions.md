<!--STATUS
state: LIVE — behaviors → backend: a FRAME (you design the details as step 1, then build).
updated: 2026-10-18
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/group-maneuvers/DESIGN_Building_Clearing.md — OWNS this: CB-19 (interactions), CB-21 (lean), CB-24 (the poses), §7a. This frame only points at it.
  - docs/DESIGN_Entity_Interactions.md — (yours) R-221: the request the actor sends and the target's owner applies; the new kinds join it.
  - docs/designs/look-direction/DESIGN_Look_Direction_And_FOV.md — (yours) LD-1: where the eyes point; the lean moves where they sit.
-->

# HANDOFF — behaviors → backend: search / detain / restrain, and leaning *(BR-4, BR-5)*

**Dispatched at:** the commit that adds this file, on `behaviors`. ⭐ Your scope is FROZEN at that commit; documents that change after
it are FYI only — if one invalidates an item, stop that item and say so (R-106), the other goes on.

**What it is.** The user ruled (R-2032, `2026-10-18`): 🔒 *"Search is likely an interaction channel, needing also some posture so ui
can render an animation"*, and asked for leaning and weapon-first peeking at corners. The behaviours that send these (`SearchPerson`,
`SliceThePie`) and the civilian poses are behaviors' ([DESIGN_Building_Clearing](../../designs/group-maneuvers/DESIGN_Building_Clearing.md)
CB-19…CB-24, `CE-2258`, `CE-2261`, `CE-2265`). ⭐ Build against that design's §7a rows; fold any deviation back into it.

**Fences.** ⛔ No search result decided on the soldier's node — the civilian's OWNER applies it (R-221). ⛔ The lean is not a stance
value (a man leans crouched too). ⛔ The new gesture values are behaviors' (`CE-2265`, appended to `GestureKind`) — not in this frame.

| # | item | design basis | ⭐ lean | acceptance |
|---|---|---|---|---|
| **BR-4** | **`Search` · `Detain` · `Restrain` interaction kinds** | Clearing CB-19; `InteractionMessages.cs:10-27` (the existing kinds) | appended `EInteractionKind` cases (target = the civilian, actor = the soldier). **Search**: the owner checks the actor is within 1.5 m and the target complying (hands on head or up), and after 3 s makes every Hidden carried weapon visible and lays it down (the existing laying, B-10). **Detain**: sets a replicated `Detained` flag (out of the fight, like a laid-down surrender). **Restrain**: sets `Restrained` on the same component (hands tied; the man can still walk). Refused when the conditions fail, counted | rails: a search by a soldier on another node reveals and lays down a hidden pistol on the owner and every node sees it laid down; a search of a man who is not complying is refused; Detain / Restrain replicate |
| **BR-5** | **the `Lean` body state** | Clearing CB-21; LD-1 (where the eyes point) | a replicated `Lean { None, Left, Right }` on the person, written by his brain; the visual sensor's eye point shifts ≈ 0.4 m to that side at the current stance height; the hit model treats a leaning man as exposing only the leaning side's half of his silhouette; ignored by the mover | rails: a man leaning at a corner sees a target he cannot see upright; a round aimed at his centre misses more often leaning than standing in the open (a measured ratio); ui receives the state |

**Ids.** Allocate the tracker rows from your block (rule 3) and name them in your report, so behaviors can cite them from `CE-2258`
(waits on BR-4) and `CE-2261` (waits on BR-5).
