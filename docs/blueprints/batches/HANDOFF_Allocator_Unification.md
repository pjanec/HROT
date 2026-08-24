<!--STATUS
state: LIVE
build-state: DISPATCH
updated: 2026-08-24
current-answer: dispatch pointer for HN-037 (backend lane, one session, overnight) — unify the network-id
  allocator to ONE authority per world reset to 1000 at the world boundary, AND delete the obsolete direct
  scenario-load path (ScenarioFileService.LoadScenario + the IEditorLogic facade). Carries no design: cites
  DESIGN_Deterministic_Network_Ids.md §11.
known-conflict: ⚠ the UI lane is running CONCURRENTLY (HN-030 catalog-from-routes · start_simulation mode
  param · REMOVING the /scenario/load alias) and edits EditorApplication.cs + the DebugApi scenario routes.
  This batch also edits EditorApplication.cs (Path-B deletion). See §4 — rule-4-pull and reconcile, or
  STOP-and-report on the overlap.
-->
# HANDOFF — **HN-037: unify the network-id allocator + delete the obsolete load path** *(backend lane, overnight)*

> 📌 **Dispatched at `dbdff90f3`.** ⛔ **Scope FROZEN at that sha.** ⭐ Branch fresh from the **LATEST**
> `claude/blueprint-authoring-status-6sr5ld` *(rule 7 — the UI lane's HN-030 may have merged by the time you
> start overnight; branching from the latest head is expected and desired)*. **Rule 1b: push the started-marker
> BEFORE any code.** ⛔ **No PR.**
>
> ⭐ **IDs — collision-avoidance, READ THIS:** the UI lane is **concurrently** allocating in the `HN-` series
> *(HN-030 + a few for start_simulation / the alias removal — likely `HN-040..HN-04x`)*. ⇒ ⛔ to stay clear,
> **allocate your code rows starting at `HN-050`**, **state every id** *(rule 5)*, and **rule-4-pull before your
> final commit** so a collision is caught at merge. The gap this closes is **`HN-037`**; ⭐ also close **`HN-038`**
> if you touch the replication bootstrap *(§2 item ⑥ is optional)*. Tracker **Area J**.

> 🔒 **User, `2026-08-24`:** *"one single allocation path in both [edit and live] cases. Editor is no exception…
> resets to initial value (1000 for the first entity) whenever whole 'world' resets… I still do not see any
> reason for 2 separate allocators."* Plus: *"add the deletion of the obsolete scenario load path."*

## 0. ⛔⛔ THE DESIGN IS THE SOURCE — this file is a POINTER

📄 **[`DESIGN_Deterministic_Network_Ids.md` §11](../../DESIGN_Deterministic_Network_Ids.md)** *(READY-TO-BUILD)*
— §11a current state, §11b new state *(both carry current-vs-new class + sequence diagrams)*, §11c the
three-reset-policy table, §11d the lane-tagged change points, §11f the one ordering rail. ⛔ **The 2PC state
machine is NOT yours to design** — owned by [`docs/designs/mgmt-1/DESIGN.md` §5.7](../../designs/mgmt-1/DESIGN.md).
⭐ Report per obligation ③; ⭐⭐ **fold the as-built into §11** *(obligation ⑤)* — §11 becomes AS-BUILT.

## 1. ⭐⭐⭐ PART A — THE ALLOCATOR UNIFICATION *(§11)*

⭐⭐ **The target:** ONE id authority per world — offline in the editor's one-node cluster, the DDS master in
`--mode all` — **reset to 1000 at every world reset** *(scenario-load start, after `SoftClear`)*, serving
authored *(at load)* and runtime *(after)* from **one monotonic sequence**. First authored entity = **1000 on
every host** ⇒ HN-037 gone, no band, editor not special.

| # | task | §11 | the one thing not to get wrong |
|---|---|---|---|
| ⭐ **①** | **Editor: reset the single offline allocator to 1000 at world reset.** The editor already uses one allocator for both authored + runtime *(`EditorSubsystem.cs:1127`)* — add the reset | §11d ① | ⛔ reset only at the world boundary, never mid-run |
| ⭐⭐ **②** | **CGF: point `CgfScenarioLoadHandler` at `_context.IdAllocator`** *(the DDS client)* and **retire the standalone `cgfIdAllocator`** *(`CgfSubsystem.cs:488`)* — authored ids now come from the one authority | §11d ② | ⭐ `CreateEntityRequestSystem` already prefers `PreAllocatedNetworkId`, so the wiring is localized. ⛔ don't leave the local allocator constructed-but-unused |
| 🔴🔴 **③** | **Fire `Req_Reset(Start=1000)` on the world-reset / load fan-out, GUARDED to the world boundary ONLY** — never mid-exercise | §11d ③ | 🔴🔴 **the load-bearing guard.** `Req_Reset` is cluster-wide-destructive by design *(§2c)* — correct at a world reset, catastrophic mid-exercise *(it fights §5.7's forward high-water and clobbers live pools)*. ⛔ Reachable ONLY from the scenario-load/world-reset path, **asserted by a rail** |
| 🔴🔴 **④** | **THE ORDERING + PARITY RAIL** — after a load, the lowest authored id is **1000 on every host**, and editor == `--mode all` | §11f | ⛔ CGF must pull the first chunk *(`[1000-1099]`)* after the reset and before any other node draws a runtime chunk. ⭐ Safe today *(only CGF allocates during `LoadingLive`)* but **assert it, don't assume** — an out-of-order chunk pull is exactly the silent-divergence HN-037 was. ⭐⭐ **This rail closing green IS the proof HN-037 is fixed** |
| ⭐ **⑤** | **Flip + delete the HN-037 tripwire; update conformance** — the `entity-inspector` id-divergence declaration is now resolved *(ids match)* | HN-029 report deviation ④ | ⚠ the **node-local-entity** difference *(IG lists networkId 0)* is a SEPARATE reason — if it still measures, `entity-inspector` stays DECLARED for THAT, not for ids. ⭐ State which reasons remain |

## 2. ⭐⭐⭐ PART B — DELETE THE OBSOLETE DIRECT LOAD PATH *(user + NotebookLM, VERIFIED)*

📐 **Verified by the coordinator `2026-08-24`** *(so you build on measurement, not on NotebookLM's word)*: the
direct path `ScenarioFileService.LoadScenario(repo, filePath)` has **ZERO production callers except the facade
itself** — `EditorApplication.cs:155` *(the `IEditorLogic.LoadScenario` impl)*; **every other caller is a test**
*(~8 files)*. No design doc DEFENDS it as a capability; `§9` of the id design frames it as the superseded
file-carried path. ⇒ ⭐ **deletion, not routing — no capability is lost** *(the genesis pipeline IS the editor's
load; raw round-trip belongs to `ScenarioSerializer`)*.

⛔⛔ **Why the direct path is a trap** *(NotebookLM, and it matches the measured pipeline)*: it bypasses the
`EntityLifecycleModule` handshake *(no `Constructing` phase, no `AuthorityMask`)*, leaves transient genesis
Intents *(`InitialVehicleIntent`/`InitialPassengersIntent`/…)* **dangling** because `GenesisMaterializationSystem`
never runs, and **never syncs the allocator** ⇒ a later spawn collides. ⭐ It is exactly the class of bug Part A
exists to prevent — so removing it is part of the same story.

| # | task | the one thing not to get wrong |
|---|---|---|
| ⭐⭐ **⑥** | **Delete the facade** — `LoadScenario(string filePath)` from `IEditorLogic` + `EditorApplication.cs:153-157` | ⚠ **EditorApplication.cs is also edited by the concurrent UI lane** *(§4)* — reconcile or STOP-and-report |
| ⭐⭐ **⑦** | **Reroute the ~8 test callers** — editor-load tests → `LoadScenarioByName`/genesis *(`HrotEditLoadHandler`)*; **raw serialize/deserialize round-trip tests → `ScenarioSerializer.Deserialize` directly** *(`ScenarioFileServiceTests`, `EditorFileIOIntegrationTests`, `EditorFileOpsIntegrationTests`, `ZoneScenarioLoadIntegrationTests`, `EditorPreviewAndSaveIntegrationTests`, `DebugApiServiceTests`)* | 🔴 **STOP-and-report if any test needs a capability ONLY the direct path provides** — ⛔ do NOT weaken an assertion to make it pass. ⭐ some of these test the SERIALIZER, not the editor load — those keep their coverage by targeting `ScenarioSerializer` |
| ⭐ **⑧** | **Purge `ScenarioFileService.LoadScenario`** once ⑥/⑦ sever the references. ⭐ Keep `SaveScenario` | ⚠ confirm `SaveScenario` (or anything else) does not privately call the load method |

⚠ **The order matters:** ⑥/⑦ before ⑧ *(sever references, then purge)*. ⭐ If ⑦ uncovers a real dependency, that
item STOPs-and-reports; ⛔ **it does not stop Part A** *(`R-106` — do every unblocked item)*.

## 3. ⚠ WHAT WILL BITE

| ⚠ | |
|---|---|
| 🔴🔴 **the guarded reset (③)** | `Req_Reset` mid-exercise is catastrophic. ⭐ gate it to the world-reset path and prove the guard with a **revert-goes-red** rail *(a reset fired mid-exercise must be unreachable/asserted-against)* |
| ⭐⭐ **the DDS client is chunked** | `CHUNK_SIZE=100`. Authored `1000-1007` needs CGF to pull `[1000-1099]` first, after the reset. Assert *(④)*, don't assume |
| ⚠ **editor is offline** | no DDS — its "authority" is the offline allocator; the reset is a local set. ⭐ same *policy*, different *implementation* |
| ⚠ **preview is UNTOUCHED** | §4d's local capture/restore stays exactly as built — ⛔ do NOT route preview through the master reset *(world not cleared → collision)*. §11c is the reconciliation |
| ⚠ **Part B bypass symptoms** | dangling genesis Intents / missing `AuthorityMask` are how a mis-rerouted test will fail — that is the pipeline working, not a regression |

## 4. ⛔ LANE, SCOPE & THE CONCURRENT UI LANE

⭐ **Yours (backend lane, one session):** `EditorSubsystem`/editor allocator · `CgfSubsystem` + `CgfScenarioLoadHandler` · the orchestrator/`ClusterMaster` + DDS `Req_Reset` world-reset wiring · the conformance/ordering rails · Part B *(`IEditorLogic`, `EditorApplication`, `ScenarioFileService`, the ~8 tests)*.

⛔⛔ **CONCURRENT UI LANE — the real coordination risk:** the UI session is running **HN-030** *(generate the
tool-catalog from routes)*, making **`start_simulation` take a `mode` parameter** *(was always `--mode editor`)*,
and **REMOVING the `/scenario/load` alias**. That work edits **`EditorApplication.cs`** and the **DebugApi
scenario routes** — files Part B also edits.
- ⭐ **Rule 4: pull the coordinator branch before your final commit** and read what the UI lane merged.
- ⛔ **If Part B's `EditorApplication`/`IEditorLogic` edits collide with the UI lane's alias/`start_simulation`
  changes, RECONCILE onto their merged version** *(they land first — this is overnight)*; if the reconcile is
  ambiguous, **STOP-and-report that item**, don't guess.
- ⚠ The `/scenario/load` alias removal is THEIRS — ⛔ do not also remove it; Part B removes the **direct file
  loader** *(`ScenarioFileService.LoadScenario`)*, a different thing from the HTTP alias.

⛔ **STOP-and-report, do not silently cross:** if you cannot reconcile a UI-lane overlap, or if a Part-B test
reveals a live capability. ⭐ Everything else proceeds *(`R-106`)*.

## 5. GATES

⭐ Standing contract *(rule 8)*: one row per gate · verbatim command · pass/fail/skip · **delta vs `dbdff90f3`
and vs your started-marker** · a `--no-build` column · every RED pre-existing **by name** · golden movement as
a diff shape · `tracker-counts.py --check` · `rulings-check.py` · `design-digest.py --check` ·
`mermaid-check.mjs` on §11 if you edit it · **the ids you allocated** *(rule 5)*.

⭐⭐⭐ **Row 8 — this touches id allocation, the cluster load, and the editor, so it needs the SYSTEM invariants:**
- `bash scripts/run-system-tests.sh` *(baseline `83/83`)* — ⭐ the conformance content diff should now show authored ids MATCHING across hosts *(④/⑤)*.
- **`Hrot.ClusterRunner.Integration.Tests --filter DistributedScenarioLoadTests`** — 📐 it asserts CGF's authored ids; update its expectations to the unified 1000-block and report.
- **`SimTimeSyncIntegrationTests` + `TimeControlIntegrationTests`** — the reset touches the orchestrator/2PC; prove cluster time/replication still hold.
- **the guard rail (③) and the ordering rail (④) shown RED by inverse edit** — ⛔ a rail never seen red is decoration.
- ⚠ If the DDS-allocator crash makes an integration suite un-gateable, that is a **reported finding with the base sha**, not a silent skip.

⚠ **Known quirks — not yours:** `tracker-counts.py` blind to `HN-`/`MX-` rows · `Fdp.Presentation.Tests` ~18–20 pre-existing *(`BP-419`)* · `Hrot.SimHost.Tests` / `Fdp.Toolkits.Tests` rotating-flaky *(`DEBT-AIB-030`)* — confirm by `--filter`, don't quote a total · known `rulings-check` staleness WARNs.

## 6. ⭐ WHEN YOU ARE DONE

⭐⭐ **Fold the as-built into [`DESIGN_Deterministic_Network_Ids.md` §11](../../DESIGN_Deterministic_Network_Ids.md)**
— §11 becomes AS-BUILT *(the unified authority's real shape, the guard, the ordering rail, and what Part B
removed)*, prior state marked SUPERSEDED where it deviates. ⭐ Close `HN-037` in the tracker, state every id you
allocated, and name any new open finding *(e.g. a Part-B test that could not reroute)*. ⛔ Design content in the
design; the report points at it.
