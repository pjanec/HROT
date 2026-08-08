# RESUME / HANDOFF — Blueprint gaps & QoL programme (2026-08-07, rev 4)

> **Goal:** make blueprint editing fully functional and pleasant.
> **Branch:** `claude/blueprint-authoring-status-6sr5ld` · **HEAD at handoff:** `f6c8f4b` + the Q25
> answers commit (docs only — no code changed in the Q25 round)
> **Live state:** [Blueprint_Issues_Tracker.md](Blueprint_Issues_Tracker.md) (checklist — **every row
> deep-links to its detail entry**, `#bp-<id>`) ·
> [Blueprint_Issues_Detail.md](Blueprint_Issues_Detail.md) (per-issue evidence + `DONE` notes)
>
> **The two tracker docs are the source of truth.** This file is orientation only — if it and the
> tracker disagree, the tracker wins.

### Starting a fresh session? Read in this order

1. **Status** and **Next up** below — where the programme is and what comes next.
2. **🎯 Next task briefing** — **BP-24 SHIPPED (Batch 15, 2026-08-06)** to the Q23 decisions
   (A2+B2+C2+D1, recorded in
   [Architect_Question_23_Graph_Create_And_Switching.md](Architect_Question_23_Graph_Create_And_Switching.md)).
   **BP-24 is now closed** — Batch 16 shipped **BP-71** 🔴 and **BP-72**, the two gaps its post-ship
   audit found ([Q24](Architect_Question_24_Function_Return_Value_Wiring.md) A1+B1+C3). **Batch 17
   shipped [BP-69](Blueprint_Issues_Detail.md#bp-69)** 🔴 plus the general unwired-pin hardening it
   forced, and **Batch 18 shipped [BP-73](Blueprint_Issues_Detail.md#bp-73)** — N function outputs,
   closing the last of Q24. Next up: **[BP-70](Blueprint_Issues_Detail.md#bp-70)** 🔴 or **BP-67's
   EqsResult slice**; BP-57 and BP-25 are newly unblocked. ⚠ **Nothing in batches 9–18 has been
   looked at in the running editor** — the visual check is the largest outstanding risk.
   🔎 **A functions audit followed BP-73** and registered six gaps — **BP-74**…**BP-78** plus BP-57.
   Two of them had been parked in *Out of scope* on a **false "absent from the codebase" premise**
   (fifth and sixth such overturn, always `Hrot/` searched and `FDP/` not). **Macros are now in scope.**
   ✅ **[Q25](Architect_Question_25_Macros.md) is ANSWERED (2026-08-07)** — a **self-researched** round
   (no NotebookLM; same footing as Q23/Q24 and recorded as such): **A1** own `GraphKind.Macro` ·
   **B1** new `Stage2_5_ExpandMacros` between Validate and Normalize · **C1** asset-local now ·
   **D3** one exec-in / **N ≥ 0** exec-out · **E** four rails kept, two added, one dropped.
   `BP-78` is closed as a **design** item; implementation is **BP-79…BP-83**, in that order.
   ⚠ **Start with BP-79** — it is small, and it closes the two *silent*-failure holes
   (`Stage5:4314`'s `_ => IrGraphKind.Function` catch-all, and `InstanceEmitter:82`'s "first Function
   graph is the Tick graph" fallback) **before** any expansion code exists, so every later bug in
   BP-81 is a build error instead of a silent miscompile.
   📌 The round overturned **two** of the question's own claims and revised a third — see its
   verification log. Every correction made the feature *cheaper* or its failure mode *louder*; none was
   cosmetic. Notably the doc's headline mechanism claim ("the latent cursor machinery exists only for
   the top-level graph") was **false**, and `BP-81`'s "no node-clone prior art" was **also false** —
   `BlueprintClipboard.Rehydrate` shipped it in `BP-23a`, in this same programme. **That is overturn
   #7, and the first one that was our own `Hrot/` code**: before writing "no prior art exists", grep
   the tracker's **done** rows.
3. **Traps that cost real time** — nine of them, each one earned. Trap #5 (`default:` returns
   success) and #6 (asset-scoped features belong at the host) have each bitten more than once;
   **#9 is why BP-71 survived a 2788-test suite — and it then struck BP-69's own test, which passed
   against the bug.** Reverting the fix to watch the test go red is now a **required** step.
4. **Test baseline** — what "green" means, and which two failures are known flakes.
5. **Working agreement** — how the user wants this programme run.

Then open the tracker for the item you are taking and re-derive its claim against code before
building. **The audit register has been wrong ten times**; the corrections table in the detail doc
lists every one.

---

## Status

**61 open · 53 fixed · 1 refuted (BP-46).** Counts and per-complexity breakdown live in the tracker
table; do not duplicate them here.

| Batch | Items |
|---|---|
| 1 — silent-failure | BP-59, BP-29, BP-16, BP-15, BP-12e |
| 2 — undo + docs | BP-02, BP-47, BP-48, BP-49, BP-50 |
| 3 — palette | BP-04, BP-09 |
| 4 — test health & reflection | BP-62, BP-35 (+ suite serialization) |
| 5 — coverage | BP-41 |
| 6 — undo unification | BP-11 ⭐ |
| 7 — wiring | BP-03, BP-05…BP-08, BP-10, BP-12a, BP-63, BP-64 (+ BP-65 🔴, BP-66 🔴) |
| 8 — custom-event authoring | BP-12c, BP-68 🔴 (+ BP1407/BP1408, dispatcher section removed) |
| 9 — promote to variable | BP-60 🔴 (lifts BP-02's last bypass) |
| 10 — canvas clipboard | BP-23a |
| 11 — panel item CRUD | BP-12b |
| 12 — alignment | BP-13 |
| 13 — node header | BP-17, BP-18 |
| 14 — navigation aids | BP-19, BP-20 |
| 15 — graph create + switching | BP-24 ⭐ (Q23 A2+B2+C2+D1; + BP-12b rename-undo desync fixed) |
| 16 — closing out BP-24 | BP-71 🔴 (Q24 A1+B1+C3; +BP1655/BP1656), BP-72 |
| 20 — functions & macros UX, steps 1–2 | BP-92 (dispatch at create), BP-89 (outputs on the Return node) |
| 21 — the Batch-20 visual check's defects | BP-103 🔴 (blank template had zero graphs — crashed on open, broke the build), BP-104 🔴 (Library outputs ignored), BP-105 (inert Status combo); re-ticks BP-92 |
| 22 — end-to-end smoke test | BP-109 🔴 (two entities, two blueprints, one shared Library) — which found **BP-110** 🔴: a `CallPeerBlueprint` had never compiled at all |

**Batch 7's visual pass (2026-08-05)** earned its keep: one bug of mine (the bookmarks ✕ was
unreachable — a full-width `Selectable` swallowed the click), one long-standing 🔴 (**BP-66**, the
peer catalog scanning a directory that does not exist), and two scope findings.

**Batches 8–14 ran overnight on 2026-08-05.** Seven batches, nine items, ~120 new tests, all suites
green. Three themes worth carrying forward:

1. **The `default:`-returns-success trap has now bitten four times** — BP-60, BP-68 and BP-18 all
   had a command the sink silently accepted and ignored. **Never assert on `Success`; assert the
   effect.** Grep for `GraphCommand` variants with no `case` in `BlueprintCommandSink` before
   assuming a command works.
2. **Asset-scoped features belong host-side, not in the vendored sink command.** BP-60 and BP-23a
   both looked like "add a sink case" and both were wrong: the single opaque command hides the ids
   the caller needs for an inverse. Composing the gesture at the host from primitives the sink
   already implements keeps BP-11's invariant *and* makes it one undo entry.
3. **A wiring fix exposes the next one.** Shipping BP-12c made the very next gesture fail (BP-68),
   because until then no asset could declare a custom event. The visual check is what finds these,
   not the suite.

---

## Next up

**Shipped and closed:** BP-24 (Batch 15) → BP-71 🔴 + BP-72 (Batch 16) → BP-69 🔴 (Batch 17)
→ BP-73 (Batch 18) → **Q25 answered, `BP-78` design closed** (2026-08-07, docs only)
→ **Batch 19 (2026-08-08, Windows): [BP-86](Blueprint_Issues_Detail.md#bp-86) 🔴 +
[BP-84](Blueprint_Issues_Detail.md#bp-84) 🔴 + [BP-85](Blueprint_Issues_Detail.md#bp-85)** — the three
defects the visual check found in its first ten minutes.
→ **Batch 20 (2026-08-08): [BP-92](Blueprint_Issues_Detail.md#bp-92) + [BP-89](Blueprint_Issues_Detail.md#bp-89)** —
steps 1 and 2 of the [functions & macros UX plan](Blueprint_Functions_Macros_UX_Plan.md).
→ **Batch 21 (2026-08-08): [BP-103](Blueprint_Issues_Detail.md#bp-103) 🔴 +
[BP-104](Blueprint_Issues_Detail.md#bp-104) 🔴 + [BP-105](Blueprint_Issues_Detail.md#bp-105)** — the
three defects the user found driving the editor after Batch 20; re-ticks **BP-92**.

**Batch 19 verified by the user in the running editor:** Get/Set/Literal delete → undo → redo → save →
reopen all correct (**BP-84 closed**); shorter renames produce exact names on the entry node's data-out
pins (**BP-86 closed for Blueprints**); the canvas breadcrumb reads correctly (**BP-85 closed**).

⚠ **The T-series (T1–T7, [BP-73](Blueprint_Issues_Detail.md#bp-73) N function outputs) is STILL
unverified — but it is no longer blocked.** The gate was that the user could not find how to add
function outputs at all ([BP-89](Blueprint_Issues_Detail.md#bp-89)); **BP-89 shipped in Batch 20**, so
outputs are now declared on the Return node's Details panel, where the designer already is.
**T1–T7 are performable and remain unperformed — that is the next thing to do in the running editor,
and it is still the programme's largest outstanding risk.**

> 🧱 **Five UX blockers found in the batch-19 visual check (2026-08-08)** —
> [BP-88](Blueprint_Issues_Detail.md#bp-88) (no Event graph exists in an Instance blueprint, and none
> can be created — **unblocked by BP-92**, since a functions-only asset can now declare itself a
> `Library` rather than mislabel itself `Instance`) · ~~[BP-89](Blueprint_Issues_Detail.md#bp-89)~~
> (**closed in Batch 20**) · [BP-90](Blueprint_Issues_Detail.md#bp-90) (blackboard rename has no affordance)
> · [BP-91](Blueprint_Issues_Detail.md#bp-91) (no way to add an HSM event) ·
> ~~📐 [BP-92](Blueprint_Issues_Detail.md#bp-92)~~ (**closed in Batch 20** — the dispatch choice was
> never an architect question; the one that *is* — whether `Library` calls skip the `CallablePeers`
> opt-in — stays open).
>
> **Four of the five are discoverability, not missing features** — the code is there. 🔁 Third
> confirmed instance of *double-click-only, no affordance, no hint* (BP-75, BP-90, and BP-89's Outputs
> table).
>
> ⚠ **BP-90 and BP-91 also left [BP-86](Blueprint_Issues_Detail.md#bp-86) sites 2 and 4–6 unverified by
> hand** — those panels could not be reached. Headless coverage stands; the editor confirmation does not.

> 🔴 **Two persistence defects surfaced by the same session — read before touching assets:**
> [BP-93](Blueprint_Issues_Detail.md#bp-93) — **the editor saves tracked assets without an explicit
> Save.** The user opened a BTree and an HSM, never invoked Save, and both files came back modified
> *and* fully reformatted. There is currently **no safe way to explore an asset in the editor** —
> assume anything you open may be written. ·
> [BP-94](Blueprint_Issues_Detail.md#bp-94) — the load→save round-trip **changes fields nobody
> touched** (`IsAutoManaged: false → absent`, `Blackboard.Managed: false → true`).
>
> ⇒ **Check `git status` after every editor session** until these are fixed. The one test that would
> guard both: load every shipped asset, save untouched, assert deep equality.

⚠ The visual check remains the highest-value activity in this programme: batch 19 shipped a defect
(`▸` rendering as `?`) that **no headless test in this repo could see**.

> 🔴 **New, and it blocks `dotnet build`:** [BP-87](Blueprint_Issues_Detail.md#bp-87) — the parameter
> type dropdown offers **8 types the compiler cannot resolve**. The untracked `SquadState1.bp.json`
> from the visual check fails the build with `BP1500: Pin type 'Vector3' does not resolve`. Batch 19
> was built and gated with that file temporarily moved aside; it is untracked and was left in place.
> **Needs an architect call on scope before fixing** — see the item.

📋 **Coordinated batches.** A coordinator session owns the tracker and writes handoffs;
implementation sessions build. ⚡ Every handoff carries the **Sonnet-delegation rule** — see
*Working agreement → Coordinator / implementation split*.

🔀 **Two things run in parallel right now (2026-08-08):**
1. ✅ **Build — DELIVERED (Batch 22).**
   [HANDOFF_Batch22_EndToEnd_Smoke.md](HANDOFF_Batch22_EndToEnd_Smoke.md): **BP-109** 🔴 shipped —
   two entities · two blueprints · one shared Library function, as recipe assets **plus** a gate test
   that loads those same on-disk files. Runs in **2 seconds**.
   ⭐ **It found what it was built to find, immediately: [BP-110](Blueprint_Issues_Detail.md#bp-110)
   🔴 — a `CallPeerBlueprint` had never COMPILED.** The hole was deeper than "never executed": the
   emitted call named `__Peer_{id:X8}_Bp`, a class nothing declares, so it *could not* execute.
   Reproduced with caller and peer in the same merged compilation, which disproves the
   `NodeCoverageTests` comment claiming production resolves it by compiling siblings together.
   ✅ Two entities on different blueprints in one world — flagged unproven — **works, no code changes**.
   📌 Also registered: [BP-111](Blueprint_Issues_Detail.md#bp-111) — the known-flake list is incomplete
   and the gate's `-v q` hides the failing test's name; that cost time twice in this batch.
2. **Verify** — the Windows visual check of Batches 20–21 (see *🎯 Batch 20 — DO THIS FIRST* below).
   Findings go to the coordinator and land in a **later** batch, not Batch 22.

⚠ **[BP-108](Blueprint_Issues_Detail.md#bp-108) (Print/Log node) is deliberately NOT in Batch 22.**
There is no `ToString`/`Format`/`Concat` node and no string coercion, so a Print node taking only a
string could print literals only. It needs a format-literal + typed-args pin shape no node has ⇒ design
note first. BP-109 asserts via `TryGetField<T>` instead and does not wait.

Three independent fronts; pick by appetite, not by order.

| # | Item | Why now |
|---:|---|---|
| 1 | **BP-79** 🔴 (`RW-L`) | **The cheapest high-value thing on the board.** Adds `GraphKind.Macro` and closes the two *silent*-failure holes (`Stage5:4314` catch-all, `InstanceEmitter:82` tick-graph fallback). Small, self-contained, and it makes every later macro bug loud. Do this before BP-80/81 whatever else happens |
| 2 | **BP-80 → BP-81 → BP-82 → BP-83** | The rest of macros, in that order. BP-81 is the only `RW-H`; its cloning half is already built (`BlueprintClipboard.Rehydrate`, from BP-23a) so the real work is boundary rewiring |
| 3 | **BP-70** 🔴 (`WIRING`, one line — **a design decision, not a bug fix**) | The emitter's `EventTypeFqn ?? Name` fallback never fires because `EventTypeId` defaults to `""`, not null, so Event graphs land in `EventHandlers` under `""` and the documented name-keyed bus dispatch is unreachable. Fixing it makes every custom event globally raisable by name-hash. **Ask the user (or fold into Q23-B3) before applying** |
| 4 | **BP-74** 🔴 · **BP-75** · **BP-76** · **BP-77** 🔴 | The rest of the functions audit. BP-74 (collapse → function/macro) pairs naturally with BP-80; **BP-77 is closed for free by BP-80** |
| 5 | **BP-67** (`RW-M`) | The When node's other three forms; the **EqsResult slice is `RW-L` and shippable alone** (see briefing) |
| 6 | Then | BP-57 (function locals, unblocked) · BP-25 (unblocked) · BP-56 (wire glow) · BP-23b (cross-asset paste) · BP-61 🔴 (inert-default HSM guards) |

**Context for BP-24 (from the 2026-08-06 discussion with the user):** a blueprint-local custom event
is a strictly weaker Function graph — same call shape, no return value, name-paired instead of
id-paired — and its one distinguishing capability (bus-raisable by name) is dead (BP-70). BP-24 is
what makes Function graphs and custom-event bodies authorable at all, which is why the user chose it.

**Still unregistered on the My Blueprint menu** (deliberately out of BP-12b's scope):
`editor.move-to-category`, `editor.change-variable-type`, `editor.show-properties`,
`editor.find-references` (that one is BP-12d).

**Blocked / do not build as written:** `BP-31` (premise inverted — see BP-61) ·
`BP-40`, `BP-38`, `BP-52` (architect decision first) · `BP-53`, `BP-54` (UNCLEAR, re-scope first).

---

## 🎯 Next task briefing — scouting already done, do not re-derive

Verified against code on 2026-08-06. Written so a fresh session can start editing immediately.
**BP-24 shipped in Batch 15** — its what-was-built summary is below the BP-67 briefing; decisions in
[Architect_Question_23](Architect_Question_23_Graph_Create_And_Switching.md).

### BP-67 — the When node's other three mode forms (`RW-M`) · *a strong next candidate*

BP-10 fixed **EventFired**. The other three each render one `TextDisabled` line and cannot be
configured at all, so the node is effectively EventFired-only.

| Fact | Where |
|---|---|
| The three stubs, ~3 lines each | `NodeDrawers/WhenNodeDrawer.cs` — `DrawValueChangedForm` `:170`, `DrawConditionMetForm` `:333`, `DrawEqsResultForm` `:339` |
| The form to mirror (shipped, tested) | `DrawEventFiredForm` in the same file — filtered `BeginCombo`, `ApplyEventTypeId`, `ApplyTargetFilter` |
| Data model — all four payloads already exist | `Assets/Nodes.cs:267-360`: `ValueChangedPayload`, `EventFiredPayload`, `ConditionMetPayload`, `EqsResultPayload` |
| Undo/dirty plumbing | already there — `_editService.RecordPropertyEdit` + `NotifyStructureChanged`, same as BP-10 |

⚠ **Not the same shape as BP-10.** EventFired was `WIRING` because its catalog was already injected
*and already called* — only the result went unrendered. These three have **no ready-made source**.
Do them in this order, easiest first:

1. **EqsResult** — the tractable one. `EqsResultPayload { SensorVariableName, Trigger, ScoreThreshold, MaxAgeSeconds }`;
   `EqsTrigger` is a 4-value enum (combo), and **the sensor picker already exists**:
   `ReadEqsResultNodeDrawer.cs:28-33` lists `FDP.Eqs.EqsSensorHandle`-typed variables and `:59`
   shows the empty-state message to copy. This one is genuinely `RW-L`.
2. **ValueChanged** — `ValueChangedPayload` has a 3-way `Source` discriminator (`SelfComponent` /
   `PeerBlueprintVariable` / `WorkingStateField`), so it is really three sub-forms. `ComponentTypeId`
   can reuse the component picker; **`PropertyPath` is the new part** — `ComponentFieldReflector`
   (see trap #3 on assembly load order) gets you fields, not paths.
3. **ConditionMet** — `ConditionMetPayload.Condition` is a raw `JsonNode` holding a predicate tree;
   the editor converts to/from `SearchPredicateDto` at its own boundary. **A predicate-tree editor is
   a component in its own right** — worth splitting into its own item rather than smuggling it in.

**Done means:** pick a mode, configure it, Ctrl+Z reverses it, and the preview pill at the bottom of
the drawer reflects it. Take EqsResult alone if time is short; it is a clean, shippable slice.

### BP-24 — ✅ SHIPPED (Batch 15, 2026-08-06)

Everything about it now lives in two places: the decisions + retarget audit in
[Architect_Question_23](Architect_Question_23_Graph_Create_And_Switching.md), the ship notes in
`Blueprint_Issues_Detail.md#BP-24`. The pieces a future session may build on:

- **`BlueprintGraphSwitcher`** (`Host/BlueprintGraphSwitcher.cs`) — `SwitchTo(graphGuid)` /
  `SwitchToViewId(GraphId)` / `CurrentGraph`; owns per-graph viewport+selection cache, the
  GraphMetadata camera persistence, the debug-adapter rebind, and the `UndoStack` context hooks.
- **`editor.go-to-graph`** — the one navigation entry point; accepts `Args["itemId"]`
  (`graph:{guid}` or `evt:{guid}` → body graph) or `Args["graphId"]`.
- **`CreateFunctionGraph` / extended `CreateCustomEvent`** in the factory — both undoable when a
  `GraphView` is passed; `FindCustomEventBodyGraph` is the pairing lookup.
- **Not in the slice (deliberate):** graph rename/delete from the panel, Construction graphs,
  cross-restart last-viewed (parked until something composes `BlueprintEditorPreferences` —
  today nothing loads that file).

---

## 👀 Visual check — batches 9–18 · **NOT YET DONE**

⚠ **Status: pending.** Batches 9–14 shipped overnight, 15–16 the following day, then 17 (BP-69) and
18 (BP-73). All are logic-tested headless, but **no human has looked at any of them in the running
editor yet**. What a test cannot see is layout, wording and feel — and the batch-7 pass proved that
half (the bookmarks ✕ was correctly positioned and completely unclickable). Treat every row as
unverified.

> 🎯 **The single most valuable thing to look for: a pin-projection mismatch.** Every batch from 15 on
> touched **both** `NodePinSchema` (editor) and `Stage0_Rehydrate` (compiler), which must agree. A
> headless test typically exercises one of them, so a divergence survives green suites and shows up
> only in the editor, as one of these:
> - a pin **renders but refuses a wire** (editor invented a pin the compiler doesn't have), or
> - a compile error **naming a pin you can plainly see** (compiler expects a pin the editor didn't draw).
>
> If you hit either, that is a real defect regardless of what the row said to expect — capture the
> node kind, the pin name, and the diagnostic code.

### 📂 Fixtures — what is actually openable (verified 2026-08-08)

Two roots, and the distinction matters (`AssetRoots.cs`): **`Assets/Blueprints`** is the browse/save
destination (the asset browser); **`Recipes/Blueprints`** is the *creation* source — reachable only via
**New from Recipe**. Both resolve from `AppContext.BaseDirectory`; the 13 recipes are `Content` with
`CopyToOutputDirectory=PreserveNewest`, so they do reach the output dir.

⚠ **There is exactly ONE shipped asset anywhere with a Function graph that declares an output:**

| Fixture | Where | Shape |
|---|---|---|
| **`SquadState`** | **Recipe** (New from Recipe) | Instance · graph `GetThreatLevel`, `Kind=Function`, **1 output** (`ThreatLevel`, `System.Single`) · 3 nodes (EventEntry, GetVariable, Return) · **`Links: []`** |

Everything else — all 12 other recipes and every asset under `Assets/Blueprints` — has **0 outputs on
every graph**. ⇒ **Shipped-asset coverage of function return values is effectively zero**, which is
precisely why this check matters.

⚠ **Two traps in using `SquadState`:**

1. **Compiling it as-shipped does NOT produce BP1655.** `V_FunctionGraphReturnValue` has a documented
   *unauthored-stub* exemption — `if (graph.Links.Count == 0) continue;` — and `SquadState` has no
   links. To see BP1655 you must first add **any** link in that graph (e.g. EventEntry exec → Return
   exec) while leaving the Return's **value** pin unwired. See R3.
2. **It has no graph named `Tick`.** `InstanceEmitter.cs:81-82` therefore selects `GetThreatLevel` as
   the tick graph (`?? FirstOrDefault(Kind == Function)`), and `:83` then excludes it from the `Func_`
   emission. Whether a peer can still call it is **unverified** — flagged, not diagnosed. This is the
   same fallback BP-79 adds a guard for; it is unrelated to the pin-projection checks below, so it does
   not block them.

### 🔴 Findings so far (check STARTED 2026-08-08)

| Verdict | What |
|---|---|
| ✅ **BP-71 confirmed working** | On `SquadState1`, the Return node showed a value pin **named after the declared output** (`ThreatLevel`) as an **input**, and it **accepted a wire** from `GetVariable`. The saved JSON proves it: a link with `ToNodeId` = the Return node. R1 + R2 pass |
| ✅ EventEntry projection correct | One `Out` exec pin on a 0-input Function graph |
| 🔴 **BP-84 — NEW defect, ✅ diagnosed** | Delete a `GetVariable`, **Ctrl+Z**, and it returns **without its `Value` output pin**. **Discriminating experiment run:** only the *restored* node loses pins; the wired sibling is untouched ⇒ **view-model rebuild**, not node ordering. Blast radius one node. `RW-L` — see [BP-84](Blueprint_Issues_Detail.md#bp-84) |
| 🔴 **BP-86 — NEW, ✅ root-caused** | Renaming to a **shorter** value keeps the old value's tail past an embedded NUL: `P1` over `Param0` persists `P1␀am0`. `TrimEnd('\0')` strips only *trailing* nulls; `InputText` leaves the buffer's tail intact. **Truncate at the first null.** ⚠ **Seven sites** share the idiom, across the HSM and Blackboard editors too — see [BP-86](Blueprint_Issues_Detail.md#bp-86) |
| 🔴 **BP-85 — NEW** | The canvas never names the active graph, so creating a function reads as *"my graph was emptied"* |
| ✅ **BP-75 confirmed live** | Single-click **and** drag of a Functions-section item both do nothing. Only double-click works |
| ⚪ Not defects (checked) | `Header: {}` on save is **correct** — `$meta` supersedes it (D-021, `GraphTypes.cs:162`) · `"VariableId": "var:<guid>"` is a **tolerated form by design** (`BlueprintDocumentFactory:1083-1085`) · New-from-Recipe **writing the file before any save** is by design (`NewFromRecipeService` returns an unregistered asset "ready for the host to save and register") · a Return node with **no** value pin in a 0-output function is **correct** — declare an Output in Graph Signature first · ⚠ **adding *Input* params correctly leaves the Return node unchanged** — inputs surface as data-outs on the **entry** node; the Return node reflects **Outputs**. Mistaking this for a bug cost real time, which is BP-85's case in one sentence |

📋 **Fixing these on Windows? Start from
[HANDOFF_Windows_Fix_Session.md](HANDOFF_Windows_Fix_Session.md)** — self-contained, with the exact fix
for BP-86, what is already ruled out for BP-84, the fixture setup, and the in-editor verification steps.

**🔬 Open question for BP-84:** does closing and reopening the asset heal the pin-less node?
Pins are stripped on save and re-projected on load, so it probably does — which would downgrade the
data-loss framing to render-until-reopen. Confirm rather than assume.

Ordered **newest-first**, because newest is least verified.

### 🔴 Batch 20+21 visual check — results so far (2026-08-08)

⚠ **The run stopped at section B. Sections C–F, including the T-series, are STILL unperformed** — the
T-series is now unverified for a **sixth** batch.

| Section | Result |
|---|---|
| **A1–A3** | ✅ `Function Library` template exists; `FuncLib1` **created and opened without throwing** ⇒ [BP-103](Blueprint_Issues_Detail.md#bp-103) confirmed fixed, [BP-92](Blueprint_Issues_Detail.md#bp-92) confirmed re-tickable |
| in-memory hot reload | ✅ OK |
| **A4** | 🔴 **FULL BUILD FAILS — `CS9191`** in the generated adapter of an otherwise-empty library ⇒ **[BP-110](Blueprint_Issues_Detail.md#bp-110)** |
| **B1** (`ushort` output) | 🔴 **`BP1500: Pin type 'ushort' does not resolve`** ⇒ **[BP-87](Blueprint_Issues_Detail.md#bp-87) confirmed live**, from the dropdown, at build time, with no author-time warning |
| peer call, multi-output | 🔴 `CallPeerBlueprint` shows **one** output pin regardless ⇒ **[BP-111](Blueprint_Issues_Detail.md#bp-111)** |
| **C · D · E · F** | ⬜ **not reached** |

⇒ **Two of the three failures are on the *generator/MSBuild* path, which no test covers for `Library`
dispatch** (see BP-110). The user's question — *"why can't the implementation session exercise this
headless?"* — is answered: **it can**; the gate simply has no Library fixture.

### 🎯 Batches 20+21 — DO THIS FIRST · **rewritten 2026-08-08 for the post-Batch-21 state**

> ⚠ **The previous revision of this section is STALE and two of its rows would have produced false
> regression reports.** Batch 21 changed what you should expect:
> **A4** — an empty Library no longer errors (BP-103 seeds a starter graph), and
> **D** — the `Status` combo is now *hidden* on an Instance graph (BP-105). Both are rewritten below.
> This is the same failure mode as the retired `R5`/BP1656 row: a checklist that outlives its subject.

**Budget ~25 min. In order — each section sets up the next.**

#### A. Create a Function Library (BP-92 + BP-103) — ~4 min

| # | Do | Expect |
|---|---|---|
| A1 | **New Blueprint** → look at the template list | **Two** blank templates: **`Empty`** and **`Function Library`**, each with a description (plus the 13 disk recipes) |
| A2 | Pick **`Function Library`**, accept the default name | Defaults to **`NewBlueprint`**, *not* `Function Library` |
| A3 | ⭐ **It opens without throwing** | Batch 21's headline fix. Before it, this threw *"Blueprint asset … has no graphs"* |
| A4 | ⭐ **Compile it immediately, untouched** | ✅ **CLEAN — no BP5001.** ⚠ **This expectation is INVERTED from the previous guide.** BP-103 seeds a starter Function graph, so a new library is valid on creation. **Any BP5001 here is now a defect** |
| A5 | Check the `.bp.json` | `"Dispatch": "Library"`, and `Graphs` is **not empty** |
| A6 | Create an **`Empty`** (Instance) one too, compile it | Also opens and compiles clean |
| A7 | Put a **Delay** in a library function, compile | **BP1101**, naming the node by its palette name, saying a library cannot suspend |
| A8 | Open any pre-existing asset | Still `Instance` — nothing was migrated |

⚠ **Delete any scratch assets before you finish.** [BP-87](Blueprint_Issues_Detail.md#bp-87) is still
open: an asset using an unresolvable type (`Vector3`, `uint`, …) **breaks the solution build** for
anyone who pulls.

#### B. Library function outputs (BP-104) — ~3 min · ⭐ the new one

This path had **never been compiled by any test** before Batch 21.

| # | Do | Expect |
|---|---|---|
| B1 | In your Function Library, select the function's **Return** node → add **one** output, wire it | Pin appears |
| B2 | **Compile** | ✅ Clean. ⚠ Before Batch 21 this was a hard Roslyn error (`CS0266` for a single numeric output, `CS0029` for a tuple) |
| B3 | Add a **second and third** output, wire them, compile | Clean — the tuple path |
| B4 | Now **remove all outputs**, compile | Still clean — a zero-output library function returns `NodeStatus`, which is deliberate |

#### C. Outputs on the Return node (BP-89) — ~4 min

| # | Do | Expect |
|---|---|---|
| C1 | Open a Function graph in an **Instance** blueprint, select **Return** | An **Outputs** section, headed *"Outputs — one data-in pin on this Return node, and one data-out pin on every call site."* |
| C2 | With none declared | *"This function declares no outputs. Add one to return a value."* |
| C3 | Click **`+`** | Row appears; the Return node grows a data-in pin immediately |
| C4 | Rename it **shorter** than the default | ⚠ **BP-86 guard** — exact name, no `?`, no leftover tail |
| C5 | **Ctrl+Z** after each of add / remove / rename / retype | ⭐ **One** undo step each, exact prior state |
| C6 | Add a third output → Ctrl+Z ×2 → Ctrl+Y ×2 | Stable. This path had an aliasing bug Batch 20 found and fixed — most worth stressing |

#### D. `Status` visibility (BP-105 + BP-14) — ~3 min · ⚠ **fully rewritten**

BP-105 made `Status` and `Outputs` appear **only where the compiler actually reads them**:

| Asset dispatch | Outputs section | Status combo |
|---|---|---|
| **Instance** | ✅ shown | ❌ **hidden** |
| **Library**, 0 outputs declared | ✅ shown | ✅ shown |
| **Library**, ≥1 output | ✅ shown | ❌ hidden |
| **AiPrimitive** | ❌ hidden | ✅ shown |

| # | Do | Expect |
|---|---|---|
| D1 | Return node in an **Instance** function graph | **No Status combo.** ⚠ The old guide said to expect an editable combo here — that was correct *before* BP-105 and is wrong now |
| D2 | Return node in a **Library** function with **zero** outputs | Status combo **is** shown, editable, with a line saying why |
| D3 | Add an output to that library function | Status **disappears**; Outputs remains |
| D4 | Change Status where it *is* shown, then **Ctrl+Z** | One undo step, restores the previous value ⇒ **BP-14 closes** — tell the coordinator |

#### E. ⚠ Known gap — confirm, do not report as new (BP-102)

| # | Do | Expect |
|---|---|---|
| E1 | Add an output from the **Graph Signature window** (not the Return node), then **Ctrl+Z** | ❌ **Will NOT undo.** That is [BP-102](Blueprint_Issues_Detail.md#bp-102), already registered. Just confirm the asymmetry |

#### F. ⭐ Then the T-series — blocked five batches, now performable

Use section C's `+` to add three outputs to an **Instance** function graph, then run `T1`–`T7` below.

⚠ **T7 is the one to watch:** a second output must **compile cleanly**. Any surviving *"multiple
outputs not supported"* message is a leftover defect — an older revision of that row wrongly told you
to *expect* it.

#### Reporting

Send the **section id** (A4, B2, D1, T7…), what you saw vs expected, and the **exact diagnostic code**.
`BP5001` vs `BP1101` vs a Roslyn `CS` number point at completely different halves. Screenshots for
anything about wording or layout — precisely what 4,700 green tests cannot see.

---

### N function outputs (BP-73) — batch 18, newest

| # | Where | What to do | What should happen |
|---|---|---|---|
| T1 | A Function graph · Graph Signature → **Outputs +** three times, name and type each | look at the **Return** node | It grows **three value pins, all on the LEFT**, in declaration order. Not one pin, not pins on the right |
| T2 | Wire all three, then place a call to that function | look at the **call node** | It shows **three data-out pins**, names and order matching the signature |
| T3 | Wire one of the call node's outputs downstream and compile | — | Compiles clean. ⚠ The emit returns a `ValueTuple` and the call site unpacks it — if you get a Roslyn `CS` error mentioning `Item1`/`__t`, that is BP-73's carrier fan-out and I need the exact text |
| T4 | Leave **one** of the three Return pins unwired · compile | — | A **BP1655** naming that pin. The other two must still work — a partial wiring is an error about the one pin, not a collapse |
| T5 | Leave **one** call-node output pin unwired · compile | — | Compiles clean. Unused outputs are fine; only unwired *Return* inputs are errors |
| T6 | Reorder or delete an output in Graph Signature | look at Return + call nodes | Both follow. A stale pin left behind on either is a projection bug |
| T7 | Graph Signature → add a **second** Output | compile | ✅ **It compiles.** ⚠ **BP1656 is RETIRED by BP-73** — if you see *any* diagnostic saying multiple outputs are unsupported, or an inline warning under the Outputs list, that is a **leftover and a defect**. The old checklist told you to expect BP1656 here; that expectation is now wrong |

### Name-referenced custom event calls (BP-69) — batch 17

⚠ **Trust this suite least.** BP-69's own end-to-end test passed *against the bug* (trap #9) and was
only caught by reverting the fix.

⚠ **But this needs a hand-edited `.bp.json`, and it is lower *live* risk than its position suggests —
do it last, or skip it if time is short.** Verified 2026-08-08:

- **No shipped asset contains a `CallCustomEvent` node at all** — 0 across every `*.bp.json` in the
  repo. The eight `HillAssault2*` assets that carry a name-form `"EventId"` are **`PublishEvent`**
  nodes, a different kind on a different projection (`PublishEventPins`), so they do **not** exercise
  this path.
- **The editor never mints the name form.** `BlueprintCommandSink.ResolveCustomEventId:427-441`
  normalises a declared event to its GUID (`return known?.Id.ToString("D")`). So the name form is
  reachable only from a hand-authored or legacy file — which is exactly why BP-12b's rename path
  (`BlueprintDocumentFactory:673`) rewrites name-keyed refs.

⇒ **BP-69 is a load-path fix for hand-authored assets, not a live-UI regression risk.** Still worth
checking, because the fix hardened ~20 `ResolveDataPin` call sites that *everything* uses (see K3).

| # | Where | What to do | What should happen |
|---|---|---|---|
| K1 | **Setup required.** Author a custom event with 2+ parameters, place its Call node, save, then hand-edit the `.bp.json` to replace that node's `"EventId"` GUID with the event's **name** · reopen | look at the Call node | It shows **one data-in pin per event parameter**. Before BP-69 the node collapsed to exec-only and every argument wire was silently dropped on load |
| K2 | Wire the argument pins and compile | — | Clean compile, and the generated call passes the arguments. A `CS7036` (missing argument) means the pins projected but the compiler half didn't |
| K3 | Same node, leave an argument pin **unwired** · compile | — | Clean compile — the unwired pin becomes a typed `default(T)`. ⚠ A `CS0103` (undeclared `__t0`) is the exact failure the general `ResolveDataPin` hardening was added to prevent; report it verbatim |
| K4 | Rename the event in My Blueprint | look at the name-referenced Call node | Follows the rename (BP-12b rewrites name-keyed refs). A node still showing the old name would compile to a BP1403 |

### Function return values + graph signatures (BP-71, BP-72) — batch 16

| # | Where | What to do | What should happen |
|---|---|---|---|
| R1 | A Function graph (My Blueprint → Functions **+**) · Graph Signature → **Outputs +**, name it, pick a type | look at the **Return** node | It grows a **value pin on the LEFT**. Before BP-71 that pin was on the right and nothing could reach it |
| R2 | Drag any value output → the Return node's value pin | — | **The wire connects.** This is the gesture that was impossible; if it refuses, BP-71 regressed |
| R3 | Leave the value pin unwired and compile — ⚠ **the graph must contain at least one link**, else the unauthored-stub exemption skips the check | — | A **BP1655 error naming the graph and the pin** — not a Roslyn CS0103, and not a silently-defaulted return. **A graph with `Links.Count == 0` is exempt by design**, so an all-unwired stub (e.g. `SquadState` as shipped) correctly reports *nothing* |
| R4 | Click the Return node's value pin box without wiring anything | type a number | An **inline default editor** — a function can return a constant with no Literal node. Free consequence of the pin being an input |
| ~~R5~~ | ~~Graph Signature → add a second Output~~ | ~~compile~~ | ❌ **RETIRED — do not check this row.** BP1656 no longer exists; BP-73 removed the gate, the `DiagnosticCodes` entry is marked `[retired]`, and the authoring warning became an informational note. **Superseded by T7**, which asserts the opposite outcome |
| R6 | Open a multi-graph asset · switch the canvas between graphs | watch **Graph Signature** | Its picker **follows the canvas**. Before BP-72 it sat on the first Function graph, so you edited a signature you were not looking at |
| R7 | Pick a different graph in the Graph Signature combo, then switch the canvas | — | Your explicit pick **sticks** until the canvas actually moves — the combo must not fight you every frame |
| R8 | Create a custom event with a parameter · select its **body graph** in Graph Signature | add a parameter | The section is titled **Parameters**, the row reads `Name (event)`, and **Outputs shows "n/a"** (a custom event returns nothing). The new parameter appears on the Call node's pins, and compiling does **not** raise BP1408 — the declaration was mirrored |

### Graph create + switching (BP-24)

| # | Where | What to do | What should happen |
|---|---|---|---|
| G1 | My Blueprint, any multi-graph asset (e.g. the CustomEventSubscriberDemo) | **double-click** a graph row | The canvas shows that graph. Double-click back — pan/zoom and selection of each graph are **remembered** |
| G2 | My Blueprint header **+ ▼ → + Function** (or the Functions section **+**) | type a name | New Function graph appears under **Functions** (a new section split: Function graphs there, Event bodies under Graphs) and **the canvas opens on it**, showing one entry node |
| G3 | **Custom Events +** → create an event with a parameter | — | The event AND its body graph are created; **the canvas lands on the body**. One Ctrl+Z removes both |
| G4 | Add a node on graph A, switch to graph B, **Ctrl+Z** | — | The canvas **switches back to A by itself** and the node is gone — undo follows the edit, Unreal-style |
| G5 | Copy a node on A, switch to B, **Ctrl+V** | — | The paste lands on **B** (the graph you are looking at) |
| G6 | Set a bookmark (Ctrl+Shift+1) on A, switch to B, press **Ctrl+1** | — | The canvas jumps back to A, centred on the bookmark — cross-graph bookmarks are live |
| G7 | Open an asset, switch graphs, close and reopen it | — | It reopens on the **last graph you were viewing** (same session) |
| G8 | Rename a custom event (My Blueprint → Rename) that has a Call node, then Ctrl+Z | — | Name, body-graph name and the call node all revert **together** (the BP-12b desync fix) |

### Canvas clipboard (BP-23a) — the big one

| # | Where | What to do | What should happen |
|---|---|---|---|
| C1 | Select a node, **Ctrl+C**, then **Ctrl+V** | — | A copy appears, **offset** from the original and **selected**, so you can drag it straight away |
| C2 | Select two wired nodes, copy, right-click empty canvas → **Paste** | — | Both land with the **wire between them intact**, top-left corner at the cursor |
| C3 | Select one end of a wired pair, copy, paste | — | Just that node — a half-selected wire is **not** copied |
| C4 | **Ctrl+X** then **Ctrl+V** | — | Cut removes, paste restores. Ctrl+Z after either reverses it in **one** press |
| C5 | Copy node A, select node B, **Ctrl+D** | — | B is duplicated and **the clipboard still holds A** |
| C6 | Configure a `Compare` node (operator `>`), copy, paste | — | The copy keeps `>`. *(This is the audit's trap: a paste built on `AddNode` would have silently reset it)* |

### Promote to Variable (BP-60)

| # | Where | What to do | What should happen |
|---|---|---|---|
| P1 | Right-click a node's **data input** pin → Promote to Variable | type a name | A **Get** node to the **left**, wired in; the variable appears in My Blueprint with the pin's type |
| P2 | Right-click a **data output** pin → Promote | — | A **Set** node to the **right**, fed by that pin |
| P3 | Ctrl+Z after either | — | **One** press reverses all of it — node, wire and variable. Ctrl+Y restores |
| P4 | Promote twice with the same name | — | The second becomes `Name1` rather than failing |

### My Blueprint item CRUD (BP-12b)

| # | Where | What to do | What should happen |
|---|---|---|---|
| M1 | Right-click a variable → **Rename** / **Duplicate** / **Delete** | — | All three work. They were live-looking and inert before |
| M2 | Rename a **custom event** that has a Call node placed | — | The node's header follows the new name; Ctrl+Z restores |
| M3 | Delete a variable that a Get node uses | — | The declaration goes; **the node stays** (dangling, recoverable) rather than vanishing |

### Node header (BP-17, BP-18)

| # | Where | What to do | What should happen |
|---|---|---|---|
| N1 | Right-click a node → **Rename…** | type a title | The header shows it, and the **generated title moves to the subtitle** — you can still tell what the node is |
| N2 | Rename again, clear the field | — | The generated title comes back |
| N3 | Right-click → **Collapse Node** | then Expand | The body folds to the header and back. Ctrl+Z reverses each |

### Alignment (BP-13)

| # | Where | What to do | What should happen |
|---|---|---|---|
| A1 | Select 2+ nodes → right-click → **Align** ▸ Left / Right / Top / Bottom | — | Right and Bottom use the node's **far edge**, so different-width nodes line up properly |
| A2 | Select 3+ → **Align ▸ Distribute Horizontally** | press twice | Even gaps; the **second press changes nothing** |
| A3 | Select a wired chain → **Align ▸ Straighten Connection** | — | They snap onto the **first selected node's** row; wires run flat |
| A4 | Align an already-aligned pair, then Ctrl+Z | — | Undo reverses your *previous* edit — an alignment that moves nothing records nothing |

### Navigation (BP-19, BP-20)

| # | Where | What to do | What should happen |
|---|---|---|---|
| V1 | Right-click empty canvas → **Show Minimap** | — | A corner overlay: nodes as blocks, the current view as an outline |
| V2 | Click / drag inside the minimap | — | The canvas recentres and follows the drag. Pan far off-graph → the view outline stays **inside** the overlay |
| V3 | On a graph with a red node, press **F8** | Shift+F8 | Selects and centres each problem node, wrapping. **Errors before warnings** |

### Custom events (batch 8, if not already checked)

| # | Where | What to do | What should happen |
|---|---|---|---|
| E1 | **My Blueprint** → drag a custom event onto the canvas | look at Details | A real **Call Custom Event** node, bound, with a pin per parameter. Header shows the **name**, not a GUID *(BP-68)* |
| E2 | "Custom Events" **+** | — | Create modal: name + typed parameter rows; bad names refused *before* Confirm |
| E3 | The panel's section list | — | **No "Event Dispatchers" section** — intentional, the concept is superseded |

> ⚠ Compiling a `CallCustomEvent` still needs an `Event` graph of the same name, which the editor
> cannot create yet (**BP-24**). That fails as **BP1407** naming the graph to add, not as a Roslyn
> error. The create modal says so before you confirm.

---

## Traps that cost real time — read before touching these areas

### 1. 🔁 The inert-default guard (**three instances, one still open**)
*An optional ctor dependency defaults to an inert value; tests pass it explicitly and prove the
logic; every production site omits it, so the feature is silently dead.*

| Where | Effect | Status |
|---|---|---|
| `PredicateCompiler.blueprintRegistry` (**BP-29**) | conditional breakpoints never fired | Fixed |
| `HsmValidator.isStatefulSubtree` / `sharedScopeKeys` (**BP-61**) | both HSM concurrency rules never fire | **Open** |
| `DebugProbe.NewTick`'s `Sink as IBlueprintDebugSession` (**BP-35**) | would have silently dropped ticks behind the multiplexer | Fixed |

> **A green suite is not evidence a guard is wired. Grep the production construction sites.**

### 2. Absence claims need **both** trees
Four "nothing exists" findings were overturned because a search covered `Hrot/` but not `FDP/`.
Always search both.

### 3. Assembly load order (**BP-62** — fixed, but the shape recurs)
`AppDomain.CurrentDomain.GetAssemblies()` returns only *already-loaded* assemblies and the CLR loads
lazily; a `ProjectReference` does **not** force a load. Use
`EditorTypeResolutionScope.Assemblies()` for any type scan in the editor. Never cache the assembly
array — hot reload adds ALCs at runtime.

### 4. 🆕 Sinks apply; the undo stack records (**BP-11**) — and must adopt the caller's ids (**BP-65**)
`IGraphCommandSink.Apply` must **never** push an undo entry. `UndoStack.ApplyAndRecord` applies the
forward *then* pushes, so a sink that also records lands an inner entry first — and on undo the
inverse re-enters the same sink method and pushes a third. The caller snapshots the prior state and
issues the pair through `GraphView.Execute`. This was invisible for as long as the sink's stack
(`CommandHistory`) was dead; it is a live trap now.

> `GraphCommand` is a plain `public abstract record`, so a **host** assembly can extend the command
> vocabulary — `BlueprintEditCommand(Label, Mutate)` does — without editing the vendored NodeEdit
> tree. Any new variant needs a sink case in the same change (see the `default:` trap below).

**A sink must also adopt the ids the caller assigned.** `CommandBuilder` mints an id, puts it in the
forward command and names it in the paired inverse; a sink that mints its own instead produces
inverses that match nothing. That was BP-65 — node placement was non-undoable for the life of the
feature, while the BTree and HSM sinks had it right all along. **When adding a sink case for an
`Add*` command, check what the inverse references.**

### 5. 🆕 `default:`-returns-success — **four instances now**
`BlueprintCommandSink.Apply`'s `default:` arm returns `new GraphCommandResult(true, null)` for any
command it has no case for. A feature can therefore be fully built, fully wired, and silently do
nothing while reporting success.

| Command | Symptom | Status |
|---|---|---|
| `PromoteToVariable` (**BP-60**) | modal opened, name typed, nothing happened | Fixed |
| dynamic kinds (**BP-68**) | dragged custom event became an unbound `FunctionCallNode` | Fixed |
| `SetNodeCollapsed` (**BP-18**) | collapse silently ignored | Fixed |

> **A test asserting `Success` proves nothing here — that is the bug.** Assert the effect. Before
> assuming any `GraphCommand` works, grep `BlueprintCommandSink.Apply` for a `case`.

### 6. 🆕 Asset-scoped features belong at the host, not in a sink case
`GraphCommand.PromoteToVariable` and a paste both look like "add a sink case". Both are wrong: the
single opaque command allocates ids *inside* the sink, so no caller can write the inverse — and
`ApplyInitialProperties` only knows 8 node kinds of 50, so a paste routed through `AddNode` silently
strips the other 42. Compose the gesture at the host from primitives the sink already implements
(`BP-60`, `BP-23a`): the caller owns every id, BP-11's invariant holds, and it is one undo entry.

### 7. 🆕 A full-width `Selectable` eats every click in its row
An `ImGui.Selectable` with no explicit size spans the whole remaining row, so a button drawn after it
with `SameLine` is *drawn and correctly positioned but unreachable*. Size the Selectable to stop
short of the trailing controls — more predictable than `AllowItemOverlap`, which depends on draw
order and on remembering `SetItemAllowOverlap`. (BP-03's delete button; caught only by the visual
pass, since no headless test can see it.)

### 8. Conventions the code enforces that are easy to miss
- **Decision-asset ids are NOT parseable GUIDs.** Shipped `CombatPostureDecision` uses
  `3c6f9e42-5d10-6f3a-ac23-posture0000001`. A `Guid.TryParse` check rejects real assets.
- **Custom events resolve by *Name* as well as GUID** (`Stage5.FindCustomEventIndex`).
- **Every new `BPxxxx` diagnostic code needs a `[CoversDiagnosticCode]` test** —
  `V_AllValidatorsCoverageTests` fails the build otherwise.
- **`BlueprintCommandSink.Apply`'s `default:` silently returns success** for unknown commands, so an
  unhandled `GraphCommand` no-ops *and reports success* (this is BP-60). A test asserting `Success`
  therefore proves nothing — assert the *effect*.
- Palette baking is safe: `CreateAssetNode` builds via `CreateInstance` then only *overlays* caller
  props — it does **not** route through `ApplyInitialProperties`' 8-of-50 whitelist.
- **Blueprint assets live under `Assets/Blueprints`** (`AssetRoots.AssetsRelative`), resolved against
  the project dir when found. Never hand-build a blueprint path — BP-66 was a lone
  `{BaseDirectory}/blueprints` that silently made a whole feature inert.
- **A "graceful fallback" hides a wiring bug.** `CallPeerBlueprintPins` falls back to untyped pins
  when the peer lookup finds nothing — indistinguishable from no lookup at all, which is exactly how
  BP-66 survived. When a path degrades silently by design, test the *populated* case.

### 9. 🆕 Two halves of a contract, each tested alone, never together (**BP-71**)
*Both sides of an interface can be fully implemented, individually correct, individually
**test-locked** — and still unusable together, because no test ever crosses the seam.*

The `Return` node's value pin is `Direction=="Out"` in the editor projection **and** in the compiler
rehydrator, and `Stage5.BuildReturnTerminator` reads it as an *input*. Two tests assert the `"Out"`
contract in prose. The canvas rejects same-direction links, so the pin can never be wired — a
Function graph cannot return a value. **No test placed a Return node in a Function graph with an
output and then tried to author the link**, and no shipped asset does it either (of 92 `.bp.json`,
zero wire a function return).

> **A convention asserted at both ends is not the same as a path exercised end-to-end.** When a
> feature spans editor *and* compiler, ask what the *designer's gesture* is and whether any test
> performs it. Absence of a failing test is the expected state for a seam nobody crosses.

---

## Test baseline — what "green" means

**Full appendix:** [Blueprint_Issues_Detail.md § Test baseline](Blueprint_Issues_Detail.md#appendix--test-baseline-what-green-means-in-this-repo).

- `Hrot.Blueprints.Tests` is now **serialized** (`xunit.runner.json` + the `<Content Include>` line —
  without the latter the config never reaches the output dir and does nothing). It was the only
  suite of 10 running parallel. Before: 1 varying failure *every* run. After: **2657 / 0**.
- ⚠ **`Blueprint_Component_Access_RESUME.md`'s "~8–9 reds, DO NOT chase" is STALE** — banner-marked
  at the source. Expect 0 failures; investigate any.
- ⚠ **The known-flake list below is INCOMPLETE — see [BP-111](Blueprint_Issues_Detail.md#bp-111).**
  `WhenNodePerfTests.WhenNode_EqsResult_Under150ns_perTick` also flakes under full-suite load (passes
  5/5 in isolation) and is **not** listed, so it reads as a regression. Worse, the gate command uses
  `-v q`, which prints counts but **not the failing test's name** — re-run with
  `--logger "console;verbosity=normal"` to identify it. This cost time twice in Batch 22.
- Residual: `PdbEmbeddedSourceTests` pair flaked once in ~6 runs (real Roslyn+PDB emission,
  resource-sensitive). Not yet chased. **`WhenNodePerfTests.WhenNode_ValueChanged_Under100ns_perTick`
  joins it** — a wall-clock ns/tick benchmark; it reds under load and passes alone. Re-run the single
  filter before treating either as a regression.
- **Batch 16 baseline (2026-08-06, all eight gates run on Linux/cloud):** blueprints
  **2845 total / 2835 passed / 10 skipped** · NodeEdit core **208** + UI **131** · AiShared **1204** ·
  BTree editor **612** · breakpoints **130** · generators **189** · solution build **0 errors /
  58 pre-existing warnings**. (Batch 14 was 2788/2778; +41 from BP-24, +23 from Batch 16.)
  Both known flakes passed in this run — do not treat a green one as proof they are fixed.
- ✅ **`Hrot.Editor.AiShared.Tests` is now in the gate list** (1204/0). Its 2 Windows-only reds were
  BP-64, fixed. It was missing from the list, which is why they went unnoticed.
- **To classify a failure:** `git stash` → re-run the same filter → `git stash pop`. If it fails
  identically without your changes, it predates you. This is how BP-64 was classified.

```bash
# gates used throughout (all headless)
dotnet build IOS-IG-SimHost.sln -v q --nologo
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj -v q --nologo
dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/Hrot.Diagnostics.Breakpoints.Tests.csproj -v q --nologo
dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/NodeEditor.Core.Tests.csproj -v q --nologo
dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.UI.Tests/NodeEditor.UI.Tests.csproj -v q --nologo
dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj -v q --nologo
dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Hrot.BTree.Editor.Tests.csproj -v q --nologo
dotnet test Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/Hrot.AiEditor.Generators.Tests.csproj -v q --nologo
```

---

## Working agreement (from the user, this programme)

- **Verify claims against code; do not trust the audit doc or the architect blindly.** **Nine** audit
  claims and two architect statements were wrong and were corrected in-repo. Every correction is
  recorded in the detail file rather than silently applied. Note the failure mode is not only "claim
  is false" — BP-41's claim was true but named the *wrong risk*, so building it as written would have
  re-proved something already covered.
- **Fix, don't disable.** Flaky/failing tests get root-caused; skipping is a permanent silent
  coverage hole.
- **Non-trivial designs get an architect round** (`Architect_Question_N_*.md`) — the user relays to
  NotebookLM; Claude cannot reach it. Trivial mirror-pattern work proceeds directly. ⚠ An approval is
  not a verification: Q22's approved D2 was the one step that could not work (see BP-11 gap 4). Check
  the approved plan against the code before building it.
- **Record findings in the detail doc**, not only in commit messages.
- Ask in plain prose; **never** the multiple-choice widget.

### 🎛️ Coordinator / implementation split (from 2026-08-08)

The user has split the roles. **A coordinator session owns the plan; implementation sessions own the
code.**

| Role | Owns |
|---|---|
| **Coordinator** (this session) | the tracker · writing handoffs · reviewing the returned **diff** (not the summary) · re-running gates · deciding the next batch |
| **Implementation session** | building exactly what the handoff scopes, and reporting back honestly |

**⚡ Model-delegation rule — every handoff must restate this, with a per-item split.**

The implementation session runs on **Opus** and is expected to **delegate to Sonnet sub-agents**
anything that does not need Opus-level reasoning. Tokens are the constraint; this is not optional.

| Give to **Sonnet** | Keep on **Opus** |
|---|---|
| Mirror-an-existing-pattern slices (a new drawer copying an existing drawer) | Novel scheduler / IR / compiler work |
| Mechanical edits across many call sites | Anything where the *design* is still open |
| Broad searches, inventory sweeps | The final **diff review** and the **gate runs** |
| Test scaffolding from a stated contract | Deciding whether a test actually proves the fix |

⚠ **State the split per item in the handoff**, not as a general aspiration — "use Sonnet where
possible" gets ignored; *"BP-89's drawer is a Sonnet slice, the pin-projection parity check is Opus"*
does not.

⚠⚠ **Subagents share ONE working tree** — a constraint this rule itself created, reported by the
Batch-20/21 session as the trap that cost it the most time. Two failure modes, both real:
a **concurrent `dotnet build` corrupts state** (a phantom "missing method" that vanished on retry),
and an agent doing **revert-and-watch-it-go-red leaves the tree intentionally broken for minutes** —
which nearly produced six phantom regressions read off a stale test DLL.
⇒ **Run agents strictly sequentially**, wait for `ps aux | grep -c "[d]otnet build\|[d]otnet test"`
to reach `0` *and* the expected files to be present, and **gate every commit on the fix actually being
in the tree** (`git status` shows the source modified) — never on an agent reporting success.

⚠ **Delegation never transfers the verification duty.** Opus re-runs the gates and applies the
revert-and-watch-it-go-red discipline on Sonnet's work exactly as on its own. Trap #9 exists because a
test passed against the bug.

## Key code reference points (as of batch 14)

| Concern | File |
|---|---|
| Canvas undo / context menus | `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Canvas/CanvasRenderer.cs` |
| Undoable delete (the correct path) | `.../NodeEditor.UI/Action/EditCommands.cs` |
| Command application + bakes | `Hrot/.../Hrot.Blueprints.Editor/Host/BlueprintCommandSink.cs` |
| Stage2 validators (BP-15/BP-16 live here) | `Hrot/.../Hrot.Blueprints.Compiler/Compiler/Stages/Stage2_Validate.cs` |
| Palette entries | `Hrot/.../Hrot.Blueprints.Editor/NodeDrawers/BlueprintNodePaletteEntries.cs` |
| Type resolution scope (BP-62) | `Hrot/.../Hrot.Blueprints.Editor/NodeDrawers/EditorTypeResolutionScope.cs` |
| Probe fan-out (BP-35) | `Hrot/.../Hrot.Blueprints.Core/MultiplexingProbeSink.cs` |
| Undo transport (BP-11) | `Hrot/.../Hrot.Blueprints.Editor/Host/BlueprintEditCommand.cs` · `NodeDrawers/EditService.cs` (`RecordUndoable`) · wired in `Host/BlueprintDocumentFactory.cs` |
| New Details-panel drawers (BP-05…BP-08) | `Hrot/.../Hrot.Blueprints.Editor/NodeDrawers/{ReadRankedResult,WaitForChannel,CallCustomEvent,CallPeerBlueprint}NodeDrawer.cs` · registered in `BlueprintEditorBootstrap.cs` |
| Bookmarks panel (BP-03) | `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Bookmarks/BookmarksPanel.cs` + `Core/Bookmarks/BookmarkStore.cs` |
| Canvas clipboard (BP-23a) | `Hrot/.../Hrot.Blueprints.Editor/Host/BlueprintClipboard.cs` · commands in `Host/BlueprintDocumentFactory.cs` (`RegisterClipboardCommands`) |
| Promote to Variable (BP-60) | `Host/BlueprintDocumentFactory.cs` (`RegisterPromoteToVariableCommand`) · call site `CanvasRenderer.DrawPromoteVariableModal` |
| My Blueprint item CRUD (BP-12b) | `Host/BlueprintDocumentFactory.cs` (`RegisterMyBlueprintItemCommands`, `RenameItem`/`DeleteItem`/`DuplicateItem`) · `Windows/ItemRenameModal.cs` |
| Custom-event authoring (BP-12c) | `Windows/CustomEventCreateModal.cs` · `Host/BlueprintDocumentFactory.CreateCustomEvent` · validator `Stage2_Validate.V_CustomEventHandlers` |
| Align / distribute (BP-13) | `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Action/AlignCommands.cs` |
| Minimap + jump-to-issue (BP-19/20) | `.../NodeEditor.UI/Canvas/MinimapRenderer.cs` · `.../NodeEditor.UI/Action/ErrorNavigationCommands.cs` |
| Node title / collapse (BP-17/18) | `Assets/GraphTypes.cs` (`NodeMetadata.CustomTitle`, `.Collapsed`) · `Host/BlueprintNodeModel.cs` · sink cases in `Host/BlueprintCommandSink.cs` |
| AiPrimitive composition rail (BP-41/BP-30) | `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/Emit/BTreeBridgeEmitCore.cs` (slot keys + manifest) · `FDP/Toolkits/.../Behavior/Systems/BehaviorIngressSystem.cs` (provisioning) |
