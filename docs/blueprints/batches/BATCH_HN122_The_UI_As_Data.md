<!--STATUS
state: LIVE
build-state: BUILT
updated: 2026-08-23
current-answer: the whole file — the report for Batch HN-122 (MX9 Group T + MX2 Group Q + MX3 Group R),
  dispatched at aba159c8d. ⛔ EPHEMERAL: the durable truth was folded into MCP_Integration.md
  §"AS-BUILT — SLICE ③", DESIGN_UI_Observability_Snapshot.md's STATUS + §"Perf & correctness", and the
  tracker. Quote those, not this.
known-conflict: none.
-->
# BATCH HN-122 — **the UI as data, and the drain that had stopped**

> 📌 **Dispatch:** [`HANDOFF_Group_T_And_Reuse_Endpoints.md`](HANDOFF_Group_T_And_Reuse_Endpoints.md),
> stamped `aba159c8d`. Started marker `fd299d8f7`. ⭐ ids **`MX-`**, tracker **Area J**.

## 1. ⭐⭐ The items

| # | what | outcome |
|---|---|---|
| **`MX9`** | Group T — `GET /panels` · `/panels/{id}` · `/panels/_gizmo`, + the capture flag | ⭐ built; §2 |
| **`MX2`** | Group Q — attach/detach a blueprint, + `GET /blueprints` | ⭐ built, ⛔ **after fixing what made it impossible** — §3 |
| **`MX3`** | Group R — `GET /entities/{id}/state` | ⭐ built, ⚠ **minus a field that has no source** — §4 |
| **`MX5`/`MX6`** | 8 Node tools *(54 → 62)*, `SKILL.md`, 6 smoke cases | ⭐ built; §6 |
| ⭐⭐ **beyond the dispatch** | `POST /breakpoints/continue` · `/step` | ⛔ **not optional** — without it the dispatched items could not work; §5 |

## 2. ⭐ `MX9` — the UI made readable, and what the endpoint refuses to pretend

⭐⭐ **The list returns BOTH sets** — `registered` *(instrumented at all)* and `captured` *(published this
frame)* — because collapsing them turns *"the assertion found nothing"* into *"the UI showed nothing"*.
📐 Measured live: **47 instrumented, 11 published**, 8 kinds.

⚠⚠ **What it will not pretend:** the snapshot has **no frame boundary**. Entries are latest-wins, so a
panel whose window CLOSED still reports its last model. ⛔ **`PanelSnapshot.Clear()` cannot fix it** — it
drops **both** sets, and `RegisteredPanels` is declared **once at construction**, so clearing per frame
would permanently lose every instrumented-but-not-drawing panel: exactly the false green the two-set
design exists to prevent. ⇒ ⭐ **the contract needs a captured-only clear** *(`ClearCaptured()`)*, which is
the UI lane's to add *(`MX-006`)*; ⭐ meanwhile `GET /panels` carries a `staleness` note rather than
implying a freshness it cannot promise.

⭐ **`_gizmo` is projected per shape**, not serialized: `DebugPrimitive` is a 64-byte union whose payload
fields overlap, so a blanket dump would emit whichever field aliased the bytes — and it would read as
data. A shape with no projection yet says so by name.

## 3. ⛔⛔ `MX2` — "the mechanism already exists; just expose it" was half true

📐 The CONSUMER existed *(`BlueprintEventIngressSystem`, registered by the editor at `:1134`)*. ⛔ **Nothing
had ever declared its events on the editor's bus**, and the bus is strict ⇒ the first publish threw
*"Managed event type 'AttachInstanceBlueprintEvent' was published without being explicitly registered."*

⚠⚠ **And it was never only the API's problem:** the editor's own **`EntityBlueprints` panel** publishes
the same two events on its non-paused commit branch *(`EntityBlueprintsPanel:291-295`)* ⇒ **runtime
hot-attach was unreachable in this host from any caller.** ⭐ Declared beside the systems that drain
them, so schema and consumer cannot drift *(`MX-008`)*.

⚠ **Only RUNNING it found this.** The consumer's presence made the mechanism look complete, and it reads
complete in the design too.

## 4. ⚠ `MX3` — the field that was designed and cannot exist

📐 `grounded` is in the design's field list. Measured: **this engine has no ground-contact component** —
no `Grounded`/`OnGround`/`GroundContact` struct exists to read. ⇒ ⛔ **not built**: deriving it from the
position against terrain would be a guess wearing a fact's name, and an agent would trust it *(`MX-007`)*.
⭐ **`speed` was added instead** — the scalar a *"did it move?"* assertion actually wants, computed one
way for every caller.

## 5. 🔴🔴 The finding this batch turned up — **staged writes stop draining after ANY breakpoint hit**

⭐ **How it surfaced:** the `MX1` watch case passed alone and hung in the full suite. ⛔ **Not a flake — a
real dependence on what ran before it.**

📐 **Mechanism, read then reproduced:** `ResumeAndDrainSystem` returns early on **`_staged.IsRewound`**,
and **deleting a breakpoint does not resume** — only `RequestContinue`/`RequestStep` clear that state.

| the one-probe repro | |
|---|---|
| stage `42` + step, clean world | ⭐ `value=42, pending=false` — drains |
| arm a `Lifecycle` breakpoint, let it fire, **delete it** | `/breakpoints/hits` still says `isPaused:true` |
| stage + step ×5 | ⛔⛔ **`pending=true`, forever** |

⚠⚠ **This is `M-41`'s hazard, live:** *"accepted and silently discarded."* ⛔ **Not fixed here —
`DataBreakpointManager` is the UI lane's file** *(`MX-009`)*. ⭐ **What this batch did instead:**

1. **`POST /breakpoints/continue` + `/step`** — the API could ENTER the stopped state and had no way out.
2. **`POST …/variable` now reports `willDrain:false`** when the debugger is stopped, so it cannot claim a
   write that cannot land.
3. **The harness's `ResetToIdleAsync` resumes as well as clearing** — trap ④ one level deeper: *the
   effect outlives the breakpoint*.

## 6. ⭐⭐ `HN-006` closed — by `MX2`, not by scenario content

📌 Filed last batch because no curated scenario carries a blueprint with working state. ⭐ With
hot-attach, **the harness arranges its own world**: attach the first attachable `Instance` blueprint,
stage, read pending, step, read applied — measured **`LoopLastItem: 0 → staged 17 → after the drain 17`**.
⛔ No hard-coded blueprint name; it detaches in `finally`.

## 7. ⚠ A rail that could not see the thing it was for

📐 Eight tools reached `tool-catalog.mjs` and `SKILL.md` while the command meant to add their **handlers**
failed silently. ⛔ **All 435 catalog assertions still passed** — they compare the catalog to a list, and
the catalog was right. The only symptom was the server saying **54 tools where the catalog had 62**.
⭐ Fixed with a rail that reads `src/index.mjs` as text and asserts a handler per catalogued tool, ⚠ and
**verified by breaking it** *(`MX-010`)*.

## 8. ⚠ Two lanes, one feed — flagged before it becomes a duplicate

⭐ The coordinator dispatched **`U-obs-3`** *(register `DebugPrimitiveBuffer` INTO `PanelSnapshot`)* to the
UI lane the same day this batch built `/panels/_gizmo` reading **the buffer directly**. ⇒ ⛔ once both
exist, one feed has two routes. ⭐ **Lean: the endpoint should then read the snapshot entry**, keeping the
per-shape projection. ⚠ **Neither half is wrong today** — filed as **`MX-011`** so the second to land
routes instead of duplicating. ⭐ **Also corrected my own overclaim**: an earlier edit of the
observability design said `U-obs-3` was "BUILT too" — it is not; the two are different things, and the
STATUS now says so.

## 9. ⭐ Ids allocated

| id | |
|---|---|
| **`MX-006`** | no frame boundary; `Clear()` cannot be one — needs `ClearCaptured()` *(UI lane)* |
| **`MX-007`** | `grounded` has no producer in this engine |
| **`MX-008`** ✅ | blueprint lifecycle events unregistered — hot-attach unreachable *(fixed)* |
| **`MX-009`** 🔴🔴 | staged writes never drain after a breakpoint hit *(UI lane; mitigated here)* |
| **`MX-010`** ✅ | the catalog rail could not see a missing handler *(fixed)* |
| **`MX-011`** | `/panels/_gizmo` vs `U-obs-3` — route, don't duplicate |
| closed | **`HN-006`** |

## 10. ⭐⭐ GATES — *(rule 8's contract; base `aba159c8d`)*

| # | gate — verbatim command | result | `--no-build`? | delta vs base |
|---|---|---|---|---|
| 1 | `dotnet build IOS-IG-SimHost.sln --no-restore` | ⭐ **0 errors** | builds | unchanged |
| 2 | ⭐⭐ **`bash scripts/run-system-tests.sh`** *(Row 8 — real editor headless under Xvfb, `Category=SystemSmoke`)* | ⭐ **40 passed · 0 failed · 0 skipped** | builds first | **+6 cases** *(Group T ×4, Group R, Group Q)*; 34 → 40 |
| 3 | `dotnet test …Hrot.Editor.Tests --no-build` | **229 / 0** | ✅ | +11 — ⚠ **the UI lane's**, arriving with the coordinator merge; mine are the 4 `DebugApiCompositionTests` from last batch |
| 4 | `dotnet test …Hrot.Blueprints.Tests --filter "…Tests.Editor"` ⭐ *(the `EditorSubsystem` gate)* | **1090 / 0**, 9 skipped | ✅ | +45, all the UI lane's |
| 5 | ⭐ `…ClusterRunner.Integration --filter "…TimeControlIntegrationTests"` *(cross-node invariant)* | **9 / 0** | ✅ | unchanged |
| 6 | `dotnet test FDP/Engine/Fdp.ModuleHost.Tests --no-build` | ⚠ **192 / 6** | ✅ | **unchanged** — the 6 are pre-existing `TM-023` Convoy/SoD, named in Batch HN-121's table |
| 7 | `node test-catalog.mjs` | ⭐ **497 / 0** | — | +62 assertions, incl. the new server-coverage rail |
| 8 | `node generate-skill.mjs --check` | ⭐ **PASSED** | — | regenerated for 8 tools |
| 9 | `node src/index.mjs --url …` | ⭐ **starts clean, 62 tools** | — | **54 → 62** |
| 10 | `python3 scripts/design-digest.py --check` | ⭐ **PASS** *(STATUS + INVENTORY + UML)* | — | +1 doc |
| 11 | `python3 scripts/rulings-check.py` | **22/22 verified** | — | ⚠ **3 staleness WARNs, none a defect**: `.claude/CLAUDE.md` *(pre-existing)*, `DataBreakpointManager.cs` *(the COORDINATOR's empty-breakpoint fix)*, `Fdp.Core.md` *(my own HN-121 edit)*. ⭐ Re-read both cited rulings — `R-63` and `R-44` — and neither moved |
| 12 | `python3 scripts/tracker-counts.py --check` | **OK — open 97 / done 309** | — | ⚠ counts only `**BP-` rows ⇒ not evidence about `MX-` |

⭐ **Working tree clean after every suite run; no goldens moved** *(0 golden files touched)*.
⛔ **No new skip added; none removed** *(the two from HN-121 stay at zero)*.

## 11. ⚠ `R-106` — items STOPPED

**None.** Every dispatched item was built. ⭐ Two things were added beyond the dispatch — the breakpoint
resume and `GET /blueprints` — ⛔ **neither is scope creep**: without the first, `MX1`'s writes silently
never land; without the second, `MX2` takes a name nobody can discover.
