# Architect question #28 — the cross-host binding mechanism (BTree · HSM · Blueprint)

> **Raised 2026-08-14** by the cross-host variable/call design session, branch
> `claude/cross-host-variable-model-3k8cfh`, after the prior-art sweep in
> [`PriorArt_Cross_Host_Variable_Model.md`](PriorArt_Cross_Host_Variable_Model.md).
>
> ⭐⭐ **This is the MECHANICAL half** — how a binding is identified, where its offset comes from, and
> which layout is true. [#29](Architect_Question_29_Cross_Host_Variable_Semantics.md) carries the
> semantic half.
>
> ⚠ **Every "ground truth" row below was read out of the code or measured on 2026-08-14.** Nothing is
> inherited from the HSM session's §1 or the bootstrap's §4 without re-checking — three of their rows
> did not survive.

---

## Ground truth

### The three projection paths that exist today

| # | emitter | key | offset comes from | projection |
|---|---|---|---|---|
| 1 | `AiPrimitiveEmitter.cs:267` (blueprint standalone) | `"{Ns}.{Class}.BTreeTick@0"` — ⛔ **always `@0`** | ⛔ **nothing** | `bb.BehaviorParameters[paramIndex * SizeOf<Params>()]` — **stride** |
| 2 | `BTreeBridgeEmitCore.cs:488` (BTree managed) | `"{MethodFqn}@{ByteOffset}"` | ⭐ **the asset** (editor packer) | `Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], offset)` |
| 3 | ⭐⭐ `HsmActionGenerator.cs:261` (**HSM, already ships**) | `"{MethodName}@{offset}"` → `ComputeHash` | ⛔ **a source attribute** | **same as 2** |

⇒ ⭐⭐ **The HSM session's central proposal (`Q-A`) is already implemented.** The convention is not in
question. **Two things about it are.**

### 🔴 Path 1's multiplier is not a parameter slot

`Interpreter.cs:655` passes `node.PayloadIndex`; `NodeDefinition.cs:28` says that indexes
`MethodNames[]`; `TreeCompiler.cs:212 GetOrAddMethodName` **dedups by name**. Each generated class
multiplies that ordinal by **its own** `sizeof(Params)`. ⇒ **no allocator reserves these regions.**
⭐ **Safe today only because every path-1 key is `@0`.**

### 🔴 The id space is 16 bits, with three unguarded hazards

| | |
|---|---|
| `ComputeHash` | FNV-1a-32 → `(ushort)(hash & 0xFFFF)`. **Char-identical** in `HsmFlattener.cs:385` and `HsmActionGenerator.cs:802`; ~10 copies repo-wide |
| **collisions** | `RegisterAction` does `ActionTable[id] = ptr` — **silent overwrite**. The static-initialiser path uses `Add` — **throws**. Two failure modes, one hazard. ⚠ **~300 distinct keys ⇒ ~50 % chance of at least one collision** |
| ⭐ **reserved values** | `HsmKernelCore.cs:304,448,669,714` treat **both `0` and `0xFFFF`** as *"no action"*. ⛔ **`ComputeHash` excludes neither.** A key hashing to either is **silently never dispatched** |
| 🔴 **`HSM-016` on top** | `HsmBridgeEmitCore.cs:119` registers no-op stubs at `actionId = 100++` / `200++` while the flattener uses `ComputeHash(name)` ⇒ **a stub can overwrite a real action** |

⭐⭐ **Prior art for the fix already ships:** `UtilityInputGenerator.cs:173` +
`SharedUtilityDiagnostics.UT0103_HashCollision` — the same FNV-1a-16 gate, for utility input names.

### 🔴 The packers disagree with the compiled struct — measured

All three packers use `align = min(size, 8)`
(`BlackboardBinPacker.cs:102`, `BTreeBlackboardPackHelper.cs:22`, `HsmActionGenerator.GetTypeAlign`).

```
struct S { byte B; Vector3 V; }        // measured, .NET 8
Marshal.OffsetOf<S>("V")  = 4          // the DD calls this AUTHORITATIVE
packers compute min(12,8) = 8          // and BlackboardBinPackerTests.cs:131 ASSERTS the 8
```

⭐ The rule is right whenever `size ≤ 8`. It is wrong for **any type whose size exceeds its natural
alignment** — `Vector3` (12/4), `Vector4`/`Quaternion` (16/4). All are in every packer's `KnownSizes`.
📌 **No shipped asset hits it yet** — it needs such a variable preceded by a field ending off an
8-byte boundary. ⭐ **The `bool`/`[MarshalAs(I1)]` rule (`BTreeEmitCore.cs:112`) is the same class of
hazard, already handled. This one is not.**

### ROM constraints that bound the answers

`StateDef` = **32 B**, `[FieldOffset]`-explicit, **one spare byte** (`Reserved29`).
`TransitionDef` = **16 B**, none spare. ⇒ ⛔ **widening an action id from `ushort` to `uint` costs a
ROM change on both.**

---

## Q28-A — What identity does a binding carry?

| | Option | ⚖️ |
|---|---|---|
| **A1** | ⭐ Keep both conventions as they are — BTree `{MethodFqn}@{offset}`, HSM `{MethodName}@{offset}` | ✅ zero work · 🔴 **simple-name keying collides across types**; two conventions is the "two copies of an ordering that must agree" shape `BP-226` was about |
| **A2** | ⭐⭐ **Unify on `{MethodFqn}@{offset}`; HSM adopts the FQN form** | ✅ one convention, and the FQN removes the cross-type collision entirely · ⚠ changes every HSM `CompoundKey`, so every hashed id moves — **a one-time re-bake, headlessly checkable** |
| **A3** | Drop hashing: allocate dense ids at flatten time, store a side table | ✅ no collisions by construction · 🔴 **breaks hot-reload registration** — the registrar is a separate compilation that cannot see the allocator |
| **A4** | Keep `{MethodName}` but widen the id to `uint` | ✅ collision pressure ~vanishes · 🔴 **ROM change on `StateDef` AND `TransitionDef`** (16 B, nothing spare) |

⭐ **Independent of the choice, three gate items follow from the ground truth:**
**(i)** reject duplicate ids at generate time (mirror `UT0103`); **(ii)** reject any key hashing to
`0` or `0xFFFF`; **(iii)** ⛔ **`HSM-016`'s counter-allocated stubs must go** — they can overwrite.

📐 **Claude's lean: A2 + all three gate items.** ⭐ **A4 is the one to avoid**: it spends a ROM change
to buy what a compile-time gate gives free, and the 16-bit space is not actually the problem — the
*absence of a gate* is. ⚠ **A3 is worth a sanity check from you**: it is the only option that makes
collisions structurally impossible, and I may be over-weighting the hot-reload objection.

---

## Q28-B — Who computes the offset? *(this is the real `Q-A`)*

| | Option | ⚖️ |
|---|---|---|
| **B1** | **Source-driven** — the offset comes from `[SharedAiAction(typeof(Dto), "Field")]`, as HSM does today | ✅ already built; the analyzer can type-check the field · 🔴 **the editor cannot bind a variable to an offset no attribute declared** ⇒ HSM authoring stays broken |
| **B2** | ⭐⭐ **Asset-driven** — the editor's packer picks the offset, the bridge emits one thunk per `(method, offset)` pair found in the asset. **BTree path 2, generalised** | ✅ the editor can bind anything; **one mechanism instead of three** · ⚠ needs an HSM twin of `EmitManagedActionThunks` — **which is what `HSM-016` already is** |
| **B3** | Both, asset wins where present | ✅ no migration · 🔴 **two sources of truth for one number** — precisely the shape that produced `BP-226` |

📐 **Claude's lean: B2.** ⭐ **It is nearly forced:** B1 cannot express editor-chosen bindings at all,
and the work B2 needs is work `HSM-016` already scopes. ⚠ **The thing I want your ruling on is
whether B1 survives alongside B2 for hand-written library actions** — `BlueprintLifecycleLibrary`'s
three `[SharedAiAction]` methods are source-declared and work fine. **My lean is yes: B1 stays as the
hand-authored path, B2 is the authored-asset path, and they never describe the same binding.**

---

## Q28-C — Which layout is true?

| | Option | ⚖️ |
|---|---|---|
| **C1** | ⭐⭐ **Make the packer true by construction** — emit the blackboard struct with explicit `[FieldOffset]` at the packed offsets | ✅ **byte-stable for the existing corpus** (today packer == CLR everywhere it is exercised); eliminates the whole drift class, `bool` included · ⚠ `LayoutKind.Explicit` on every generated blackboard |
| **C2** | Fix the packers to compute **true natural alignment** (`min(size, 8)` → the struct's own max field alignment) | ✅ restores the DD's stated contract · 🔴 **offsets move ⇒ every key moves ⇒ the golden corpus moves**, and the DD's *"advisory"* packer stays advisory — the drift class returns for the next type |
| **C3** | Keep `min(size, 8)` and **refuse** variable types where `size > naturalAlign` | ✅ cheapest; a diagnostic, not a layout change · 🔴 **bans `Vector3`/`Vector4`/`Quaternion`** — all three are in `KnownSizes`, i.e. intended |

📐 **Claude's lean: C1.** ⭐ **The argument that decides it:** the DD says the packer *"must replicate
C# sequential layout exactly"* — C1 removes the obligation to replicate anything, because the packer's
number **becomes** the layout. C2 leaves a rule that must be re-verified for every future type; C1
leaves nothing to verify. ⚠ **And C1 is the only one that is byte-stable**, which matters with `U-12`
in flight and the golden corpus as the gate.

⚠⚠ **Whichever you pick, one gate is independent of the choice:** ⭐ **add a `U-1` Tier-1 corpus asset
with a `Vector3` preceded by a `byte`.** Today the corpus cannot see this class at all — a green that
means nothing.

---

## Q28-D — Does the standalone stride path (path 1) survive?

| | Option | ⚖️ |
|---|---|---|
| **D1** | ⭐ **Retire it** — route the standalone `BTreeTick` through the offset form with a baked `@offset` | ✅ **one projection formula repo-wide**; removes an unallocated region scheme · ⚠ touches `AiPrimitiveEmitter` + `CSharpEmitter`, and `U-12` is in flight |
| **D2** | Keep it, and **constrain it to `@0`** with a rail that refuses anything else | ✅ minimal; matches what ships today · ⚠ freezes a path that cannot host two actions |
| **D3** | Keep it and make the stride real — allocate slots properly | 🔴 **builds a second allocator** to sit beside the packer |

📐 **Claude's lean: D1, sequenced AFTER `U-12`.** ⭐ **The reason it matters beyond tidiness:** the
HSM session is about to adopt "the BTree convention", and BTree currently has **two** — if we answer
`Q-A` without saying which, we export the ambiguity. ⚠ **D2 is an acceptable interim** if you would
rather not open `AiPrimitiveEmitter` during the store flip; it is honest and costs one rail.
⛔ **D3 is the one to avoid** — it is `BP-226`'s shape a third time.

---

## Answers

⛔ **Not yet run past the architect.** The section below is **Claude simulating the architect**, at the
user's request, so the reasoning is on the table before the real round — ⚠ **it is NOT an architect
ruling and must not be built from.** ⭐ **Prior architect answers redirected the approach in four of
the last nine batches; treat every line here as a hypothesis.**

### ⚠ SIMULATED — Claude-as-architect

**A → A2, with all three gate items, and the gate is the actual deliverable.**
⭐ The framing that matters: **the collision risk is not created by adding offsets to keys — it is
merely revealed by it.** A 16-bit space with silent-overwrite registration was always unsafe; it
survived because the key population was small. ⇒ **ship the gate first, independently, before anything
widens the key population.** It is a compile-time diagnostic over a list the generator already has —
cheap, headless, and it makes A2's re-bake verifiable instead of hopeful.
⛔ **A3 rejected, but not for the hot-reload reason Claude gave** — the stronger objection is that
dense allocation makes the id **positional**, and a positional id over a set that two compilations
must agree on is exactly `BP-226`. The hashed key is *content-addressed*, which is why it survives
separate compilation at all. ⭐ **Keep content-addressing; add the gate.**

**B → B2 for authored assets, B1 retained for hand-written library actions — and say so in one rule.**
The rule: ⭐ **an offset is authored where the binding is authored.** A `[SharedAiAction]` method
declares its own binding in source; an asset declares its bindings in the asset. **They are two
authoring surfaces, not two sources of truth for one number** — B3's defect is that it lets *both*
describe *the same* binding. ⇒ **the rail to build is not "asset wins", it is "a method that carries
`[SharedAiAction]` may not also be asset-bound"** — refusable at generate time, and it makes the
distinction structural rather than a precedence rule.

**C → C1, and Claude's reason is right but understated.**
⭐ The real argument is about **who is allowed to be wrong**. Under C2/C3 the packer must *predict* the
CLR, and a prediction can drift silently with any future type or runtime. Under C1 the packer
*dictates* and the CLR obeys — ⛔ **there is no oracle to disagree with.** That converts a recurring
correctness obligation into a one-time emit change.
⚠ **One caveat Claude missed:** `LayoutKind.Explicit` will refuse to lay out a struct containing a
managed reference at an overlapping offset, and it interacts with `[InlineArray]` / `fixed` buffers.
⇒ **verify C1 against the fixed-capacity list types from Q#19/Q#21 before committing** — if it does not
hold there, the answer degrades to C2 **plus** the corpus asset, not to C3.
⭐ **The corpus asset (`Vector3` after `byte`) is required under every option** — build it first; it is
the only thing that makes any of these answers falsifiable.

**D → D2 now, D1 once `U-12` lands, and the interim rail is not optional.**
⭐ Claude's sequencing is right, but the framing should be sharper: **path 1 is not "a second
convention", it is a path with no allocator at all.** A rail that refuses any standalone key other
than `@0` is therefore not a stopgap — it is **the statement of the invariant that currently holds by
accident**, which is the thing worth writing down regardless of when D1 happens.
⇒ **Build the rail in the same batch as the `Q28-A` gate** (both are generate-time diagnostics over
keys), and let D1 follow the store flip.

### 📌 What the simulated answers change about the questions

| | |
|---|---|
| ⭐ **A gate batch appears that no question asked for** | duplicate-id + reserved-value + standalone-`@0` rails are one coherent, headless deliverable, and **every other answer depends on it being green** |
| ⚠ **C1 acquired a precondition** | it must be checked against `[InlineArray]`/`fixed` blackboards before it can be ruled |
| ⭐ **B's rail changed shape** | from *"asset wins"* to *"a method may not be bound both ways"* — structural, not precedence |
