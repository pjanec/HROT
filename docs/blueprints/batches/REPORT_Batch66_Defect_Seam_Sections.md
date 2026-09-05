# REPORT — Batch 66: the live defect · the resolver seam · the sections split

> **Implementation session, `2026-08-16`.** Re-synced from
> `claude/blueprint-authoring-status-gm0akp` at **`31cd948`** (rule 7) and re-fetched before the final
> commit (rule 4). ⭐ **All four items built. One commit per item. No STOP condition halted the run —
> but item 2's blast-radius STOP fired as a decision to report, and §3 is that report.**
>
> ⭐ **Rule 4 caught one landing:** `be2fe50` *(the `W6`/`W7` re-derivation, plan revision 9)* arrived
> **during** this run. Read and merged. ⛔ **It changes nothing in Batch 66** — `W6`/`W7` are not in
> this batch — but ⚠ **note the overlap with §7 below**: its `W7c` *"coverage hole in a shipped rule"*
> and `DEBT-AIB-028`/`029` *"the S2-4 cross-region validator is dormant in production"* are the same
> validator seen from two sides, and the debt row names a **third** gap the re-derivation does not —
> ⭐ **`StateNode.SubtreeAssetId` is not persisted to JSON.**

---

## 0. 🔴 `StructureHash` — **unchanged for all 43.** `persistence-shape.txt` — **unchanged.**

Golden Tier 1 **and** Tier 2 zero files changed; the tree was clean after every suite run, so nothing
regenerated. ⭐ Every item is runtime / engine / editor, exactly as the handoff predicted — **nothing
here touches emission.**

---

## 1. Gates — one row per gate, verbatim command

| gate | command | result |
|---|---|---|
| solution build | `dotnet build IOS-IG-SimHost.sln -t:Rebuild -v q --nologo` | ✅ **0 errors / 69 warnings** *(full rebuild)* |
| Blueprints | `dotnet test …/Hrot.Blueprints.Tests.csproj --no-build -v q --nologo` | ✅ **3638 / 3628 / 0 / 10** *(was 3628/3618 ⇒ **+10**)* |
| AiShared | `dotnet test …/Hrot.Editor.AiShared.Tests.csproj --no-build -v q --nologo` | ✅ **1216 / 1216 / 0 / 0** |
| BTree.Editor | `dotnet test …/Hrot.BTree.Editor.Tests.csproj --no-build -v q --nologo` | ✅ **612 / 612 / 0 / 0** |
| Breakpoints | `dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/*.csproj --no-build -v q --nologo` | ✅ **134 / 134 / 0 / 0** *(**+4**)* |
| Generators | `dotnet test Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/*.csproj --no-build -v q --nologo` | ✅ **196 / 196 / 0 / 0** |
| Toolkits | `dotnet test …/Fdp.Toolkits.Tests.csproj --no-build -v q --nologo` | ⚠ **1951 / 1950 / 1**, then **1951 / 1951** — see §2 *(**+7**)* |
| NodeEdit Core | `dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/NodeEditor.Core.Tests.csproj -v q --nologo` | ✅ **208 / 208 / 0 / 0** ⭐ **no `--no-build`, honoured** |
| NodeEdit UI | `dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.UI.Tests/NodeEditor.UI.Tests.csproj -v q --nologo` | ✅ **131 / 131 / 0 / 0** ⭐ **no `--no-build`, honoured** |
| tracker | `python3 scripts/tracker-counts.py --check` | ✅ **open 61 / done 133 (+1 refuted)** |

⭐ **Did any suite need a re-run to go green? YES — `Fdp.Toolkits.Tests`, once.** §2.

---

## 2. ⚠ The `Fdp.Toolkits.Tests` race — ⭐⭐ **and this batch found its FILED explanation**

`StatelessGizmoRegistryTests.SC_GZ022_2_Register_UnregisteredType_Throws` — **the filed one this
time**, not Batch 65's different member. Failed on one full run, green on the next, and
✅ **green in isolation** (`--filter StatelessGizmoRegistryTests` ⇒ 2/2).

⭐⭐⭐ **The cause is written down, in `.dev/`, and this programme has been calling it unexplained for
three batches:**

> 📄 **`DEBT-AIB-030` (P2)** — *"`Fdp.Toolkits` behavior stateful tests are non-deterministic in the
> FULL unfiltered suite. They pass deterministically under `--filter Behavior` and in isolation, but a
> varying subset fails when the whole suite runs."*
> 📄 **`DEBT-AIB-010`** — cause named: *"**xUnit cross-collection parallelism + process-global
> ECS/component-id/registry state corrupted by unrelated collections**."*

⇒ ⛔ **Not a gizmo bug and not ours.** It is one process-global registry being clobbered by whichever
collection happens to run alongside. 📌 That also predicts what we have seen: **a different test each
time** (Batch 65's was `DangerAreaProviderTests`), all of them registry/allocation-shaped.

---

## 3. 🔴🔴 The surgical write — **the STOP condition's answer, and the decision I took**

### The defect IS where the handoff said. Measured, not assumed

| # | measured on `HEAD` |
|---|---|
| ① | `OnHit` captures `_postTickSnapshot ← _liveRepo`, then **rewinds** `_liveRepo ← _preTickSnapshot` |
| ② | while paused **`ActiveView` IS `_preTickSnapshot`** ⇒ the edit dialog is seeded with **pre-tick** values for every field |
| ③ | `RequestStep`/`RequestContinue` restore `_liveRepo ← _postTickSnapshot` and **then** drain |

⇒ a whole-component write built from ② lands on ③ and carries every untouched field **back a tick**.

🔴 **The red-first test FAILED before the fix** — and for that reason, not another: forcing the diff
back through the whole-component path reddens **3 of 4**. ⭐ (The handoff asked me to say so *"if it
did not, that is the headline."* It did.)

### 📐 Blast radius — **`SetComponentRaw` IS on an interface**

| `IEntityCommandBuffer` implementers | count | what I did |
|---|---|---|
| the real `EntityCommandBuffer` | **1** | ⭐ real implementation |
| production delegating wrappers *(`AutonomousPerceptionModule`, `CognitiveSpatialModule`)* | **2** | ⭐ explicit delegation — a wrapper that did not delegate would hit the throwing default with the real buffer sitting behind it |
| **test mocks** | **9** | ⭐ untouched |
| **total** | **12** | |

⭐⭐ **The proposal, implemented and flagged rather than taken silently:** the new member carries a
**default implementation that THROWS**. ⛔ **Not a no-op** — a silent no-op here is a *lost edit*,
which is the exact class of defect the method exists to remove. ⚠ **Overrule if the
default-implementation route is unwanted;** the alternative is nine test files growing a body for a
method they never call.

📌 A **second** interface pair took the same treatment for the same reason: the 4-arg `StageMutation`
on `IMutationInterceptor` / `IDataBreakpointManager`, whose default **forwards** to the
whole-component write so every existing caller and double is unchanged.

⭐ **`IComponentTable.SetRawAt` did NOT need this** — measured: **2 implementers**, both sealed, both
in `Fdp.Core`.

### ⭐ One thing came out better than specified

📐 **The granularity is finer than "one field": the diff is over BYTES.** Editing an `int` from `1`
to `99` stages a **one-byte** run. ⇒ ⭐⭐ **the mechanism needs no field-layout knowledge at all** — no
StructEdit change, no per-field dirty tracking, and nothing to keep in sync with a struct's real
offsets. ⚠ Which is why the rail asserts the **invariant** *(no staged run reaches into the
simulation's field)* rather than a remembered byte count.

---

## 4. ⚠ `G4` — **the dispatch's measurement was stale, and the real gap is next door**

⛔ *"`Register` ends `_definitions[id] = definition; _nameToId[name] = id;` — indexer assignment,
silent overwrite"* — **the indexer assignment is real but a guard precedes it.** The duplicate-**name**
hard error has shipped since **Phase 1e** (`:214-224`), with an idempotent same-instance escape.

⭐⭐ **The missing half is the ID, and it is `W1`'s shape exactly:** the id is `FNV-1a-32` of the name,
so two **distinct** names can hash to one id ⇒ `_nameToId` holds both names → that id while
`_definitions[id]` holds only the second definition ⇒ **the first behaviour silently resolves to the
second's topology.** ⚠ And `Register(int id, …)` is public, so a generated registrar reaches it with no
hashing at all.

✅ **STOP did not fire — the shipped corpus registers cleanly**, asserted through the **production**
scanner with an explicit non-vacuity check.

---

## 5. `C-sections` — ⚠ **what the suspended visual check leaves unverified**

✅ **Checked headlessly:** each kind lands in its own section **and in no other** · a section with no
declarations is **empty, not absent** · each new section **declares** a create command · invoking it
**adds a declaration of that kind** · the **production `Retarget`** registers both · repeated creates
produce distinct names **across every kind**.

⛔ **NOT checkable without eyes:** the sections **drawing** — three headers in the right visual order,
an **empty** one rendering as a header rather than a gap, and the per-section **"+"** appearing where a
designer expects it. 📌 Also unverified: whether *"Inputs"* and *"Working State"* read well **below**
Local Variables, which is where `SortOrder` 6/7 puts them.

⚠⚠ **A revert probe found a hole in my own test, and it is worth recording:** deleting the
registration call from the production `Retarget` left the invoke test **green** — that test registers
the commands itself. ⇒ **it proved the handler works and said nothing about whether anything calls
it**, which is trap #5 in miniature. A second rail now drives the production path.

---

## 6. ⭐ IDs allocated *(rule 5)*

**`BP-256`** *(`G4`)* · **`BP-257`** *(the surgical write)* · **`BP-258`** *(`G1`)* ·
**`BP-259`** *(`C-sections`)*. ⛔ **No new diagnostic codes.**
⇒ next free: rows **`BP-260+`**, blueprint diagnostics **`BP1675+`**, analyzer diagnostics **`BHU_022+`**.

---

## 7. ⭐⭐⭐ The carried question — **which unresolved `DEBT-AIB` rows are in our blast radius**

📐 **Measured: 30 ids in `.dev/_DONE/btree-ai-action-binding/DEBT-TRACKER.md`, 12 resolved/verified,
⭐ 18 OPEN.** ⛔ **Named, not fixed**, as asked.

### 🔴🔴 Highest value first — **two of these explain things we have been re-deriving**

| id | claim | why it is ours |
|---|---|---|
| 🔴🔴 **`DEBT-AIB-010`** + **`DEBT-AIB-030`** | *"behavior stateful tests are non-deterministic in the FULL unfiltered suite… pass under `--filter Behavior` and in isolation"* — cause: **xUnit cross-collection parallelism + process-global ECS/component-id/registry state** | ⭐⭐⭐ **This is the `Fdp.Toolkits.Tests` race, filed.** We have re-measured it as *"unexplained"* in three consecutive batches |
| 🔴🔴 **`DEBT-AIB-021`** | *"the generated `ParseParams` writes only baked defaults — **it ignores the incoming `json`** at assignment time"* | ⭐⭐ **That is `G1`'s own path.** ⇒ **runtime per-assignment override does not work for managed BTree assets at all** — and `DESIGN_Parameter_Model.md` §3.2's *"scenario JSON overlays, runtime wins"* is therefore **not true of that path** |
| 🔴 **`DEBT-AIB-009`** | hardcoded-DTO reflection *"not wired in production DI"* — the render path takes `_actionSchemaExporter`, but **neither production constructor supplies it** | ⭐ **Track C's ground truth.** A value column over a schema nothing supplies in production is trap #5 again, already filed |
| 🔴 **`DEBT-AIB-028`** + **`029`** | the S2-4 **cross-region** validator is *"dormant in production"* — `StateNode.SubtreeAssetId` is **not persisted**; and the check walks **direct children only** | ⭐⭐ **`W7`'s neighbour.** `W7` is the blueprint-variable analogue of exactly this conflict check ⇒ worth reading **before** re-deriving `W7` |

### The rest, by track

| track | ids | one line |
|---|---|---|
| **parameter seam / layout** | `001` · `002` · `008` · `011` | ⭐ `bool` `[MarshalAs(I1)]` **silent offset drift**, and bin-packer vs compiled `Marshal.OffsetOf` fidelity ⇒ **`S2`/`W4`/`BP-249`'s family**. ⚠ `011` is *partially bounded*: the per-asset struct is nominal, so divergence can only **over-pad** |
| **parameter model** | `003` · `004` · `005` · `025` | heavy DTO (>100 B) generation *(§2's storage table)* · shared blackboard / squad scope *(§4 + the "asset globals" section)* · a genuinely **blueprint-authored** AiPrimitive demo · full BTree-node→`TickCore` composition *(§4.1/§4.4)* |
| **Track E / HSM + host** | `022` · `031` | Mission-Editor per-entity-type affinity · the hot-reload coordinator's re-publish has **no production subscriber** |
| **cleanup** | `023` · `024` | migrate ~5 whole-`BrainBlackboard` CGF actions to focused DTOs; `Action_HoldPosition` is **dead** |

⚠ **`DEBT-AIB-007` is explicitly *"NOT ours, pre-existing"*** and is excluded from the 18.

📌 **Recommendation, unchanged in shape from Batch 65 but now pointed:** ⛔ do not sweep all 51 ids.
⭐ **Start with `010`/`030` and `021`** — one explains a flake we keep re-measuring, the other says a
documented capability does not work — and read `028`/`029` **before** re-deriving `W7`.
