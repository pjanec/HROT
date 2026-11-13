<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-13
current-answer: the whole file
related-designs:
  - ../../DESIGN_Threaded_Review_Source.md — ⭐ THE design (classes §1.1, sequence §1.2, who calls it §1.3, rules §2, acceptance §3) — build it
  - ../../UX/DESIGN_Review_Perspective.md — what the review does (§0a one AAR, §3a entering / leaving, §6.12 the merge, §6.13 the watch)
  - STATUS_Overnight_Threaded_Build.md (on exp/threaded-build) — §5 the open item this closes; D-7 / D-9 / D-14 the patterns to reuse
-->

# HANDOFF — coordinator → backend (second VM lane): AUTONOMOUS — the review source on the simulation side (R-4220)

**For:** a backend VM session (the second one — the first is on CE-4480…CE-4488). **From:** coordinator. **Dispatched at `bdefd8917`** — scope frozen.
🔒 User, `2026-11-13`: *"yes, write the design and the handoff for the other backend VM lane."*

## ⛔⛔⛔ Never stop — the overnight contract

Never stop until every item is built and tested; no question in chat. Trouble ⇒ the option that follows best practice and fits the engine, no
shortcuts, no hacks — recorded in DECISIONS, then continue. A bug ⇒ fix + a rail. Blocked ⇒ the other items, then back with a different approach
(two before OPEN). A red ⇒ root-cause; ⛔ never skip a test. Commit + push after every item, by path; tick its tracker row (`🔄` → `[x]` + commit,
or `⚠ OPEN`). A deviation from the design is folded back INTO the design (obligation ⑤).

## Where to work

A NEW branch **`exp/threaded-review`** from **`origin/exp/threaded-build`** (the first VM lane works on that one — ⛔ never commit to it; merge it
in when you need its later commits, by merge, never rebase). Push `chore: started threaded review source at <sha>`. Status file
`docs/blueprints/batches/STATUS_Threaded_Review_Source.md`. Merge `origin/coordinator` once for the design file.

## The work — the design's §1–§3, in this order

| # | item | tested when |
|---|---|---|
| **R-1** · `CE-4495` | ⭐ **`ReviewReadModel` + `ReviewViewRequest` + `ReviewModelBuilder`** (§1.1) — the builder reads `ReviewSource` and computes By-node / watch views through `ReplicaViews`; a Version key so an unchanged state rebuilds nothing | rails: the model equals what the panels read today, for Merged and a part; 0 B on an unchanged build |
| **R-2** · `CE-4496` | ⭐ **`IReviewView` / `IReviewCommands` + `InlineReviewChannel`**; re-point the panels (`FederationPanel`, `ReplayTimelinePanel`, `ByNodeSection`, `ReplicaWatchLane`, `FileReview`, the toolbar combo, the status section, the Offsets window host) onto them — first in the STANDALONE Replay Browser and the switch-off editor | the Replay Browser's Federation / timeline / By-node rails green; a rail: no review UI type references `FederatedReplayManager` (§3 ④) |
| **R-3** · `CE-4497` | ⭐ **the review source on S** — ONE call site after the kernel tick in both kernel modes (§1.3); the frame overlay at `EditorSubsystem.cs:5931` removed; `ReviewSource(world: S)`, `clearWorld` = `NewScenario(S)`; `OnRecordingOpened` on the sim side | switch OFF: the review's own rails green, behaviour unchanged |
| **R-4** · `CE-4498` | ⭐ **`ThreadedReviewChannel`** — commands as sim jobs; the model built after the step and carried by the staging (the readout pattern, overnight D-9); the request rides up only when it changes | rails: a command posted from the UI applies on the sim thread; the model reaches the UI after the next pull; the strict guard silent |
| **R-5** · `CE-4499` | ⭐⭐ **it really works in mode 3** (§3 ①): a multi-participant recording (the Replay Browser rails' fixture group, or one recorded with `--mode all` — say which) opened in the threaded editor — Merged, step, scrub, ▶ / rate, Source ▸ a part and back, an offset, a locals toggle, By node on a selected unit, a watch item, a search hit seek; ⭐ a rail: A still holds the merged entities after N pulls | every step correct, strict guard 0 hits, 0 faults; switch OFF identical (§3 ②); 0 B per steady paused frame (§3 ⑤) |
| **R-6** · `CE-4500` | **fold the as-built** into `DESIGN_Threaded_Review_Source.md` (`build-state: BUILT`, deviations, prior state SUPERSEDED) and Threaded Presentation §14.8 (the open row closed); Review Perspective gains the threaded note | the design commits named |

## How to work

Read the design end to end first, then Review Perspective §0a / §3a / §6.12–§6.13. `scripts/cbm.sh` (graph — ⚠ it indexes the checkout you
run it in) AND grep before claiming something exists. The touched features' OWN rails only — the Replay Browser's and the review's suites, by
filter; ⛔ no broad suites. Build the TEST project before `--no-build`; restore after merging. Display rails under `xvfb-run -a` (not skipped).
Long runs in the background, never polled. One build at a time. No LINQ / allocation per frame. ⛔ No screenshots in git.

## The report (top of the status file)

DONE · GATES · DECISIONS · BUGS FIXED · OPEN.

---
_coordinator lane, 2026-11-13_
