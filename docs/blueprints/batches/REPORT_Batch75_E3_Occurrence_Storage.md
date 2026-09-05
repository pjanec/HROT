# REPORT — Batch 75: **`E3` has no subject** · the dialog that would have opened empty · `-028`(a)

> **Branch** `claude/hrot-implementation-j1jvin` · **base** `6d444ec` *(dispatch `509f80ac3`)*
> ⭐ **Rule 1b honoured** — started-marker **`4ab9483`**, pushed before any code.
> **Rule 7** re-synced at start · **rule 4** re-fetched before the final commit.
>
> ⛔⛔ **Item 1 (`E3`) STOPPED — and not on either `Q35` half.** ⭐ **Both rulings are fine; the item has
> nothing to move.** §2.
> ✅ **Items 2 and 3 landed, and each found a defect that only became visible by doing it.**

---

## 1. Gates — one row per gate, verbatim command, result

| gate | command | result |
|---|---|---|
| solution build | `dotnet build IOS-IG-SimHost.sln -t:Rebuild -v q --nologo` | ✅ **0 errors / 69 warnings** |
| Blueprints | `dotnet test …/Hrot.Blueprints.Tests.csproj --no-build -v q --nologo` | ✅ **3691 / 3681 / 0 / 10** |
| AiShared | `dotnet test …/Hrot.Editor.AiShared.Tests.csproj --no-build -v q --nologo` | ✅ **1289 / 1289 / 0 / 0** *(**+8**)* |
| BTree.Editor | `dotnet test …/Hrot.BTree.Editor.Tests.csproj --no-build -v q --nologo` | ✅ **615 / 615 / 0 / 0** |
| Breakpoints | `dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/*.csproj --no-build -v q --nologo` | ✅ **134 / 134 / 0 / 0** |
| Generators | `dotnet test Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/*.csproj --no-build -v q --nologo` | ✅ **266 / 266 / 0 / 0** |
| Hsm.Editor | `dotnet test …/Hrot.Hsm.Editor.Tests.csproj --no-build -v q --nologo` | ✅ **549 / 549 / 0 / 0** *(**+6**)* |
| AiEditor.Persistence | `dotnet test …/Hrot.AiEditor.Persistence.Tests.csproj --no-build -v q --nologo` | ✅ **136 / 136 / 0 / 0** |
| Examples.Scenarios | `dotnet test FDP/Examples/Fdp.Examples.Scenarios.Tests/*.csproj --no-build -v q --nologo` | ✅ **68 / 56 / 0 / 12 skipped** ⭐ **quarantine UNCHANGED at 12** |
| Examples.UrbanCombat | `dotnet test FDP/Examples/Fdp.Examples.UrbanCombat.Tests/*.csproj --no-build -v q --nologo` | ✅ **29 / 29 / 0 / 0** |
| Toolkits *(sample 1)* | `dotnet test …/Fdp.Toolkits.Tests.csproj --no-build -v q --nologo` | 🔴 **1 failed** — `GizmoRegistryTests.SC_GZ004_2` |
| Toolkits *(sample 2)* | *same* | 🔴 **1 failed** — **the same test** |
| ⭐ **Toolkits *(isolated)*** | `… --filter "…GizmoRegistryTests.SC_GZ004_2"` | ✅ **1 / 1** |
| ⭐⭐ **Toolkits *(whole `Gizmos` namespace)*** | `… --filter "…Gizmos"` | ✅ **187 / 187** ⇒ **`DEBT-AIB-030`** |
| NodeEdit Core | `dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/NodeEditor.Core.Tests.csproj -v q --nologo` | ✅ **208 / 208** ⭐ **no `--no-build`** |
| NodeEdit UI | `dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.UI.Tests/NodeEditor.UI.Tests.csproj -v q --nologo` | ✅ **131 / 131** ⭐ **no `--no-build`** |
| tracker | `python3 scripts/tracker-counts.py --check` | ✅ **open 63 / done 172 (+1 refuted)** |

🔴🔴 **Goldens:** ⛔ **the blueprint set did not move in any commit** *(`persistence-shape` · 43
`Emit/*.cs.txt` · `StructureHash`)*. ⛔ **Nor did the HSM set** — item 3's field is omitted when empty,
so no corpus asset's bytes change; ⭐ **the handoff expected `hsm-persistence-shape` to move and it does
not**, for the same reason Batch 73's item 4 did not: the change is additive-when-absent by design.

---

## 2. ⛔⛔ Item 1 — **`E3` STOPPED. The storage move has no subject.**

> ⭐ **Neither `Q35-A` nor `Q35-B` is the problem.** The delivery ruling *(carry the pair on
> `HsmCommandWriter`)* is sound and the kernel call sites all have the identity in scope. ⛔ **The
> problem is downstream: there is nothing whose bytes would move.**

### 📐 The measurement, exhaustive

| | measured `2026-08-17` |
|---|---|
| ⭐ **the only DTO-bound HSM shape** | `[SharedAiAction]`/`[SharedAiCondition]` — its generated thunk projects the bound field at `bb.BehaviorParameters[0] + offset`. ⛔ **A plain `[HsmAction]` takes `(void*, void*, HsmCommandWriter*)` and resolves no DTO at all** |
| 🔴 **where the four production `[SharedAi*]` methods live** | **`Fdp.Toolkits`** — `BlueprintLifecycleLibrary` ×3, `DemoEnumAction` ×1. ⛔ **That assembly does NOT run `HsmActionGenerator`**: no `HsmActionRegistrar.g.cs` is generated for it |
| 🔴 **the ONLY assembly that runs the generator** | **`Hrot.AI.Behaviors`** — whose sole HSM action is **`CgfHsmNodes.StubIdle`**, DTO-free, body empty |
| ⭐ **and the Examples** | `ApcHsmActions` ×2 · `UrbanCombatNewScenario` ×2 — all the raw pointer shape |

⇒ ⛔⛔ **ZERO DTO-bound HSM thunks exist in any binary.** `E3` moves storage for something with no
instances.

### 🔴🔴 The pre-written rail **cannot fail before the change**

> 📄 plan §4B: *"two concurrently-active orthogonal regions running the SAME action write DIFFERENT
> bytes"*, **failing before the change** — ⭐ *"`HsmOrthogonalRegions` is in the corpus for exactly
> this."*

📐 **It is** — and both its regions run **`StubIdle`**, whose body is `// Intentionally empty`.
⇒ ⛔ **there are no bytes to collide**, so the rail is green before and after, on nothing.

### ⭐⭐ The ordering nobody had stated

⚠ **`E3` is blocked behind the very boundary this handoff's §4 lists as OUT of scope** — *"the
compound-key thunk's bytes: no shipped assembly generates one — the thunk must be generated where the
method lives."* ⭐⭐ **They are the same blocker.** 📌 I named that boundary at the end of Batch 74 as
`E7b`'s limit and did not connect it to `E3`; ⛔ **this is the first time the two are stated as one.**

### ⛔ Why nothing was half-built

⭐ The kernel half *(two fields on `HsmCommandWriter`, five stamps)* is small and I could have landed it.
⛔ **It would have been a delivery mechanism with no consumer** — and with the baked-offset path still
in place, **two mechanisms**, which is precisely what `Q35-C`'s *"ONE path, not two"* forbids. ⚠ Also
the exact shape §0b of the last handoff ruled against: *build only what the runtime already supports*.

### ⭐ What `E3` actually needs, in order

| # | | note |
|---|---|---|
| **1** | ⭐⭐ **a DTO-bound HSM action in a generator-bearing assembly** | either move/duplicate a `[SharedAiAction]` into `Hrot.AI.Behaviors`, or apply the analyzer to `Fdp.Toolkits`. ⚠ **A decision, not an implementation detail** |
| **2** | the corpus asset binds it in **both** regions | `HsmOrthogonalRegions` already has the shape; only the action changes |
| **3** | ⭐ **then the rail fails**, and `E3` proceeds exactly as `Q35` rules | ⛔ **not before** |

### ⚠ Two adjacent kernel measurements, recorded while looking

| | |
|---|---|
| ⭐ **the identity is in scope at every call site** | `InitializeSlot` has `slotIndex`+`targetState`; `ProcessActivityPhase` has `r`+`current`; the three `ExecuteTransition` sites have `stateId` ⇒ ⭐ **`Q35-A`/`B` are implementable as ruled** |
| ⛔⛔ **but `ExecuteTransition` HARD-CODES region 0** | `HsmKernelCore:733` — `activeLeafIds[0] = finalLeafId`, and no region index is threaded into that method. ⇒ ⚠ **a transition in region 1 already writes region 0's active leaf.** 📌 **A pre-existing orthogonal-region defect, unrelated to `E3` and not filed anywhere I can find** — ⭐ worth its own row before `E3` relies on that path |

---

## 3. ⭐⭐⭐ Item 3 — `-028`(a), and **`E4`'s missing half arrives**

🛠 `SubtreeAssetId` round-trips through `HsmAssetDto` + both directions of `HsmAssetMapper`, **omitted
from JSON when empty**.

⛔ **The projector deliberately does not carry it.** `HsmAssetProjector` rebuilds from the compiled blob
plus its `[HsmLayout]` entry, and this is **authoring intent, not topology**. ⭐ Under JSON-SoT the mapper
IS the round trip that matters; a layout slot would be a second home for a field the JSON already owns.

⚠⚠ **The round trip is NOT the claim worth testing** — it passes the moment the property exists and says
nothing about the rules, which is what was broken. ⭐ **So the rail authors a rule-8 violation,
serialises, deserialises, and asserts the error appears on the LOADED asset.**

### ⭐⭐ Do rules 8/8b fire on a disk-loaded asset now? **YES — with one condition, and finding it was the work**

🔴 **The rail failed first**, and the cause was not the field: **a region with no `InitialChild` is
ORPHANED on load.** 📐 The JSON region list carries **no parent reference**, so ownership is re-derived
from `region.InitialChild?.Parent` *(RHS-05)* ⇒ ⛔ **no initial child ⇒ no owner ⇒
`composite.RegionNodes.Count < 2` ⇒ both rules skip the composite** — **no diagnostic, asset validates
clean.**

⭐⭐ **Same class as `-028`(a) itself: a rule that cannot reach its input** — and invisible until now,
because nothing got that far. 🛠 **Filed `BP-299`** and asserted as a named gap; ⭐ the fix is a parent
reference on `RegionNodeDto`, a **persistence-shape** change, not a test.

### ⚠ And `DEBT-AIB-029` is now a REAL defect, not a theoretical one

📐 Rule 8 walks **direct children only**. ⭐ While the field was unpersisted this could not be reached
from a saved asset; ⛔ **with it round-tripping, a designer can author a nested host and SAVE it**, and
the rule stays silent on exactly the corruption it exists to prevent. ⭐ Asserted as a named gap
*(`ANestedSubtreeHost_EscapesRuleEight_Yet`)* — ⭐ **invert when `-029` lands.**

🔴 **Probe M** *(stop writing the field)* reddens **4 of 6** rails, including both gap tests — correctly:
without the field they cannot even measure the gap.

---

## 4. ⭐⭐⭐ Item 2 — **the wiring found that the value dialog opens EMPTY**

🛠 `VariableEditGestureBinder` is the seam: headless, attaches to the table's two gestures, resolves the
row's entry by delegate, **reads the run state PER GESTURE** *(writability changes when the sim starts;
a captured snapshot would offer an editable dialog mid-replay)*, and opens through the one launcher.

| the handoff asked | answer |
|---|---|
| ⭐ **which gesture opens which scope, asserted** | ✅ **VALUE cell → the field** *(document root `"$.Count"`)*; **NAME cell → the whole object** *(root `"$"`, both fields)* |
| ⭐ **the inverted gap rail** | ✅ the launcher is now **constructed**; ⛔ the other half of that rail — the panel still routes — is unchanged |
| ⛔ **confirm `ExactlyOneCallSite` still holds** | ✅ **unchanged**, with two entry points. ⭐ That is what ruling 9 asks for |

### 🔴🔴 The defect

📐 `ScopeFor` built its path in **variable space** — the bare name — while
`ReflectionEditDocumentBuilder.FilterNode` matches **`node.JsonPath`**, which for a top-level field is
`"$.Health"`. ⇒ ⛔ **nothing matched, `ApplyScope` fell through to an empty `"$"` SelectionRoot, and the
value dialog would have opened blank.** 🛠 `ScopeFor` now roots the path, idempotently.

⚠⚠ **It was invisible because nothing ever CONSTRUCTED the launcher** — ⭐ **the defect needed the seam
to exist before it could be seen.** 📌 That is the argument for wiring a finished-looking pair even when
both halves have tests.

### ⛔⛔ And the rail that should have caught it was VACUOUS — a SIXTH instance

📐 `TheTwoActions_DifferOnlyByTheEditScope` asserted `IncludedPaths[0] == "Health"` — **the expectation
read straight out of the argument it had just passed in.** ⭐ It stayed green throughout, **because a
scope that selects NOTHING still has exactly one included path.** 🛠 Updated to assert JSON-path space,
with the reason recorded in the test.

⚠ **The distinguishing observable is the document root's `JsonPath`, not its child count** — I tried the
child count first and both cases were **0**: a field-scoped document's root IS the field *(a leaf)*, and
an empty scope's root is an empty `"$"`. ⭐ **Two different documents, identical counts.**

🔴 **Probe N** *(un-root the path)* reddens both scope rails · 🔴 **Probe O** *(the binder stops calling
the launcher)* reddens **4**, including the inverted gap rail.

---

## 5. ⭐ IDs allocated *(rule 5)*

| kind | allocated |
|---|---|
| tracker rows | ⭐ **`BP-297`** *(`E3` escalation, OPEN)* · **`BP-298`** · **`BP-299`** *(OPEN)* · **`BP-300`** |
| diagnostics · architect questions | ⛔ none |

⭐ **Started-marker sha *(rule 1b)*: `4ab9483`.**

---

## 6. ⭐⭐ Debt rows touched

| row | what happened |
|---|---|
| ✅ **`DEBT-AIB-028`(a)** | **resolved** — the field persists, and rules 8/8b now fire on a loaded asset (§3) |
| ⚠ **`DEBT-AIB-029`** | **promoted from theoretical to real** — reachable from a saved asset for the first time (§3) |
| ⚠ **`DEBT-AIB-030`** | 🔴 **red in BOTH full samples** — ⭐ but **`SC_GZ004_2`**, Batch 73's test, **not** Batch 74's `SC_GZ022_2` ⇒ ⭐⭐ **the identity of the red ROTATES between runs**, which is the strongest evidence yet that it is scheduling, not the code. ✅ Green isolated and green for all **187** `Gizmos` tests; this diff touches no gizmo code. ⛔ **Not signal** — **seven** distinct tests now |

---

## 7. Not done

⛔⛔ **`E3`** *(§2 — no subject; needs a DTO-bound HSM action in a generator-bearing assembly first)* ·
**`E5`** *(`E3`'s mechanism)* · **`E7a`** · ⛔ **the compound-key thunk's bytes** *(the same blocker as
`E3`)* · **`-029`'s deep walk** · **`BP-299`'s region-parent persistence** · ⚠ **the
`ExecuteTransition` region-0 hard-code** *(§2, unfiled before this batch)* · blueprint multi-occurrence
*(user-deferred)* · wiring the producer picker *(parked)* · the 12 quarantined scenario tests · the
Track C **visual check**.
