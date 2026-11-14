<!--STATUS
state: LIVE
updated: 2026-08-19
current-answer: this whole file — the Batch 95 report.
stale-below: nothing.
known-rot: none.
known-conflict: none.
-->
# REPORT — Batch 95: **the feed and the resolver**

> 📌 **Dispatched at `c890cbda3`** · **started at `f0bb6b2`** *(marker `e225a69`, rule 1b)* ·
> **base for every RED = `c890cbda3`**.
> ⭐ **Scope was frozen at the dispatch sha and nothing landed on the coordinator branch during the
> run** — re-fetched before this commit, `HEAD..origin/claude/blueprint-authoring-status-gm0akp` is
> **empty**. ⛔ No later document invalidated an item.
> ⭐ **`95a` ✅ · `95b` ✅ · `95c` NOT STARTED** — §7 says why, and the handoff sanctioned it.

---

## 1. ⭐⭐⭐ THE HEADLINE

| | |
|---|---|
| ⛔ **`95a`** | *"Edit value…"* / *"Properties…"* could **never** open on Blueprint — the resolver could not **express** a blueprint asset |
| ⛔ **`95b`** | the live feed read a store **nobody writes** — four selection stores, **one** `Connect` |
| ⭐ **Both** | fixed, and railed as **a value arriving through a constructed production object**, not as an argument being passed |

⭐ **IDs allocated (rule 3/5): `BP-350` · `BP-351` · `BP-352` · `R-105`.** ⛔ No others.

---

## 2. 🛠 `95a` — **the decision, and the measurement that made it**

### ⭐⭐⭐ The mandated measurement, run FIRST

```
VariableEditLauncher.Open (VariableEditing.cs:218)
  → DefaultValueAuthoring.OpenSession (DefaultValueAuthoring.cs:99)
        var instance = Hydrate(varEntry.FieldType, varEntry.DefaultValueJson);
        return editService.Open(instance, varEntry.FieldType, scope);
```

⇒ ⭐⭐ **the consumer reads `FieldType` and `DefaultValueJson`. Nothing else.**
⇒ ⭐ **a synthesised entry is fully substitutable for an authored one** — which is exactly the doubt
the handoff attached to option (b) *("I have NOT measured what the launcher does with the returned
entry")*. ⭐ **It is measured now, and it is a rail, not a claim**
*(`TheRowCarriesItsDeclarationTests.TheOpenerReadsOnlyFieldTypeAndDefaultValueJson`)*.

### ⭐ The decision: **(b)** — ⛔ not (a), ⛔ not (c)

| | |
|---|---|
| ⛔ **(a)** `BlueprintAsset : IBlackboardManagedAsset` | **rejected**, on the handoff's own reasoning, and now **pinned by a rail** *(`TheBlueprintAssetIsStillNotABlackboardManagedAsset`)* so re-introducing it has to be argued |
| ⭐⭐ **(b)** the resolver stops being hard-wired to the AI vocabulary | **BUILT** |
| ⛔ **(c)** the launcher stops needing a `BlackboardVariableEntry` | ⛔ **not attempted** — it is the design call the handoff reserved, and (b) turned out to be substitutable |

### ⭐⭐⭐ **But the CARRIER is the ROW, not the composition root** — and that is a measurement, not a preference

📐 **Measured:** Blueprint's rows come from `BlueprintMyBlueprintWindow`, whose `Local Variables` arm
resolves against **`_currentGraph()` at call time**, and which is registered as an **EXTRA** window —
long after `PerspectiveWorkspaceServices.CreateRegistrar` has returned.

⇒ ⛔ **a resolver supplied at `CreateRegistrar` could answer `Variables` and `Parameters` and NOT
`Local Variables`.** ⭐ The source that built a row already holds the schema that declares it, so the
declaration travels **with the row** and there is nothing new for a call site to forget *(`R-67`)*.

| file | what changed |
|---|---|
| `Variables/VariableRow.cs` | `ReadVariableDeclaration` + an optional trailing `ReadDeclaration` arm — ⭐ the exact idiom Batch 90 used for `ReadValueObject` and Batch 94 for `ReadWritten` *(ruling 9: one precedent, not a new idiom)* |
| `Variables/VariableRowSources.cs` | `SectionVariableRowSource` projects its `VariableViewModel` — which **already carries** `Name`/`FieldType`/`Comment`/`DefaultValueJson` |
| `Variables/BlackboardSectionRowSource.cs` | the AI half carries the **authored entry itself** — ⛔ both sources, or one question would have two rules |
| `Windows/PerspectiveWorkspaceRegistrar.cs` | `ResolveEntry` asks the row first; ⭐ **the store lookup STAYS as the fail-closed fallback** |
| `EditorSubsystem.cs` | `RegistrarFor(perspective)` — an internal test hook, so a rail can assert on the object the REAL composition root built |
| `PerspectiveWorkspaceRegistrar.SelectionStore` | same reason: put production into production's state instead of building a second root |

---

## 3. 🛠 `95b` — **one entity, four stores**

### 📐 The measurement

| | |
|---|---|
| stores built | **four** — `_aiEditorSelectionStore` *(`:269`)* + the three perspective stores *(`:320`–`:322`)* |
| `Connect(` call sites | **one** — `_selectionBridge.Connect(_aiEditorSelectionStore)` |
| the providers read | the three **perspective** stores |
| ⇒ | `SelectedEntity` **null on all three, always** ⇒ `GetLiveObjects` returns `null` on its **second line** ⇒ every row, every host, `(pending)` |

⚠⚠ **And the composition root asserted the opposite, in a comment**: *"Both selection stores share the
same entity selection (global), so … both read the same entity via their respective store."*
🔴 **False when written.** ⭐ It is now true by construction, and the comment says so.

### ⭐⭐⭐ The design basis — **cited, per rule**

📄 **`AI_Editor_Shared_Infrastructure.md:450`** — *"SelectedEntity stays global because entities exist
independently of which asset is being edited — the same entity is selectable while looking at any of
its associated assets."*
📄 **`:45`** — `EditorSelectionStore` is *"the single selection bus all three editors subscribe to."*

⇒ ⭐⭐ **The entity was never meant to be per-perspective.** The split arrived later, for `ActiveAsset`
*(AIE-025)*, and took the entity with it. ⭐ **Filed as `R-105`** so the next session does not re-derive
it. ⚠ **`ActiveAsset` stays per-perspective** — that half of the split IS intended.

### ⭐ What was built

| file | |
|---|---|
| `Selection/SharedEntitySelection.cs` *(new)* | one cell, one `Changed` event |
| `Selection/EditorSelectionStore.cs` | takes it **optionally** *(a store given none keeps its own ⇒ every standalone and test construction unchanged)*; `SelectedEntity` routes through it; the cell re-raises `OnSelectionChanged` so **every** store's panels repaint |
| `EditorSubsystem.cs` | one cell, given to **all four** stores in the constructor; the fields are now `readonly` |

⛔ **NOT three more `Connect` calls** — 📌 the shape `PerspectiveWorkspaceServices` exists to abolish.
⛔ **The bridge still connects exactly one store**; nothing about it changed.

---

## 4. ⭐⭐⭐ THE END-TO-END RAILS — **named, with the faked layer stated**

> 📌 Handoff §6: *"name it, and say exactly which production objects it constructs. If it constructs a
> fake at any layer, say WHICH — that is the layer the defect could still hide in."*

| rail | constructs | ⛔ faked |
|---|---|---|
| ⭐⭐⭐ **`TheDialogOpensOnEveryHostTests`** *(Blueprints.Tests, 8)* | the **real `EditorSubsystem`**, its registrar, edit service, binder, launcher, run-state source, selection store; a real `BlueprintAsset` + `BlueprintVariableSchemaSource` + `SectionVariableRowSource` on the Blueprint arm | ⚠ the `IconAtlas` is a zero handle *(no GPU)*; ⚠ the BTree/HSM arms stand on a minimal `IBlackboardManagedAsset` — **`HsmAsset`'s ctor is internal (DTO-mapper only)** and `BehaviorTreeAsset`'s needs a whole `Fbt` blob. ⭐ The old resolver type-tested the **interface**, which the stand-in satisfies identically; ⚠ the ROWS are built by the test, which is the layer the next rail covers |
| ⭐⭐ **`TheRowCarriesItsDeclarationTests`** *(AiShared.Tests, 4)* | **both production row sources**, the sampler, the pinned store | ⛔ nothing — it is a source-level rail by design |
| ⭐⭐⭐ **`TheSelectedEntityReachesEveryPerspectiveTests`** *(Blueprints.Tests, 10)* | ① the **real `EditorSubsystem`** — select on the store the bridge actually writes, assert all three perspectives see it **and repaint** · ② a real `BlueprintLiveValueProvider`, `SectionVariableRowSource`, `VariableTableModel`, `VariableValueFormatter`, joined by one `SharedEntitySelection`, driving **`42`** until the cell stops saying `(pending)` | ⛔ **the RUN** — arm ② stubs the state reader, because the alternative is stubbing a **36-member** debug session *(the provider's own doc-comment names that cost)*. ⭐ **Arm ① is what covers that layer**: it fakes nothing and proves the entity reaches production's own stores |

⭐⭐ **Every assertion is *a value arrived*, not *an argument was passed*** — 📌 `M-22`'s correction,
which is the whole reason this batch exists.

### ⚠ One residual, RAILED ON PURPOSE — **`BP-352`**

`TheSelectionIsNotVisibleUntilTheNextPulse` **asserts the gap**: `VariableRowSampler` samples once per
`BehaviorFrame` pulse *(`R-103`, the user's own spec)*, so a selection made while the debugger holds
time shows the previous sample until the run continues; and every production row source is built with
**`entity: default`**, so `VariableRowOrigin.Key` is identical across entities and two entities share
one sample cache and one change baseline. ⛔ **Out of `95b`'s scope** — its claim is that a value can
arrive at all. ⚠ **If someone fixes it, that rail goes RED — flip it, do not delete it.**

---

## 5. ⭐⭐ REVERT-GOES-RED — **per item, never delegated**

| probe | un-applied | result |
|---|---|---|
| **P1 · `95a`** | the row-first line in `ResolveEntry` | 🔴 **2 of 8 red — BOTH Blueprint arms**, BTree/HSM green ⇒ ⭐ **the defect's exact shape**, and evidence the rail is not vacuous on the hosts that already worked |
| **P2 · `95b`** | the three perspective stores stop taking `_sharedEntitySelection` | 🔴 **6 of 10 red** — both composition-root theories, all three perspectives |

⭐ **Both probes un-applied with the INVERSE EDIT** *(⛔ never `git checkout --`)*, and both suites
re-confirmed green afterwards. ⭐ **Working tree clean after every suite run.**

---

## 6. ⭐⭐ GATES — the seven-row contract

| # | gate | result | Δ vs `c890cbda3` | `--no-build`? |
|---|---|---|---|---|
| 1 | AiShared | **1545 / 0 / 0** | **+4** *(the row-declaration rail)* | ✅ |
| 2 | BTree.Editor | **622 / 0 / 0** | 0 | ✅ |
| 3 | Hsm.Editor | **554 / 0 / 0** | 0 | ✅ |
| 4 | AiEditor.Generators | **277 / 0 / 0** | 0 | ✅ |
| 5 | AiEditor.Persistence | **143 / 0 / 0** | 0 | ✅ |
| 6 | Blueprints | **3796 / 0 / 10 skip** | **+18** *(8 dialog + 10 selection)* | ✅ |
| 7 | Hrot.Editor | **201 / 0 / 0** | 0 | ✅ |
| 8 | Breakpoints | **143 / 0 / 0** | 0 | ✅ |
| 9 | NodeEditor.Core | **211 / 0 / 0** | 0 | ⛔ **NO — out of solution** |
| 10 | NodeEditor.UI | **135 / 0 / 0** | 0 | ⛔ **NO — out of solution** |
| 11 | Fhsm | **300 / 0 / 0** | 0 | ⛔ **NO — out of solution** |
| 12 | `Fdp.Presentation` *(`BP-337`, filtered to `~Fdp.Presentation.Tests.WindowManager`)* | **146 / 0 / 0** | 0 | ✅ |
| 13 | `Fdp.Toolkits.Tests --filter CognitiveRuntimeModuleTests` | **1 / 0 / 0** | 0 | ⛔ **`--filter` ONLY** *(`DEBT-AIB-030`)* |
| 14 | Blueprints `--filter Benchmarks` | **8 / 0 / 1 skip** | 0 | ✅ ⚠ **see below** |

⚠ **The ONE red observed, and it is documented pre-existing.** Gate 14 failed **once in four runs** on
`WhenNodePerfTests.Spawn_ZeroAllocation`. ⭐ **That exact test is named in `BP-111`** — *"expected 0
bytes, got 7 696, green on re-run in the same session"* — and the whole `WhenNodePerfTests` family
carries `[Trait("Category","HostTimingSensitive")]`, which is why gate 6 *(the gated suite)* is green:
the default filter excludes it and an explicit `--filter Benchmarks` overrides that. ⭐ **Runs 2, 3 and
4 were green.** ⛔ **No worktree run against `c890cbda3` was needed** — the row already names it.

⭐ **Quarantine: Blueprints 10 skip, everything else 0. ⛔ NO NEW SKIP.**
⭐ **No golden movement** — this batch touches no emitter and no asset; `git status` is clean after
every suite.

### ⭐ 7 — the scripts, **UNFILTERED**, with `EXIT`

```
$ python3 scripts/tracker-counts.py --check
TRACKER COUNTS DISAGREE WITH THE ROWS: … Total: table says open=73 done=211, rows say open=74 done=213
EXIT=1                     ⭐ EXPECTED — the summary table is DERIVED, and three rows were added

$ python3 scripts/tracker-counts.py --check      # after the corrected table
tracker counts OK — open 74 / done 213 (+1 refuted)
EXIT=0

$ python3 scripts/rulings-check.py
70/70 rulings verified against their sources
EXIT=0                     ⭐ 69 → 70: R-105 added, no staleness warning
```

---

## 7. ⛔ `95c` NOT STARTED — **and the handoff's own condition is why**

> ⭐ Handoff §4, verbatim: *"If `95a`/`95b` consume the batch, STOP — `95c` is worth a batch of its own
> and is worth nothing before the two failures are fixed."*

⭐⭐ **The two rails this batch DID build are the first two members of the class `95c` would
systematise** — one per live capability, each asserting a VALUE ARRIVES through the constructed
object. ⭐ **A `95c` batch now has two worked examples to generalise from rather than a blank page**, and
📌 the handoff's own warning stands: ⛔ **no generic detector** *(one was tried and thrown away on
`2026-08-16`)*.

---

## 8. ⭐ WHAT THE NEXT PASS SHOULD KNOW

| | |
|---|---|
| ⭐⭐⭐ **The visual check is runnable again** | `C` and `D` needed the dialog *(`95a`)*; `C7`/`H9` and the `E2`–`E7` family need the feed *(`95b`)*. ⛔ **I claim nothing about what renders** — 📌 `R-21`/`R-62` |
| ⚠ **`BP-352` will be visible during it** | a selection change shows the previous sample until the next pulse, and two entities share one sample cache. ⭐ **Expected, filed, railed** |
| ⚠ **`94g` still not started** | a pin does not survive a scenario reload — ⭐ **expected, not a finding** |
| ⭐ **Carried, untouched** | `BP-342` · `BP-345` · `BP-346` · `BP-348` · `BP-337` *(half-fixed)* |
