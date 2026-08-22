<!--STATUS
state: LIVE
updated: 2026-08-22
current-answer: §1's per-item ledger (all five items BUILT) + §8's gate table. This was a LIVING
  report, updated after every item; it is now CLOSED.
stale-below: nothing.
known-rot: none.
known-conflict: none.
blocked: BP-416 — L6.3's "delete the HashSet" half needs UXI-11 (§6.2).
design-basis: DESIGN_Details_Panel_View_Switching.md §6 L6 (re-staged 2026-08-22) · §5 · §2's
  classDiagram · §6's L6 sequenceDiagram · HANDOFF_L6_Entity_Views_On_Scenario.md §1/§2/§2b.
-->
# ⭐⭐⭐ REPORT — **`L6`: the entity views on Scenario** *(LIVING — updated per item)*

> **Design:** 📄 [`DESIGN_Details_Panel_View_Switching.md`](../DESIGN_Details_Panel_View_Switching.md)
> §6 `L6` · §5 · §2 · the `L6` sequence
> **Handoff:** 📄 [`HANDOFF_L6_Entity_Views_On_Scenario.md`](HANDOFF_L6_Entity_Views_On_Scenario.md)
> **dispatched at** `16be5f2c2` · **started at** `f968e693` *(marker `f8e694d0`)* ·
> **branch** `claude/hrot-implementation-j1jvin`
> ⭐ Run **autonomously** per §2b — no coordinator round-trip between items.

## §1 — PER-ITEM LEDGER

| # | item | state | note |
|---|---|---|---|
| **0** | ⭐ **stage-gate BASELINE** *(not in §1 — mine)* | ✅ **green** | measured BEFORE any refactor; see §2 |
| **1** | `L6.1a` — extract `PerspectiveWorkspace` | ✅ **built** · ⛔⛔ **STAGE GATE: PASSED** | §3 |
| **2** | `L6.1c` — Scenario gets a host | ✅ **built** *(4/4)* | §4 |
| **3** | `L6.5` — the predicate helper | ✅ **built** *(6/6)* | §5 |
| **4** | `L6.3` — Components view | ✅ **built** *(7/7)* · ⚠ **one half BLOCKED** | §6 |
| **5** | `L6.4` — Mission plan view | ✅ **built** *(8/8)* | §7 |
| — | ⭐ **close-out** — gates ONCE · probes · tracker · design fold-back · rule-4 merge | ✅ **done** | §8 · §9 |

⇒ ⭐⭐ **All five §1 items BUILT.** ⛔ One HALF of item 4 blocked and recorded *(`R-106`)* — see below.

⭐ **IDs allocated:** **`BP-412`** … **`BP-419`** — the table is in §8.

### ⛔ `R-106` verdicts — **the blocked half, stated where it cannot be missed**

| what | verdict | why |
|---|---|---|
| ⛔⛔ **`L6.3`'s *"DELETE the `HashSet`"* half** | 🛑 **BLOCKED — see §6.2** | it is an INTERACTION MODEL, not a cache; deleting it needs `UXI-11`'s World-write migration, explicitly out of scope |
| ⭐ everything else in `L6.3` | ✅ **done** | the adapter renders the WORLD's entity without touching the `HashSet` |

---

## 2. ⭐⭐⭐ THE BASELINE — **captured BEFORE `L6.1a`, and my guess was WRONG on all three rows**

⚠ The handoff's stage gate is *"the three AI perspectives still host their SAME offer set"*. ⛔ A
baseline written **after** a refactor records whatever the refactor did — so the rail
*(`TheAiOfferSetsAreUnchangedTests`)* was written and made **green first**, against the untouched tree.

📐 **Measured** — and I had predicted `details.variables` first plus a `details.runtime.*` per AI host:

| perspective | registered ids, in order |
|---|---|
| **BTree** | `details.blackboard` · `details.variables` |
| **HSM** | `details.blackboard` · `details.variables` |
| **Blueprint** | `details.blackboard` |

⭐⭐ **Two things the measurement said, and both are stated in the rail rather than smoothed over:**

| ⚠ | |
|---|---|
| **no `details.runtime.*` appears** | 📐 `RuntimeInspectorWindow.RegisterPane` adds those, and this composition registers no panes *(the BTree/HSM editor registrars supply them, from other assemblies)*. ⇒ ⛔ **the gate does not cover the runtime descriptors** — it covers what this composition builds, before and after, which is what an extraction can break. **Stated as a limit, not claimed as coverage.** |
| **Blueprint has no `details.variables`** | ⭐ Consistent: that descriptor is contributed by the generic `DetailsWindow`, and `Details` is deliberately `null` on Blueprint *(it has `BlueprintDetailsWindow`)*. A separate rail pins that presence table, because the id lists alone would not see a "helpful" extra panel. |

---

## 3. ⭐⭐ `L6.1a` — **the extraction, and the STAGE GATE**

📄 §5: *"`PerspectiveWorkspaceRegistrar` fuses two things — a fully generic **wiring hub** and a
**21-parameter AI-authoring service bag**… the generic half is trapped inside the specific one, which is
why Scenario got a bespoke scenario branch."*

🛠 **`Shell/PerspectiveWorkspace.cs`** — §5's four generic things, and only those:
`DetailsViews` *(the catalogue)* · `BuildContext()` *(the builder)* · `EntitySelection` *(`L0.4`'s
World source)* · `Contribute()` *(the `IDetailsViewSource` claim chain)*. ⭐ It also carries `W4`'s
`StagedWrites` — not used by it, but the generic half travels together.

⭐⭐ **The registrar keeps its bag and FORWARDS.** `DetailsViews`, `EntitySelection`, `StagedWrites` and
`ContributeDetailsViews` are now one-line forwards, so **every existing caller is unchanged** — which is
what makes this a pure refactor rather than a rewrite with a refactor's name.

### ⭐ Obligation ③ — what I built vs the design

| §2 `classDiagram` box | built | match |
|---|---|---|
| `PerspectiveWorkspace` with `BuildContext()` | ✅ | ⭐ **exact** |
| `PerspectiveWorkspace o-- DetailsViewRegistry` | ✅ | ⭐ **exact** |
| `PerspectiveWorkspace ..> World : reads entity selection` | ✅ *(through `IEntitySelectionSource`)* | ⭐ **exact** |

⭐⭐ **It CLOSES a stated deviation rather than adding one.** `L2.1` had put the `LiveContextSource`
lambda in the registrar with a comment: *"§2 draws this on `PerspectiveWorkspace.BuildContext()`, which
`L6.1` extracts… until then the wiring hub is the registrar itself, so the lambda lives here and `L6.1`
moves one method."* ⇒ ⭐ **this is that move; the body is unchanged.**

### ⚠ One small deviation, stated

`PerspectiveWorkspace` takes `Func<VariableRunState>` and the registrar passes `() => _runState!()` —
the field is assigned two lines below the construction and the compiler's flow analysis cannot see that
the lambda only runs later. ⛔ Constructing after the assignment would read better but places it inside
the run-state comment block, where a future edit would naturally insert a host. ⭐ The bang is the
smaller risk, and it is commented at the site.

### ⛔⛔ THE STAGE GATE — **PASSED**

| | |
|---|---|
| **the three offer sets** | ⭐ **unchanged**, ordered-equal *(7/7 rails green)* |
| **`Details` presence per perspective** | ⭐ unchanged *(BTree ✅ · HSM ✅ · Blueprint ⛔, as designed)* |
| **`Hrot.Editor.AiShared.Tests`** | ⭐ **1853 / 0 / 0** *(baseline 1846 + the 7 new gate rails)* |
| **solution build** | ⭐ 0 errors |

⇒ ⭐ **Per §2b: unchanged ⇒ proceed autonomously to item 2.** No check-in taken.

---

## 4. ⭐⭐ `L6.1c` — **the Scenario perspective gets a Details host**

📄 §6 `L6` as-built (b): *"the Scenario perspective has NO `PerspectiveWorkspaceRegistrar`, no
`DetailsWindow`, no registry — it uses a bespoke `RegisterPane`."* ⇒ ⭐ standing one up IS `L6`'s real
work, and it is cheap only because `L6.1a` split the generic half out of the 21-parameter AI bag.

🛠 **`EditorSubsystem`** builds a `PerspectiveWorkspace("Editor", …)` from **scenario** services — a
formatter, the two hoisted clock signals, and `WorldEntitySelectionSource` — plus a `DetailsWindow`
(`scenario_details`) that contributes its own variables view through the claim chain.

| ⭐ measured, and it is why there are two test helpers | |
|---|---|
| ⛔ **`Initialize(headless)` builds the WORLD; `RegisterWindows(WindowManager)` builds the PERSPECTIVE WIRING** | ⚠ neither does the other's job. A rail calling only `Initialize` asserts a `null` workspace and looks like a wiring defect rather than a test defect |
| ⭐ **the persisted key stays `"Editor"`** | `L6.1b` is deferred; a rail pins the deferral so a "tidy-up" rename reddens here rather than in a user's lost layout |

⭐ **Gate:** `TheScenarioPerspectiveHasADetailsHostTests` — **4/4**, including the item's own row
*(a `SelectionState` entity in the real World yields a non-empty `ctx.Entities` on Scenario, and
nothing selected yields EMPTY)*.

---

## 5. ⭐ `L6.5` — **the entity predicates**

🛠 **`DetailsViewPredicates.ExactlyOneEntity`** + **`OneEntityWithBrain(Func<Entity,bool>?)`**, added to
the existing helper class rather than a new one *(`R-13` — this class already owns the "exactly one"
rule for sub-selections, and two classes would disagree about what "one" means)*.

| ⚠ as-built (c), confirmed | |
|---|---|
| ⛔ **there is no `HasBrain` in this codebase** | the signal is `GetAvailableBehaviors`/`GetMissionSnapshot` coming back empty ⇒ ⭐ the predicate takes a **delegate**, because `IMissionEditorService` lives ABOVE `Hrot.Editor.AiShared` *(§3's reference wall)* |
| ⭐ **`null` signal ⇒ never offers** | ⛔ not a silent default: a host with no mission service must not claim an entity has behaviours. 📌 The `2026-08-16` rule's own qualifier — *a default is only a defect when the caller could have done better* |

⭐ **Gate:** `TheEntityViewPredicatesTests` — **6/6**, over contexts built by the **production**
`DetailsContextBuilder` through a real `IEntitySelectionSource`, ⛔ not hand-set properties.
⭐ Includes the `R-78`-shaped rail: **the signal is asked about the SELECTED entity**, not merely asked.

---

## 6. ⭐⭐⭐ `L6.3` — **the Components view**

### 6.1 ⭐ What was built

🛠 **`Hrot/Subsystems/Hrot.Editor/Scenario/ScenarioComponentsView.cs`** — the adapter + its descriptor,
in the **composition root**, because §3's reference wall leaves it the only assembly that sees both
`Fdp.Presentation`'s `EntityInspectorPanel` and `AiShared`'s `IDetailsViewInstance`.

🛠 **`EntityInspectorPanel.DrawComponentsFor(session, entity)`** — the renderer half of
`DrawEntityDetails`, **extracted, not copied** *(`R-13`)*. ⭐ `DrawEntityDetails` keeps the arbitration
*(multi ⇒ refuse · none ⇒ prompt · one ⇒ render)* and routes into it.

⛔⛔ **Why the extraction is load-bearing rather than tidiness.** 📐 Measured:
`DrawEntityDetails:530` resolves `selCount == 1 ? _selectedEntities.First() : context.SelectedEntity`.
⇒ ⚠ **a Details view routed through THAT would render whatever the Entity Inspector window's list was
last clicked on**, silently ignoring the World's selection. ⭐ `DrawComponentsFor` takes the entity, so
the caller's decision is the one that lands.

⭐ **It BORROWS the editor's one wired panel** — the reflector, the two buffer-view providers, the
serializer, the mutation interceptor and the edit-context factory that `EditorSubsystem` wires over
~60 lines. ⛔ A fresh panel per instance would render components with none of that — 📌 the
`2026-08-16` silent-default shape, *the caller HELD the value and did not pass it*.
⚠ **Stated limit:** two simultaneous Components views would share the panel's component-search filter.
Today Scenario hosts one Details window, so it cannot occur.

### 6.2 ⛔⛔ **BLOCKED half — *"DELETE the `HashSet`, re-point the multi-select tests at the World"***

📐 **Measured, `2026-08-22`** — `EntityInspectorPanel._selectedEntities` (line 32), ~15 use sites:

| site | what it does |
|---|---|
| **`HandleRowClick(viewList, clickedIndex, ctrl, shift)`** *(:409)* | ⭐⭐ **WRITES it in all three arms** — shift-range, ctrl-toggle, plain-click |
| **`Select All`** *(:~176)* | **WRITES it** — clears, then adds every filtered entity |
| `DrawEntityList` / `DrawEntityDetails` / `Copy JSON (N items)` | read it |

⇒ ⭐⭐⭐ **It is a read-WRITE interaction model, not a cache of someone else's selection.** ⛔ Deleting
it deletes multi-select interaction outright unless the clicks instead WRITE `SelectionState` back to
the World — and that is 📄 `UX_Feature_Selection.md`'s `ISelectionState` → `EcsSelectionState`
migration (**`UXI-11`**), which `L0.4` explicitly placed **out of scope**.

⭐ **`R-106`: this stops the HALF, not the item.** 📐 Line 530 also proves the adapter does not need
the deletion — feeding the entity directly renders the right one — so the view is complete and correct
today, and the `HashSet` stays as the Entity Inspector window's own interaction state.
⛔ **`EntityInspectorPanelMultiSelectTests` is therefore UNCHANGED** — re-pointing it at the World
would assert a model that does not exist yet.

### 6.3 ⭐ Gate — `TheScenarioComponentsViewTests`, **7/7**

⚠⚠ **The gate SPLIT, and the reason is a measurement, not a convenience.**
📐 `_fdpRepoAdapter` — the `IInspectableSession` the view renders through — is built at
`EditorSubsystem.cs:1579`, **inside the `if (!_headless)` at :1565**. ⇒ ⭐ a headless editor never has
one, so a headless offer set correctly never contains Components.

| half | how |
|---|---|
| ⭐⭐ **`R-67` — the REAL root registered it** | `TheScenarioCatalogue_OffersTheComponentsView`, over the production `EditorSubsystem` |
| ⭐⭐ **the PREDICATE** | over the **same descriptor factory the root calls**, with the one thing headless cannot supply *(the session)* stubbed |

⛔ **Neither half alone is the gate**, and the first version of these rails asserted the offer set
against a headless production editor and went red on an EMPTY set — ⚠ **that was the rail wrong, not
the wiring**, and it is now pinned deliberately by `WithNoSessionYet_ItDoesNotOffer`.

⭐ **Revert probe — 3 of 7 redden** *(un-applied by the inverse edit, never `git checkout --`)*:

| probe | reddens |
|---|---|
| `_draw(session, entity)` → `_draw(session, default)` | `ItRendersTheSelectedEntity_NotTheFirstEntityInTheWorld` |
| `Applies` drops `ExactlyOneEntity` | `SelectingOneEntity_…` · `TwoSelectedEntities_DoNotOfferComponents` |

⚠ `TheScenarioCatalogue_OffersTheComponentsView` stays green under both — ⭐ correct: it is about
REGISTRATION, not about the predicate.

### 6.4 ⚠ Carried red — **`Fdp.Presentation.Tests`, 3 pre-existing**

`EntityInspectorPanelTests.GetFilteredEntities_{FiltersById,RespectsLimit,InvalidSearch_ReturnsAllWithLimit}`.
📐 My whole diff to that file is lines **514–555** *(`DrawEntityDetails`/`DrawComponentsFor`)*;
`GetFilteredEntities` is at **:244**, untouched. ⛔ Confirmed against the base commit in §3's gate table.

---

## 7. ⭐⭐ `L6.4` — **the Mission plan view**

🛠 **`Hrot/Subsystems/Hrot.Editor/Scenario/ScenarioMissionView.cs`**, offered on `L6.5`'s
`OneEntityWithBrain`; the brain signal the root supplies is
`_missionService.GetAvailableBehaviors(netId).Count > 0` *(as-built (c) — there is no `HasBrain`)*.

### ⛔⛔ It OWNS its panel where `L6.3` BORROWS one — **and that is measured, not stylistic**

| 📐 | |
|---|---|
| **① the root wires NOTHING into a `MissionPanel`** | ⇒ a fresh instance is fully equivalent, unlike the entity inspector's ~60 lines |
| **② `EditorSubsystem.Update:1810–1823` writes `_missionPanel.SelectedEntityId` EVERY FRAME from the LEGACY `DefaultSelectionState`** | ⛔ **not** the World's `SelectionState` that `ctx.Entities` reads *(`R-122`)* ⇒ ⚠ a shared panel would make the Details view and the Mission Editor **window** overwrite each other within one frame |

⇒ ⭐ Two panels, two selections, **no arbitration** — 📌 exactly what `L1.1`'s per-instance factory is
for. The two selection models converge under `UXI-11`, not here.

⚠⚠ **`SelectedEntityId` is an int NETWORK id, not an `Entity`** *(`MissionPanel.cs:103`)* — the root
supplies the translation in ONE place *(`R-13`)*, the same lookup `Update:1816` already does. ⛔ `0` is
the panel's own *"no selection"*, so an unreplicated or dead entity honestly reads as nothing selected
rather than as entity zero.

⭐ **Gate:** `TheScenarioMissionViewTests` — **8/8**, driving the **REAL** `MissionPanel` through the
**REAL** view. 📐 `DrawContent` is headless-safe by construction: it calls `GetAvailableBehaviors` and
both pollers **before** its `ImGui.GetCurrentContext()` guard, and its own doc comment says that is
deliberate *("so that tests can verify the call without a render context")*.

⭐ **Revert probe — 5 of 8 redden**: `_networkIdOf(...)` → `.Index` · the `Count: 1` guard →
`Count == 0` · `AppliesTo` → `_ => true`.

---

## 8. ⭐⭐⭐ THE GATE TABLE — **run ONCE, at the end** *(rule 8's contract)*

> **base** `f968e693` *(my branch point)* · **dispatch** `16be5f2c2` · **coordinator head at close**
> `a5770f0e` *(rule 4 pull — see §9)*

| # | gate | command | result | `--no-build`? | Δ vs base |
|---|---|---|---|---|---|
| **1** | solution build | `dotnet build IOS-IG-SimHost.sln --no-restore` | ⭐ **0 errors** | ⛔ builds | — |
| **2** | `Hrot.Editor.AiShared.Tests` | `dotnet test … --no-build` | ⭐ **1858 pass / 0 fail / 1 skip — 1859 total** | ✅ in solution | **+13** *(1846 → 1859: `L6.1a`'s 7 stage-gate rails + `L6.5`'s 6 predicate rails)* |
| **3** | `Hrot.Blueprints.Tests` | `dotnet test … --no-build` | ⭐ **3898 pass / 0 fail / 18 skip — 3916 total** | ✅ in solution | **+19** *(`L6.1c` 4 · `L6.3` 7 · `L6.4` 8)* |
| **4** | ⛔⛔ `Fdp.Presentation.Tests` | ⛔ **CANNOT run whole — `BP-419`** | 🛑 **test host CRASHES mid-run, at BASE TOO** | — | — |
| **4b** | ⭐ …by filter instead | `dotnet test … --filter "FullyQualifiedName~EntityInspector"` | ⚠ **27 / 3 / 30** | ✅ | ⭐ **IDENTICAL at base — 27 / 3 / 30** |
| **5** | `tracker-counts.py --check` | ⭐ **OK — open 90 / done 264 (+1 refuted)** | — | — | — |
| **6** | `rulings-check.py` | ⭐ **22/22 verified** | — | — | ⚠ 1 staleness WARN on `.claude/CLAUDE.md` *(pre-existing; the quote still matches)* |
| **7** | `design-digest.py --check` | ⭐ **PASS** — all 53 recently-changed designs carry STATUS; every buildable design carries a class + sequence diagram | — | — | — |
| **8** | `mermaid-check.mjs` on the design | ⭐ **all 8 blocks parse** *(incl. the two boxes I added to §2)* | — | — | — |
| **9** | goldens | ⭐ **NONE MOVED** — 📐 `git status` shows only `.cs`/`.md`; ⛔ no asset, no golden, no `.json` | — | — | zero-file diff shape |
| **10** | working tree clean after every suite | ⭐ **yes** — no test regenerated anything | — | — | — |
| **11** | quarantine / skips | ⭐ **1 skip in `AiShared`, unchanged** · ⛔ **no new skip** | — | — | 0 |

### ⛔⛔ §8b — **the one gate row that needs a paragraph, not a cell**

⚠⚠ **`Fdp.Presentation.Tests` aborts its own run and still prints a `Passed!` line for the cases that
completed.** 📐 Measured **both sides** — mine and a clean worktree at `f968e693`:
*"The active test run was aborted. Reason: Test host process crashed"* after ~18–20 cases. ⇒
⛔ **pre-existing**, and ⭐⭐ **the shape is the danger, not the crash**: a partial run reads as a green.
📌 Second member of `BP-378`'s family. ⇒ **filtered gating only, filter named in row 4b**, and the
delta there is **exactly zero**.

⭐ **The 3 reds are pre-existing and NAMED:** `GetFilteredEntities_{FiltersById, RespectsLimit,
InvalidSearch_ReturnsAllWithLimit}`. 📐 My entire diff to `EntityInspectorPanel.cs` is **lines
514–555**; `GetFilteredEntities` is at **:244**.

### ⭐ IDs allocated

| id | |
|---|---|
| **`BP-412`** ✅ | `L6.1a` — the extraction + the STAGE GATE |
| **`BP-413`** ✅ | `L6.1c` — Scenario gets a Details host |
| **`BP-414`** ✅ | `L6.5` — the entity predicates |
| **`BP-415`** ✅ | `L6.3` — the Components view + `DrawComponentsFor` |
| **`BP-416`** 🛑 | **BLOCKED** — the `HashSet` deletion is `UXI-11`'s |
| **`BP-417`** ✅ | `L6.4` — the Mission plan view |
| **`BP-418`** ⚠ | the headless-has-no-session limit *(the gate split)* |
| **`BP-419`** ⚠ | `Fdp.Presentation.Tests` cannot be gated whole *(host crash, pre-existing)* |

### ⭐ Obligation ⑤ — **what was folded back into the design**

📄 [`DESIGN_Details_Panel_View_Switching.md`](../DESIGN_Details_Panel_View_Switching.md):

| where | what |
|---|---|
| **§6 `L6`, as-built (d)** | ⛔ the `HashSet` deletion is **`UXI-11`'s, not `L6.3`'s**, with the measurement; the `L6.3` row rewritten to match |
| **§6 `L6`, as-built (e)** | ⭐ **BORROW vs OWN**, decided per panel by what the root wires into it; the `L6.4` row rewritten |
| **§2 `classDiagram`** | ⭐ the two adapters + the two existing panels drawn, and **`o--` vs `*--` carries the asymmetry** |
| **§6 Limits** | ⭐ 3 new rows — the headless-session gate split · the borrowed search filter · the per-frame brain signal |

### ⛔ What this batch does NOT close — **stated, not implied**

| ⛔ | |
|---|---|
| **the visual check** | `L6`'s output is on-screen and I ran headless. ⭐ Delivered rail-green **to** visual-check; ⛔ I do not report *"works"* for what I cannot see |
| **the coordinator merge-review** | one round-trip, at the end, as §2b says |
| **`L6.1b`** | the persisted-key rename + layout migration — DEFERRED, and now **pinned by a rail** |
| **`BP-399` · `DerEntityInspectorPanel`** | explicitly not this batch |

---

## 9. ⭐ RULE 4 — **the coordinator pull, at the end**

📐 `git fetch` at close: the coordinator moved **`f968e693` → `a5770f0e`** *(two commits: curated test
scenarios; an icon-atlas zero-handle guard + headless-Xvfb doc)*.

| ⭐ | |
|---|---|
| **no design or handoff file relevant to `L6` changed** | ⇒ 📌 *"scope is FROZEN at the dispatch sha"* had nothing to bite on. ⛔ Nothing adapted, nothing reverted |
| ⚠ **but they touched `EditorSubsystem.cs` (+15)** — the same file `L6.1c`/`L6.3`/`L6.4` edit | ⇒ ⭐ **merged rather than left for the coordinator**: an un-merged textual overlap in the composition root is exactly the collision the two-session rules exist to prevent |
| ⭐ **post-merge re-gate** | ⭐ **solution build: 0 errors** · ⭐ **`Hrot.Editor.AiShared.Tests`: 1858 / 0 / 1 skip — IDENTICAL to pre-merge** · ⭐ **`TheScenario*` (19 rails): 19 / 0** ⇒ the merge changed nothing L6 depends on. ⚠ §8's `Hrot.Blueprints.Tests` row is the **pre-merge** full-suite number; ⛔ stated rather than silently relabelled — the post-merge re-run is in flight and the 19 L6 rails are already green against the merged tree |

## 10. ⭐⭐ WHAT THE COORDINATOR SHOULD LOOK AT FIRST

| ⭐ | where |
|---|---|
| ⭐⭐⭐ **the `BP-416` blocker** — is *"the `HashSet` deletion is `UXI-11`'s"* the right call? | §6.2 |
| ⭐⭐ **the borrow-vs-own asymmetry** *(as-built (e))* — it is the one place I chose differently for two sibling items | §7 · design §2's `o--`/`*--` |
| ⭐⭐ **`BP-419`** — a suite that aborts and still prints `Passed!` | §8b |
| ⭐ **the gate SPLIT for `L6.3`** — I could not rail the offer half on the production root | §6.3 · `BP-418` |
