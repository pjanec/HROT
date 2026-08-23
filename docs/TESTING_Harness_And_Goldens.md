<!--STATUS
state: LIVE
doc-type: runbook (the standing HOW-TO for system/smoke/conformance tests + golden maintenance — not a
  buildable design, so no build-state/UML gate). Handoffs REFERENCE this; every implementation session follows it.
updated: 2026-08-23
current-answer: the whole file — how the C# harness drives the system over HTTP, how to write a smoke test,
  the perspective-switch capture protocol, how goldens are made/maintained, and how conformance reuses it all.
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

| fixture | boots | reaches panels of |
|---|---|---|
| **`EditorProcessFixture`** *(built, HN-120)* | `Hrot.ClusterRunner --mode editor` | the editor's perspectives |
| **`ClusterRunnerFixture`** *(to build — conformance)* | the cluster runner *(CGF + SimHost + Orchestrator in ONE process)* | each submodule, **by switching perspective** |

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
| **create/update** | run with **`UPDATE_GOLDENS=1`** *(env)* → the test writes the dump instead of comparing |
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
editor:         load S → switch to the perspective with panel K → step → dump K
cluster runner: load S → switch to the CGF perspective with panel K → step → dump K
assert:         dump_editor[K]  ==  dump_cgf[K]     // diff by PanelKind
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
