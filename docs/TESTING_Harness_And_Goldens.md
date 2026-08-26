<!--STATUS
state: LIVE
doc-type: runbook (the standing HOW-TO for system/smoke/conformance tests + golden maintenance — not a
  buildable design, so no build-state/UML gate). Handoffs REFERENCE this; every implementation session follows it.
updated: 2026-08-26
current-answer: the whole file — how the C# harness drives the system over HTTP, how to write a smoke test,
  the perspective-switch capture protocol, how goldens are made/maintained, how conformance reuses it all,
  and §7 — how to tell a real red from an exhausted process.
design-basis: DESIGN_MCP_System_Test_Harness.md (the harness) · DESIGN_UI_Observability_Snapshot.md (PanelSnapshot) ·
  DESIGN_Headless_Testability.md (the taxonomy) · MCP_Integration.md (the API).
known-conflict: none.
-->
# RUNBOOK — how we test the system, and how we keep goldens honest

> ⭐ **This is the doc handoffs cite.** When a batch touches behaviour or what a panel shows, the implementing
> session follows §6 **in the same batch**, and the coordinator verifies it on merge.

## 1. The harness at a glance

⭐ The harness boots the **real process as a subprocess** (headless, Xvfb on Linux) and drives it over HTTP with
a typed **C# `McpClient`** — ⛔ **not** through the Node MCP server *(that is the agent-facing driver)*. Same HTTP
control plane, C# client.

```
[Fact] test  →  McpClient (HTTP)  →  DebugApiHost :port  →  DebugApiService (sim thread)  →  the process
```

⭐⭐ **It is ONE binary — `Hrot.ClusterRunner` — and `--mode` selects which subsystem(s) run.** The editor is
not a different executable; it is the cluster runner hosting the **editor** subsystem. ⇒ there is really **one
fixture, parameterised by mode**:

| `--mode` | subsystem(s) in the process | perspectives you can snapshot |
|---|---|---|
| **editor** *(fixture built, HN-120)* | the **editor** subsystem | the editor's perspectives |
| ⭐ **all** *(to reach for conformance)* | **`orchestrator,simhost,ig,excon,cgf` — FIVE subsystems** in ONE process | each submodule, **by switching perspective** *(`PerspectiveCoordinatorSystem`)* |

⚠⚠ **CORRECTED `2026-08-23`: there is NO `--mode cluster`.** The shorthand is **`all`** *(= `demo`)*; an unknown
mode **throws** *(`HrotRunnerConfiguration.cs:104-123`)*. ⛔ And the two modes' perspective sets are **disjoint**,
so conformance **discovers** them per mode rather than switching both to one name.

⭐⭐ **Consequence for the read-API:** `PanelSnapshot` is a **process-wide static singleton** — it already exists
in every mode. What is editor-only today is the **`DebugApiHost` wiring** *(constructed in `EditorSubsystem`)*.
⇒ the conformance prerequisite is NOT *"add the API to two more hosts"* — it is **wire the existing
`DebugApiHost` one level up** *(the `ClusterRunner` host, mode-independent)* so `/panels`, `/perspective` etc.
answer in **cluster** mode too. One implementation, enabled in every mode.
⭐ One process per test-collection; tests share it; scenarios load **sequentially** within the collection.

## 2. Writing a smoke test — the shape

```csharp
[Fact, Trait("Category","SystemSmoke")]
public async Task Squad_advances_and_the_watch_shows_it() {
    await Mcp.LoadScenarioAsync("hill-attack");      // a curated scenario (git-seeded)
    await Mcp.EnterPreviewAsync();
    await Mcp.StepAsync(600);                         // deterministic ticks
    // behaviour/state layer:
    var st = await Mcp.GetEntityStateAsync(squadId);
    Assert.True(st.Speed > 0);
    // panel/visual layer (pixel-free — the model the renderer reads):
    var watch = await Mcp.GetPanelAsync("ai.watch");
    Assert.Equal("11", watch["rows"]![0]!["value"]!.GetValue<string>());
}
```

⭐ **Three read layers, all over the same API** — assert at whichever the feature lives in:
1. **behaviour/state** — `/entities`, `/entities/{id}/state` *(position·velocity·speed·behavior)*, `/entities/{id}/variable` *(watch value + pending)*, `/events`, `/breakpoints/hits`.
2. **panel/visual** — `GET /panels/{id}` → the panel's **view-model JSON** *(from `PanelSnapshot`)*. ⭐ this is "what the user sees", machine-checkable, no pixels.
3. **determinism** — deterministic timestep + record/replay for frame-exact checks.

## 3. ⛔⛔ THE PERSPECTIVE PROTOCOL — a panel only snapshots when its perspective is ACTIVE

⭐⭐ **Panels register to `PanelSnapshot` only when their DRAW runs, and only the ACTIVE perspective draws.**
📐 Measured: an editor reports **~11 of 47** instrumented panels captured at once — the rest belong to other
perspectives. ⇒ to snapshot a panel that lives in perspective *P*:

```
POST /perspective {"name":"P"}     // WindowManager.SwitchPerspective / PerspectiveCoordinatorSystem
POST /sim/step {"ticks":1}         // let P's panels draw once so they register
GET  /panels/{id}                  // now captured
```

⚠ **Required capability, NOT yet built:** `GET /perspectives` *(list)* + `POST /perspective {name}` *(switch)*
on the DebugApi. ⛔ Until it exists, only the default perspective's panels are reachable. **This is a prerequisite
for cross-perspective smoke and for ALL conformance.**

⭐ **Cluster runner:** its perspectives ARE the submodules — CGF · SimHost · Orchestrator
*(`PerspectiveCoordinatorSystem` maps the names)*. Same protocol: switch to the CGF perspective to snapshot CGF's
panels.

## 4. Goldens — assertion vs snapshot, and how to keep them honest

Two styles, used for different jobs:

| style | what | when |
|---|---|---|
| ⭐ **hand-written assertion** | the expected value is IN the test *(`Assert.Equal(11, tier)`)* | specific, meaningful checks — the capability ladder + scenario cases |
| ⭐ **golden / snapshot** | dump the whole model *(all captured panels + state)* to a JSON file in git, compare future runs to it | broad *"did anything change?"* coverage per scenario |

### How a golden is made and maintained

| step | how |
|---|---|
| **location** | `Hrot.SystemTests/Goldens/<scenario>/<perspective>.json` *(one per scenario × perspective)* — checked into git |
| **create/update** | ⭐ follow the **existing** per-family env convention *(`EQS_GOLDEN_CAPTURE` is the precedent)* — set **`PANEL_GOLDEN_CAPTURE=1`** and the test writes the dump instead of comparing. ⛔ **Do not invent a new golden mechanism** — reuse the `<FAMILY>_GOLDEN_CAPTURE` shape |
| ⛔⛔ **review the diff** | a regenerated golden is a **DIFF you must read**, never a rubber-stamp — 📌 the rule-8 gate already demands *"golden movement as a diff shape"* |
| ⭐ **ship it with the feature** | ⛔ **a change that alters what a panel shows regenerates its golden IN THE SAME BATCH** — see §6 |

⚠ **Determinism is mandatory for goldens** — fixed timestep, fixed frame count, sorted keys. A golden that
flakes is worse than none.

## 5. Conformance — the same tests, a different assertion

⭐⭐ Conformance **reuses** the scenarios, the driver and the read surface. It only swaps the final assert:

| | asserts | reference data |
|---|---|---|
| **smoke** | host X shows the RIGHT thing | a golden or a hand-written expectation |
| ⭐ **conformance** | host X and host Y show the SAME thing | ⛔ **none** — the reference IS the other host's live dump |

```
proc A (--mode editor):  load S → switch to the perspective with panel K → step → dump K
proc B (--mode all):     load S → switch to the CGF perspective with panel K → step → dump K
assert:                  dump_A[K]  ==  dump_B[K]     // same binary, two modes; diff by PanelKind
```

⭐ **No golden to maintain for conformance** — both hosts change together when a feature changes; if they DON'T,
that divergence is the bug conformance exists to catch.

## 6. ⭐⭐⭐ THE OBLIGATION — every implementation session, every batch

> ⛔ **If your change alters system behaviour or what a panel shows, the test/golden update ships in the SAME
> batch.** A green suite that still encodes the old behaviour is a false green.

| your change | you do, in the same batch |
|---|---|
| new/changed **behaviour** | add or update the **assertion** *(or the scenario case)* |
| new/changed **panel content** | regenerate the affected **golden** *(`UPDATE_GOLDENS=1`)* and **read the diff** in your report |
| new **panel** | it publishes in some perspective ⇒ add it to that perspective's golden |
| new **capability** *(endpoint/feature)* | add one **smoke case** to the ladder |

⭐ **The coordinator verifies on merge** *(rule 8 + obligation ⑤)*: a golden moved without a diff shape in the
report, or a behaviour change with no test change, is an **incomplete batch** — sent back.

⛔ **Do NOT** hand-edit a golden file to make a test pass — regenerate it and justify the diff. A hand-patched
golden is the exact false-green this runbook exists to prevent.

---

## 7. ⛔⛔⛔ WHEN THE SUITE ITSELF IS THE DEFECT — **the leak that read as flakiness for ~40 batches**

> ⭐⭐⭐ **The rule this section exists for: ⛔ a red is not evidence until the INSTRUMENT is known good.**
> A process that has run out of memory fails tests that have nothing wrong with them, and it fails a
> *different* set every run — which is indistinguishable from flakiness, and was read as flakiness.

### 7.1 📐 What was actually happening *(measured `2026-08-26`, 16 GB / 4 CPU)*

| the symptom, as previously recorded | ⭐ what it really was |
|---|---|
| `Hrot.ClusterRunner.Integration.Tests` **aborts at a different count every run** *(`38`, `76`, `89`, `117`…)* — `BP-378`, `F17` | the host **ran out of memory and died**; the count is just how far it got |
| *"the failure identity ROTATES between runs"* — `DEBT-AIB-030`, `TM-032`, `TM-036`, `ST-026`, `HN-019`, `BP-471` | ⚠ under memory pressure, **whichever test allocates at the wrong moment loses** |
| *"every named one PASSES under `--filter`"* | ⭐ of course — **a filtered run never reaches the pressure** |
| three different proximate causes *(DDS `dds_take -3`, a `ModuleHost` timeout, `OutOfMemoryException`)* | ⛔ **ONE root cause with three faces** |

⭐⭐ **The chain, end to end:**

```
a node teardown releases the KERNEL but not the REPOSITORIES it ran on
  → RSS climbs monotonically (measured 4.1 → 9.9 GB in 45 s of one run)
  → 77 × OutOfMemoryException out of EntityIndex / NativeChunkTable ctors
  → a harness CONSTRUCTOR throws
  → xUnit does NOT call Dispose on an instance whose ctor threw
  → its DDS participant + background id-allocator poll thread survive
  → that thread calls dds_take on a dead handle
  → unhandled exception on a NON-TEST thread ⇒ the whole host process dies
```

⛔⛔ **The dominant term was NOT the world.** `ModuleHostKernel.Initialize` builds a `SnapshotPool` with
`warmupCount: 10`, and nothing ever released it ⇒ **ten `EntityRepository` instances leaked per kernel**,
each holding an `int[1_000_000]` free list plus one `NativeChunkTable` per registered component.
⭐ **This is a PRODUCT defect, not a test defect** — every node teardown in the shipping runner leaked them.

### 7.2 ⭐⭐⭐ THE INSTRUMENT — **`EntityRepository.LiveInstanceCount`**

⛔ **A leaked repository is invisible**: it throws nothing, logs nothing, and fails no assertion. So the
count is the whole difference between arguing and knowing.

```csharp
int before = EntityRepository.LiveInstanceCount;
using (var harness = new HrotRunnerHarness()) { /* … */ }
Assert.Equal(before, EntityRepository.LiveInstanceCount);   // ⭐ a DELTA, never an absolute
```

⭐⭐ **And when it is non-zero, get the LINE, not just the number:**

```bash
FDP_TRACK_REPO_LEAKS=1 dotnet test <proj> --no-build --filter TheHarnessReleasesEveryWorld
```

📌 It records each repository's construction stack and prints them in the failure. **That is what found
`SnapshotPool` and then `OnDemandProvider`** — the count alone had only said *"ten of them, somewhere."*
📐 One full five-subsystem harness round-trip: **32 leaked → 2 → 0.**

### 7.3 ⭐ THE HABIT — **three checks before you believe a red**

| # | check | why |
|---|---|---|
| **①** | ⭐⭐⭐ **did the run FINISH?** `Total tests: Unknown` + *"Test Run Aborted"* ⇒ ⛔ **the numbers are not comparable to anything** — not to the base tree, not to the previous run | the pre-fix suite could not produce a total at all |
| **②** | ⭐⭐ **`grep -c OutOfMemoryException` the log** | ⛔ **any** non-zero count invalidates every red in that run |
| **③** | ⭐ **watch RSS**: `while :; do free -m; sleep 15; done` alongside the run | ⭐ a MONOTONIC climb is a leak; a plateau is a working set |

⇒ ⭐⭐ **Only after ①–③ are clean is a filtered base-vs-change comparison meaningful.** ⚠ `F17`'s
*"quote a filtered subset run on both trees"* was the right MITIGATION for an un-gateable suite — ⛔ it was
never the fix, and it is no longer needed for this suite.

### 7.4 ⛔ WHAT THIS FORBIDS

⛔⛔ **`DisableParallelization` is not a cure for a rotating red.** 📌 It was proposed as the cheap fix for
`DEBT-AIB-030` and it *does* reduce the reds — ⚠ **because it lowers the peak memory, not because ordering
was the problem.** ⇒ that is `R-131`'s permanent filter-around wearing a different hat. ⭐ **The one
legitimate use is the opposite one:** the world-leak rail is `DisableParallelization` **because
`LiveInstanceCount` is process-wide**, so a concurrent collection would make the delta meaningless — the
attribute is protecting a *measurement*, not hiding a *failure*.
