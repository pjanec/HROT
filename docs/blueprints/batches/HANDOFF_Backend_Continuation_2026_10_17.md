<!--STATUS
state: LIVE — DISPATCHED at the commit that adds this file (2026-10-17); scope frozen there.
updated: 2026-10-17
current-answer: the whole file. §1 is the queue, in order; §2 what changed today that you will meet; §3 the traps that cost this session time.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/blueprints/RESUME_Backend_Lane.md — the lane's state doc; its §00000 points here while this handoff is live
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §5h ARMED PEOPLE (behaviors, R-2025…R-2029, AW-1…AW-24) OWNS items 1–10 below; §7.3b–§7.3d the cloud + gas as a danger area
  - docs/DESIGN_Entity_Creation_Unification.md — §3.4d (CE-3228, as-built at its end): one creation request on the FDP bus
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Armed_People.md — behaviors' FRAME for B-7…B-14, and its four NOTEs (amended B-7, status for combatants, B-15/B-16, the simple-path bounds)
  - docs/blueprints/batches/HANDOFF_Backend_Continuation_2026_10_16.md — the previous continuation (its items are done or carried into §1 here)
-->

# HANDOFF — backend → backend: the continuation of 2026-10-17 (armed people next)

**For:** the next backend session, on branch **`backend`**. **From:** the backend session of `2026-10-17` (the CE-3228 takeover, the gas
cloud, the armed-people intake). Dispatched at the commit that adds this file; scope frozen there.

🔒 **The user's standing mode for this lane:** *"pls keep going autonomously, merging behaviors branch as you go, reading and processing their
handoffs, keep writing handoff fo behavior branch, they act similarly (merging yours, checking and processing handoff), keep working
autonomously"* · *"Please do not use triggers just to check if other lane advanced … simply check other lanes for new content after finishing
a turn."*

## 0. Start here

1. RULE ZERO: read [`RULINGS.md`](../RULINGS.md), run `python3 scripts/design-digest.py` and `python3 scripts/rulings-check.py`, open the reply
   with the per-lane `DESIGN BRIEF` (lane: backend).
2. `git fetch origin backend ui behaviors` → start at `backend@` the commit that adds this file; merge `ui` / `behaviors` if they moved
   (`git merge-tree --write-tree` first; gate what their diff touches, in the BACKGROUND). Push a `chore: started …` marker naming the shas.
3. Ids: tracker next free **`CE-3239`** (CE-3229…CE-3238 are filed for you — §1); rulings next free **`R-3016`**.
4. ⭐ Read [Civilians §5h](../../DESIGN_Civilians_And_Crowd_Control.md) END TO END before item 1 — its class / state / sequence / module diagrams are
   the contract; do not redraw them, fold deviations back into §5h.

## 1. The queue, in order

Items 1–10 are behaviors' [frame](HANDOFF_Behaviors_To_Backend_Armed_People.md) + notes [1](NOTE_Behaviors_To_Backend_Armed_People_Amended.md)
· [2](NOTE_Behaviors_To_Backend_Armed_People_Status.md) · [3](NOTE_Behaviors_To_Backend_Armed_People_Combatants.md) ·
[4](NOTE_Behaviors_To_Backend_Armed_People_Simple_Path.md). ⛔ The frame alone is NOT the spec — B-7 was amended twice after it went out.

| # | id | item (design §5h) | the parts that are easy to get wrong |
|---|---|---|---|
| **1** | [`CE-3229`](../Blueprint_Issues_Tracker.md) | **B-7 `Armament`** — the ONE armed rule (AW-15, AW-21, AW-24) | ⭐ ORDER inside it: ① STATUS first — an enemy combatant (Hostile, not `Civilian` class, `EntityClassTable` 3.1.*.2) is armed by status unless `IsOutOfTheFight` or laid down, return at once; ② only then the per-observer judgement (`Appearance` × identification range ⇒ identified lethal / unidentified / nothing). A truth form (`LethalInHand`) for non-observer readers (the figure, "can I fire"). ⚠ "the 27 readers" is behaviors' count — **ENUMERATE it yourself** (`search_graph` + grep for `HasComponent<WeaponState>`; the MCP may be down — the CLI `codebase-memory-mcp cli search_graph …` is not) and classify each as *armed* vs *can I fire* before routing. Acceptance: every `ThreatDanger` / ranking rail unchanged; the conduct-path COUNTER rail (zero entries without a `Civilian`-class entity); `ua-armed-status` green |
| **2** | [`CE-3230`](../Blueprint_Issues_Tracker.md) | **B-8** the hands mount + `Indirect` on 6037 / 6038 | `WeaponChoice` must never pick an empty / non-lethal-only hands mount — rail: a rifleman with an empty hands mount fires his rifle as before |
| **3** | [`CE-3235`](../Blueprint_Issues_Tracker.md) | **B-13** `RoundPickUp` + stone sources + a landed stone stays 60 s | a cross-node grant ⇒ an INTEGRATION row; one winner of two |
| **4** | [`CE-3232`](../Blueprint_Issues_Tracker.md) | **B-10** `LaidDown` | a flag on the mount state, replicated; unarmed on every node |
| **5** | [`CE-3231`](../Blueprint_Issues_Tracker.md) | **B-9** the pistol | only the types that should carry one; re-run `ua-threat-ranking`, `ua-posture`, `ua-universal-soldier`, `bt-window-duel` UNCHANGED (a deviation is a finding, never a re-baseline) |
| **6** | [`CE-3233`](../Blueprint_Issues_Tracker.md) | **B-11** carry stations Hands / Back / Hidden, pick-up weapons, the charge | the charge bursts at its CONTROLLER's position; Hidden is never armed to an observer |
| **7** | [`CE-3234`](../Blueprint_Issues_Tracker.md) | **B-12** `HeldItem` + pocket items | the `Gesture` shape (CE-3224); "Weapon" = `WeaponPosture.ActiveMount`, never a copy |
| **8** | [`CE-3236`](../Blueprint_Issues_Tracker.md) | **B-14** the cloud's source (a venting canister) | extends YOUR cloud (§2): the cloud is created by the default processor — keep the source step on the cloud's owner |
| **9** | [`CE-3237`](../Blueprint_Issues_Tracker.md) | **B-15** the shooter on the damage | APPEND to `DamageAssessedEvent` + `EntityHitDamage` (the CE-3219 `Effect` pattern); unset = unknown |
| **10** | [`CE-3238`](../Blueprint_Issues_Tracker.md) | **B-16** `HostileAct` kind (lethal or not) | a byte beside `SinceSimTime`; behaviors writes it |
| 11 | [`CE-3227`](../Blueprint_Issues_Tracker.md) | gas as a `DangerAreaKind` (Civilians §7.3d) | behaviors' reactions wait on it ([frame](HANDOFF_Backend_To_Behaviors_Gas_Reactions.md)) — do it between armed items if behaviors asks |
| 12 | [`CE-3222`](../Blueprint_Issues_Tracker.md) | B-4 the civilian perf gate (`scripts/perf-civilians.sh`) | unblocked: `CivilianHsm` landed; place the cast INSIDE test-town and off the pond (behaviors' CivilianHsm note) |
| 13 | — | `CE-3220` part 2 (retire civilian 1001/1003) · the live `ua-civ-neutral-gate` re-check (CE-2242 + CE-3225) · CE-3216 S2 (perception by weather) | backlog |

**Gates for every armed item** (the gate-report contract): the item's rails red-proved · the touched unit suites · ⭐ `ua-armed-status`
(behaviors' guard rail, §5c) · the in-process twin `PostureScenarioTests.CE2233_CivNeutralGate_…` (it reads danger).
**Reply to behaviors** with a handoff naming the ids above and the backend sha each item landed at.

## 2. What changed today that you will meet

| change | where it bites |
|---|---|
| ⭐ **`EntityCreationRequest` is an FDP-bus event** (`Fdp.Toolkit.NetworkSpawning.Events`, CE-3228). Every local author publishes it (`repo.Bus.PublishManaged`, or `EntityCreation.Publish`); the pack's `BusEntityCreationRequestSource` reads it. ⛔ The queue singleton, `LocalRequests`, `ScenarioSource` properties are gone | a request is read after the frame's swap; a test that publishes must `SwapBuffers()` before the request system runs. Owner 0 = "the system picks" (the default processor) |
| ⚠ **a FORWARDED request crosses DDS as `CreateEntityRequest`, whose `EntityDescriptorUnion` is a CLOSED list** (master, world pos, overlay, route, and now `dtAreaEffectCloud`) | ⭐ B-11 / B-13 / B-14 — anything you create with owner 0 from a non-arbiter node and a NEW component needs its own union case + `DescriptorMapper` decode, or it arrives without it (Entity Creation §3.4d ③). The request's `InitType` does not travel either |
| ⭐ **a replicated entity ends through `DestroyEntityCommand`** (the ELM teardown), never `cmd.DestroyEntity` — a local destroy leaves every replica behind (measured on the cloud) | B-13's picked-up round, B-14's canister, a spent stone |
| **the editor is node 1** (`EditorSubsystem.EditorNodeId`) | "no valid node id is 0" |
| **component ids**: `CivilianState` 399, `Crowd` 400 (behaviors), `AreaEffectCloud` 401 | ⚠ 400 is also used by test-only components in `Fdp.Core.Tests` (their own registry). Take **402+** for B-7…B-16 and check `origin/behaviors` + `origin/ui` FIRST — two collisions in one day |
| **the gas cloud** (CE-3219 part 3): asked for with the request (owner 0), created and owned by CGF (a bare TKB type ⇒ no role grants ⇒ the creator owns its position too), drifts at 1 Hz, ends via the teardown command | B-14 builds on it; design Civilians §7.3c |
| **`ScenarioLoadStep` readiness ①** waits two kernel ticks after it published | a load test must tick the world twice before `IsResolved` |
| `CgfLogicPackTests` counts CGF's systems (now 23 simulation / 28 total, after behaviors' `CivilianFearSystem` + `CrowdSystem`) | behaviors adds CGF systems without updating it — red on THEIR branch; bump it with the reason in the comment |

## 3. Traps that cost this session time

| trap | do this |
|---|---|
| a compile error, then `dotnet test --no-build` runs the OLD binary and reports green | a guard in every script: stop on "Build succeeded" missing (`/tmp` script pattern: `b() { … grep -q "Build succeeded" \|\| exit 1; }`) |
| a red-proof script mutates a source while the stop hook asks you to commit | never commit while a mutant is applied — check `grep -c MUTANT <file>`; commit after the script restores it |
| several test projects were never restored in a fresh container (`Hrot.IG.Tests`, `Hrot.NodeComposition.Tests`, `Hrot.SimHost.Integration.Tests`, `Hrot.Common.Tests`) | `dotnet restore <proj>` once, then build `--no-restore` |
| a WIP commit from another session may be truncated (a missing brace, a stray brace) | build it before trusting anything it claims |
| ⚠ the remote temp branch `wip/backend-ce3228-cloud` could NOT be deleted from the cloud session (the push of a delete is refused; it is fully in `backend`) | the user deletes it on GitHub |
| `DebugApiScenarioLoadTests.cs` is `Compile Remove`d and still names the deleted queue | dead file — leave it or delete it with the other removed DebugApi batches; it does not build |
| `CE417_R3_PerBindingCSharpCalls_AllocateNothing` failed once in a full toolkit run, 3/3 green alone | allocation noise under parallel load — not this lane's change; file it as flaky if it recurs |
