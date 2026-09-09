# HANDOFF — Batch 66: **the live defect · the resolver seam · the sections split**

> 📌 **Dispatched at `02f824b2a`.** Frozen per `.claude/CLAUDE.md` → *Two-session protocol* rule 1
> *(rule 1a: re-dispatch is legal only while this sha is NOT in your history)*.
> ✅ **Batch 65 MERGED at `8c09d5004`** — all four items, **all eight gates coordinator-re-run and
> matching your table**. ⭐⭐ **Your gate table is why I could verify selectively — keep that format.**
> ⭐ **Rule 7 / Rule 4.** ⛔ **Rule 3: the coordinator allocates no ids.**
> ⭐ **One commit per item · per-item STOP conditions.**

---

## 0. ⭐⭐⭐ Read these two first — they are new authorities

| | |
|---|---|
| 📄 **[`DESIGN_Parameter_Model.md`](DESIGN_Parameter_Model.md)** | ⛔ **THE parameter story. Supersedes every prior parameter design.** ⭐ **§0 is a "do not re-derive" table of ten things this programme got wrong** — most of them mine. **§8 is the rail set for item 3** |
| 📄 **[`DESIGN_Variable_Details_And_Editing.md`](DESIGN_Variable_Details_And_Editing.md)** §1c | **Track C.** ⭐ **§1c is item 4 verbatim** — sections are the classification |

### ⭐ Your Batch 65 correction is accepted and propagated

⛔ **`DEBT-AIB-012` does not cover the `ComputeStructSize` triplication** — I introduced that citation and
repeated it in three documents. **All corrected to cite `BATCH-03-REPORT.md:100`.** ⭐ **You were right
that *"suggested"* ≠ *"filed"*, and the distinction mattered.**

---

## 1. `G4` — the duplicate-name guard ⭐ **smallest item; `W1`'s sibling on the other registry**

| | |
|---|---|
| **measured** | `BehaviorRegistry.Register(int id, string name, def)` ends `_definitions[id] = definition; _nameToId[name] = id;` — ⛔ **indexer assignment, silent overwrite** |
| **the design** | 📄 `Behavior_Parameter_Resolver_Detailed_Design.md` §3.1: *"**Duplicate name = hard error.** Two registrations of the same name is a mistake (or an intended replacement…) and must **fail loudly, not silently overwrite**. This is the fix for the double-registration bug (§5.1)"* |
| ⭐ **precedent** | **copy the blueprint registry's collision-throw** — the design says so explicitly. ⛔ **Do not invent a new diagnostic shape** |
| **rail** | two registrations of one name ⇒ **throws**, naming both. ⭐ **And a test that the CURRENT corpus registers cleanly** — if it does not, ⛔ **STOP: you have found a live double-registration** |
| **impact** | runtime-only. ⛔ **`StructureHash` / `persistence-shape` MUST NOT move** |

⚠ **`E6`/`W9` is NOT this item** — that is the HSM simple-name hash, a different registry, later batch.

---

## 2. 🔴🔴 **The surgical field write** — ⭐ **the one LIVE defect on the page**

| | |
|---|---|
| **the defect** | `DataBreakpointManager.StageMutation:530` takes a **whole component**; `DrainPendingMutations:565` writes it with `ecb.SetComponentRaw(...)` — **no offset** — *after* the restore. ⇒ ⛔ **every other field of that component reverts post-tick → pre-tick.** On the shared `Blackboard1024` that reverts **BTree and HSM** state |
| ⭐ **already ruled** | **ruling 14 names the signature**: `SetComponentFieldRaw(Entity, int typeId, int byteOffset, void* src, int size)` in `Fdp.Core` |
| **measured** | ⛔ **it does not exist** — zero hits. `SetComponentRaw` is on `EntityCommandBuffer` (`Fdp.Core:226`), playing back via `repo.SetComponentRawFast`, and ⚠ **~15 files mention it, several of them MOCKS** |

### ⛔ STOP conditions — **read before writing code**

| | |
|---|---|
| ⭐⭐ **measure the blast radius FIRST** | **Is `SetComponentRaw` on an interface?** `AutonomousPerceptionModule:278` delegates to `_realEcb`, which smells like one. ⇒ **count the implementers that would be forced to add the new method.** 📐 **If it is an interface with many implementers, say so and propose whether a default implementation is acceptable — do not silently add a member to a public interface** |
| 🔴 **RED-FIRST is mandatory here** | ⭐ **The payload's exact origin is UNVERIFIED.** Write the failing test first: **stage an edit to ONE field of a component whose OTHER fields the sim changes in the same tick, then assert the other fields survive.** ⛔ **If that test passes before your fix, STOP and report** — the defect is not where I said it is |
| ⚠ **do not widen scope** | the optimistic-display half of the write path is **Track C**, not this batch |

**rail:** the red-first test above, plus **`SetComponentFieldRaw` writes exactly `size` bytes at
`byteOffset` and touches nothing else.**
**impact:** engine + debug only. ⛔ **`StructureHash` / `persistence-shape` MUST NOT move.**

---

## 3. `G1` — split deserialize from resolve ⭐ **and make the signature change ONCE**

📄 **`DESIGN_Parameter_Model.md` §3.1–§3.4.**

| | measured |
|---|---|
| **half of it landed already** | `ParseParamsDelegate(string json, byte* memory, EntityRepository world, Entity self)` **already carries world + self** |
| ⛔ **the split did not** | there is **no generic auto-deserializer keyed by `ParamsDtoType`** — that field feeds **rendering only** (ReplayBrowser drawers, StructEdit context) |
| **the seam** | `BehaviorIngressSystem.cs:96` is the **one** production call site; ⭐ **parse-before-commit must survive** — a failed parse leaves the entity **100% on its old behaviour** |

### ⭐⭐ Add the host argument NOW, in the same change

```csharp
public interface IHostVariableAccess {          // 📄 DESIGN_Parameter_Model.md §3.4
    bool TryRead<T>(string variableName, out T value) where T : unmanaged;
    bool TryReadBytes(string variableName, Span<byte> destination, out int written);
}

public unsafe delegate void ParseParamsDelegate(
    string json, byte* memory, EntityRepository world, Entity self,
    IHostVariableAccess? host);              // ⭐ ALWAYS null until E7a
```

⭐ **Why now:** adding a parameter is a **breaking change to every resolver**. ⛔ **Doing it twice is
the avoidable cost** — `E7a` populates `host` much later, and `null` is its defined value for a root
behaviour anyway. ⚠ **Do NOT implement `IHostVariableAccess` in this batch** — declare it, pass `null`,
and leave it unimplemented.

**rails:** 📄 **`DESIGN_Parameter_Model.md` §8** — ⛔ **use those, do not invent new ones.** The two that
matter here: **one supply mechanism** *(a reflection rail: exactly one parameter-resolution path)* and
**parse-before-commit** *(a failing resolve leaves the old behaviour intact)*.
**impact:** ⛔ **`StructureHash` / `persistence-shape` MUST NOT move.**

---

## 4. `C-sections` — split the Variables section per kind ⭐ **Track C's first item**

📄 **`DESIGN_Variable_Details_And_Editing.md` §1c** *(user ruling `2026-08-16`)*.

| | measured |
|---|---|
| ⭐ **the machinery ships** | `MyBlueprintSectionDescriptor(Id, DisplayName, SortOrder, IconKey, CanCreateItems, CanHaveCategories, CreateCommandId)` — **sections are data, each with its own create command**; `MyBlueprintPanel` is in `NodeEditor.UI`, `IMyBlueprintModel` in `NodeEditor.Core` |
| ⛔ **the gap** | `BuildVariableItems()` lists **only `DeclarationKind.Variable`** ⇒ **Parameters and WorkingState are not shown in My Blueprint at all** |
| ⭐ **the precedent to copy** | `SectionLocalVariables` — and copy its subtlety: *"**Empty rather than absent** when the canvas has no graph… a section that appears and disappears reads as a broken feature"* |

⭐⭐ **The ruling: a variable's classification is WHERE IT WAS CREATED.** ⛔ **No `Role`/`Scope` control
is introduced anywhere by this item.**

⚠ **In scope:** the per-kind split + per-section `CreateCommandId` for the **blueprint** model.
⛔ **NOT in scope:** giving BTree/HSM their own `IMyBlueprintModel` (`C-outline`, later), the table, the
dialog, the Watch panel.

**rails:** ⭐ **headless** — the section list for an asset contains the expected sections in
`SortOrder`; **creating in a section produces a declaration of that kind**; a section with no items is
**empty, not absent**. ⚠ **The DRAWING is not headlessly checkable and the visual check is suspended** —
📐 **say in your report what you could not verify.**
**impact:** editor-only. ⛔ **`StructureHash` / `persistence-shape` MUST NOT move.**

---

## 5. ⏭ Carried — **take it only if the run has room**

**The latency rail.** 🔴 **Nothing forbids a latent node in an AiPrimitive**, and
`BTreeEvaluate` emits `return TickCore(…) == NodeStatus.Success;` ⇒ ⭐ **`Running` maps to `false`**,
so **a latent CONDITION silently reads false while it waits**, then flips true later with `__phase` left
mid-sequence. ⛔ **Silent wrong behaviour, not an error.**

⭐ **The rule: latency is legal iff the hosting can RE-ENTER** —
⛔ `Condition` → `BTreeCondition`/`HsmGuard` **never** · ✅ `Action` → `BTreeAction` · ✅ `Action` → HSM
Activity/subtree · ⛔ `Action` → HSM Entry/Exit/Timer.
⭐⭐ **A third dimension on `V_DispatchKindCompatibility`, which already does intent-vs-hosting
(`BP1022`/`BP1023`)**, and ⭐ **the detector already exists** — `MacroLatency.IsLatent` /
`FindTransitivelyLatentNode`, used today by `BP1661`. ⇒ **the rule is missing, not the analysis.**

⚠ **Only the HSM rows are speculative** *(HSM slot hosting is not built)* — **the `Condition` row is the
one that matters and is fully specified today.**

---

## 6. ⛔ NOT in this batch

`G3` · `G7`+`W10` · `W7` *(I still owe you its re-derivation from `Blackboard_Authoring_DD` §9.6)* ·
**all of Track E** *(`E1`–`E7b`, the HSM catch-up)* · the rest of Track C *(table, dialog, Watch,
`C-outline`)* · the Instance params seam · multi-occurrence.

---

## 7. Gates

**Baseline — coordinator-verified at `8c09d5004` (`2026-08-16`):** build **0 errors / 69 warnings** ·
Blueprints **3628 / 3618 / 0 / 10** · AiShared **1216** · BTree **612** · Breakpoints **130** ·
Generators **196** · Toolkits **1942** · NodeEdit **208 / 131** · tracker **open 61 / done 129**.

| | |
|---|---|
| 🔴 **`StructureHash` unchanged for all 43** | ⭐ **every item in this batch is runtime/editor/engine** ⇒ **a move means you changed emission, which nothing here should** |
| **`persistence-shape.txt`** | ⛔ **UNCHANGED** |
| ⭐ **golden Tier 1 unchanged** · **per-item revert-goes-red** · `tracker-counts.py --check` | |
| ⚠ **the two NodeEdit gates take NO `--no-build`** | as last batch |

---

## 8. Reporting

⭐⭐ **The gate table again — one row per gate, verbatim command, result.** ⭐ **It worked: I re-ran the
two NodeEdit gates, the four suites your diff could reach, and `Fdp.Toolkits.Tests` for a second sample,
and accepted the rest on your table.** ⛔ **Also state: did any suite need a re-run to go green?**

**Per item:** 🔴 **the surgical write's blast radius** *(interface or class? how many implementers?)* ·
⭐ **whether the red-first test failed before the fix** — ⛔ **if it did not, that is the headline** ·
⭐ **what `C-sections` could not verify without the visual check** · **`StructureHash` unchanged, stated
FIRST** · per-suite numbers · `tracker-counts.py --check` · ⭐ **every id you allocated**.

⭐⭐⭐ **The question to carry.** Your Batch 65 measurement — **51 distinct `DEBT-*` ids, 30 of them
`DEBT-AIB`, ~22 unresolved; 54 `DEBT-TRACKER.md` files of which only 6 are non-empty; `docs/`
references essentially none** — is the most useful thing that came out of that batch. 📐 **Of the ~22
unresolved `DEBT-AIB` rows, which are inside the blast radius of Track C, the parameter seam, or Track
E?** ⛔ **Do not fix them. Name them, so I can fold them into the plan instead of rediscovering them one
batch at a time.**
