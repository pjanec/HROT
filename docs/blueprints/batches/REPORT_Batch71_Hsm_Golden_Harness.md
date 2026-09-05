# REPORT — Batch 71: **give Track E a floor** — `E0` · backfill `E1`/`E2` · `E6`/`W9` · `E7b`

> **Branch** `claude/hrot-implementation-j1jvin` · **base** `20368ad` *(coordinator dispatch `aefb2f39f`)*
> **Rule 7** re-synced at start · **rule 4** re-fetched before the final commit — ⭐ **nothing new on the
> coordinator branch.** ⭐ **Order kept: item 1 first.** ⚠ **Item 2 is PARTIAL by decision — §4.**

---

## 0. 🔴 The BLUEPRINT golden set — **unchanged.**

`persistence-shape.txt` · the 43 `Emit/*.cs.txt` · `StructureHash` — ⭐ **all untouched.** This batch
**adds** a corpus and shares no harness code with the existing one.

| baseline | commit | moved? |
|---|---|---|
| ⭐ **blueprint** *(43 assets)* | — | ⛔ **no, in any commit** |
| ⭐ **HSM shape + emit** *(new)* | `ca605c1` *(item 1)* | ✅ **created** — 1 shape file + 8 emitted-text files |
| ⭐ **HSM emit** | `7ffa9a4` *(item 2)* | ⛔ **no** — item 2 is behaviour-preserving, **and that is the point of landing it before the decision** |

---

## 1. Gates — one row per gate, verbatim command, result

| gate | command | result |
|---|---|---|
| solution build | `dotnet build IOS-IG-SimHost.sln -t:Rebuild -v q --nologo` | ✅ **0 errors / 69 warnings** *(baseline exactly)* |
| Blueprints | `dotnet test …/Hrot.Blueprints.Tests.csproj --no-build -v q --nologo` | ✅ **3690 / 3680 / 0 / 10** |
| AiShared | `dotnet test …/Hrot.Editor.AiShared.Tests.csproj --no-build -v q --nologo` | ✅ **1280 / 1280 / 0 / 0** |
| BTree.Editor | `dotnet test …/Hrot.BTree.Editor.Tests.csproj --no-build -v q --nologo` | ✅ **615 / 615 / 0 / 0** |
| Breakpoints | `dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/*.csproj --no-build -v q --nologo` | ✅ **134 / 134 / 0 / 0** |
| **Generators** *(the diff's home)* | `dotnet test Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/*.csproj --no-build -v q --nologo` | ✅ **228 / 228 / 0 / 0** *(**+20**)* |
| **Hsm.Editor** | `dotnet test …/Hrot.Hsm.Editor.Tests.csproj --no-build -v q --nologo` | ✅ **543 / 543 / 0 / 0** *(**+12**)* |
| AiEditor.Persistence | `dotnet test …/Hrot.AiEditor.Persistence.Tests.csproj --no-build -v q --nologo` | ✅ **136 / 136 / 0 / 0** |
| Toolkits *(sample 1)* | `dotnet test …/Fdp.Toolkits.Tests.csproj --no-build -v q --nologo` | 🔴 **1 failed** — `GizmoRegistryTests.SC_GZ004_2_Register_UnregisteredComponent_Throws` |
| Toolkits *(sample 2)* | *same* | ✅ **1964 / 1964** |
| ⭐ **Toolkits *(the red, isolated)*** | `… --filter "FullyQualifiedName~GizmoRegistryTests.SC_GZ004_2_Register_UnregisteredComponent_Throws"` | ✅ **1 / 1** ⇒ **`DEBT-AIB-030`** |
| NodeEdit Core | `dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/NodeEditor.Core.Tests.csproj -v q --nologo` | ✅ **208 / 208** ⭐ **no `--no-build`** |
| NodeEdit UI | `dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.UI.Tests/NodeEditor.UI.Tests.csproj -v q --nologo` | ✅ **131 / 131** ⭐ **no `--no-build`** |
| tracker | `python3 scripts/tracker-counts.py --check` | ✅ **open 61 / done 157 (+1 refuted)** |

⭐⭐ **`DEBT-AIB-030` widened exactly as you predicted** — the red is a **gizmo** registry test, not an AI
one, confirming *"process-global registry state GENERALLY."* Green in isolation; **not signal**.

---

## 2. ⭐⭐⭐ Item 1 — **can the new gate FAIL? YES, and it is asserted, not claimed**

> 🔴 **Asked first, because a new green gate proves nothing.**

`BothTiersRedden_WhenAnAssetChanges` mutates a corpus asset **in memory** and shows:

| mutation | shape tier | emit tier |
|---|---|---|
| rename a blackboard variable | ✅ **hash moves** | ✅ **registrar text moves** |
| ⭐⭐ rename a **state** | ⛔ *(it does move, but…)* | ✅ **moves** |

⭐ **The asymmetry is the argument for the second tier**: the shape file can tell you *an asset
changed*; only stored text tells you **which line**. ⛔ And an **id** change — `E6`'s whole subject —
is not in the asset at all, so the shape tier could never see it.

### ⭐ What was seeded beyond the two shipped assets, and why those features

📐 **The shipped corpus is two assets and NEITHER has a managed blackboard**, so `E1`/`E2` had nothing
to be backfilled into.

| seed | covers | for |
|---|---|---|
| ⭐ **`HsmVariableShowcase`** | `Role=Input` *(with a default)* · `Role=State` @ **Behavior** · `Role=State` @ **Entity** | **`E1`/`E2` backfill** — the stateful-slot manifest is now IN the baseline |
| ⭐ **`HsmOrthogonalRegions`** | two orthogonal regions, **both reaching one Behavior-scoped slot** | **`E3`** — ⭐ the gate exists **before** the fix |

⚠ **They are CORPUS, not fixtures** — they live with the corpus and the **production generator
compiles them**, so the solution build is a second gate on their validity.
⛔ **`E6`'s same-simple-name pair is deliberately NOT seeded**: it is a duplicate key in a dictionary
*initializer* ⇒ `ArgumentException` at type init ⇒ it must land **with** the fix, not before it.

### ⭐ Did generalising over asset kind cost anything? — **NO. One line: it is three delegates.**

`AiAssetKind` = *(glob suffix, canonicalize, emit)*, all three already static DTO-in/string-out
functions on both hosts. ⇒ ⭐⭐ **BTree's 26 ungated assets are a REGISTRATION, not a rewrite** — a line
item, not a leftover. ⛔ Not seeded here, as instructed.

### ⭐ HSM emitter non-determinism — **none found**

Asserted in-process per asset, **and verified across two separate processes** by regenerating twice and
comparing the whole baseline. ⚠ **One ordering is deterministic by implementation detail rather than by
construction**: `HsmBridgeEmitCore` emits its slot array by iterating `Dictionary<int,…>.Values`.
Insert-only `Dictionary<int,V>` enumerates in insertion order in practice — ⛔ **but that is a
convention, not a guarantee**, and a single removal would break it. 📌 **Flagged, not changed** — fixing
it in item 1 would have moved the baseline it was creating.

---

## 3. 🔴🔴 Item 1 also **found something the moment the floor existed**

⭐⭐ **An HSM `Role=Input` variable reaches NO emitted output.** Not the topology core, not the
registrar. `HsmBridgeEmitCore` emits a slot manifest and **no params handling of any kind** — ⛔ there
is **no HSM counterpart to the BTree bridge's `ParseParams`**, so `DEBT-AIB-021`'s fix has nothing to
fix on this host.

⚠ **Asserted deliberately as a GAP, and named as one** — Batch 70's rule: *"a test asserting the
absence of a feature is indistinguishable from a test asserting a bug."* ⇒ **invert it, do not delete
it**, when the HSM params path is built. ⛔ Out of `E0`'s scope; filed as **`BP-281`**.

---

## 4. ⛔⛔ Item 2 — **STOP. `E6` is not the defect the plan describes.**

> ⭐ **The standing ask, used exactly as written: this one changes the plan, so it is escalated rather
> than decided.**

📐 **Measured — there is a THIRD site:**

| # | site | hashes |
|---|---|---|
| 1 | `HsmActionGenerator` → dispatcher table | the **simple** name |
| 2 | `HsmActionGenerator` → `RegisterAll` | the **simple** name |
| 🔴 **3** | **`Fhsm.Compiler.HsmFlattener`** | ⭐ **whatever string the ASSET stored** |

⇒ and **`HsmEmitCore` stores the FQN**: `.OnEntry("Hrot.AI.Behaviors.CgfHsmNodes.StubIdle")`.

🔴🔴 **So the blob addresses `16038` while the registrar registers `32291`, and
`HsmActionDispatcher.ExecuteAction` is a `TryGetValue` MISS.** The shipped `HsmShowcase` entry and
activity actions **silently do nothing** — no crash, no log, exactly `W3`'s counter-allocated-stub
shape. ⭐⭐ **True today WITHOUT any collision**, and confirmed against the **real compiled blob** rather
than a recomputation of itself.

### 🔴 Why I did not just apply the handoff's fix

⭐ Hashing the `FullName` fixes the JSON path **and** separates same-simple-names — both halves at once.
⛔ **But it inverts the breakage onto the other shipped consumer:** `FDP/Examples` build machines by
hand and address by **simple name** (`.Activity("Activity_Cruise")`), which is **correct under today's
key** and wrong under an FQN key. Those projects are in the solution.

⇒ ⛔ **the key string reaches outside Track E ⇒ plan-level ⇒ escalated.** 📌 Two coherent designs, and
picking is yours:

| | fixes JSON path | kills the collision | breaks |
|---|---|---|---|
| **(A) FQN everywhere** | ✅ | ✅ | **4 call sites** in `FDP/Examples` |
| **(B) simple name everywhere** *(`HsmEmitCore` emits the simple name)* | ✅ | ⛔ **no** | nothing |

⭐ **My lean is (A)** — (B) leaves `E6` unfixed — but it is a decision about the FastHSM boundary
convention, not about Track E.

### ⭐ What DID land, because it is the precondition for either choice

⭐⭐ **`HsmActionKey` — the ONE place the id is computed.** The **seven** sites that each spelled out
the FNV-1a now call `ForActionName` / `ForCompoundKey` / `ForExitCleanup`; the private duplicate is
gone; `"ExitCleanup_"` gets one home. 📌 *"Two call sites each computing the same key is the
duplication that produces a disagreement"* — **that mechanism is fixed regardless of which string
wins.**

⭐ Plus **`HsmActionIdAgreementTests`** — the whole measurement, in code, so the decision is made
against a measurement rather than a memory. ⚠ **Invert those tests when it is decided.**

⚠ **No revert-goes-red for the shared resolver**, and I am saying so rather than inventing one: it is
behaviour-preserving, its only observable is the baseline, and **the baseline is unchanged.** The
behavioural rail is the disagreement test, which fails the moment the identity is fixed.

---

## 5. `E7b` — **the count half, and the runtime half is blocked on something else**

> ⭐ **Asked: did the runtime half need `E3`? — NO, and that guess was wrong.**

📐 **`ExpressionTargetField` is emitted NOWHERE** — zero occurrences in `HsmEmitCore` **and**
`HsmBridgeEmitCore`. ⇒ it never reaches the blob, so **there are no bytes to assert**. ⛔ Not blocked on
`E3`'s occurrence key; blocked on **the field being emitted at all**, which is a bigger piece than this
item.

⭐ **The count half is done and real**: both transition kinds *(a global transition is excluded from the
validator's cross-region rule because it belongs to no region — ⛔ **but it is still a writer**)*, one
shared predicate with `HsmValidator.IsLocallyBoundTo`, case-insensitive to match. 🔴 **Revert-goes-red:
the hardcoded `0` reddens 4 of 12.**

---

## 6. ⭐ IDs allocated *(rule 5)*

| kind | allocated |
|---|---|
| tracker rows | ⭐ **`BP-280` · `BP-281` · `BP-282` · `BP-283`** |
| blueprint / analyzer diagnostics · architect questions | ⛔ none |

---

## 7. ⭐⭐ Debt rows touched

| row | what happened |
|---|---|
| ⭐ **`DEBT-AIB-030`** | ⚠ **widened again, as you predicted** — this batch's red is a **gizmo** registry test, not an AI one. Green under `--filter`. **Not fixed, not signal** |
| ⚠ **`DEBT-AIB-029`** *(the direct-children-only walk)* | ⛔ **did NOT come up.** `E7b`'s count walks `AllTransitions` / `AllGlobalTransitions`, which are already flat asset-wide lists — no hierarchy walk involved |

⛔ **No other row on the partition list was touched.**

---

## 8. What this batch did **not** do

**`E3`** · **`E5`** *(needs `-028`(a))* · **`E7a`** *(`IHostVariableAccess` still receives `null`)* ·
⛔ **`E6`'s identity fix — escalated, §4** · blueprint **multi-occurrence** *(blocked on
`Architect_Question_34`)* · the `InspectorWindow` "STATIC PARAMETERS" retirement · the Track C **visual
check** · ⛔ **seeding BTree into the new harness** *(cost measured — §2 — deliberately not done)* ·
⛔ **the HSM params path** *(`BP-281`)*.
