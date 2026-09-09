# REPORT — Batch 77: `E5` **STOPPED** · the `E3` tripwire · `BP-304`

> 📌 **Started at `49efe83`** *(rule 1b marker, pushed before any code; dispatch sha `b66b744` was
> already an ancestor)*. ⭐ **Rule 4/7 re-check at the end: no new coordinator commits since dispatch.**
> ⭐ **IDs allocated: `BP-305`, `BP-306`, `BP-307`, `BP-308` · `Architect_Question_36`**
> *(next free `N` across **all** branches — max was 35 on the coordinator, implementation and
> cross-host branches alike)*.
> ⭐ **`DEBT-AIB` rows touched: `-030` only** *(observed, not changed)*.

---

## 0. ⭐⭐⭐ The headline — **two of your premises failed, and one of them was mine**

| | |
|---|---|
| ⛔⛔⛔ **`E5` is STOPPED** | not on the STOP you named *(`AttachToEntity` / a breaking FastHSM change)*, but one upstream of it: ⭐⭐ **there is ONE brain per entity and nowhere to run a hosted child**, and ⛔ **resolve has no input for a BTree child.** → **`Q36`**, **`BP-308`** |
| ⛔⛔ **the `E3` census was WRONG, and it was mine** | Batch 75's *"zero DTO-bound HSM thunks in any binary"* rested on *"`Fdp.Toolkits` does not run `HsmActionGenerator`"*. ⭐ **It does** — and `Fdp.Toolkits.dll` holds **four** of them. → **`BP-305`** |
| ✅ **item 2 delivered anyway**, with the corrected subject | and **shown to fail on real code**, not only synthetically |
| ✅ **item 3 delivered** | ⭐ **both reds had ONE cause**, including the one you called unexplained. **`Fhsm.Tests` 300 / 300, 0 skipped** |

---

## 1. 🔴 Item 1 — `E5`: **STOPPED**, escalated as `Architect_Question_36`

⭐ **Your ground-truth table is correct on everything it covers** — storage route, the stale `E3`
dependency, `SubtreeAssetId` persisting, rules 8/8b live. ⛔ **What it does not cover is the two steps
`E5` takes FIRST**, and both are blocked.

### 📐 ① ONE BRAIN PER ENTITY — *there is nowhere to put the child*

```csharp
// Behavior/Components/BehaviorComponents.cs:44
public struct BehaviorState { public int ActiveBehaviorHash; public uint InstanceId; public byte BrainTier; }
```

⭐ **One hash, one tier.** `BTreeTickSystem:83` and `HsmTickSystem:158` both key off that single field.
⇒ ⛔⛔ **a state hosting a subtree has no second brain slot to run it in.** ⭐ `Q34` §7's *provision by
KEY* answers **storage** — it does not answer **which brain ticks the child**, and nothing else does
either.

### 📐 ② RESOLVE HAS NO INPUT for a BTree child

| | measured |
|---|---|
| HSM registers under | `DeterministicIdFromGuid(dto.AssetId)` — `HsmBridgeEmitCore:131,152` |
| BTree registers under | `BehaviorHash.FromName(name)` — `BTreeBridgeEmitCore:446` |
| `BehaviorRegistry` indexes | **name** (`_nameToId`) and **int id** (`_definitions`) — ⛔ **no asset-id index at all** *(`:175-176`)* |
| ⇒ | ⭐ **an HSM child resolves from a Guid by accident; a BTree child cannot resolve at all** — and *HSM + BTree on one entity* is the case `E5` exists for |

⭐⭐ **`BTreeBridgeEmitCore:349` computes `int behaviorId = DeterministicIdFromGuid(dto.AssetId);` and
never uses it** — dead, commented *"the deterministic behavior ID"*, and **exactly** the value that
would have made the two id spaces agree. ⛔ **It reads as the mechanism when it is not one.**

### ⭐⭐⭐ And the route the codebase ALREADY SHIPS is **by NAME**

| | |
|---|---|
| `BehaviorTreeBlob.SubtreeAssetIds` | is a **`string[]`** — of **names**; the field name misleads |
| `BTreeEmitCore:836` | emits **`p.SubtreeName`** as the subtree reference |
| `BTreeSubtreePayload` | carries **`SubtreeAssetId` + `SubtreeName` + `IsResolved`** |
| ⛔ **`StateNode` / `StateNodeDto`** | carry the **Guid alone** |

⚠⚠ **That half-pair is MINE.** Batch 75 persisted `SubtreeAssetId` and did not carry the name across.
⭐ **Nothing read it then, so nothing said which half was the resolving half** — the `BehaviorTreeBlob`
measurement is what says it, and it was not made until now.

### ⭐ Why I did not just pick

⭐⭐ **One-brain-per-entity is an invariant**, and choosing what breaks it is not an implementation
detail — it is the *"truly architectural issue with large blast radius"* these documents exist to
isolate. 📄 **`Architect_Question_36`** states both sub-questions with options and leans:

| | lean |
|---|---|
| **`Q36-A`** — which brain runs the child | ⭐ **`B`: the HOST ticks it inline** — no second component, no new system, no ordering question; `Q34` §7 stays satisfied by construction |
| **`Q36-B`** — what resolve reads | ⭐ **`A`: mirror the BTree pair** — add `SubtreeName` beside the Guid. **One mechanism with the shipped BTree subtree path**; the name resolves, the Guid survives a rename |

⛔ **`Q33` §1.5.4 rules out the cheap option** *(swapping `ActiveBehaviorHash`)*: *"a hosted subtree
does NOT block its state's transitions"*, and a swapped brain means the host is not running.

⇒ ⭐ **Nothing was half-built.** No `AttachToEntity`, no partial lifecycle, no FastHSM change.

---

## 2. ✅ Item 2 — the `E3` tripwire, **and the census it corrects**

### ⛔⛔ First: the census was wrong

📐 **`Fdp.Toolkits.csproj:57-59` references `Fdp.Toolkits.Analyzers` with
`OutputItemType="Analyzer"`.** A fresh `-t:Rebuild` puts four thunks in `Fdp.Toolkits.dll`:

```
Action_AttachInstanceBlueprint_At0   Action_RemoveInstanceBlueprint_At0
Action_ReplaceInstanceBlueprint_At0  Action_AlertNearbyUnits_At0
```

⭐⭐ **The right claim is "zero REGISTERED", not "zero EMITTED".** They are inert for **two** separate
reasons, and the baseline records both: nothing calls
`Fdp.Toolkits.Generated.HsmActionRegistrar.RegisterAll()` *(the only production `RegisterAll` sites are
`Fdp.Examples.UrbanCombat` and `Fdp.Examples.Scenarios`, neither of which declares a `[SharedAi*]`)*,
and **all four sit at offset `0`**. ⚠ **They carry the attribute for the BTREE host** — the HSM thunk
is incidental, because both generators key on the same attribute.

⇒ ⭐ **Your framing survives** — the hazard is still latent, nothing is corrupting anything. ⛔ **But
"manufacture no subject" was already moot: a subject exists, it is simply unregistered.**

### 🛠 What was built

| | |
|---|---|
| ⭐⭐ **the project set is DERIVED** | a `ProjectReference` to `Fdp.Toolkits.Analyzers` carrying `OutputItemType="Analyzer"` is what makes the generator run ⇒ **a new such project is covered the day it is created**, not the day someone remembers to list it. ⭐ **Nine found** |
| ⭐ **Roslyn, not grep** | parses each project's sources and mirrors `GetMethodInfo`'s own accessibility filter *(private / protected / `private protected` are skipped by both)* — ⚠ the `.dev/`-corpus comment false-positives a text scan produces do not arise |
| ⭐⭐ **named baseline, not a count** | the four, each with the reason it is exempt |
| ⭐ **BOTH directions** | an addition reddens **and a disappearance reddens** — the exemption text would then no longer describe the code |
| ⭐ **and a set-non-empty guard** | ⛔ **a scan over an empty set passes forever** — the failure mode this programme keeps finding |
| ⭐ **the message POINTS** | `Q35` (RESOLVED) + `DESIGN_Hsm_Storage_Model.md` §3, and reads *"this now needs `E3`, which is designed and ready"* — ⛔ **not a ban** |

### 🔴 **Does it FAIL when one is added?** — yes, shown on REAL code

A fifth `[SharedAiAction]` added to `Fdp.Toolkits`:

```
A DTO-BOUND HSM ACTION APPEARED IN A GENERATOR-BEARING ASSEMBLY.

  Fdp.Toolkits :: ProbeActions.ProbeAction   (…/Behavior/Demo/TripwireProbe.cs:16)

HsmActionGenerator will emit a thunk that reads this DTO at a BAKED byte offset from
BrainBlackboard.BehaviorParameters[0] and register it as {MethodFqn}@{offset}. …

This is NOT a ban — it means the change now needs E3, per-occurrence HSM storage, whose
design is finished and waiting:
  * docs/blueprints/Architect_Question_35_Hsm_Occurrence_Delivery.md (RESOLVED: …)
  * docs/blueprints/DESIGN_Hsm_Storage_Model.md section 3
Build E3, then move the new entry out of this test's Baseline.
```

⭐ **Plus a second rail** driving the **same** scan over synthetic source — ⛔ otherwise the green above
is indistinguishable from a scanner that finds nothing.
⭐ **The XML-doc fallback was taken AS WELL AS, not instead of** — the sentence is in
`HsmActionGenerator`'s doc comment, where whoever is editing the generator will see it.

### ⚠ Two findings the probe produced — **filed, NOT adopted**

| id | |
|---|---|
| **`BP-306`** | ⛔ **`BTreeActionGenerator` emits NON-COMPILING code the moment `Hrot.AI.Behaviors` gains its first `[SharedAiAction]`** — `FbtActionRegistrar.g.cs(162,54): error CS1666`. ⭐ **`Fdp.Toolkits` compiles the same shape fine**, so it is assembly-dependent. ⚠ **The probe had to be moved for this reason** — and it means **the one generator-bearing assembly the AI programme owns cannot host a shared AI action today** |
| **`BP-307`** | ⛔ **`Fhsm.Tests.csproj:25` points an analyzer `ProjectReference` at `Fhsm.SourceGen`, which DOES NOT EXIST.** MSBuild prints *"Skipping project … because it was not found"* and succeeds ⇒ ⭐⭐ **the suite's `SourceGen/*` tests exercise `Helpers/GeneratedRegistrarStub.cs`, a HAND-WRITTEN file, not generator output.** ⚠ **Same family as `BP-304`** |

---

## 3. ✅ Item 3 — `BP-304`: **both reds, one cause**

⭐⭐ **`SetTraceBuffer` was removed in `behav-diag-1`** ⇒ ⛔ **nothing writes to an `HsmTraceBuffer`
anywhere**, so every assertion whose observable is a trace record is unreachable.

| test | cause | outcome |
|---|---|---|
| `FailSafeTests.InfiniteLoop_Detected_And_Stops` | ⭐⭐ **the SAME cause — it was not unexplained.** It failed at `Assert.True(traceData.Length > 0, "Trace buffer is empty")`, **one assertion before** it could say anything about the fail-safe | ⭐ **FIXED as a named gap.** ⭐⭐⭐ **The RTC fail-safe ITSELF works and always did** — safe state `0xFFFF` and phase `Idle` both pass, and are kept live. Only the observability half was dead ⇒ ⛔ **never a kernel regression; Batch 76 is not implicated** |
| `OrthogonalRegionTests.OutputLane_Conflict_Detected` | as you said, in-file | ⭐ **FIXED as a named gap.** ⛔ **It had NO behavioural half** — the trace record was its only observable ⇒ what survives is the setup plus the statement that `ArbitrateOutputLanes` is **unobservable** |

⛔⛔ **And a third thing, which was green:** `OutputLane_NoConflict_Passes` **was passing vacuously** —
asserting *no* conflict record against a buffer nothing writes to. ⭐ **The seventh instance of this
shape**, and it was never red, so nobody looked.

⭐ **Named gaps, not skips.** Each ends `Assert.Empty(traceBuffer.GetTraceData().ToArray())`:
executable, keeps the machine construction alive, and **reddens the day the `HsmTraceContext` rewrite
lands** ⇒ invert then, do not delete.
⚠ **The "(DEBT)" the in-file comment points at was NEVER FILED** — `.dev/_DONE/behav-diag-1/DEBT-TRACKER.md`
holds only `DEBT-NOTE-1`, about `DebugState` placement. ⭐ **`BP-304` is now that record.**

⭐⭐ **`Fhsm.Tests` 300 / 300, 0 skipped** *(was 298 / 300)*.

---

## 4. Gates — one row per gate, verbatim command, result

| gate | command | result |
|---|---|---|
| solution | `dotnet build IOS-IG-SimHost.sln -t:Rebuild` | ✅ **0 errors / 69 warnings** |
| ⭐ **FastHSM** *(NO `--no-build`)* | `dotnet test FDP/ExtDeps/FastHSM/tests/Fhsm.Tests/Fhsm.Tests.csproj` | ✅ **300 / 300, 0 skipped** *(was 298 / 300)* |
| Blueprints | `dotnet test …/Hrot.Blueprints.Tests.csproj --no-build` | ✅ **3681 / 3691, 0 failed, 10 skipped** |
| AiShared | `dotnet test …/Hrot.Editor.AiShared.Tests.csproj --no-build` | ✅ **1289** |
| BTree.Editor | `dotnet test …/Hrot.BTree.Editor.Tests.csproj --no-build` | ✅ **615** |
| Breakpoints | `dotnet test …/Hrot.Diagnostics.Breakpoints.Tests.csproj --no-build` | ✅ **134** |
| ⭐ **Generators** | `dotnet test …/Hrot.AiEditor.Generators.Tests.csproj --no-build` | ✅ **268** *(was 266 — the two tripwire rails)* |
| Hsm.Editor | `dotnet test …/Hrot.Hsm.Editor.Tests.csproj --no-build` | ✅ **551** |
| AiEditor.Persistence | `dotnet test …/Hrot.AiEditor.Persistence.Tests.csproj --no-build` | ✅ **136** |
| Examples.Scenarios | `dotnet test …/Fdp.Examples.Scenarios.Tests.csproj --no-build` | ✅ **56 / 68, 12 skipped** |
| Examples.UrbanCombat | `dotnet test …/Fdp.Examples.UrbanCombat.Tests.csproj --no-build` | ✅ **29** |
| ⚠ **Toolkits, run 1** | `dotnet test …/Fdp.Toolkits.Tests.csproj --no-build` | ✅ **1964** |
| ⚠ **Toolkits, run 2** | *(same command)* | 🔴 **1 failed** — `GizmoRegistryTests.SC_GZ004_2_Register_UnregisteredComponent_Throws` |
| ⭐ **Toolkits, `--filter SC_GZ004_2`** | `… --filter "FullyQualifiedName~SC_GZ004_2"` | ✅ **1 / 1** |
| ⭐ **Toolkits, `--filter Gizmos`** | `… --filter "FullyQualifiedName~Gizmos"` | ✅ **187** |
| NodeEdit Core *(NO `--no-build`)* | `dotnet test …/NodeEditor.Core.Tests.csproj` | ✅ **208** |
| NodeEdit UI *(NO `--no-build`)* | `dotnet test …/NodeEditor.UI.Tests.csproj` | ✅ **131** |
| tracker | `python3 scripts/tracker-counts.py --check` | ✅ **open 65 / done 178 (+1 refuted)** |

⭐ **`DEBT-AIB-030` again, and the identity did NOT rotate this time** — `SC_GZ004_2`, the same test as
Batch 76, green in isolation and green across all 187 `Gizmos` tests. ⛔ **Neither the red nor the
green is evidence**; the `--filter` rows are.

### Golden movement

| | |
|---|---|
| ⭐ **NOTHING moved** | no golden, snapshot, `Emit/*.cs.txt` or `persistence-shape` file appears in `49efe83..HEAD` |
| ⭐ **and none was expected** | ⛔ **no DTO field was added this batch** — `Q36-B`'s `SubtreeName` is the change that *would* move `hsm-persistence-shape`, and it is not built. ⚠ **Even then it would move only on re-save**, per `BP-302` |
| **quarantine** | **12** scenario tests — ⭐ **unchanged**, and ⭐⭐ **`Fhsm.Tests` contributes ZERO**: the two `BP-304` tests are executable named gaps, not skips |

---

## 5. Carried forward

⛔⛔ **`Q36` blocks `E5`, and `E7a` needs `E5`'s host** ⇒ ⭐ **the Track-E queue is stalled on one
joint decision.** · **`E3`** *(latent; the tripwire is filed, `BP-305`)* · **`E7b`'s bytes** ·
**`BP-306`**, **`BP-307`** *(filed, not adopted)* · ⛔⛔ **blueprint multi-occurrence** *(user-deferred)* ·
⛔ **wiring the producer picker** *(parked)* · the 12 quarantined scenario tests · the Track C
**visual check**.
