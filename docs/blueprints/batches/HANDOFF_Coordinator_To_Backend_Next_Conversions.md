<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-13
current-answer: the whole file
related-designs:
  - STATUS_Windows_Threaded_Rerun.md (on exp/windows-threaded-measure) — ⭐ V-2 / V-3: the real-core ranking after the conversions (what is still serial)
  - STATUS_Mode_And_Serial_Systems.md (on exp/threaded-build) — M-5 / M-6 how Stress / Callout / Threat / the weapon dispatcher were converted; M-4b the profile
  - ../../DESIGN_Parallel_Simulation.md — §18.5 / §18.6 the per-entity rule; §18.7 the gate
  - ../../DESIGN_Egress_Change_Detection.md — egress change detection (what is published, when)
-->

# HANDOFF — coordinator → backend (the SECOND VM lane): AUTONOMOUS — the next conversions: what is still serial on real cores

**For:** the free backend VM lane (it built the threaded review source). **From:** coordinator. **Dispatched at `90416dc3e`** — scope frozen.
🔒 User, `2026-11-13`: *"yes"* (to the next-conversions batch for the free lane).

## ⛔⛔⛔ Never stop — the same contract

Never stop until every item is built and tested; no question in chat. Trouble ⇒ the best-practice option that fits the engine, no hacks,
recorded in DECISIONS. A bug ⇒ fix + a rail. Blocked ⇒ the other items, then back (two approaches before OPEN). ⛔ Never skip a test. Commit +
push per item, by path; tick its tracker row. A finding that changes a design is folded into it.

## Where to work

A NEW branch **`exp/next-conversions`** from **`origin/exp/threaded-build`** (the first VM lane works THERE — ⛔ never commit to it; merge it
in when you need its later commits, never rebase). Push `chore: started next conversions at <sha>`. Status file
`docs/blueprints/batches/STATUS_Next_Conversions.md`.

## Why

Windows, Release, 1 000 men, after the conversions: the editor's tick **110 → 75 ms** (1.9× the shipped editor), the CGF node **57.5 ms at 8
threads** (1.79×) — and both STILL FLAT from 4–6 threads. What is left serial at 8 threads (tick 76 ms): `WeaponDispatcherSystem` **5.6 ms
(converted, but 1.89× — did not move)** · `SquadCoordinationSystem` **5.0** · `StaffComposerSystem` **2.6 (fully serial)** · a tail ≤ 1 ms each
(~25 ms). On the CGF node the two `CycloneEgressSystem` passes **~15.6 ms, serial, unchanged** — its largest topic `EqsSensorConfigTopic`
**28 134 samples in the window, 6.2 ms** (more than `WorldPos`: 15 779).

## The work

| # | item | done when |
|---|---|---|
| **X-1** · `CE-4516` | ⭐ **why the weapon dispatcher does not scale** — profile it at 1 / 4 / 8 threads (its serial pre / post pass, the follow-ups, the merge, a lock); fix the cause | its ms at 8 threads drops with threads, or the measured reason it cannot is written up; its rails green serial and parallel |
| **X-2** · `CE-4517` | ⭐ **`SquadCoordinationSystem`** (per level today) — what is serial in it; make it scale | as X-1 |
| **X-3** · `CE-4518` | ⭐ **`StaffComposerSystem`** — a per-entity phase under the rule (own entity direct, others by command a tick later, declared cross reads) | as X-1; the read survey (`HROT_READ_CHECK=2`) clean |
| **X-4** · `CE-4519` | ⭐⭐ **the CGF egress**: why `EqsSensorConfigTopic` sends 28 k samples (a config that rarely changes — is it republished unchanged? per sensor per frame?) — fix at the source per the egress change-detection design; then measure the two `CycloneEgressSystem` passes and make the rest cheaper or off the frame (the replica path, CE-3427) where the design allows | the sample count and egress ms before / after; the receivers' rails green (what they see is unchanged) |
| **X-5** · `CE-4520` | ⭐ **why the tick plateaus at 4–6 threads** — measure, at 4 / 8 / 12: the serial time between phases (pre-passes, playback, follow-ups), pool idle while waiting, and the ≤ 1 ms tail summed; name the biggest piece and fix it if it is a defect; ⛔ a plateau explained is a valid outcome | the breakdown table; any fix measured |
| **X-6** · `CE-4521` | ⭐ **it really works**: the stepped gate (`scripts/stepped-hash.py --threads 1,4,8`, `HROT_PARALLEL_SIM=1`) equal; the threaded 1 000-man session ×2 (the M-7 chain) 0 faults, the strict guard 0; the touched systems' own rails serial and parallel; switches off: 0 B per steady frame | the GATES table |
| **X-7** · `CE-4522` | numbers before / after on the VM (Release, 1 / 2 / 4) + the Windows command for 1…12; fold the as-built into Parallel Simulation §18 and the egress design | the design commits named |

## How to work

The touched features' OWN rails; build the TEST project before `--no-build`; restore after merging; long runs in the background, never polled;
one build at a time on the machine; `scripts/cbm.sh` AND grep before claiming something exists. No LINQ / allocation per frame. ⛔ No screenshots
in git.

---
_coordinator lane, 2026-11-13_
