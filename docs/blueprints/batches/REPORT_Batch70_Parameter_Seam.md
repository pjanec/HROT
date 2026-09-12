# REPORT — Batch 70: **the parameter seam** — `DEBT-AIB-021` · the Instance params seam · `G7`+`W10`

> **Branch** `claude/hrot-implementation-j1jvin` · **base** `4eadc30` *(coordinator dispatch `7f166ffe6`)*
> **Rule 7** re-synced at start · **rule 4** re-fetched before the final commit — ⭐ **nothing new on the
> coordinator branch.** ⭐ **All three items done, one commit each.**

---

## 0. 🔴 `StructureHash` — **unchanged for all 43.** `persistence-shape.txt` — **unchanged.**

⭐ **Stated first.** ⭐⭐ **And for item 2 it is a PREDICTION THAT HELD, not luck:** the design measured
296 Instance assets with **zero** parameters ⇒ `N = 0` ⇒ `16 + N == 16`, byte-identical. §3 explains
what *did* move and why that is a different file.

---

## 1. Gates — one row per gate, verbatim command, result

| gate | command | result |
|---|---|---|
| solution build | `dotnet build IOS-IG-SimHost.sln -t:Rebuild -v q --nologo` | ✅ **0 errors / 69 warnings** *(baseline exactly)* |
| Blueprints | `dotnet test …/Hrot.Blueprints.Tests.csproj --no-build -v q --nologo` | ✅ **3690 / 3680 / 0 / 10** *(**+33**)* |
| AiShared | `dotnet test …/Hrot.Editor.AiShared.Tests.csproj --no-build -v q --nologo` | ✅ **1280 / 1280 / 0 / 0** |
| BTree.Editor | `dotnet test …/Hrot.BTree.Editor.Tests.csproj --no-build -v q --nologo` | ✅ **615 / 615 / 0 / 0** |
| Breakpoints | `dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/*.csproj --no-build -v q --nologo` | ✅ **134 / 134 / 0 / 0** |
| Generators | `dotnet test Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/*.csproj --no-build -v q --nologo` | ✅ **208 / 208 / 0 / 0** *(**+5**)* |
| Hsm.Editor | `dotnet test …/Hrot.Hsm.Editor.Tests.csproj --no-build -v q --nologo` | ✅ **531 / 531 / 0 / 0** |
| AiEditor.Persistence | `dotnet test …/Hrot.AiEditor.Persistence.Tests.csproj --no-build -v q --nologo` | ✅ **136 / 136 / 0 / 0** |
| Toolkits | `dotnet test …/Fdp.Toolkits.Tests.csproj --no-build -v q --nologo` | ✅ **1964 / 1964** · sample 2 ✅ **1964 / 1964** |
| NodeEdit Core | `dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/NodeEditor.Core.Tests.csproj -v q --nologo` | ✅ **208 / 208** ⭐ **no `--no-build`** |
| NodeEdit UI | `dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.UI.Tests/NodeEditor.UI.Tests.csproj -v q --nologo` | ✅ **131 / 131** ⭐ **no `--no-build`** |
| tracker | `python3 scripts/tracker-counts.py --check` | ✅ **open 61 / done 153 (+1 refuted)** |

⚠ **Toolkits: two samples, both green** — ⛔ per `DEBT-AIB-030` that is **not evidence**, just this
run's outcome.

---

## 2. 🔴🔴 Item 1 — **did the (b) rail fail before the fix? YES, and alone**

> ⭐ **Asked explicitly.**

| probe | reddens |
|---|---|
| guard back to `defaults.Count == 0` *(+ the options field's matching guard)* | ⭐ **exactly 1** — `ManagedAsset_NoVariableHasDefault_StillEmitsParseParams_ForTheOverlay` |
| ⭐ **also suppress the step-2 overlay emission** | **all 7 behaviour rails** |

### ⚠ The locked registrar text — **updated deliberately, and here is what it said**

| test | was | is |
|---|---|---|
| `ManagedAsset_NoVariableHasDefault_*` | asserted `ParseParams` is **ABSENT** | asserts it is **emitted, for the overlay** |
| `ManagedAsset_OnlyFirstVarHasDefault_*` | *bakes only that one* | *bakes only that one, **but BOTH are overridable*** |

⭐⭐ **The first one is the interesting half: defect (b) had been written down as intent.** A test
asserting the absence of a feature is indistinguishable from a test asserting a bug, and only the
design record separates them.

### ⭐ A third guard nobody had named

📐 The `JsonSerializerOptions` field's emit guard was **also** keyed on `DefaultValueJson != null` ⇒
fixing (b) made the whole generated corpus fail with **CS0103 on `__paramJsonOpts`**. Same defect, one
level up; now keyed on *"managed with ≥1 variable"*.

⭐ **`persistence-shape` did not move** — the STOP held. This item changes registrar text, and the 43
golden assets are all `.bp.json`.

---

## 3. ⭐⭐⭐ Item 2 — `StructureHash` FIRST, then the four answers asked for

### 3.1 🔴 `StructureHash` / `persistence-shape` — **unchanged**, structurally

⭐ The params field is emitted **only when the asset declares parameters**, and none do. ⚠ **What DID
move: 17 generated-source snapshots** *(`Snapshots/Emit/*` + `Snapshots/Golden/Emit/*`)*. 📐 **The diff
is exactly two additive lines per Instance:**

```diff
+    public const int ParamsOffset = 16;
+    public static int ParamsSize => 0;
...
+            ParamsOffset = HealthRegen_9EC49CD9_Bp.ParamsOffset,
+            ParamsSize = HealthRegen_9EC49CD9_Bp.ParamsSize,
```

⛔ **No offset moved, no field appeared in `State`, no descriptor changed.** ⭐ Regenerated deliberately
and inspected line by line — this is the *"generated registrar text IS compared somewhere"* case the
handoff warned about for item 1, which turned up in item 2 instead.

### 3.2 ⭐ Is `ReadManaged` non-consuming within a frame? — **YES. The STOP does not fire.**

📐 `ManagedEventStream<T>.Read()` returns **`_front` itself**, and only `Swap()` (end-of-frame) clears
it. ⇒ **exactly `Read<T>()`'s contract**, which is what Replace's drain-twice depends on. The
remove-before-add ordering is enforced by the system's loop order, and that is untouched.

### 3.3 Which lifecycle events were converted, and which were not

| event | verdict |
|---|---|
| ⭐ **`AttachInstanceBlueprintEvent`** | **CLASS** — it carries `ParamsJson` |
| ⭐ **`ReplaceInstanceBlueprintEvent`** | **CLASS** — its add half attaches, so it must carry params too |
| ⛔ **`RemoveInstanceBlueprintEvent`** | ⭐ **stays a STRUCT** — a detach carries no params; converting it *"for symmetry"* costs an allocation per event and buys nothing |

### 3.4 Where `ParamsOffset` lives

⭐ **`FieldLayout.ParamsStructBase(asset)`** is the one home. The emitter **asks it** rather than
holding its own `16`, the class emits both constants, and `BlueprintDefinition` carries them so no
runtime call site re-derives anything. ⚠ **The first draft did hold its own `16`** — §5.

---

## 4. ⛔⛔ Item 2 — **a blocking premise the handoff did not name: `BP1031`**

> ⭐⭐⭐ **Reported, not worked around — and then decided, because the decision was inside the item.**

📐 `Stage2_Validate` refused an Instance that declared parameters, **fatally**:

> *"Instance asset must not declare parameters; **nothing supplies them at spawn**."*

⇒ ⛔ **the entire seam was unreachable the moment it was built.** ⭐⭐ **The rule's own message states
its reason, and this batch makes that reason false**: the attach event carries the JSON,
`BlueprintDefinition.ParseParams` resolves it, the payload reserves the bytes.

| | |
|---|---|
| ⭐ **What I did** | **retired it on `BP1024`'s precedent** — kept defined so the number is not reused, listed `RETIRED` in the coverage ratchet, and the positive test **inverted** rather than deleted |
| ⭐ **Why not stop** | leaving it standing ships a **producer with no consumer** — the *"inert rule"* shape this programme keeps filing (`DEBT-AIB-028`, trap #5). The design's ruling is unambiguous; the rail's own message was the only thing left disagreeing |
| ⚠ **What to overrule if you disagree** | the retirement is one commit and one ratchet entry. ⛔ **The seam is useless without it**, so the alternative is not "seam without retirement" but "no seam" |

📌 **The design KNEW** — `DESIGN_Parameter_Model.md:26` lists *"`BP1031` means nothing supplies params"*
among the things got wrong once, and `PLAN_Remaining_Work.md:568` notes *"`BP1031` means 0 Instances
declare `Parameters` today."* ⚠ **Neither says to retire it**, and the handoff's *"what must be built"*
table does not list it.

---

## 5. 🔴🔴 **Two rails of mine were weak, and the probes are what said so**

⭐ **Fourth batch running.** Both were found by probing, not by review.

| # | the weak rail | what the probe showed | fixed by |
|---|---|---|---|
| **1** | the emitter held **its own `=> 16`** | with the layout base reverted to `0`, the emitted `ParamsOffset` **stayed 16** while the fields were laid at `0` ⇒ the declaration and the layout describing **different memory**, and the declaration rail stayed **green** | it now asks `FieldLayout.ParamsStructBase` — ⭐ one home, and the probe now reddens it |
| **2** | the cursor rail called `ParseParams` **by hand** at `def.ParamsOffset` | ⛔ it read the offset **from the very field under test**, so reverting the layout left it green | it drives the **real attach path**, against a cursor pattern the fixture's `InitDefault` stamps — ⭐ a plain `Clear()` would have left the two cases indistinguishable (zeroes either way) |

📌 **The generalisation, third time:** *ask the artefact, not the thing that produced it.* Batch 68
counted methods instead of call sites; Batch 69 scanned a signature instead of the constructed object;
here a rail read its expected value out of the field it was testing.

---

## 6. ⭐⭐⭐ Item 3 — **ONE catalog or two? ONE. And it is asserted, not argued.**

> 🔴 **The STOP question, answered.**

⭐⭐ **A parameter resolver and a variable initializer differ in what CONSUMES the value, never in what
produces it.** Both ask *"what can produce a `float`?"* ⇒ one catalog, one picker.
`OneCatalogServesBothCallers` compares the two offer lists and requires the offer to be non-empty, so
the rail cannot pass by two empty lists agreeing.

| decision | |
|---|---|
| ⭐ **shape** | `ProducerCatalog` in `BehaviorActionCatalog`'s shape — 📄 `AN7-REPORT.md:73–95`'s precedent, *"add a source enum member + contributing catalog, not a new picker"* |
| ⭐ **sources** | **two, both with a real contributor**: Library Function graphs · hand-written CLR methods *(the resolver design's `E3` escape hatch)*. ⛔ **No member that nothing supplies** — that is a picker option which can never appear |
| ⭐⭐ **identity** | the **generated FQN**, `Hrot.AI.Behaviors.Generated.{SanitizedName}_{BlueprintId:X8}_Bp.{Fn}` — architect `AQ2`. ⚠ **Asserted as the STORED STRING**, because an AssetId round-trips just as happily; a second rail pins it to `LibraryEmitter`'s formula, **computed** not pasted |
| ⭐ **union** | offered for `Parameter` · `WorkingState` · `Variable` |
| ⭐ **"None"** | first row, selectable, persists as `null`, round-trips None → producer → None |
| ⚠ **dangling** | **KEPT and reported unresolvable**, ⛔ not silently cleared — resetting turns a broken reference into a plausible-looking deliberate choice |

⛔ **Out of scope as instructed:** `E5` create-resolver · `E6` divergence detection · `E1` Library
authoring.

⚠ **Not verifiable, visual check suspended:** the drop-down **drawing** and its placement in the
Details panel. ⭐ What IS asserted is the meaning: what is offered, what is selected, what is stored.

---

## 7. ⭐ IDs allocated *(rule 5)*

| kind | allocated |
|---|---|
| tracker rows | ⭐ **`BP-275` · `BP-276` · `BP-277` · `BP-278` · `BP-279`** |
| blueprint diagnostics | ⛔ **none allocated** — ⭐⭐ **and one RETIRED: `BP1031`** *(kept defined; the number is not free)* |
| analyzer diagnostics · architect questions | ⛔ none |

---

## 8. ⭐⭐ Debt rows touched *(the standing ask)*

| row | what happened |
|---|---|
| ⭐ **`DEBT-AIB-021`** *(Track G)* | ✅ **CLOSED** — and it was **two** defects plus a third guard, not the one the row describes |
| ⭐ **`DEBT-AIB-030`** *(Toolkits flake)* | ⚠ two green samples; **not evidence either way**, recorded per the standing rule |

⛔ **No other row on the partition list was touched.** ⚠ `-001`/`-002`/`-008`/`-011` were **not**
reached: item 2 changed the blueprint payload and its attach path, not the AI-host slot machinery.

---

## 9. What this batch did **not** do

**`E0`** *(HSM golden harness)* · `E3` · `E5` · `E6` · `E7a`/`E7b` *(`IHostVariableAccess` is still
passed `null`, which is its defined value for a root)* · blueprint **multi-occurrence** *(pulled —
`Architect_Question_34`)* · the `InspectorWindow` "STATIC PARAMETERS" retirement · the Track C **visual
check** · **an AiPrimitive `ParamsOffset`/`ParamsSize` on its definition** *(the AiPrimitive path does
not attach through `BlueprintInstanceService`, so it has no consumer; the layout base for it is
unchanged at `0` and asserted)*.
