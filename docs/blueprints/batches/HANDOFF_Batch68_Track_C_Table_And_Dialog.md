# HANDOFF — Batch 68: **Track C's table and dialog** *(+ `W7b`, `E4`)*

> 📌 **Dispatched at `9603832b8`.** Frozen per rule 1 *(rule 1a: re-dispatch only while this sha is NOT
> in your history)*. ✅ **Batch 67 MERGED at `f52b1af15`** — gates re-run, all matching.
> ⭐ **Rule 7 / Rule 4.** ⛔ **Rule 3: the coordinator allocates no ids.**
> ⭐ **One commit per item · per-item STOP conditions.**

---

## 0. ⭐⭐ Batch 67 — **you corrected me twice, and both are in the plan**

| | |
|---|---|
| ⛔⛔ **`G3` was ALREADY SHIPPED** | `IGeographicTransform` carries `[ComponentId]` and is published with `SetSingletonManaged` at **three** production sites. ⭐ **My *"constructor-injected ⇒ unreachable"* named a second CONSUMER, not a second mechanism.** 🔴 **The instructive half:** rev-3 said *"world-singleton is shipped ⇒ adopt"*; I found its **citation** wrong and **discarded the conclusion with it**. ⇒ ⭐⭐ **correcting a bad citation is not grounds for reversing the claim** |
| ⭐ **the corpus reframing was better than mine** | *(a)* is not *"add HSM assets"* — **there is no HSM golden harness at all.** ⇒ **promoted to `E0`, its own batch**, and `E1`/`E2` get backfilled into it |
| ⭐⭐⭐ **`W7c`'s boundary found `DEBT-AIB-028`** | it contains **`E4` verbatim plus an activation recipe**, and the fact that **`StateNode.SubtreeAssetId` is not persisted** ⇒ **`E5` gained a prerequisite.** 📌 **Third find from the carried question — it is paying for itself** |
| ⭐ **your vacuous-test catch** | `PickableGeoPoint` serialises as `[lat, lon]`; an object-shaped fixture gave zeros and `0 != 14` passed a weak assertion. **Exactly the self-check I want reported** |

---

## 1. ⭐⭐⭐ `C-table` — the Details section table *(the big item)*

📄 **`DESIGN_Variable_Details_And_Editing.md` — §1a, §1b, §4a, §4b, §5. §9 is the rail set.**
⛔ **That design is authoritative. Do not re-derive it here.**

### The shape, in one line

⭐⭐ **The control renders `IReadOnlyList<VariableRow>` and knows NOTHING about where rows came from.**

```
VariableRow
  Origin      : (AssetId, Entity, Section, VariablePath)   ← identity; ⭐ Entity is PART of it
  ShortName   : string        ← the CONTROL qualifies it when grouping has not
  TypeText / ClrType
  ReadValue   : () -> ReadOnlySpan<byte>   ← raw, for display AND change-diff
  AssetTick   : () -> uint    ← ⭐ THIS row's asset tick
  RowKind     : Normal | ReadOnlyPassthrough | NodeOwned
  IsStale     : bool
```

| in scope | |
|---|---|
| **sources** | `SectionSource(asset, section)` — Details. ⛔ **`PinnedSource` (Watch) is NOT this batch**, but ⭐ **the control must already be source-agnostic**, and §9's heterogeneous rail proves it |
| **columns** | `Name` + `Value` **mandatory**, `Type` **one toggle**. ⛔⛔ **No general column framework** — the control has **seven** today; that is what we are escaping |
| **grouping** | `GroupBy` = an **ordered facet list**, ⛔ **not hardcoded modes**. ⭐ **A uniform facet emits NO header** · folding reuses `CollapsingHeader` *(already used 3× there)* · ⭐⭐ **a collapsed header inherits its children's red/yellow** |
| **value rendering** | primitives inline · structs **one-line elided + pretty-printed tooltip** · ⛔ **never raw hex** *(that was `BP-01`'s symptom)*; undecodable ⇒ **`<unreadable>`** |
| **change highlight** | 🔴 red one tick = the sim changed it · 🟡 yellow = your pending edit. ⭐ **Diff RAW BYTES** — a formatted value hides a float moving in its 7th digit |

### 🔴 STOP conditions — **three flagged unknowns, all still true on `HEAD` (I re-measured)**

| | |
|---|---|
| ⭐⭐⭐ **the tick unit** | the ruling is **a NON-FROZEN ASSET TICK** — not a frame, not a world tick. ⚠ **`BlueprintDebugSession:108` says it detects a new tick by *observing `_view.Tick` change*** ⇒ 📐 **that looks like the WORLD tick.** ⛔ **Measure it. If it is the world tick, say so and STOP before building the highlight on it** — the ruling's whole point is that **paused, the highlight persists until you Step** |
| 🔴 **the 64-byte Watch buffer** | `_valueBuffer = new byte[64]` and `WriteValue` **throws above 64** ⇒ **`MemberSlotList` (96), `WaveState` (104), `HillAttackSharedState` (136) cannot go through it.** ⛔ **The shared row renderer must NOT inherit that limit** |
| ⚠ **`BTreeFacets.TickCount` is a trap** | still `TickCount = 0` at `BTreeFacetMapper:170` **and** `:191`. ⛔ **Do not build on it before verifying it is populated** — this is trap #5's shape |

**rails:** 📄 **§9 — use them, do not invent.** ⭐ **The one that matters most: the HETEROGENEOUS SOURCE
test** — feed the control rows from **two different assets AND the same asset on two entities**, and
assert distinct identities · **independent** highlight state · a stale row renders and refuses its
dialog. ⛔ **That is the test that stops the control quietly assuming one asset.**
⭐ **The change-highlight predicate is headlessly testable** even though the colour is not — including
⭐⭐ **frozen: N world frames with NO asset tick ⇒ STILL highlighted.**

⚠ **The DRAWING, the gestures and the budget-indicator switch are not headlessly checkable and the
visual check is suspended.** 📐 **Say in your report exactly what you could not verify.**

---

## 2. `C-dialog` — **one dialog, two scopes**

📄 **`DESIGN_Variable_Details_And_Editing.md` §2, §3, §4.**

| | |
|---|---|
| ⭐⭐ **the two menu items ARE the two `EditScope`s** | *"Edit value…"* ⇒ **`ForField`** *(double-click the **value** cell)* · *"Properties…"* ⇒ **`WholeComponent`** *(double-click the **name** cell)* |
| ⭐ **run state decides WRITABILITY, not which dialog** | planning: both editable · running/paused: value **staged**, properties **read-only** · replay: **no dialog** |
| ⛔ **ONE implementation** | same `IEditSession` lifecycle, differing **only** by the `EditScope` argument. ⭐ **§9's rail is a reflection test: exactly ONE call site constructs the variable edit session** |
| ⭐ **kind-driven fields** | **§2's table is measured, not spec'd** — seven editable properties, and the set **differs by declaration kind**. ⛔ **Fields with no storage are OUT** *(`D7`'s Replication and Range have no member on any carrier)* |
| ⛔ **`Role`/`Scope` is NOT a property** | §1c's ruling — **the SECTION is the classification.** ⛔ **No `Role`/`Scope` control on any host** |
| ⚠ **`IsExposedOnSpawn`** | persisted, **nothing reads it at spawn**. ⛔ **KEEP it — do not "clean it up"** *(unreferenced ≠ unintentional)*. 📐 **File the gap** |

⭐ **The write path is already ruled:** planning ⇒ `DefaultValueAuthoring.CommitAndSerialize` *(re-host,
do not rebuild)*; running ⇒ ⭐⭐ **OPTIMISTIC DISPLAY** — paint the new value immediately in **yellow**,
then **stage**. ✅ **The surgical field write landed in Batch 66**, so the prerequisite is met.

---

## 3. `W7b` — **"Allow concurrent writes"** *(finishes the `W7` trio)*

📄 **`Blackboard_Authoring_Detailed_Design.md` §9.4.** The explicit-enable path for a designer who
**wants** the race: variable `⋮` → *"Allow concurrent writes"* → checkbox.
⭐ **`W7c` and `W7a` shipped in 67**, so the diagnostic and the per-pair suppression both exist.
⚠ **Keep the distinction:** ⭐ **suppression is per (variable, writer-pair); "allow" is per VARIABLE.**
⛔ **Do not collapse them into one flag.**

---

## 4. `E4` — wire the validator resolvers ⭐ **the recipe is already filed**

⛔ **Do not re-derive this — `DEBT-AIB-028` names the steps:**

> *"(b) `_isStatefulSubtree` defaults to `_ => false` and production never supplies a real resolver;
> (c) the production `HsmAssetValidator` entry point isn't threaded to pass the resolver. Activation
> needs: … a `BehaviorTreeAsset.HasAnyStatefulNode()` (any `ThreeParamReusableStateful` action) + HSM
> equivalent, wire `id => catalog.TryFind(id,out a) && a.HasAnyStatefulNode()` through the production
> validator ctor."*

⚠ **`DEBT-AIB-028`(a) — persisting `StateNode.SubtreeAssetId` — is `E5`'s prerequisite, NOT this item.**
⇒ 📐 **Rules 8/8b may still not FIRE on real assets afterwards** *(nothing sets the field yet)*.
⭐ **That is expected and fine** — this item makes the wiring honest; `E5` makes it reachable.
📌 **Also note `DEBT-AIB-029`:** the check walks **DIRECT children only**. ⛔ **Out of scope; do not fix.**

**rail:** the rules fire **through the production constructor** on a fixture that trips them.

---

## 5. ⛔ NOT in this batch

`C-watch` · `C-outline` *(BTree/HSM `IMyBlueprintModel`)* · **`E0`** *(the HSM golden harness — its own
batch, as ruled)* · `E3` · `E5` · `E6` · `E7a`/`E7b` · the Instance params seam · multi-occurrence ·
`G7`+`W10`.

---

## 6. Gates

**Baseline — coordinator-verified at `f52b1af15`:** build **0 / 69** · Blueprints **3649 / 3639 / 0 / 10** ·
AiShared **1216** · BTree **612** · Breakpoints **134** · Generators **203** · **Hsm.Editor 517** ·
Toolkits **1958** · NodeEdit **208 / 131** · tracker **open 61 / done 139**.

| | |
|---|---|
| ⭐ **`Hsm.Editor` is now a standing gate** | **you added it and you were right** — `E4` reaches it |
| ⭐⭐ **`Fdp.Toolkits.Tests`** | a **full-suite red is not signal by itself** — confirm with `--filter`/isolation; ⛔ **and a full-suite green is not evidence either.** `DEBT-AIB-030` |
| 🔴 **`StructureHash` unchanged for all 43** · **`persistence-shape.txt` UNCHANGED** | ⭐ **every item here is editor/validator** ⇒ a move means you touched emission |
| **per-item revert-goes-red** · `tracker-counts.py --check` · ⚠ **the two NodeEdit gates take NO `--no-build`** | |

---

## 7. Reporting

⭐⭐ **The gate table — one row per gate, verbatim command, result**, plus any suite you add because the
diff reaches it *(you did that for `Hsm.Editor` unprompted; keep doing it)*.

**Per item:** 🔴 **the tick-unit measurement — world or asset?** *(if world, that is the headline)* ·
⭐ **what `C-table`/`C-dialog` could NOT be verified without the visual check** · ⭐ **whether the
heterogeneous-source rail failed before the change** · **`StructureHash` unchanged, stated FIRST** ·
`tracker-counts.py --check` · ⭐ **every id you allocated**.

⭐⭐⭐ **The carried question — please finish it.** 📐 **Of the ~22 unresolved `DEBT-AIB` rows, which sit
inside Track C, the parameter seam, or Track E?** ⛔ **NAME them, do not fix them.**
⭐ **Three have already paid for themselves**: `-012`'s mis-citation, `-030`'s race, and `-028`'s recipe.
