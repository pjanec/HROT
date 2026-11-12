<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-12
current-answer: the whole file
related-designs:
  - ../../DESIGN_Threaded_Simulation_Presentation.md — §13.8 / §14.1 "reads are reads" (A1-2: a managed read stamps the chunk version); §14.3 POC 2's P-1
  - ../../DESIGN_Parallel_Simulation.md — §14 (on exp/parallel-sim-3) T-2: the egress visit gate skips nothing because OwnershipEgressSystem's reads stamp every chunk
  - ../../DESIGN_Egress_Change_Detection.md — §2 the hazards a change gate must respect
  - HANDOFF_Coordinator_To_Backend_Parallel_Sim_POC_4.md — runs IN PARALLEL on the other backend session (one pool); no shared files expected
-->

# HANDOFF — coordinator → backend: managed component READS stop stamping — the AUDIT, then the default flipped ON

**For:** the free backend session (the one that ran the threaded POCs). **From:** coordinator. **Dispatched at `<stamp>`**, scope frozen there.
🔒 User, `2026-11-12`: *"yes, queue the audit; the other backend lane is free, if it can be done in parallel with POC4"* — on the lean: make
"a managed read does not stamp" the DEFAULT after an audit of every in-place mutation of a managed component (R-4210).
⛔⛔ **Autonomous — never stop for a question** (R-4156). Decide, record with a claim table, continue; a failed premise stops THAT item (R-106).

## Where to work

⭐ A NEW branch **`exp/managed-reads`** from **`coordinator`** (clean — this is a production defect, the most likely of the experiments to go
to a lane). Status file `docs/blueprints/batches/STATUS_Managed_Reads_Audit.md` ON THAT BRANCH. ⛔ Nothing merges into a lane without the user's
word; ⛔ do not push to `backend`. It runs in parallel with POC 4 (`exp/parallel-sim-4`, the worker pool) — no shared files expected; if you must
touch `Fdp.Core` / `Fdp.ModuleHost` threading, say so in the status file.

## The situation

A managed `GetComponent<T>` / `GetComponentRO<T>` goes to `UnsafeShim.GetManagedRO`, which returns the table's `GetRW` and BUMPS the chunk version
(`ManagedComponentTable.cs:181-196`). ⇒ ① every read marks the chunk "changed" — the recorder, egress and snapshots see false changes
(POC 3 T-2: `OwnershipEgressSystem` reads every entity's `DescriptorOwnership`, so the egress visit gate on the biggest CGF translator skips 0 of
~6 M visits); ② off the sim thread a read is a write race (the threaded editor). ⭐ CLAUDE.md already rules managed components IMMUTABLE and
replaced whole (`SetManagedComponent`) — so code that mutates one in place after reading it is ALREADY a recording bug; the stamp hides it.

⚠ **Two implementations of the fix exist — unify them into ONE** (R-shared-and-unified): `exp/threaded-poc-2` `f98bf9b60` ("reads are reads",
P-1, switch-bound) and `exp/parallel-sim-3` `754d52ce2` (`HROT_MANAGED_READ_NO_STAMP=1`). Take the better, say why.

## The items

| # | item | done when |
|---|---|---|
| **M-1** | ⭐ **the static audit**: every site that obtains a managed component (any read path) and then mutates the object IN PLACE — a field / property set, a collection `Add` / `Remove` / `Clear` / indexer set, a method that mutates — instead of building a new instance. Use the graph + grep AND the Roslyn reach tool (`Hrot/Tools/Hrot.Tools.ReachAnalysis` on `exp/threaded-analysis` — it already counts collection mutation as a write). POC 2 named four replication sites; X-2 named `ParamUsage.cs:110-121` and `UnitDebugSettingsMemory.cs:43-48` | a table: site · type · what it mutates · `file:line`; coverage stated (`check_index_coverage` is not in the CLI) |
| **M-2** | ⭐ **a RUNTIME mutation detector** (Debug, opt-in `HROT_MANAGED_MUTATION_CHECK=1`): a managed component handed out by a READ is fingerprinted, and at the frame's end a changed fingerprint on an un-stamped chunk is reported with the type and the reader. Run it on the 1 000-man rig, the editor's feature rails, the recording / replay rails and a scenario-regression subset | every hit listed; every hit not in M-1's table is a finding |
| **M-3** | fix every site: build a new instance and `SetManagedComponent` (or make the type unmanaged when it is recorded — CLAUDE.md); where a hot path would allocate per frame, say so and choose (R-2084) | the detector silent over the same runs |
| **M-4** | ⭐ **flip the default**: a managed read never stamps; the old behaviour only behind a diagnostic switch for one release | the recording / replay / snapshot / egress / replication rails green with it ON; a recording made before and after replays identically; ⭐ measured: the egress visit gate's skip rate on the CGF (POC 3: 0) and the recorder's bytes per frame, before / after |
| **M-5** | the verdict — what was found, what was fixed, what the flip saves, anything that must stay | top of the status file; folded into the threaded design §14.1 and Parallel Simulation §13 |

## Rules

- **Measure before claiming**; every "no other site mutates" from the audit AND the detector's runs.
- **0 B per steady frame** (R-2084) — a fix that allocates per frame is a finding, not a fix.
- Run anything slow in the background; never poll. ⛔ No screenshots or recordings in git.

**Done when:** M-5 is written — however far M-1…M-4 got.

---
_coordinator lane, 2026-11-12_
