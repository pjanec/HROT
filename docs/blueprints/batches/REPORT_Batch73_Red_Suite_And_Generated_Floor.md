# REPORT — Batch 73: **a red suite, a floor that sees generated code, and `E3` escalated again**

> **Branch** `claude/hrot-implementation-j1jvin` · **base** `a6fc739` *(dispatch `d98d8ab61`)*
> **Rule 7** re-synced at start · **rule 4** re-fetched — nothing new on the coordinator branch.
> ⭐ **Items 1, 2 and 4 landed. Item 3 escalated a second time, with the consumer census §3 demanded.**

---

## 0. 🔴 Goldens — **blueprint set unchanged.**

| baseline | moved? |
|---|---|
| ⭐ **blueprint** *(`persistence-shape`, 43 `Emit/*.cs.txt`, `StructureHash`)* | ⛔ **no, in any commit** |
| ⭐ **HSM shape + emit** | ⛔ **no** — item 4 deliberately preserves the shipped order (§5) |
| ⭐ **generated-code** *(NEW, item 2)* | ✅ **created** in `995bcf8` — `Golden/Generated/HsmActionRegistrar.g.cs.txt` |
| ⭐ **BTree shape** | ⛔ no |

---

## 1. Gates — one row per gate, verbatim command, result

| gate | command | result |
|---|---|---|
| solution build | `dotnet build IOS-IG-SimHost.sln -t:Rebuild -v q --nologo` | ✅ **0 errors / 69 warnings** |
| Blueprints | `dotnet test …/Hrot.Blueprints.Tests.csproj --no-build -v q --nologo` | ✅ **3690 / 3680 / 0 / 10** |
| AiShared | `dotnet test …/Hrot.Editor.AiShared.Tests.csproj --no-build -v q --nologo` | ✅ **1280 / 1280 / 0 / 0** |
| BTree.Editor | `dotnet test …/Hrot.BTree.Editor.Tests.csproj --no-build -v q --nologo` | ✅ **615 / 615 / 0 / 0** |
| Breakpoints | `dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/*.csproj --no-build -v q --nologo` | ✅ **134 / 134 / 0 / 0** |
| **Generators** | `dotnet test Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/*.csproj --no-build -v q --nologo` | ✅ **249 / 249 / 0 / 0** *(**+4**)* |
| Hsm.Editor | `dotnet test …/Hrot.Hsm.Editor.Tests.csproj --no-build -v q --nologo` | ✅ **543 / 543 / 0 / 0** |
| AiEditor.Persistence | `dotnet test …/Hrot.AiEditor.Persistence.Tests.csproj --no-build -v q --nologo` | ✅ **136 / 136 / 0 / 0** |
| 🔴 **Examples.Scenarios** *(NEW ROW — item 1)* | `dotnet test FDP/Examples/Fdp.Examples.Scenarios.Tests/*.csproj --no-build -v q --nologo` | ✅ **68 / 56 / 0 / 12 skipped** *(was 12 RED)* |
| ⭐ **Examples.UrbanCombat** | `dotnet test FDP/Examples/Fdp.Examples.UrbanCombat.Tests/*.csproj --no-build -v q --nologo` | ✅ **29 / 29 / 0 / 0** |
| Toolkits *(sample 1)* | `dotnet test …/Fdp.Toolkits.Tests.csproj --no-build -v q --nologo` | 🔴 **1 failed** — `GizmoRegistryTests.SC_GZ004_2` |
| Toolkits *(sample 2)* | *same* | ✅ **1964 / 1964** |
| ⭐ **Toolkits *(the red, isolated)*** | `… --filter "…GizmoRegistryTests.SC_GZ004_2…"` | ✅ **1 / 1** ⇒ **`DEBT-AIB-030`** |
| NodeEdit Core | `dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/NodeEditor.Core.Tests.csproj -v q --nologo` | ✅ **208 / 208** ⭐ **no `--no-build`** |
| NodeEdit UI | `dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.UI.Tests/NodeEditor.UI.Tests.csproj -v q --nologo` | ✅ **131 / 131** ⭐ **no `--no-build`** |
| tracker | `python3 scripts/tracker-counts.py --check` | ✅ **open 61 / done 165 (+1 refuted)** |

⭐ **Examples suites run, named:** `Fdp.Examples.Scenarios.Tests` **and** `Fdp.Examples.UrbanCombat.Tests`.

---

## 2. 🔴🔴 Item 1 — **the cause of each cluster, and the handoff's hypothesis is wrong**

### ⚠ First: the harness could not say WHY

📐 `ScenarioSubsystem` catches `ScenarioFailureException`, logs the diagnostic and calls
**`ExitWith(1)` — the same code for every phase**. ⇒ a failing test could only report *"exit 1"*.
⛔ **A red that names no cause trains everyone to ignore the gate.** ⭐ The subsystem now retains
`LastFailure` and the harness surfaces it; that produced both diagnoses in **one run each**.

### 📐 Two clusters, two causes, neither in this programme

| cluster | measured message | attributed to |
|---|---|---|
| `ComponentDamageScenarioTests` **× 5** | *"Phase 2 FAILED tick=21: health=100 still at max=100 after hit at tick 20"* | **damage / event pipeline** |
| `DistributedTankScenarioPhaseATests` **× 7** | *"Phase B3 FAILED tick=25: ghost not promoted in time"* | **DDS replication / ghost promotion** |

### 🔴🔴 **Does `E6`'s fix reach these scenarios? IT CANNOT.**

⭐⭐ `ComponentDamage_Phase4_LocomotionCleared_ByHSM` was read as *"the HSM did not clear
locomotion"* — `E6`'s symptom. ⛔ **It is not.** The scenario throws at **phase 2 (tick 21)**, and
`ExitWith(1)` ends the run ⇒ **tick 25 never happens** and phase 4 is **never evaluated**.
⇒ **the test is a casualty of phase 2, not evidence of an HSM defect** — and it was so before `E6` too.

⭐ **12 fixed: 0. 12 quarantined.** Per the STOP, the causes are scenario/DDS issues outside this
programme, so they are **not** fixed. Each skip carries the phase, the measured message, the attributed
subsystem, and the note that they are identical on `5d01a5c`.

---

## 3. ⭐⭐⭐ Item 2 — **the acceptance test PASSES**

> 🔴 **Asked for explicitly: reverting `E6`'s FQN key must redden this tier.**

⭐ **It does** — `TheGeneratedRegistrarIsUnchanged` fails at **line 18**, the `RegisterAction` id line.
The baseline carries **`16038`** (the FQN's hash); the simple-name key would emit **`32291`**. A second
test derives that id **independently of the generated text**, so the two sides never agree by
construction.

⭐ **Determinism: verified across two processes**, not just in one.

### ⚠⚠ What a synthesized `Compilation` could not reach — **named, per the STOP**

| | |
|---|---|
| ⭐ **HSM analyzer — fully reached** | it needs only the attribute shapes, which is why the acceptance test lands here |
| ⛔ **BTree generator — NOT reached** | it builds a `structSizeResolver` from the **semantic model** and runs `BTreeDeactivatorScanner.Scan` over **real method bodies**. A synthesized compilation would emit fallback output ⇒ **a baseline of what production never produces**, the trap `GoldenCorpus.Options()` already records |

⇒ ⭐ **BTree's emit tier needs the REAL solution compilation, not a driver over stubs.** Stated as a
measurement so the next batch can size it; ⭐ **invert the test when it lands.**

---

## 4. ⛔⛔ Item 3 — `E3` **escalated a second time, with the census §3 demanded**

> ⭐ §3 pre-authorised this: *"if `E3` is bigger than one commit, LAND ITEMS 1, 2 AND 4 AND ESCALATE IT
> AGAIN — that is a good batch, not a failed one."* ⚠ And: *"if the kernel change forces a public API
> break in `Fhsm`, name every consumer before changing it."* 📐 **It does. Here they are.**

| surface | measured |
|---|---|
| ⭐ **attributed methods** | **55** `[HsmAction]`/`[HsmGuard]` across **25 directories** — including **`FDP/ExtDeps/FastHSM`'s own demos, examples and tests**, **both `FDP/Examples` projects** *(in the solution)*, `Hrot.AI.Behaviors`, and editor/test fixtures |
| ⭐ **kernel call sites** | **13** `ExecuteAction` / `EvaluateGuard` in `Fhsm.Kernel` |
| ⭐⭐ **emitters producing the fixed `delegate*` shape** | **FIVE** — `HsmActionDispatcher` · the analyzer's `HsmActionGenerator` · ⚠ **`CSharpEmitter`'s blueprint `HsmActivity`/`HsmGuard` registration** *(the blueprint side registers HSM thunks too)* · two FastHSM test helpers |

⇒ ⛔ **Widening the delegate is an ABI break reaching every one of those**, *on top of* the storage move
(per-occurrence bytes from the partition allocator under `ComputeStatefulSlotKey(…, Scope.Node, …)`).

⭐ **The precondition is now satisfied**: item 2 is the gate that watches thunk emission, and its
acceptance test proves it reaches the ids. ⭐ **The three gap tests from Batch 72 stand, to be
INVERTED when `E3` lands.**

⛔ **The params-base half was not folded in** — it did not come free, as measured last batch.

---

## 5. ⭐ Item 4 — ordered by construction; **the baseline does NOT move, and that is deliberate**

⛔ The slot array came from `Dictionary<int,…>.Values`. Insert-only `Dictionary<int,V>` **does**
enumerate in insertion order **in practice** — ⚠ **a BCL convention, not a guarantee**, and one
`Remove` breaks it. 📌 A baseline over convention-ordered output can move for a reason nobody changed,
which trains everyone to regenerate — ⭐ *and a gate that is routinely regenerated is not a gate.*

⭐ A `HashSet` dedups and a `List` carries the order: the two jobs the dictionary did at once are now
separate and **neither is implicit**.

⚠ **The handoff expected the HSM emit baseline to move. It does not** — the fix **preserves** the
shipped declaration order on purpose. ⛔ Making it move would have meant choosing a different order for
no reason beyond proving the diff.

---

## 6. ⭐ IDs allocated *(rule 5)*

| kind | allocated |
|---|---|
| tracker rows | ⭐ **`BP-288` · `BP-289` · `BP-290` · `BP-291`** |
| diagnostics · architect questions | ⛔ none |

---

## 7. ⭐⭐ Debt rows touched

| row | what happened |
|---|---|
| ⚠ **`DEBT-AIB-030`** | one gizmo red on sample 1, green on sample 2, green under `--filter`. **Not signal** |

⛔ **No other partition row touched.**

---

## 8. Not done

⛔ **`E3`'s fix** *(§4, escalated with the census)* · **BTree's emit tier** *(needs the real solution
compilation — §3)* · blueprint **multi-occurrence** *(user-DEFERRED)* · the 12 quarantined scenario
tests *(causes outside this programme — §2)* · `E5` · `E7a` · `E7b`'s runtime half · `BP-281` · the
`InspectorWindow` "STATIC PARAMETERS" retirement · the Track C visual check.
