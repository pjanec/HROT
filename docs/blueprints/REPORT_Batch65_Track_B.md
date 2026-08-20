# REPORT — Batch 65: Track B (`S2` · `S4` · `S3` · `S5`)

> **Implementation session, `2026-08-16`.** Branched from / re-synced with
> `claude/blueprint-authoring-status-gm0akp` at **`a765b96`** (rule 7), re-fetched before the final
> commit — **nothing landed on the coordinator branch during the run** (rule 4).
> ⭐ **All four items built. One commit per item. No STOP condition fired.**

---

## 0. 🔴 `StructureHash` — **unchanged for all 43.** `persistence-shape.txt` — **unchanged.**

⭐ **Stated first, as asked.** Golden **Tier 1 zero files changed**, Tier 2 zero files changed — the
working tree was clean after every suite run, so nothing regenerated. 📐 **Why `S2`/`S4` could not move
a shipped layout, measured rather than hoped:** no shipped asset declares a `global::`-form struct
(the 4 occurrences of `global::Hrot.AI.Behaviors.StructDemoData` in `StructValueDemo.bp.json` are all
**pins**), and the one shipped fixed list (`ListVariableDemo.Waypoints : List<int>[4]`) has a
registered element, so it never took the fallback.

---

## 1. Gates — one row per gate, verbatim commands

| gate | command | result |
|---|---|---|
| solution build | `dotnet build IOS-IG-SimHost.sln -t:Rebuild -v q --nologo` | ✅ **0 errors / 69 warnings** *(full rebuild, not incremental)* |
| BP diagnostics | `dotnet build …/Hrot.AI.Behaviors.csproj -t:Rebuild -v n --nologo \| grep -oE "warning BP[0-9]+: [^[]*" \| sort -u \| wc -l` | ✅ **10 distinct** |
| Blueprints | `dotnet test …/Hrot.Blueprints.Tests.csproj --no-build -v q --nologo` | ✅ **3628 / 3618 / 0 / 10** *(was 3618/3608/0/10 ⇒ **+10**)* |
| AiShared | `dotnet test …/Hrot.Editor.AiShared.Tests.csproj --no-build -v q --nologo` | ✅ **1216 / 1216 / 0 / 0** |
| BTree.Editor | `dotnet test …/Hrot.BTree.Editor.Tests.csproj --no-build -v q --nologo` | ✅ **612 / 612 / 0 / 0** |
| Breakpoints | `dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/*.csproj --no-build -v q --nologo` | ✅ **130 / 130 / 0 / 0** |
| Generators | `dotnet test Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/*.csproj --no-build -v q --nologo` | ✅ **196 / 196 / 0 / 0** |
| Toolkits | `dotnet test …/Fdp.Toolkits.Tests.csproj --no-build -v q --nologo` | ⚠ **1942 / 1941 / 1 / 0**, then **1942 / 1942** ×2 — see §2 |
| NodeEdit Core | `dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/NodeEditor.Core.Tests.csproj -v q --nologo` | ✅ **208 / 208 / 0 / 0** ⭐ **no `--no-build`, honoured** |
| NodeEdit UI | `dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.UI.Tests/NodeEditor.UI.Tests.csproj -v q --nologo` | ✅ **131 / 131 / 0 / 0** ⭐ **no `--no-build`, honoured** |
| tracker | `python3 scripts/tracker-counts.py --check` | ✅ **open 61 / done 129 (+1 refuted)** |

⭐ **Did any suite need a re-run to go green? YES — one, and it is not mine.** See §2.
⭐ The five `--no-build` suites were run in parallel, one log each.

---

## 2. ⚠ The `Fdp.Toolkits.Tests` failure — **a DIFFERENT member of the known flaky family**

| | |
|---|---|
| **what failed** | `Fdp.Toolkit.Squad.Tests.DangerAreaProviderTests.FakeDangerAreaProvider_Refresh_ZeroAllocAfterWarmup` |
| **runs** | **1 failure · 0 · 0** on the identical binary |
| ⛔ **not the filed one** | the recorded race is `StatelessGizmoRegistryTests.SC_GZ022_2` — ⚠ **this is a different test in a different namespace**, so *"the known race"* would have been the wrong label |
| ⭐ **what it is** | a **zero-allocation** assertion ⇒ `BP-111`'s class (wall-clock / allocation budgets flaking under full-suite load), not an order or race question |
| **my diff** | touches nothing under `FDP/Toolkits/` or `Fdp.Toolkit.Squad` |
| 📌 **honest claim** | *a non-deterministic budget assertion in an assembly this diff does not touch.* ⛔ Two greens are not proof it is gone, exactly as the coordinator noted for the gizmo race |

---

## 3. What each item did

### `S2` — the struct-size oracle ⇒ **`BP-252`**

| | |
|---|---|
| ⭐⭐ **placement** | `CompileOptions.StructSizeOracle : Func<string,int?>` — the mandate's shape verbatim. ⛔ **No Compiler→Generators project reference**; the compiler stays reflection- and Roslyn-free |
| 🔴 **the defect was sharper than the dispatch said** | the guess is 4 bytes **and** it was flagged `SizeReliable = true` on the `global::` arm — the arm the **editor** persists. The dotted fallback had always stamped `false`. ⇒ since `W4`, `[FieldOffset]` was baked from a guess and a 12-byte struct had the next variable laid **8 bytes inside it** |
| ⭐ **enums too** | `MakeFieldSizeDelegate`, not `MakeDelegate` — a `byte`/`long`-backed enum stops being sized at 4 |
| **gate** | Batch 60's `EmittedStateLayoutTests`, reused as asked. 🔴 revert-goes-red **2 of 2** |

#### ⭐⭐ Did `S2` add a fourth `ComputeStructSize`? **No — and here is the mechanism**

`Hrot.Blueprints.Generators` compiles the **same source file**:

```xml
<Compile Include="..\..\AI\Hrot.AiEditor.Generators\StructSizeResolver.cs"
         Link="Shared\StructSizeResolver.cs" />
```

⇒ `StructSizeResolver.cs` is now **dependency-free by requirement** (Roslyn and nothing else; its two
`Hrot.AiEditor.Persistence` crefs became plain `<c>`).

⚠ **A `ProjectReference` was considered and rejected**: it would make blueprint codegen depend on the
BTree/HSM JSON generator assembly — and transitively `Hrot.AiEditor.Persistence` — for one static
method. Both are already `Analyzer` items on `Hrot.AI.Behaviors`, so it would have *worked*; it buys a
layering edge for nothing a linked file does not give.

#### 📐 What consolidating the remaining three would take *(asked for explicitly)*

| copy | where | why it is not this batch |
|---|---|---|
| `Fbt.SourceGen.BTreeActionGenerator:497` | `FDP/ExtDeps/FastBTree` | ⛔ **`ExtDeps`** — a vendored tree |
| `Fdp.Toolkits.Analyzers.BTreeActionGenerator:529` · `HsmActionGenerator:450` | `FDP/Toolkits` | different ownership tree |
| `BehaviorParameterSizeAnalyzer:81` | `FDP/Toolkits` | same |

⭐ **The mechanism is the same one used here and it is cheap** — one `<Compile Link>` per csproj, no
assembly edge, no analyzer-load risk. ⛔ **What is NOT cheap is the decision**: all four live under
`FDP/`, so it is a cross-tree edit touching the FastBTree and Toolkits analyzers, and the three copies
have drifted in detail (each carries its own `KnownSizes`) — so consolidation is a *reconciliation*,
not a move, and the reconciliation could change a size the analyzers currently accept. 📌 **Estimate:
one focused batch, gated on the Toolkits + Generators suites, with a red-first test asserting all four
agree on a struct that exercises every arm** (`bool`, enum, nested struct, explicit layout).

### `S4` — fixed-list `Capacity` ⇒ **`BP-253`**

⚠⚠ **The dispatch's premise is wrong and this is the correction.** §2 calls the
`StaticTypeRegistry.TryResolve` branch *"designed-but-unbuilt"*. ⛔ **It is built and shipped** —
`LV-1a`, `2026-08-04`: the `Capacity > 0` branch, the `__List_{Elem}_{N}` name and `SizeReliable=false`
are all present, and `Blueprint_Fixed_Collections_TASK_TRACKER.md:151` restates them verbatim.

⭐ **The real defect is the one the OLDER plan named:** *"stop dropping `Capacity` in the fallback."*
Stage 4's AN2 retry rebuilt the type ref from `TypeId` + `IsArray` alone, so a list whose element is a
dotted project type degraded to **one** element. Fixed, plus the wrapper is now sized from the
element's real size (without it, 4×12 bytes declared itself 20 and under-counted the tier budget by 32).

#### 🛑 The STOP condition: **does a sibling design doc contradict §3? NO**

| doc | opened | verdict |
|---|---|---|
| `Architect_Question_19_Fixed_Capacity_List_Variables.md` | ✅ | ✅ same wrapper shape (`__List_{Elem}_{N}`, `int Count` + `[InlineArray]`) |
| `Architect_Question_21_Action_DTO_Fixed_Lists.md` | ✅ | ✅ names the `__List_…` shape as the FC-2 one; nothing on resolution |
| `Blueprint_Fixed_List_Variables.md` | ✅ | ✅ `BP1504`, `Capacity`/`InitialLength` persistence — consistent |
| `Blueprint_Fixed_Collections_Design.md` | ✅ | ✅ `:96` names `SizeReliable=false` as heavy-foundation, consistent |
| `Blueprint_Fixed_Collections_TASK_TRACKER.md` | ✅ | ⭐ **`:151-184` restates §3 line for line and records it SHIPPED** |
| `Fixed_Collections_RESUME.md` | ✅ | ✅ `:50` repeats `SizeReliable=false` |

### `S3` — the `MarshalFromBytes` struct arm ⇒ **`BP-254`**, closes **`BP-01`**

📐 **The `BP-01` type count actually closed: 7 of 18**, and the gate printed both failure modes before
the fix:

```
7 of 18 offerable types cannot be shown:
  • Vector2/3/4, Quaternion   MarshalFromBytes returned raw bytes  (hex)
  • FixedString32/64/128      ResolveType returned null            (field SKIPPED entirely)
```

⭐ The second mode is worse than hex and was invisible. ⭐ **`Type.GetType` did find the vectors** —
only the decode was missing — so the assembly walk was load-bearing for exactly the three FixedStrings.
⛔ **`MemoryMarshal`, not `Marshal.PtrToStructure`**: the bytes are the managed layout the generated
writer stores, and the two models differ on `bool`. 🔴 revert-goes-red **2 of 4**.

⚠ **One existing test's REASON changed** — `MarshalFromBytes_UnknownType_ReturnsByteArray` held because
`DateTime` was *"not in the switch"*; it now holds because 4 bytes are not a `DateTime`'s 8. Left on
`DateTime` deliberately and the doc says why: a type that *would* decode at the right length is a
sharper witness for the bound.

### `S5` — ONE offerable type list ⇒ **`BP-255`**

⚠⚠ **The handoff contradicts itself on whether `S5` is in this batch** — §3b adds it
(*"ADDED 2026-08-16"*), §4 still lists it under *"NOT in this batch"*. ⭐ **Built it:** the re-dispatch
header says *"`S5` is now IN this batch (§3b)"* and is the newer edit. 📌 Flagging rather than
silently choosing.

⭐ **Both consumers now return the same object** (`Assert.Same`, not `Assert.Equal` — two lists holding
equal strings today is exactly the state this closes). 📐 **The gap was two-way, which the dispatch did
not say:** the parameter combo lacked structs *and* `Fdp.Core.Entity`; the variable modal lacked
`sbyte short ushort uint long ulong Vector4 Quaternion`.

⭐⭐ **The `U-8` trap fired exactly as predicted, and the fix removed a third mirror.**
`VariableCreateModal.ElementByteSize` was a hand-written switch that *"mirrors the selectable-type
set"* in its own words ⇒ widening the set would have given the eight new primitives a **0-byte budget
line**. It now asks the registry for the size, so the budget cannot disagree with the bytes the
variable occupies. ✅ `Modal_BudgetHelper_KnowsEverySelectableUnmanagedElementSize` stays green, and a
sharper sibling was added.

⚠ **The picker now writes canonical FQNs** (the union's own `NoShortNamesAreOffered` rail). Aliases
stay valid input — `IndexOfTypeId` resolves both onto one entry. `BP-87`/`BP-114`'s assertions moved
**with** the contract: `BP-87`'s `Assert.Same` retargeted, its all-resolve claim narrowed to the
primitives *(a discovered struct is accepted by Stage 4's dotted path, not by the registry table)* with
a pointer to `TypeChoiceUnionTests.EveryOfferedTypeCompiles`, which is strictly stronger.
🔴 revert-goes-red **18 of 33** in the affected classes.

---

## 4. ⭐ IDs allocated *(rule 5)*

**`BP-252`** *(`S2`)* · **`BP-253`** *(`S4`)* · **`BP-254`** *(`S3`)* · **`BP-255`** *(`S5`)`.
⛔ **No new diagnostic codes.** ⇒ next free: rows **`BP-256+`**, blueprint diagnostics **`BP1675+`**,
analyzer diagnostics **`BHU_022+`**.

---

## 5. 🔴🔴 **`DEBT-AIB-012` DOES NOT EXIST — the debt was described, suggested, and never filed**

⛔⛔ **Correcting the handoff, `PLAN_Remaining_Work.md`, my Batch 64 sweep, and my own `S2` commit
message — all four cite it.** 📐 Measured:

| where | what it says |
|---|---|
| 📄 `reports/BATCH-03-REPORT.md:100` | *"**`DEBT-AIB-012` (suggested).** The `StructSizeResolver` logic is a third copy of `ComputeStructSize`…"* — ⭐ **the word is `(suggested)`** |
| 📄 `.dev/btree-ai-action-binding/DEBT-TRACKER.md` | *"**`DEBT-AIB-012` — inspector multi-DTO read.** **RESOLVED BATCH-05**"* — ⛔ **a different item, already closed** |

⇒ ⭐⭐ **The suggested number was already taken, nobody reconciled it, and the row was never created.**
⛔ **Citing the triplication as `DEBT-AIB-012` points at a resolved row about something else** — which
reads as *"filed and known"* when it is *"described once in a report tail and dropped."*
✅ **Corrected in the code comments, the csproj comment and `BP-252`** to cite the report line instead.

⚠ **This makes the carried question worse, not better.** It is not only *"filed debt never surfaced"* —
it is *"debt that never got a row at all"*, and an id collision is exactly the failure `.claude/CLAUDE.md`
rule 3/3a exists to prevent, occurring **inside `.dev/`** where no rule reaches.

### 📐 The blast-radius count — measured, and it is a map, not a triage

| | |
|---|---|
| **distinct `DEBT-*` ids across `.dev/` + `.dev/_DONE/`** | **51** |
| **by prefix** | `AIB` **30** · `BCP` 6 · `MVE` 4 · `BF` 4 · `TEST` 3 · `ARCH` 2 · `UX` 1 · `NOTE` 1 |
| ⭐ **there ARE dedicated trackers** — 54 `DEBT-TRACKER.md` files | but **only 6 are non-empty**: `btree-ai-action-binding` (30) · `blueprint-finalize` (4) · `persistence-unification` (1) · `ai-hsm-btree-vis-edit-2` (1) · `_DONE/behav-diag-1` (1) · `_DONE/blueprint-integ-1` (1) |
| ⭐⭐ **the one in our blast radius is `AIB`** | **30 ids, ~8 marked resolved/verified** ⇒ **roughly 22 open**, in the programme that owns blueprint↔BTree action binding — the exact seam Track B and `W1`–`W13` work in |
| ⛔ **`docs/` references almost none of them** | the only mentions are the ones this programme just added, plus `BTree_AiActionParameterBinding_Detailed_Design_Status.md` |

⚠ **A count is not a triage.** Deciding which of the ~22 are still live means reading each claim
against `HEAD`, and sweeping `.dev/` is **coordinator work** by the ruling recorded this run.

📌 **Recommendation: one coordinator sweep, output = a table of `DEBT-*` id → one-line claim →
still-live? → in our blast radius?** ⭐ Cheap — the ids are grep-able and `DEBT-TRACKER.md` already
states the claims — and worth more than any single fix. ⭐ **Start with `btree-ai-action-binding`:** it
holds 30 of the 51, it is the seam this programme is working in, and it is the one that just produced a
debt with no row.
