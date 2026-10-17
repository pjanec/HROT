<!--STATUS
state: LIVE — behaviors → backend: CIV-0 (`CE-2233`) has landed; B-2 part 2 (`CE-3220`) may go.
updated: 2026-10-17
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §5b the CIV-0 as-built, §5a the M-1 / M-2 answers.
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Civilians.md — the frame this note continues (B-1…B-5).
-->

# NOTE — behaviors → backend: CIV-0 landed, B-2 part 2 is unblocked

**On `behaviors`, in the commit that carries this note** (and `8bc80907d` before it). Rows:
[`CE-2233`, `CE-2234`](../Blueprint_Issues_Tracker.md) — built / measured.

| for you | what |
|---|---|
| ⭐⭐ **`CE-3220` part 2 (retire 1001/1003, migrate the 4 scenarios) may start** | the AI no longer engages a Neutral at any of four layers — danger, the threat pick, the weapon's hold, the firing-posture inputs ([design §5b](../../DESIGN_Civilians_And_Crowd_Control.md)). ⚠ The gate reads `EntityInfo.ForceId == Neutral` LIVE; an entity with no `EntityInfo` is treated as engageable — so a migrated civilian must carry `EntityInfo` (the scenario row or your TKB `Faction` default) |
| ⭐ `ua-threat-ranking` is already migrated | its "Civilian Near" is `Neutral` (still TKB type `1001`) — when you retire 1001, move it to your new type and keep `Neutral`; the live check (`scripts/utility-demo-check.py --launch ua-threat-ranking`) asserts the neutral scores 0 and is never the winner |
| ⭐ M-1 answered (§5a) | a `DestroyEntityCommand` published on CGF removes a CGF-created person on SimHost — no ownership move needed for the shelter self-delete (`SplitAuthoritySpawnTests.CE2234_…`) |
| ⚠ next from behaviors — `CE-2235` | sets `DefaultBehaviorHash` on your `CivilianTkbCatalog.Civilian` rows (1101–1104), the cross-lane edit you OK'd; it will be named in that commit |
