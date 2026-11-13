<!--STATUS
state: LIVE — dispatched, QUEUED (start after CE-4480…CE-4488 and the Windows findings CE-4501…CE-4506)
updated: 2026-11-13
current-answer: the whole file
related-designs:
  - STATUS_Mode_And_Serial_Systems.md (on exp/threaded-build) — ⭐ M-D12 (Entity padding) and §5 OPEN (SquadManeuverTests 78 red with parallel brains): the two questions this answers
  - ../../DESIGN_Parallel_Simulation.md — §18.5 / §18.6 the per-entity rule, the brains and squads as phases; R-4212 the one-tick delay on cross-entity effects
  - ../RULINGS.md — R-4212, R-4214, R-4218, R-4222
-->

# HANDOFF — coordinator → backend (VM): AUTONOMOUS — a canonical `Entity`, and the squad drills under parallel brains

**For:** the backend VM lane, after its current batches. **From:** coordinator. **Dispatched at `<stamp>`**.
🔒 User, `2026-11-13`: *"explicit padding field is ok as long as it does not show up in the usual UI - entity inspector, entity pickers etc."* ·
*"ok to the squad drill test"* (R-4222).

## ⛔⛔⛔ Never stop — the same contract

Never stop until every item is built and tested; no question in chat. Trouble ⇒ the best-practice option that fits the engine, no hacks,
recorded in DECISIONS. A bug ⇒ fix + a rail. Blocked ⇒ the other items, then back (two approaches before OPEN). ⛔ Never skip, disable or
quarantine a test. Commit + push per item, by path; tick its tracker row. A finding that changes a design is folded into it.

## Where to work

Continue on **`exp/threaded-build`**. Status file `docs/blueprints/batches/STATUS_Entity_Padding_And_Squad_Drills.md`.

## The work

| # | item | done when |
|---|---|---|
| **E-1** · `CE-4507` | ⭐ **`Entity` has no undefined bytes** (R-4222, your M-D12): an explicit always-zero 2-byte field after `Generation` (`FDP/Engine/Fdp.Core/Entity.cs`), set by every constructor; size stays 8, so recordings stay readable. ⚠ **It must NOT show up in the usual UI** — the entity inspector, the entity pickers, the Details forms, tooltips, the JSON of the HTTP API and scenario files: measure first (graph + grep) every reflection-based reader of `Entity`'s fields (serializers, the inspector's reflector, diff / hash / watch services) and hide the field from each that would show it (private + the existing custom formatters; an explicit ignore where a serializer reflects private fields) | rails: an `Entity` built any way has its 8 bytes canonical; the weapon phase's `WeaponChannel` bytes equal across runs (the M-7 4-thread case that differed 3 in 7); the inspector / picker / scenario JSON / HTTP JSON of an entity unchanged (a before/after snapshot of each) |
| **E-2** · `CE-4508` | ⭐ **a squad harness in the PRODUCTION order** — the kernel's phases, `Tick()`, the tick-start view taken where the kernel takes it — so the squad drills run as they do in a node, serial AND with `HROT_PARALLEL_SIM=1` | the harness; `SquadManeuverTests` run on it both ways |
| **E-3** · `CE-4509` | ⭐⭐ **the drills compared, each difference classified**: ① only the one-tick delay on a cross-entity effect (R-4212 — accepted by the user) ⇒ the TEST is made tolerant of it, saying so; ② the squad DOES something wrong (a drill that no longer completes, a member left behind, a wrong order) ⇒ the BEHAVIOUR is fixed so it holds under the per-entity rule; ③ a race / undeclared read ⇒ a bug, fixed with a rail | `SquadManeuverTests` green in both modes, every class-① change listed with its reason; the stepped gate (M-7) still equal at 1 / 2 / 4 / 8 threads |
| **E-4** · `CE-4510` | fold: Parallel Simulation §18.6 (what the squad drills taught), the `Entity` change in its owning design (search `docs/` for the entity handle's layout) and §0a of Threaded Presentation | the design commits named |

## How to work

The touched features' OWN rails; build the TEST project before `--no-build`; long runs in the background, never polled; `scripts/cbm.sh` AND grep
before claiming something exists. No LINQ / allocation per frame. ⛔ No screenshots in git.

---
_coordinator lane, 2026-11-13_
