# HANDOFF — ownership programme: the remaining work (backend lane)

> **Dispatched at `c6aae29e5`** (branch `backend`). ⭐ **Your scope is FROZEN at that sha** — documents that change
> after it are FYI only; if one invalidates an item, STOP that item and report it (never the batch, `R-106`).
> ⛔ **CE-3000 (the CycloneDDS.NET library fixes) is NOT in scope** — user, `2026-10-02`: *"No ce 3000 yet."*

---

## 0. Before anything

| step | |
|---|---|
| ① | Read [`RULINGS.md`](../RULINGS.md) (`CLAUDE.md` RULE ZERO), then run `HROT_LANE=backend bash scripts/session-design-brief.sh` and open your reply with the DESIGN BRIEF |
| ② | Work on branch **`backend`**. Merge `origin/behaviors` and `origin/ui` first, push an empty `chore: started ownership-remaining at <sha>` commit (rule 1b) |
| ③ | ⭐ The owning design is [`docs/DESIGN_Ownership_Groups_And_Grants.md`](../../DESIGN_Ownership_Groups_And_Grants.md) — read its STATUS block, **§5.6** (build order, S1–S8 all ✅), **§5.7 / §5.7.1** (the E2E matrix and its live results), **§5.3** (crash/departure semantics), **§5.8** (EQS solver per sensor) |
| ④ | Tracker rows live in [`Blueprint_Issues_Tracker.md`](../Blueprint_Issues_Tracker.md). ⭐ The backend id block is **`CE-3000`–`CE-3999`; next free `CE-3006`**. ⚠ Every merge of `behaviors` conflicts on the id-block table in the tracker header: keep THEIR behaviors row and OUR backend row |

## 1. Where the programme stands — do not re-litigate

| done | evidence |
|---|---|
| Push-only ownership S1–S8 *(one owner per component; the creator grants each role's group)* | design §5.6 rows, each with its rails |
| R-179 — the brain names each EQS sensor's solver in the config | design §5.8; live 48/48 results |
| CE-515 — the debug API can create on any node (`/entities/create-request`, and `/entities/spawn` through the pack), and `/entities/{id}/ownership` reports group + claims | tracker row; design §5.7 prerequisites ①–③ |
| Live on `ClusterRunner --mode all`: E1–E3, E5, E7 ✅; E4/E6 ownership ✅ but the routed EDIT is not drivable (item 2) | design §5.7.1 |
| Live: `hill-attack`, `hill-attack-close`, `hill-attack-close-bp` all pass after S8 | design §5.7.1 |

## 2. The items, in order

⭐ For every item: **read the cited design section first** (`R-129`), find and run the **feature's own suite** (T-1), add
rails **into** that suite, and **fold what you learn back into the owning design** before the batch closes.

### ① Confirm the 15 order-dependent cluster reds are pre-existing *(small, do first)*

The full `Hrot.ClusterRunner.Integration.Tests` run at `c6aae29e5` showed 15 failures in four classes —
`EditorSubsystemBootTests`, `TimeControlIntegrationTests`, `HeadlessGizmoStreamingTests`, `IdAllocatorDiscoveryTests` —
**every one passes alone** (12/0, 14/0). A comparison run at the base `faaf2e8d2` was started and had not reported when this
was written. ⇒ Run the full suite once at `faaf2e8d2` (a `git worktree`, built from its own test project) and compare the
failing set. Same set ⇒ pre-existing, record it on `CE-518`. Different ⇒ `EditorHarness` now builds `EntityCreationPack`
(CE-515 ③, `EditorHarness.cs`) — that is the one shared-fixture change in the window; start there.

### ② `CE-3003` — a debug route that writes through `EntityWriteRouter`, then re-run E4 and E6

- **Problem (measured live):** `POST /entities/{id}/attribute` on a node that does NOT own the target compiles the patch onto
  its own world and drops it — no `UpdateEntityAttributeRequest` is sent. ⇒ §5.7 E4 *(IG edits a CGF-owned symbol)* and E6
  *(CGF edits an IG-owned symbol)* cannot be driven over HTTP.
- **Design basis:** design §5.7 prerequisite 4 and §5.7.1; the owner-side handlers exist on every host since S2b (F-10).
- **Lean (in the row):** route the attribute patch through `EntityWriteRouter.For(world)` — local apply when owned, a request
  otherwise — as the drag gizmo does (AX-007). ⚠ Check every write route (`/attribute`, `/component`) and keep the CE-191 rule:
  a write that cannot be honoured REFUSES, it never answers `ok:true`.
- **Acceptance:** E4 and E6 pass live (`scripts/ownership-e2e.py`), and §5.7.1 rows E4/E6 become ✅.

### ③ E8 — the crash run, one process per node

- **What:** kill a node that owns granted groups and watch the rest reclaim them (S7: the departure reclaim returns what the
  leaver owned to the primary owner on every node, no message). Design §5.3, §5.6 S7, §5.7 E8.
- **How:** the multi-process launch in [`RUNBOOK_Cluster_Debugging_Over_Http.md`](../../RUNBOOK_Cluster_Debugging_Over_Http.md) §1.2
  (`--mode all` cannot kill one node). Create a tank, confirm the grants (`/entities/{id}/ownership` per node), `kill -9` the
  SimHost process, and expect the records to return to the creator within the lease (~10 s measured, §5.3).
- ⭐ **`ddsmonitor` first** for any *"was it sent?"* question (`CLAUDE.md` WHICH-TOOL row) — record a capture across the kill.
- **Acceptance:** §5.7.1 row E8 filled in with the measured outcome.

### ④ `CE-3004` — mission edits over HTTP apply but report "not acknowledged within 15 s"

`POST /missions/{id}/task` and `/run` from the Scenario (CGF) perspective each waited 15 s and failed, yet the task was on the
plan and the tank drove. Debug-API ack path, not ownership. Find the ack's producer and who waits on it
(`IMissionEditorService.CommitMissionAsync`, the editor loop's `PollAcks`) and why the cluster host never resolves it.

### ⑤ `CE-516` — the editor ignores the network factory it is given

The row carries the measurement and a lean: the composition root chooses the network per node and passes
`OfflineNetworkFactory` to the editor, which uses what it is given (delete the unused fields). ⚠ Not a pure swap — the editor
takes its id allocator from the factory (`CE-203` E2). Design basis: [`PROGRAMME_Cgf_Equals_Editor_Gap_Map.md`](../../PROGRAMME_Cgf_Equals_Editor_Gap_Map.md) §0, §2c.

### ⑥ `CE-518` — 11 pre-existing red unit tests

Five groups, listed in the row. For each, read its owning design **before** deciding fix-the-test vs fix-the-code: ① a Stride
host hand-rolls its translator list (`EditorStrideSubsystem.cs`, R-174) and ② the save-handler set grew past the rail's list
may be REAL; ③ a fixture registration, ④ the cluster boots paused (CE-101), ⑤ the migrator goes to v3 look stale.

### ⑦ `CE-524` — the NavigationSolver writes a MuscleGround component in-process *(design first)*

`EngineBackedPathResponseSystem` writes `NavState` on the Muscle's entity through a shared pool — correct only while the solver
and the Muscle share a process. Direction in the row: the solver answers with a response MESSAGE the Muscle applies (the Brain
side already does). ⭐ This changes a cross-node contract ⇒ **extend the owning design with UML** (class + sequence + module
diagram) **before** building, and bring a lean to the user if the blast radius is large.

### ⑧ `CE-513 (backend)` — `AnimationChannel` / `LookAtChannel` mix Brain and Muscle writes *(dormant)*

Split each into an intent and a status component, the `StanceIntent`/`StanceStatus` shape. Dormant today (no production host
composes animation replication) — lowest priority; do it only after ①–⑥.

## 3. Out of scope

| | why |
|---|---|
| `CE-3000` CycloneDDS.NET fixes | user: *"No ce 3000 yet."* |
| `CE-520` + `CE-512 (backend)` (a) — more than one node per role | deferred by `R-157` (*"one node per role with the guard for now"*) |
| `CE-514` — a composite unit's position from its members | deferred |

## 4. Tools and traps (all measured this programme)

| | |
|---|---|
| ⭐ `scripts/ownership-e2e.py` | the §5.7 driver: `matrix`, `own <id>`, `move <id>…` (usage in its header) |
| ⭐ `scripts/hill-attack-check.py <scenario> 1000` | live hill-attack acceptance; re-run it after any change that touches ownership or ingress |
| ⛔ `pkill -f ClusterRunner` | matches its own shell's command line and kills it (exit 144). `pgrep` the PIDs, `kill` them in a separate call |
| ⛔ IG/NED unit tests vs a running cluster | some use DDS domain 0 — stop the cluster before running `Hrot.IG.Tests` / `Hrot.Network.NED.Tests` |
| ⛔ stale binary | build the TEST project, then `--no-build`; a production-only build leaves the test `bin` stale and prints green |
| ⚠ `HrotRunnerHarness` | shares ONE untracked DDS participant across its nodes ⇒ a replica's primary owner reads `-1` there (production resolves it, CE-517) |
| ⚠ `IsRecordedOwner` | the ingress skip helper — a descriptor in `OutgoingGrantsPending` is NOT owned (S8: the handover race dropped the grantee's first value) |
| ⚠ `tracker-counts.py` | counts only `BP-` rows; `CE-` rows do not move it |
| ⭐ gate report | the `CLAUDE.md` GATE REPORT CONTRACT — one row per gate, every red proven pre-existing against a named base sha, the integration suite named for cross-node changes |
