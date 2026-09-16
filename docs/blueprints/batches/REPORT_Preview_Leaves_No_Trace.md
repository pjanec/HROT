<!--STATUS
state: LIVE
build-state: BUILT
updated: 2026-08-24
current-answer: §1 what shipped · §2 the deviations from the design (obligation ③) · §3 the gates
  (rule 8's 8-row contract) · §4 the ids allocated (rule 5) · §5 what is NOT built and why.
design-basis: 📄 docs/DESIGN_Deterministic_Network_Ids.md §4c (the user-chosen approach) and §4d
  (the AS-BUILT, folded back into the design by this batch per obligation ⑤) ·
  docs/blueprints/batches/HANDOFF_Preview_Leaves_No_Trace.md · 🔒 user 2026-08-23 (§2c steer).
known-rot: none known. ⚠ This report is EPHEMERAL — the durable record is §4d of the design.
-->
# REPORT — **a preview leaves no trace** *(`HN-017`)*

> 🔒 **The user's requirement:** *"for repeated runs of the same we would like to have same ids."*
> 🔒 **The steer:** *"the preview could work also in distributed env so no hardwiring directly just for
> editor, reset must be cluster wide."*
> 🔒 **The approach, the user's own:** *"each node needs to remember the ids/chunks used during the run and
> on world reset to simply reset to their beginning while the central allocatore stays where it is."*

⛔⛔ **This report is EPHEMERAL.** ⭐⭐⭐ **The durable record is
[`DESIGN_Deterministic_Network_Ids.md` §4d](../../DESIGN_Deterministic_Network_Ids.md)** — the as-built
classes, the per-node wiring table, the deviations and the as-built UML were folded back into the owning
design **before this batch closed** *(obligation ⑤)*. ⭐ Read §4d, not this file.

## 1. ⭐⭐ WHAT SHIPPED

| ⭐ | |
|---|---|
| ⭐⭐⭐ **one implementation of *"what preview saves"*, in `Fdp.Toolkits`** | `IPreviewRewindable` · `PreviewStateBracket` · `PreviewParticipants` — ⛔ **not** in either handler, so `ReferencePreviewHandler` *(2PC, five slaves)* and `PreviewClusterOpHandler` *(editor)* **share** it while `HN-016`'s duplication stands |
| ⭐⭐⭐ **`IRestorableIdAllocator` on all five production allocators** | scalar ⇒ an integer; ⭐ **pooled ⇒ the queue it already holds.** ⛔ A capability interface, not a member on `INetworkIdAllocator` *(13 implementations; a default member would be a silent default on all 8 doubles)* |
| ⭐⭐⭐ **`NetworkEntityMap.CaptureState/RestoreState`** | ⛔ **the half without which the fix is worse than nothing** — `Register` throws on a duplicate id, and exact repetition makes that throw certain |
| ⭐⭐ **cluster-wide, no new protocol** | both handlers answer `PrepareState(LoadingPreview/UnloadingPreview)`; the master broadcasts and **each node restores its own reservation locally**. ⛔ Nothing touches the central authority |
| ⭐⭐ **the boundary is REPORTED, not assumed** | `UnrestorableParticipants` names any participant that gave no position; a mid-preview chunk is **not** re-offered *(it would be a cross-node collision)* |

## 2. ⭐⭐ OBLIGATION ③ — **the design carried UML; here is where the build DEVIATED**

📐 The design's §5 carried a `classDiagram` and a `sequenceDiagram`. ⭐ **Checked before building.**
⛔ **Three deviations, all argued and all now folded into §4d/§5:**

| # | the design said | ⛔ what was built, and why |
|---|---|---|
| **①** | §4 ④: *"hook it in `PreviewClusterOpHandler` — one home"* | ⛔ **that handler is registered on NO ClusterSlave** *(§2b)* ⇒ the fix would have been **editor-only**, the exact hardwiring the user's steer forbids. ⭐ The one home is `Fdp.Toolkits/Orchestration/Preview/`, reachable from both |
| **②** | §4 ③: a read member on `INetworkIdAllocator`, `Reset(Read())` an identity | ⛔ **impossible** *(§4b)* — `BlockIdManager.Reset` ignores its argument, `DdsIdAllocator.Reset` writes a **global** `Req_Reset`. ⭐ Built as a capability whose contract is *"restore my own position"* |
| **③** | §4b: *"the two pooled ones do not implement it"* | ⛔ **superseded by the user's §4c framing** — the pool IS the position, so **all five** implement it |
| **④** | *(the design did not anticipate this)* | ⭐ **`EntityMapFromRepository`** — SimHost sets the map singleton **after** the handler is registered, so an eager lookup would throw at startup. Resolved at `Capture()` instead |

## 3. ⭐⭐⭐ §GATES — **rule 8's contract, all eight rows**

| # | gate | verbatim command | `--no-build`? | result |
|---|---|---|---|---|
| 1 | **build** | `dotnet build IOS-IG-SimHost.sln` | must build | ⭐ **succeeded, 0 errors** |
| 1 | ⭐⭐ **the requirement** | `bash scripts/quick-check.sh FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj APreviewLeavesNoTrace` | builds | ⭐ **11 / 11 pass, 0 fail** *(new file)* |
| 1 | **preview handler** | `dotnet test Hrot/Subsystems/Hrot.SimHost.Tests/… --filter FullyQualifiedName~PreviewClusterOpHandler` | `--no-build` | ⭐ **6 / 6 pass** — ⛔ 0 delta |
| 1 | **editor** | `dotnet test Hrot/Subsystems/Hrot.Editor.Tests/… ` | `--no-build` | ⭐ **234 pass · 1 skip · 0 fail** — ⛔ 0 delta |
| 1 · 8 | ⭐⭐⭐ **the SYSTEM suite** *(row 8: this change is cross-cutting)* | `bash scripts/run-system-tests.sh` | builds | ⭐ **58 / 58 pass**, run **twice** — before and after the SimHost wiring. ⭐ Includes `DeterminismRails` and `HN-010`'s authored-load ids `1000`–`1007` |
| 2 | **out-of-solution / stale-bin** | — | — | ⭐ none of the gated projects is out-of-solution; every `--no-build` run was preceded by a full-solution build in the same tree |
| 3 | **golden movement** | — | — | ⭐ **ZERO goldens moved.** 📐 `git status` names 15 paths, **no** file under `Goldens/`; the diff is **+369/−12 lines** across 12 modified + 4 new files |
| 4 | ⭐⭐ **every RED confirmed PRE-EXISTING** | see the table below | — | ⭐ **two suites, both proved against a STASHED tree** *(base `164d63afb`)* |
| 5 | **working tree clean after every suite** | `git status --short` | — | ⭐ clean of unexpected paths after each run — only the batch's own 15 |
| 6 | **quarantine counts** | — | — | ⭐ **unchanged** — 1 skip in `Hrot.Editor.Tests`, 3 in `Hrot.SimHost.Tests`, both pre-existing. ⛔ **No new skip, no new filter** |
| 7 | **gates on the docs** | `tracker-counts.py --check` · `rulings-check.py` · `design-digest.py --check` · `mermaid-check.mjs` | — | ⭐ **OK (open 99 / done 333)** · **24/24 verified** *(2 pre-existing staleness warnings: `.claude/CLAUDE.md`, `SOLUTION-OVERVIEW.md`)* · **all 83 designs OK, buildable designs carry both diagrams** · **4 / 4 mermaid blocks parse** |

### ⚠⚠ Row 4 in full — **the two red suites, and the proof they are not mine**

| suite | with the change | ⭐ on a **stashed** tree, no changes | verdict |
|---|---|---|---|
| `Fdp.Toolkits.Tests` | **3** red / 2035 | 2 of the 3 **pass in isolation**; `FakeDangerAreaProvider_Refresh_ZeroAllocAfterWarmup` run **3×**: **pass, fail, fail** | ⛔ **flaky at base** — `DEBT-AIB-030`'s documented shape |
| `Hrot.SimHost.Tests` | **5**, then **8** red / 657 *(two identical runs)* | **4**, then **11** red *(two identical runs)* | ⛔⛔ **the failing SET ROTATES with no code change** ⇒ a second suite with the same defect. 📐 `StagingEntityExtractorTests` — one of the rotating reds, and the one that sits on the id-allocator path — passes **18 / 18 in isolation, twice**. ⭐ Filed as **`HN-019`**; ⛔ `R-131`: a defect to fix, never a filter |

### ⭐⭐ Revert-goes-red — **per item, by INVERSE EDIT** *(never `git checkout --`)*

| inverse edit | red |
|---|---|
| `SequentialIdAllocator.RestoreIssuingPosition` + `BlockIdManager.RestoreIssuingPosition` neutered | ⭐ **4 of 9** |
| `RepositoryEntityMapRewind.Capture` fabricates a map · `Restore` a no-op | ⭐ **2 of 2** new rails |

⭐ Restored to green after each probe *(11 / 11)*.

## 4. ⭐ RULE 5 — **the ids allocated**

| id | |
|---|---|
| ⭐⭐ **`HN-017`** | the fix — **DONE** |
| **`HN-018`** | `EntityLifecycleModule` is the third stale participant and is **still not rewound** — open, with the measurement of why a plain copy cannot restore it |
| **`HN-019`** | `Hrot.SimHost.Tests` rotating order-dependent reds — open |

⭐ **Closed by this batch:** **`HN-012`** *(the requirement)* and **`HN-013`** *(item ⓪'s "allocator alone
is worse" finding)*. ⚠ **Still open and untouched:** `HN-011` · `HN-014` · `HN-015` · `HN-016`.

## 5. ⛔⛔ WHAT IS **NOT** BUILT — **so silence is not read as coverage**

| ⛔ | ⭐ why |
|---|---|
| **the END-TO-END system rail** *(two previews, ids read over the API)* | 🔴 `HN-015`: `GET /entities` answers **500** after any runtime spawn. ⚠ Registering the existing safe-float converters on the API's options was **tried, MEASURED not to fix it** *(the throw is upstream in `ScenarioSerializer.SerializeEntity`)* and **REVERTED** rather than left looking like a fix — the measurement is recorded in a code comment at the attempted site. ⇒ ⭐ the rail could only ever be red for a reason unrelated to its own claim, and `R-131` forbids shipping that. ⭐ The requirement is asserted by the 11 unit rails instead |
| **a forwarding rail on the constructed EDITOR object** | ⚠ the honest gap against the `2026-08-16` control: `EditorPreviewController` is a private nested type and no unit suite constructs an initialised `EditorSubsystem`. ⭐ Both handlers expose `TestHook_Bracket`, so the rail is cheap once a harness exists |
| **`EntityLifecycleModule` as a fourth participant** | `HN-018` — and the bracket takes a **list** precisely so it can be added |
