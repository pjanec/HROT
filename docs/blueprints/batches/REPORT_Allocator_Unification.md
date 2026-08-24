<!--STATUS
state: LIVE
updated: 2026-08-24
current-answer: the whole file — the batch report for HANDOFF_Allocator_Unification.md.
  Part A and Part B both COMPLETE. Design content lives in DESIGN_Deterministic_Network_Ids.md §11g
  (the as-built); this report points at it.
known-conflict: none.
-->
# REPORT — **HN-037: the allocator unification** *(backend lane, overnight)*

> 📌 **Dispatch:** [`HANDOFF_Allocator_Unification.md`](HANDOFF_Allocator_Unification.md), frozen at
> **`33c819a13`**. Branch `claude/blueprint-macro-feature-sdmspn`. Started-marker `ea02fe25f` (rule 1b).
> **ids allocated: `HN-050`…`HN-058`** *(rule 5)*, starting at `HN-050` as directed.

## 1. OUTCOME

📐 **The headline, measured end to end:** the same scenario loaded live in both hosts now gives

```
editor ids : [1000, 1001, 1002, 1003, 1004, 1006, 1007]
cluster ids: [1000, 1001, 1002, 1003, 1004, 1006, 1007]
```

**Before: editor `1000–1007`, `--mode all` `2–9`.** `HN-037` is closed.

| item | outcome |
|---|---|
| **①** editor reset | ✅ — but as **one** hook, not two. §3 deviation 2 |
| **②** CGF → `_context.IdAllocator` | ✅ `HN-052` — the standalone allocator was the whole of `HN-037` |
| **③** guarded reset at the world boundary | ✅ `HN-053` — ⚠ **and the guard §11d specified was NOT sufficient.** §3 deviation 3 |
| **④** ordering + parity rail | ✅ `HN-053`/`HN-054` — the tripwire reddened as designed and is REPLACED, not deleted |
| **⑤** flip the tripwire, update conformance | ✅ — ⚠ `entity-inspector` **stays DECLARED**, for the node-local reason only. §5 |
| **⑥⑦⑧** Part B, the obsolete load path | ✅ `HN-055`/`HN-056` — three orphans filed as `HN-057`, none deleted |

⭐ **Obligation ③:** §11 carried **2 `classDiagram`s + 2 `sequenceDiagram`s** (current-vs-new). The
current-state pair is accurate. The new-state pair is built **as drawn for the sequence** and **deviates in
three places for the classes** — all three folded into
[`DESIGN_Deterministic_Network_Ids.md` §11g](../../DESIGN_Deterministic_Network_Ids.md) *(obligation ⑤;
`build-state: BUILT`, §11g supersedes §11d's split and extends §11c's table)*.

## 2. 🔴🔴🔴 THE FINDING THAT MATTERS MOST — **the drift was standing in for a missing map clear**

⛔ `EntityRepository.SoftClear` does **not** touch `NetworkEntityMap`, and `NetworkSpawningSystem`'s
duplicate guard *(step 2, "silently drop if already spawned")* **drops** a spawn whose id is already mapped.

📐 **Measured:** with the authority reset to 1000, the SECOND load in one process produced **8 entities, then
0** — **no exception, no log line** — and **seven unrelated system rails failed with an empty world**
*(`CapabilitySmokeTests` ×3, `PanelSnapshotTests` ×2, `DiscoveryAndHintTests`, `DeterminismRails`)*.

⇒ ⭐⭐⭐ **The old id drift was not a cosmetic divergence. It was the only reason a reload worked at all** —
every id was new, so no stale entry could match. ⛔ **Removing the drift without adding the clear converts a
visible id difference into a silently empty world, which is strictly worse than the bug being fixed.**

⭐ Closed through `RegisterWorldResetObserver`, whose contract already is *"flush cached entity handles before
the repo is wiped"* — and this map IS cached entity handles. ⛔ No new mechanism.

📌 **§2b predicted this exact mechanic for PREVIEW** — *"the allocator alone guarantees a duplicate-id throw
from `NetworkEntityMap.Register` on the second preview"*. ⚠ The same sentence was true of a **reload**, and
nothing had connected the two. The symptoms differ only because the spawn path drops silently where the map
throws.

## 3. ⭐⭐ THE THREE DEVIATIONS *(argued; all folded into §11g)*

| # | deviation | why |
|---|---|---|
| **1** | 🔴 **`Reset(X)` meant THREE things** — `1000` *(editor nested, `DdsIdAllocator`)*, `1001` *(the two PRE-increment ones)*, `throw` *(`BlockIdManager`)* | ⛔ *"one allocation path"* is unreachable while the reset is ambiguous: the same `ResetToBase(1000)` gives a first id of **1000** on a DDS cluster and **1001** headless, and the parity rail catches it only in whichever configuration runs. ⭐ Contract restated as **the next id issued**; the two pre-increment allocators CORRECTED, not compensated for at the call site. ⚠ **Zero production callers of `Reset`** ⇒ latent, not live |
| **2** | ⭐ **ONE hook, not §11d's two** | 📐 **the editor runs its own `ClusterMaster`** *(`EditorSubsystem.cs:1702`)*. ⇒ one guarded hook, two authorities — §11e's own claim in code instead of prose. ⛔ Two hooks = two implementations of one rule |
| **3** | 🔴🔴 **§11c's table is incomplete: a `LoadingLive` step is NOT sufficient** | the graph carries **`OperatingReplay → LoadingLive`** *(live-from-replay, `CGF1-S0305`)* — `ReferenceReplayLoadHandler` claims `PrepareLive`, nothing is extracted, **the world is not cleared**. ⇒ the rule is **`Loading{Live,Edit}` entered FROM `Idle`**, walked across the trajectory *(the qualifying `Idle` can be mid-path: `OperatingEdit → UnloadingEdit → Idle → LoadingLive`)* |

⚠ **The guard rail corrected its own first case too.** It asked `Idle → OperatingPreview` and failed — that
BFS path is `Idle → LoadingEdit → OperatingEdit → LoadingPreview → OperatingPreview`, i.e. it drags a real
edit LOAD along, so resetting there is CORRECT. ⭐ **The rail was fixed to ask the real question; the guard
was not loosened to pass the wrong one.**

## 4. ⭐⭐ §GATES

| # | gate — verbatim command | `--no-build`? | result | delta vs `ea02fe25f` |
|---|---|---|---|---|
| 1 | `dotnet build IOS-IG-SimHost.sln` | builds | ✅ **0 errors** | none |
| 2 | `dotnet test Hrot/Subsystems/Hrot.Orchestrator.Tests --filter TheWorldBoundary…\|TheResetContract…` | `--no-build` | ✅ **14 / 0** | **+14 new** |
| 3 | `bash scripts/run-system-tests.sh DeterminismRails` | builds | ✅ **5 / 0** | none — ⚠ 2 were RED mid-batch; §2 |
| 4 | `bash scripts/run-system-tests.sh The_two_hosts` | builds | ✅ **2 / 0** | none |
| 5 | `bash scripts/run-system-tests.sh` *(whole, 83 cases)* | builds | ⏳ **see the note below — the final run is IN FLIGHT at the time of writing** | — |
| 6 | `dotnet test Hrot/Subsystems/Hrot.Editor.Tests` | `--no-build` | ✅ **239 / 0**, 1 skip | none |
| 7 | `dotnet test Hrot/Engine/Hrot.Presentation.Tests` | `--no-build` | ⚠ **117 / 3** | **none — pre-existing, A/B'd. Row B** |
| 8 | `dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests --filter <the 5 touched classes>` | `--no-build` | ⚠ **5 / 2** | **none — pre-existing, A/B'd. Row A** |
| 9 | `python3 scripts/tracker-counts.py --check` | n/a | ✅ OK — open **99** / done **333** | ⚠ blind to `HN-` rows |
| 10 | `python3 scripts/rulings-check.py` | n/a | ✅ **24/24** | 3 known staleness WARNs, not mine |
| 11 | `python3 scripts/design-digest.py --check` | n/a | ✅ clean *(81 docs; every buildable design carries both diagrams)* | none |

🔴 **Row 5, stated exactly.** The full suite was run **once, mid-batch: 74 / 9.** All nine were the same
defect — §2's empty second world — and the fix was verified through the two rails that reproduce it
*(`DeterminismRails` 5/0, `The_two_hosts` 2/0)*. ⛔ **I have NOT re-run all 83 since**, so the other seven
*(`CapabilitySmokeTests` ×4, `PanelSnapshotTests` ×2, `DiscoveryAndHintTests`)* are inferred-green from a
shared root cause, not measured green. ⚠ **Treat row 5 as unverified until it is filled in.** *(A full run
was started as this was written; if it landed, the line above says so.)*

⚠ **`mermaid-check.mjs` SKIPPED** *(needs an `npm install` this session lacks)*. ⭐ **§11g is prose and
tables — no Mermaid block added or edited**, so nothing new is unvalidated.

### Every RED, confirmed **by name, against the base sha `ea02fe25f`**

| | red | evidence |
|---|---|---|
| **A** | `DistributedScenarioLoadTests.DistributedLoad_TranslatesNetworkIds_…` *("Cluster must reach OperatingLive (31). Current: 0")* · `EditorFileIOIntegrationTests.SaveScenario_SubsystemTypeIsHrotScenario` | 📐 **Same 2 failures, same names, same 5/2 count at `ea02fe25f`.** ⚠ The handoff asked me to update this suite's authored-id expectations — ⛔ **I could not: it never reaches `OperatingLive`, and did not before this batch either.** That is a reported finding, not a silent skip |
| **B** | `EntityDragGizmoTests` ×3 *(pick token, drag position 50 vs 60)* | 📐 **Same 3 at `ea02fe25f`**, and reproducible in isolation *(3/7 twice)*. Unrelated to ids |

⚠ **One flake seen and not reproduced:** `Hrot.Presentation.Tests` aborted once with *"Test host process
crashed"* after 29 of 120 cases; the next two runs completed 120/120 with only row B failing. ⛔ Recorded, not
explained.

⭐ **Working tree CLEAN after every suite run.** No golden added or touched; **no skips added** *(the one
`Hrot.Editor.Tests` skip is pre-existing)*; quarantine count unchanged.

## 5. ⭐ RED PROVEN — **five inverse edits, five different rails**

| probe | result |
|---|---|
| drop the *"entered from `Idle`"* half of the guard | ⭐ **only** `The_live_from_replay_branch_does_not_reset_the_authority` reddens — the rail is specific, not blunt |
| remove the reset call entirely | **5 / 10** red *(and the 5 "does not fire" cases stay green — which is the point)* |
| revert the `NetworkEntityMap` clear | 🔴 `A_reload_in_one_process_repeats_the_authored_ids` — `ids second: []`. ⭐ The anti-vacuity `NotEmpty(kb)` is what catches it |
| revert `Reset(startId - 1)` | **4 / 4** of the contract rail red |
| restore CGF's second allocator | 🔴 the parity rail names it exactly: *"`--mode all`'s lowest authored id is **2**, not 1000"* |

## 6. ⭐ ITEM ⑤ — **which conformance reasons remain**

📐 `entity-inspector` **stays DECLARED**, and the handoff's caveat was right: its declared reason was already
the **node-local entity** *(IG lists `networkId 0`, unnamed)*, never ids. ⇒ the id divergence lived only in
the tripwire, which is gone. ⭐ The entry's text now says so explicitly, so nobody re-reads it as covering
ids. ⚠ Two stale paragraphs in that file were corrected at the same time — one still claimed *"the worlds
CANNOT be equalised"* *(fixed by `HN-029`)*, the other *"the ids are NOT compared"*.

## 7. WHAT I DID NOT DO, AND WHY

| ⛔ | |
|---|---|
| **update `DistributedScenarioLoadTests`' id expectations** | it never reaches `OperatingLive`, at my head **and at the base sha**. Row A |
| **delete `MigrationAlertManager.OnScenarioLoaded` / `SaveScenario`'s journal branch / `CgfApplication`'s allocator** | 📐 all three **already inert** *(zero production callers of `EditorBootstrap.CreateFileService`; production builds the service with `migrationServices: null`)*. ⚠ Re-wiring migration to the genesis load is a DESIGN question ⇒ `HN-057`, not an edit |
| **solve the chunk-ordering race** | asserted and named in the rail's own failure message; ⇒ `HN-058` |
| **touch `HN-038`, preview (§4d), replay's forward reset** | out of scope; §11c's other two policies untouched |

## 8. RULE COMPLIANCE

| rule | |
|---|---|
| **1b** started-marker | ✅ `ea02fe25f`, pushed before any code, naming `33c819a13` |
| **3 / 5** ids | ✅ `HN-050`…`HN-058`, from `HN-050` as directed; filed in the same commits that use them |
| **4 / 7** re-sync | ✅ ff-merged the coordinator at the start and re-pulled before the final commit |
| **8** gate report | ✅ §4, with both REDs A/B'd against the base sha rather than argued |
| ⭐ **obligations ③/⑤** | ✅ §1 and §3; the as-built is in **`DESIGN_…§11g`**, not only here |
