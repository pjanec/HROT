<!--STATUS
state: LIVE
build-state: BUILT
updated: 2026-08-22
current-answer: the whole file — the report for Batch HN-121 (HN-001…HN-004 + MX1 Group O), dispatched
  at 5843055e7. ⛔ EPHEMERAL: the durable truth was folded into the owning designs (§Design edits) and
  the tracker; quote those, not this.
known-conflict: none.
-->
# BATCH HN-121 — **the crash the harness caught, fixed; and the watch, over HTTP**

> 📌 **Dispatch:** [`HANDOFF_Derisk_Engine_And_MX1.md`](HANDOFF_Derisk_Engine_And_MX1.md), stamped
> `5843055e7`. Branch `claude/time-system-refactor-batch-104-gp617x`, started marker `49c261296`.
> ⭐ ids **`HN-`/`MX-`**, tracker **Area J** only. ⛔ Stopped at the `U-obs-1` wall as dispatched.

## 0. ⭐ What this batch is

The previous batch built a harness that boots the real editor headless and drives it over the
AI-debug API — and it **found a crash on its first full run**. This batch fixes that crash and the
three wiring defects beside it, then builds **Group O**: addressing a blueprint variable by the same
`(entity, asset, path)` tuple a Details/watch row uses.

## 1. ⭐⭐ The items

| # | what | outcome |
|---|---|---|
| 🔴🔴 **`HN-001`** | `POST /preview/exit` aborted the editor (SIGABRT) | ⭐⭐⭐ **fixed at the root, after reproducing it in 20 lines** — §2 |
| **`HN-002`** | read-modify-write could not round-trip a `Vector3` | ⭐ fixed, ⛔ **and the cause was not where the finding said** — §3 |
| **`HN-003`** | `POST /shutdown` answered 200 and did nothing | ⭐ fixed through a seam both host loops honour — §4 |
| **`HN-004`** | a raw `FATAL:` on stdout from `Fdp.Core` | ⭐ routed through the logger, ⛔ **not deleted** |
| ⭐⭐ **`MX1`** | Group O — variable addressing + `MX5` wrappers + `MX6` smoke | ⭐ built; ⚠ **the write half is not railed by any curated world** — §5 |
| ⛔ **Group T** | `GET /panels` | **not started** — it needs the UI lane's `U-obs-1` |

## 2. 🔴🔴 `HN-001` — **the version protocol, not the preview handler**

⭐⭐⭐ **Reproduced before fixed** *(`R-124`'s discipline)*. `Fdp.Tests.PreviewRewindManagedComponentTests`
prints the crash's exact signature in a unit test:

```
after capture: snapHas=True
after remove:  liveHas=False
after rewind:  liveHas=True  raw=          ← presence restored, payload null
```

📐 **The mechanism, measured:**

| | |
|---|---|
| ⭐ the removal | `GenesisMaterializationSystem` consumes the intent via `cmd.RemoveManagedComponent<T>` ⇒ ECB playback ⇒ `RemoveManagedComponentRaw` ⇒ **`ManagedComponentTable.ClearRaw`** |
| ⛔ **the defect** | `ClearRaw` nulled the payload **without bumping the chunk version**, and `SyncDirtyChunks` **skips** any chunk whose version equals the source's ⇒ the rewind never copied the payload back |
| ⭐⭐ **why the PRESENCE bit came back anyway** | the entity index has no such escape — **`ApplyComponentFilter` bumps its chunk versions on EVERY sync**, so its versions never compare equal. ⇒ **the asymmetry between the two halves is the whole bug** |
| ⚠ the editor's own handler | `PreviewClusterOpHandler` passes **`includeTransient: true`** in both directions, which is why a `[DataPolicy(Transient)]` intent is in scope at all — ⛔ my first repro missed the crash because it omitted that |

⭐ **The fix:** `ClearRaw` and `Clear(int)` route through one `BumpChunkVersion` — **the same protocol
`SetRawObject` already followed**, whose comment names this exact failure for the *write* half. ⇒ the
removal half was simply never fixed.

⭐⭐ **Verified at three levels:** the unit repro · both harness rails **un-skipped** *(the whole
record→replay round trip now runs end to end)* · the full smoke suite green.

## 3. ⚠ `HN-002` — **the finding named the symptom; the cause was one line elsewhere**

⛔ The row said *"the patch parser goes through StructEdit, which wants `{X,Y,Z}`"*. 📐 **Measured:**
the patch parser built its **own bare `JsonSerializerOptions`**, carrying none of the converters the
DUMP was written with — the object form worked only by accident of default System.Text.Json handling.
⇒ ⭐ **the fix is not "accept the array too", it is "parse with the options you serialized with"**:
`DebugApiPatchOptions`, whose vector converters now read **both** shapes. ⭐ Enums stay tolerant on
input *(the integer form still parses)* while output stays canonical names.

## 4. ⭐ `HN-003` — **one stop signal, both host loops**

⭐ `SubsystemConfig.RequestAppExit` *(bound by the orchestrator to `Stop()`)* + a public
`IsRunning` the Composition Root's render loop polls each frame. ⛔ **Not `Environment.Exit`** — that
skips the runner's `finally`, and with it every subsystem's `Shutdown()`. ⭐⭐ **The harness teardown
now asks before it kills**, so the editor logs end with `[Runner] Shutdown complete.` and the
`free(): corrupted unsorted chunks` kill artifact is gone from the normal path.

## 5. ⭐⭐ `MX1` — Group O, and the premise that failed

⛔⛔ **The design AND the handoff both said `DebugApiService` "already holds `_blueprintSession`".**
📐 The **parameter** did; the **composition root never passed it** — BTree's and HSM's sessions were
handed over and Blueprint's, built ~400 lines above, was not. ⇒ **every Group O call refused on
arrival**, and only running it revealed that.

⭐⭐ **The control is a forwarding rail PER DEPENDENCY** — `DebugApiCompositionTests` reads the
composition root as text, one case per dependency, each naming the consequence of its absence.
⚠ **Verified by removing the line: exactly that case goes red.** ⛔ No behavioural rail can see this —
`EditorSubsystem` cannot be constructed headless, so every other rail builds its own composition root.

⭐ **Four deviations from the design's Group O**, all folded into `MCP_Integration.md` §"AS-BUILT —
SLICE ②": `asset` is optional and accepts a NAME; discovery via `BlueprintTierSummary`; `writable` and
`pendingValue` added to the DTO; the field's type comes from the value the snapshot already decoded.

### ⚠⚠ What is NOT proven — stated so a green suite does not imply it

📐 **Probed the running editor by hand:** `hill-attack` has **one of eight** entities carrying a
blueprint, it is **`Library`-dispatch**, and a Library blueprint has no working state ⇒ `variables: []`.
⇒ ⛔ **the stage → pending → drain → land loop is reached only by code review.** ⭐ Filed as
**`HN-006`**: the fix is scenario CONTENT *(a curated scenario with an `Instance` blueprint)*, and the
cases would then exercise the write half unchanged.

## 6. ⭐ Design edits — **obligation ⑤, the durable half**

| doc | what was folded in |
|---|---|
| ⭐⭐ `docs/projects/FDP/Core/Fdp.Core.md` | **the invariant `HN-001` established** — a new §`ManagedComponentTable<T>`: every payload mutation must bump the chunk version, why it fails silently, and why the unmanaged tier deliberately does not need it |
| `docs/designs/mgmt-1/DESIGN.md` | an AS-BUILT note on the dry-run mechanics: the rewind is only "exact" while that invariant holds |
| ⭐⭐ `docs/MCP_Integration.md` | new §"AS-BUILT — SLICE ②" *(MX1)*; `build-state` extended; **`known-rot` now names the false `_blueprintSession` claim** |
| ⭐ `docs/DESIGN_MCP_System_Test_Harness.md` §9 | `/shutdown` row **corrected** *(it is no longer inert; teardown asks first)*; `KnownDefectRails` → `PreviewLifecycleRails` with both rails un-skipped; the H4 ladder's two uncovered rows **closed**; the `ResetToIdleAsync` note re-justified now that its original reason is gone |

## 7. ⭐ Ids allocated

| id | |
|---|---|
| **`HN-006`** | new — Group O's write path has no end-to-end rail *(scenario content)* |
| **`MX-004`** | new — `_blueprintSession` never passed; the silent-default pattern's next instance |
| **`MX-005`** | new — `test-catalog.mjs` had been RED since the previous batch |
| closed | **`HN-001`** · **`HN-002`** · **`HN-003`** · **`HN-004`** · **`HN-005`** |

## 8. ⭐⭐ GATES — *(rule 8's contract; base `5843055e7`)*

| # | gate — verbatim command | result | `--no-build`? | delta vs base |
|---|---|---|---|---|
| 1 | `dotnet build IOS-IG-SimHost.sln --no-restore` | ⭐ **0 errors**, 28 warnings | builds | unchanged *(the warnings are the pre-existing `BP3010` orphan-node ones)* |
| 2 | ⭐⭐ **`bash scripts/run-system-tests.sh`** *(Row 8 — the integration gate, real editor headless under Xvfb, `Category=SystemSmoke`)* | ⭐⭐⭐ **34 passed · 0 failed · 0 skipped** | builds first | **+7 cases, and the 2 SKIPS ARE GONE** *(27/0/2 → 34/0/0)* — the skips were `HN-001` |
| 3 | `dotnet test Hrot/Subsystems/Hrot.Editor.Tests/… ` | **218 / 0** | builds | **+4** *(`DebugApiCompositionTests`)* |
| 4 | `dotnet test …Hrot.Blueprints.Tests --no-build --filter "FullyQualifiedName~Hrot.Blueprints.Tests.Editor"` ⭐ *(the gate for anything touching `EditorSubsystem`)* | **1045 / 0**, 9 skipped | ✅ `--no-build` after a solution build | unchanged |
| 5 | ⭐ `dotnet test …ClusterRunner.Integration --no-build --filter "FullyQualifiedName~TimeControlIntegrationTests"` *(cross-node invariant — this batch changed `SubsystemOrchestrator` + the runner loop)* | **9 / 0** | ✅ | unchanged |
| 6 | `dotnet test FDP/Engine/Fdp.ModuleHost.Tests --no-build` | ⚠ **192 / 6** | ✅ | **unchanged** — the 6 are **pre-existing, `TM-023`**, named: `ConvoyAutoGrouping…SharesProvider` · `ConvoyIntegration_5Modules_ShareSnapshot` · `ConvoyIntegration_MemoryUsage_Reduced` · `HonestSodGdb…ActivatedAtomically` · `HonestSodGdb…ExpandsSharedProvider` · `ProviderAssignment_AsyncSoD_MultipleModules_Convoy` |
| 7 | `dotnet test FDP/Engine/Fdp.Core.Tests --no-build --filter "<the sync suites>"` *(the HN-001 blast radius)* | **43 / 1** | ✅ | the 1 is **`EntityIndexSyncTests.Performance_100K_Entities`**, in the pre-existing stable core |
| 8 | ⛔ `dotnet test FDP/Engine/Fdp.Core.Tests` **whole suite** | ⛔⛔ **UNGATEABLE BY COUNT** | ✅ | 📐 **3 runs of the SAME binary: 12 · 5 · 13 failures**, identities rotating *(CheckpointIOWorker, EventBus stress, timing benchmarks)*. ⭐ **Stable core = 5**, measured at clean HEAD by stashing: 3 × `InMemoryMigrationStorage` + `AsyncRecorder.ErrorPropagation` + `EntityIndexSync.Performance_100K`. ⚠ **A filtered run of `ComponentOperationBenchmarks` alone PASSES 3/3 at both states** — they fail only under parallel load ⇒ **load-sensitive, not a regression.** ⭐ Filter it; row 7 is the real gate |
| 9 | `node test-catalog.mjs` | ⭐ **379 / 0** | — | ⛔ **RED at base (3 failures)** — `MX-005`, my own omission from the previous batch, fixed here |
| 10 | `node generate-skill.mjs --check` | ⭐ **PASSED** *(SKILL.md up to date)* | — | regenerated for the 3 new tools |
| 11 | `node src/index.mjs --url …` | ⭐ **starts clean, 54 tools** | — | **51 → 54** |
| 12 | `node verify.mjs` | ⛔ **FAILS** *(`MCP error -32000: Connection closed`)* | — | ⭐⭐ **PRE-EXISTING — proved by stash last batch**, unchanged here |
| 13 | `python3 scripts/design-digest.py --check` | ⭐ **all 61 pass** *(STATUS + INVENTORY + UML)* | — | +1 doc |
| 14 | `python3 scripts/rulings-check.py` | **22/22 verified** | — | ⚠ 1 staleness WARN on `.claude/CLAUDE.md` — **pre-existing**, unchanged |
| 15 | `python3 scripts/tracker-counts.py --check` | **OK — open 91 / done 283** | — | ⚠ it counts only `**BP-` rows ⇒ **not evidence about `HN-`/`MX-`** |

⭐ **Working tree clean after every suite run** — no golden was regenerated; **no goldens moved at all**
this batch *(diff shape: 0 golden files touched)*. ⭐ **Quarantine counts unchanged**; ⛔ **no new skip
was added — two were REMOVED** *(`HN-001`'s rails)*.

## 9. ⚠ `R-106` — items STOPPED

**None.** Every dispatched item was built. ⛔ Group T was not started **by instruction**, not by
blockage.

## 10. ⚠ Process notes worth carrying

| | |
|---|---|
| ⚠ **the graph tools dropped mid-batch** | `mcp__codebase-memory-mcp__*` disconnected during the `MX1` seam investigation and reconnected later. ⭐ **The seam inventory in §5 was measured with grep + reading the sources**, not with `search_graph` — stated per the CLAUDE.md rule. ⚠ It is a CONFIRMATION-shaped result *(I read the implementations end to end)*, ⛔ not an exhaustive one |
| ⭐ **two design claims failed on contact** | `HN-002`'s stated cause and `MX1`'s "already injected" premise. ⚠ **Both were plausible and both were wrong** — measuring what the code DOES cost minutes and would have cost a wrong fix each time |
