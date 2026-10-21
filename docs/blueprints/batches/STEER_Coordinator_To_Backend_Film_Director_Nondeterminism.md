<!--STATUS
state: LIVE
updated: 2026-10-21
current-answer: the whole steer
related-designs:
  - ../../DESIGN_Film_Director.md — §3d (event-driven cuts), §3e (shoot then edit, later), D-1 / D-3 / D-18, phases 1, 2, 5
  - HANDOFF_Coordinator_To_Backend_Film_Director.md — the running batch this steers (items B0, B3, B7, B12)
-->

# STEER — coordinator → backend: runs are NOT deterministic — cut on events, not on times (CE-4038, R-4064)

⚠ **For the running film-director batch** (dispatched at `751d89899`, started at `de1986d9a`). ⭐ **The user relays this; it is not an
amendment of the handoff's text** (rule 1c). It is not a correction of your work: the premise was the coordinator's.

🔒 **User, 2026-10-21:** *"scenarios are not deterministic by design. this might be quite an issue for planning clips"* · *"which might
lead to the replay from recording path."* ⭐ Canon since 2026-09-04: *"the scenario is not deterministic, entities do not go the same
ways"* (RUNBOOK_Cluster_Debugging_Over_Http §8). 📐 The coordinator's trial scout agreed: the `ua-civ-melee` blow at 4.25 s in the editor
preview against ~8.6 s from a cluster run.

## What changes — design §3d

| item | ⛔ was | ⭐ now |
|---|---|---|
| **B0** | a **gate**: two previews of one take must cut at the same frames, else STOP | ⭐ a **measurement**: how much two previews of one take differ (beat times, outcomes). Report it; it gates nothing |
| **B3** acceptance | cuts land within one frame of the **baked** times | ⭐ each cut fires on **its event in that run**. Paced stepping stays — for a fixed dt independent of rendering, not for identical runs |
| **B7** | `AnchorBook` bakes times from a scout; the scout's times drive the cuts | ⭐ `AnchorBook` resolves anchors **live, in the run being shown**; add the precursor anchors **`engage`** (the weapon channel starts aiming at the target — `AimAndFireExecutor.AimState`, ≥ 0.8 s before the round), **`countdown`** (a charge or release cue armed), and a shot's **`until`** (hold to the next event). The **scout = reliability**: N runs (3), per beat *happened k of N* and the time spread |
| **B8** capture | one pass | ⭐ **retakes**: a required beat missing ⇒ re-run, up to `retakes` (default 3), keep the first good run |
| **B12** seed | split `clips.json` as is | ⭐ split it **after the coordinator rewrites its negative offsets** (`fire-0.5`) into precursors and `until` — the coordinator does that now; take the then-current `docs/films/clips.json` |

**Unchanged:** every other item (B1, B2, B4–B6, B9–B11), the preview (R-4061), the 20 / 15 Hz fallback (R-4063), the out-of-scope list.

## Later — not this batch

**Shoot, then edit, from a recording** (design §3e, phase 5): a recorded live run, scouted once it is fixed, played back with exact leads,
slow motion and replays. It needs the record → playback fidelity and typed drawing measured first (the user has not tried it).
