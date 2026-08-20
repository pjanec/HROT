# REPORT — Batch 68: Track C's table and dialog *(+ `W7b`, `E4`)*

> **Branch** `claude/hrot-implementation-j1jvin` · **base** `1743bbc` *(coordinator dispatch)*
> **Rule 7** re-synced at start · **rule 4** re-fetched before the final commit — ⭐ **nothing new on
> the coordinator branch**, so no handoff or design file changed under this run.

---

## 0. 🔴 `StructureHash` — **unchanged for all 43.** `persistence-shape.txt` — **unchanged.**

⭐ **Stated first.** The tree was clean after every suite run; no `.bp.json` and no golden file
regenerated. ⚠ **But this batch DID try to move stored bytes once, and the gate caught it** — see §6.

---

## 1. Gates — one row per gate, verbatim command, result

| gate | command | result |
|---|---|---|
| solution build | `dotnet build IOS-IG-SimHost.sln -t:Rebuild -v q --nologo` | ✅ **0 errors / 69 warnings** *(full rebuild — baseline exactly)* |
| Blueprints | `dotnet test …/Hrot.Blueprints.Tests.csproj --no-build -v q --nologo` | ✅ **3649 / 3639 / 0 / 10** *(unchanged)* |
| AiShared | `dotnet test …/Hrot.Editor.AiShared.Tests.csproj --no-build -v q --nologo` | ✅ **1261 / 1261 / 0 / 0** *(was 1216 ⇒ **+45**)* |
| BTree.Editor | `dotnet test …/Hrot.BTree.Editor.Tests.csproj -v q --nologo` | ✅ **615 / 615 / 0 / 0** *(**+3**)* |
| Breakpoints | `dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/*.csproj --no-build -v q --nologo` | ✅ **134 / 134 / 0 / 0** |
| Generators | `dotnet test Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/*.csproj -v q --nologo` | 🔴 **203 / 201 / 2** → ✅ **203 / 203 / 0** — **see §6** |
| Hsm.Editor | `dotnet test …/Hrot.Hsm.Editor.Tests.csproj -v q --nologo` | ✅ **528 / 528 / 0 / 0** *(was 517 ⇒ **+11**)* |
| **AiEditor.Persistence** *(added — the diff reaches it)* | `dotnet test …/Hrot.AiEditor.Persistence.Tests.csproj -v q --nologo` | ✅ **136 / 136 / 0 / 0** |
| Toolkits | `dotnet test …/Fdp.Toolkits.Tests.csproj --no-build -v q --nologo` | ⚠ **two samples — see §2** |
| NodeEdit Core | `dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/NodeEditor.Core.Tests.csproj -v q --nologo` | ✅ **208 / 208 / 0 / 0** ⭐ **no `--no-build`, honoured** |
| NodeEdit UI | `dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.UI.Tests/NodeEditor.UI.Tests.csproj -v q --nologo` | ✅ **131 / 131 / 0 / 0** ⭐ **no `--no-build`, honoured** |
| tracker | `python3 scripts/tracker-counts.py --check` | ✅ **open 61 / done 143 (+1 refuted)** |

---

## 2. ⚠ Toolkits — the rule applied

| run | result |
|---|---|
| sample 1 | ✅ **1958 / 1958 / 0** |
| sample 2, identical binary | 🔴 **1958 / 1957 / 1** — `StatelessGizmoRegistryTests.SC_GZ022_2_Register_UnregisteredType_Throws` |
| isolation | ✅ **2 / 2** |

⭐⭐ **Both halves of the rule earned their keep this time.** A green first sample is **not** evidence —
the second run reddened. And the red is **not** signal — it passes under `--filter`, it is the same
registry-shaped member Batch 67 saw, and no file this batch touched is in `Fdp.Toolkits`.
📌 `DEBT-AIB-030` / `-010`.

---

## 3. 🔴🔴 The tick unit — **WORLD, and worse than feared**

> ⭐ **This is the headline the handoff asked for.**

📐 **Chain measured, not inferred:** `BlueprintDebugSession:1543` reads `_view.Tick` →
`ISimulationView.Tick`, documented *"current simulation tick (frame number)"* →
`EntityRepository.SimulationTick`, *"semantic frame clock, incremented only by `Tick()`"*.
⇒ ⛔ **per WORLD, not per asset.**

🔴 **And there is no per-asset tick anywhere to substitute.** `BlueprintTickSystem` calls `def.Tick(...)`
and stamps **no per-instance counter**; nothing else does either. ⇒ the ruling's unit does not exist yet.

### What I did about it

⛔ **Did NOT wire the world tick.** Under it, red would clear whenever any frame advanced — including
**while paused on a breakpoint**, which is the exact case the ruling exists for (*"paused ⇒ the
highlight PERSISTS… it clears when you actually Step"*).

⭐ **Instead: `AssetTick` is a per-row NULLABLE delegate.** `null` means *"this row has no tick
source"* — ⛔ not *"tick zero"*, which a `uint` sentinel could not express. A row without one reports
**no highlight and is not even recorded**: ⭐ **inert, never wrong.** That is asserted, so the choice
reads as a decision rather than an omission.

⭐⭐ **The predicate itself is complete and fully tested**, including the frozen case *(100 repaints,
100 world frames, zero asset ticks ⇒ still red)*. **It is waiting on a tick source, not on logic** —
the day a per-`(asset, entity)` counter exists it is passed to `SectionSource` and nothing else changes.

📐 **The other two STOPs, confirmed as stated:** `Watch._valueBuffer` is `new byte[64]` and
`WriteValue` **throws** above it *(and `ExpectedSizeBytes` is a placeholder `Unsafe.SizeOf<byte>()`
= 1)* — the new formatter takes any length, asserted at 136 bytes. `BTreeFacets.TickCount` is a literal
`0` at **both** `BTreeFacetMapper:170` and `:191`, and the struct carries the field **twice** ⇒ ⛔ built
on nothing.

---

## 4. `C-table` — and 🔴 **the heterogeneous rail DID fail before the change**

⭐ **Asked explicitly, answered plainly: yes** — there was no row model at all, so there was nothing for
it to pass. Written against the new model it fails immediately if identity omits `Entity`: two rows for
one asset on two entities collapse onto one cache slot and one entity's change lights the other's row.
🔴 **That probe reddens 2.**

### ⚠⚠ What could NOT be verified — the visual check is suspended

| | |
|---|---|
| ⛔ **the table DRAWING** | three headers in the right order, an empty group as a header rather than a gap, elision at real widths, the red/yellow tints |
| ⛔ **the gestures** | double-click on the **value** cell vs the **name** cell, the `⋮` menu, F2 rename |
| ⛔ **the budget indicator** | planning-only chrome; the run-state switch is written but nothing can see it drawn |

⭐ **What IS asserted is the table's MEANING** — which rows exist, what they are called, how they nest,
which are highlighted. 📌 **`VariableTableControl` is deliberately thin** for exactly this reason, and
⛔ **it does not replace `VariablesPanelControl`**: retiring that is `C-watch`/`C-outline`'s job and
would be a large change nobody can currently look at.

---

## 5. `C-dialog` — 🔴 **§9's rail failed on `HEAD`, and the probe corrected the rail**

📐 `InspectorWindow:352-365` inlined its **own copy of `DefaultValueAuthoring.Hydrate`** and opened its
own session ⇒ **two implementations of a variable's default-value dialog.** Routed, not rebuilt.

🔴🔴 **The revert probe found a hole in my own test.** Restoring the duplicate did **not** redden the
first version of the rail, because it counted **distinct METHODS** and both `Open` calls live in one
method. ⇒ ⭐ **counting CALL SITES fixes it.** A rail that cannot see the defect it was written for is
worse than none — and this is the second batch running in which a probe corrected a test rather than
the code.

⭐ The tolerated facet caller is **NAMED in the expected set**, not skipped by a predicate: a predicate
would silently widen the moment a variable dialog reappeared in that type.

---

## 6. 🔴 The one thing that moved stored bytes — **and the gate, not reading, caught it**

`W7b`'s new `ConcurrentWritesAllowed` serialised as `[]` on **every** asset, so
`MigrationEquivalenceTests` — which round-trips stored JSON and compares it **verbatim** — went red for
**HSM and BTree both**. ⇒ nullable + `WhenWritingNull`.

⚠ **Deliberately unlike its two neighbours:** `Conflict` and `Unused` have always emitted `[]`, so their
presence is already baked into every stored document; a **new** always-emitted list is a corpus-wide
byte change. 📌 **The `W7b` commit message claimed "omitted when empty" before it was true** — recorded
here rather than quietly fixed.

---

## 7. ⭐ IDs allocated *(rule 5)*

| kind | allocated |
|---|---|
| tracker rows | ⭐ **`BP-266` · `BP-267` · `BP-268` · `BP-269`** |
| blueprint diagnostics | ⛔ none *(`BP1675` remains FREE)* |
| analyzer diagnostics | ⛔ none |
| architect questions | ⛔ none |

---

## 8. ⭐⭐ The carried question — **the open `DEBT-AIB` rows, partitioned**

📐 **Measured count is 18 open** *(30 ids, 12 resolved/verified; `-007` is explicitly "NOT ours,
pre-existing")* — ⚠ **the handoff says ~22; the tracker says 18.**

| bucket | ids | note |
|---|---|---|
| ⭐ **Track C** | **`009`** | hardcoded-DTO reflection *"not wired in production DI"* — the render path takes `_actionSchemaExporter` and **neither production constructor supplies it.** 🔴 **This is Track C's ground truth**, and it is the same shape as `E4`: a value column over a schema nothing supplies. ⛔ **Read it before `C-watch`** |
| ⭐ **parameter seam** | **`001` `002` `008` `011`** · **`021`** | `bool` `[MarshalAs(I1)]` silent offset drift, bin-packer vs `Marshal.OffsetOf` fidelity *(`011` is partially bounded — the per-asset struct is nominal, so divergence can only over-pad)*. 🔴🔴 **`021` is the sharp one:** the generated `ParseParams` *"writes only baked defaults — it ignores the incoming json"* ⇒ **runtime per-assignment override does not work for managed BTree assets at all**, so `DESIGN_Parameter_Model.md` §3.2's *"scenario JSON overlays, runtime wins"* is not true of that path |
| **parameter MODEL** *(design, not seam)* | `003` `004` `005` `025` | heavy DTO (>100 B) generation · shared/squad scope · a genuinely blueprint-authored AiPrimitive demo · full BTree-node→`TickCore` composition |
| ⭐ **Track E** | **`022` `028` `029` `031`** | ⭐⭐ **`028` is now PARTLY DISCHARGED by this batch:** `(b)`+`(c)` — the resolver and the threading — **are done**; `(a)`, persisting `StateNode.SubtreeAssetId`, remains and is `E5`'s prerequisite. `029` *(direct children only)* untouched as dispatched. `022` Mission-Editor per-entity-type affinity; `031` the hot-reload coordinator's re-publish has **no production subscriber** |
| ⛔ **none of the three** | `010` `030` · `023` `024` | the `Fdp.Toolkits.Tests` race *(test infrastructure — §2)* · cleanup *(~5 whole-`BrainBlackboard` CGF actions; `Action_HoldPosition` is dead)* |

⭐ **Four rows have now paid for themselves**: `-012`'s mis-citation, `-030`'s race, `-028`'s recipe
*(this batch)*, and `-021`'s contradiction of a documented capability.

---

## 9. What this batch did **not** do

⛔ `C-watch` · `C-outline` · **`E0`** *(the HSM golden harness)* · `E3` · `E5` · `E6` · `E7a`/`E7b` ·
the Instance params seam · multi-occurrence · `G7`+`W10` · the `InspectorWindow` "STATIC PARAMETERS"
retirement *(§10 of the design lists it as still open)* · `sharedScopeKeys` *(`S3-6`'s resolver —
`DEBT-AIB-028`'s recipe names only `_isStatefulSubtree`, so the parameter is threaded but left at its
default)* · **any per-asset tick** *(§3)*.
