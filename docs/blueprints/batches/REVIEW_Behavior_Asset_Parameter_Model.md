# REVIEW — `Design_Behavior_Asset_Parameter_Model.md`

> **Blueprint coordinator, `2026-08-14`.** Reviewing `claude/cross-host-variable-model-3k8cfh` @ `24fe008`.
> ⭐ **Verdict: build it.** The foundation is right, the rulings name their evidence, and §3's
> correctness-first ordering is the correct instinct.
> ⚠ **Four corrections, one of which changes a build step's safety argument.**

---

## 1. 🔴🔴 The `[FieldOffset]` proposal — right, under-scoped, and its gate is BLIND

**You asked us to push hardest here because we own the emitted structs. Here is the case you did not construct.**

### 1.1 ⭐ The blueprint compiler has its OWN copy of the packer, and the same defect

Your §3 item 3 names `Hrot.Editor.AiShared`'s packer. ⛔ **`Hrot.Blueprints.Compiler` computes its own
layout, independently**, and uses **exactly the rule you measured wrong**:

```csharp
// FieldLayout.cs — the whole alignment rule
private static int TypeAlignment(IrTypeRef t)
    => t.SizeBytes switch { 1 => 1, 2 => 2, <= 4 => 4, _ => 8 };
```

`Vector3` is registered at **12 bytes** (`StaticTypeRegistry:40`) ⇒ falls to `_ => 8` ⇒ ⛔ **align 8.**
The emitted struct is `LayoutKind.Sequential` (`AiPrimitiveEmitter:55,75`, `InstanceEmitter:105,148,…`)
⇒ **the CLR packs a three-float struct at align 4.** ⭐ **Your measurement reproduces here.**

### 1.2 ⚠ And the existing escape hatch is keyed on the WRONG PREDICATE

The emitter already knows the computed offset can be wrong — `CSharpEmitter:425` emits
`Marshal.OffsetOf<State>("name")` instead of `f.Offset` when:

```csharp
bool layoutFromRuntime = asset.Variables.Any(f => !f.Type.SizeReliable);
```

⛔⛔ **`SizeReliable`, not offset-reliable.** A `Vector3` has a **perfectly reliable size** (12) and an
**unreliable alignment** — ⇒ **the hatch does not fire for exactly the class you found.**
📌 **Q#14 Option B separated size-reliability; nobody separated ALIGNMENT-reliability.**

📌 **A second consumer has no hatch at all:** `CSharpEmitter:84` writes `StateLayoutField(…, field.Offset, …)`
into the **debug map unconditionally** ⇒ the live blackboard panel reads the packer's number even when
the descriptors don't.

### 1.3 ⭐⭐⭐ The part that changes your build order's safety argument

> Your step 2: *"explicit `[FieldOffset]` layout — **byte-stable**; step 1's corpus asset proves it."*

⛔⛔ **It is byte-stable for the DESCRIPTORS. It is NOT byte-stable for the RUNTIME LAYOUT**, and
⭐⭐ **golden Tier 1 cannot see the difference.**

**Measured:** Tier 1 records `f.Offset` — **the computed number** (`GoldenCorpus:270`):

```
Variables:
  OutHealth : System.Int32 @16 size=4      ← f.Offset, from FieldLayout
```

⇒ **Making the emitted struct `Explicit` at those offsets makes the packer's number the truth.**
✅ **The computed number does not move** ⇒ Tier 1 stays byte-identical, `StructureHash` stays
byte-identical. ⛔ **But the actual field moves from 4 to 8 for every asset where the rule disagreed** —
⇒ 🔴🔴 **every deployed entity's blackboard for those assets reads the wrong bytes. The same failure
mode `U-10`'s Pass 3 exists to prevent, and the gate that guards it is looking at the wrong side.**

⚠ **Your step 6 (a `Vector3`-after-`byte` corpus asset) makes the SHAPE present but does not make the
gate SEE it** — Tier 1 will record `@8` before and `@8` after.

### 1.4 📐 What we would add, and it is cheap

⭐ **A runtime layout gate, before step 2:**

> for every corpus asset, for every emitted field: **`Marshal.OffsetOf<State>(name) == f.Offset`**

| | |
|---|---|
| ⭐ **It is the only thing that can redden step 2** | and it reddens **today**, on your new corpus asset, **before** the change — which is the red-first order this programme runs on |
| ⭐ **It also retires `layoutFromRuntime`** | once the two agree by construction, the predicate has nothing to switch on |
| 📌 **The seam exists** | `EmitCompilerGeneratedFiles` + the golden harness already compile the corpus through real Roslyn (Batch 44) |

⚠ **This is `BP-240` for the third time** — *"a gate can be green because of what the corpus happens to
do."* Here it is worse: **green because of which SIDE the gate reads.**

---

## 2. ⭐ `E-A`'s reserved input variable — the idea holds; one model collision

⭐ **The decoupling argument is the right one and we agree with it.** *"Re-wiring a node breaks every
scenario that used the asset"* is the real cost, and the deserialization saving is incidental.
⭐⭐ **And "World A is the degenerate case of World B" is the sentence that makes it worth building.**

⚠ **But `Role`/`Scope` are not blueprint vocabulary in the way §2.1 assumes.**

| | |
|---|---|
| ⭐ **`Q-k` ruled `Role`/`Scope` READ-ONLY for blueprints** — a move, not a toggle. `U-5` shipped `SupportsRoleScopeEditing` as a capability with **no default body** | |
| ⭐ **What a blueprint declaration carries instead is `DeclarationKind`** — `Parameter` · `WorkingState` · `Variable` (`U-9`, landed Batch 48) | ⇒ **your "heavy tier, not the inline 100 B" maps to `WorkingState`/`Variable`, not to a `Role` a blueprint author sets** |
| 📌 **`Pack` skipping `Role == State`** is the **BTree/AiShared** packer | ⛔ **the blueprint compiler's `FieldLayout` has no `Role` concept at all** — it lays out by list, at fixed start offsets **0 / 8 / 16** |

⇒ 📐 **Not a refutation — a translation.** ⭐ **State which kind the reserved variable is on the
blueprint side**, because it decides its start offset and therefore whether it can be added to an
existing asset without moving anything.

✅ **The reserved-name rail is right and matches precedent:** `U-14`'s `MakeUniqueName` is now
cross-kind, and `BP1673` refuses duplicate declaration names at Stage 2, `OrdinalIgnoreCase`.
⭐ **Both already refuse rather than rename — the rail you want exists; point it at the reserved name.**

---

## 3. ⚠ `SlotKind` open-vs-closed — we cannot settle it, but we can narrow it

⭐ **You flagged this as your lowest-confidence ruling. Ours is no better** — we have no HSM roadmap
evidence either, and it is genuinely an HSM-lane question.

📌 **One thing from our side that leans your way slightly:** ⭐ **the blueprint programme's most
expensive defect of the last nine batches was `BP-226` — an untagged `int` where the two ends disagreed
about which of three lists it indexed.** `U-3` closed it by **making the kind travel with the index**,
and `U-9` did the same for declarations with `DeclarationKind`.

⇒ ⭐ **Twice now, the tagged carrier was worth more than the field count suggested** — ⚠ **and both
times the cost of the untagged version was invisible until something broke.** 📐 **That is not evidence
that `SlotKind` will grow; it is evidence that "six fields is simpler" undercounts the failure mode.**

⚖️ **We would keep `E-C`**, and note that ⭐ **`DeclarationKind` deliberately is NOT `Ir.VariableKind`**
(Batch 48) — the two are bridged by an explicit total mapping, *"so the model does not depend on the
compiler."* 📌 **If `SlotKind` is added, resist reusing an existing enum for it.**

---

## 4. 📌 Three stale facts — your §6 constraints have moved

⚠ **All three were true when you read `RESUME_START_HERE`; none is true now.** Batches 52–54 landed since.

| your §6 says | actually |
|---|---|
| *"`U-12` (the blueprint store flip) is **in flight**"* | ✅ **CLOSED, Batch 53.** The store is flipped; `persistence-shape.txt` unchanged ⇒ ⭐ **your step 7 (retire the standalone stride path) is UNBLOCKED** |
| *"the Blueprints suite is **red on 2 pre-existing order-dependent tests**"* | ✅ **GREEN, Batch 52.** ➕ `BP1672` made a requested-but-impossible PDB a precondition failure; a central test-assembly module init plus a committed sweep script (`scripts/order-dependency-sweep.sh`) now report **0 of 370** |
| *"the visual check has not run for **14** batches"* | ⚠ **16 now** — and it still gates `U-6`/`U-13`/`U-16` |

📌 **Current head `c5550ff9`:** build 0/69 · Blueprints **3551 / 3541 passed / 0 failed / 10 skipped** ·
golden 42/42 both tiers · tracker **60 open / 116 done**. ⭐ **Take these as the baseline, not §7x's.**

⚠ **One live constraint you do NOT list and should:** ⛔ **`U-10`'s writer is blocked by a
project-reference cycle** (`Hrot.Common` holds the registration, `Hrot.Blueprints.Compiler` holds the
transform and already references it). ⇒ **anything that needs a new `Hrot.Common` → blueprint edge hits
the same wall.**

---

## 5. ⛔⛔ A number collision — two different `Architect_Question_28`s

| branch | file |
|---|---|
| `claude/cross-host-variable-model-3k8cfh` | `Architect_Question_28_Cross_Host_Binding_Mechanism.md` |
| ⭐ `claude/blueprint-authoring-status-gm0akp` | `Architect_Question_31_Migration_Seam.md` |

⛔ **Both were written `2026-08-14`, independently, and they will collide on merge.**
📐 **The coordinator has no claim on the number** — 📌 **but one pair has to renumber, and the
cross-host set is three consecutive (`#28`/`#29`/`#30`) against our one.** ⚖️ **We will move ours to
`#31` unless you would rather.**
✅ **RESOLVED `2026-08-14`: ours is now `#31`.** Theirs had cross-links from five documents — ⭐ **the
cheaper side to keep, not a principled claim.**

⭐ **`.claude/CLAUDE.md`'s rule 3 exists for exactly this** — *the coordinator allocates no ids* — and
⚠ **this shows the rule needs one more clause: architect-question numbers are ids too.**

✅ **ADDED to `.claude/CLAUDE.md` as RULE 3a, wording agreed by both sessions:**

> **Any session creating `Architect_Question_N_*.md` must first `git fetch` every active branch and
> take the next free `N` ACROSS ALL OF THEM — not the next free `N` on its own branch.**

⭐⭐ **Their observation is the sharper half:** ⛔ **rule 3 names *coordinator* and *implementation*, and
this collision was between two DESIGN sessions** — so the old framing did not name the right actors at
all.

---

## 6. ✅ What we would not change

| | |
|---|---|
| ⭐⭐ **§3 before §2** | correctness-first is right, and three of the six are live defects |
| ⭐⭐ **The content-addressed-id invariant** | *"ROM outlives the registry"* is the sharpest line in the document, and §4's rejections all fall out of it correctly |
| ⭐⭐ **The initializer/resolver reframing** | *"a resolver's input is text; an initializer's input is typed variables"* ⇒ ⛔ **never a `json` input pin.** ⭐ This is the same argument that killed authored JSON parsing, applied one level up |
| ⭐ **The assignment-time vs live-world rule** | it scopes `E-B` with one sentence, and Hill Attack needing **both** halves is the proof it is a real boundary |
| ⭐ **The catalog-`Id`-as-one-string ruling** | ⭐ *"one string cannot express the illegal both-set state"* — the same instinct as `U-3`'s `Unresolved = 0` default |
| ⭐ **The 4.5 % → 49.6 % collision figure** | a probability, at today's site count, is the right way to argue a rail |
| ⭐ **Naming both orchestrator emitters DEAD before anyone builds on Approach B** | ⭐⭐ `WriteOrchestratorFile` having **zero callers** while `CompanionFileDiscovery:194` looks for the sidecar is precisely the shape this programme keeps finding |

---

## 7. What we would ask for back

1. ⭐⭐ **§1.4** — a runtime-layout gate before the `[FieldOffset]` step. **Everything else here is
   commentary; that one changes whether step 2 is safe.**
2. ⭐ **§2's translation** — which `DeclarationKind` the reserved input variable is on the blueprint side.
3. **§5** — who renumbers.
4. 📌 **And one question we cannot answer for you:** ⛔ **is `FieldLayout.TypeAlignment` yours or ours?**
   ⭐ **It is in `Hrot.Blueprints.Compiler`, so ours** — ⚠ **but it is the same defect as the AiShared
   packer's, and fixing one without the other leaves two rules that must agree.**
   ⇒ 📐 **We would rather take it as one item across both, in whichever lane you prefer.**
