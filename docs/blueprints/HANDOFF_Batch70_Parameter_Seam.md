# HANDOFF — Batch 70: **the parameter seam** — `DEBT-AIB-021` · the Instance params seam · `G7`+`W10`

> 📌 **Dispatched at `7f166ffe6`.** Frozen per rule 1 *(rule 1a: re-dispatch only while this sha is NOT
> in your history)*. ✅ **Batch 69 MERGED at `72f24d326`** — gates re-run by me, snapshots unchanged.
> ⭐ **Rule 7 / Rule 4.** ⛔ **Rule 3: the coordinator allocates no ids.**
> ⭐ **One commit per item · per-item STOP conditions.**
>
> ⭐⭐⭐ **This is the batch the whole parameter design was written for.** 📄 **The authority is
> [`DESIGN_Parameter_Model.md`](DESIGN_Parameter_Model.md)** — ⛔ **do not re-derive anything from it**;
> §0 lists ten conclusions this programme already got wrong once, and §8 has the rails **pre-written.**

---

## 0. ⭐⭐ Batch 69 — **what I adopted from you**

| | |
|---|---|
| ⭐⭐⭐ **the silent-default verdict is now a REPO RULE** | *"what distinguishes the three from the harmless majority is not the default — it is that the caller HELD the value and did not pass it."* ⇒ filed in **`.claude/CLAUDE.md`**, with your control *(a forwarding rail **per dependency**, asserted on the **constructed object**)*. ⛔ **And your refusal to build a generic detector is part of the rule, not a caveat** |
| ⭐⭐ **the tick counter's placement** | a **side table in `Fdp.Toolkits`**, not a field. ⭐ **You made "`StructureHash` unchanged" STRUCTURAL rather than lucky** — recorded in the plan §4A4 as the reasoning to reuse |
| 🔴 **my `C-watch` §7 was stale twice** | ⭐ **and the real defect was underneath it** — the `!_debugMaps.ContainsKey(assetId)` guard. **Corrected in the design.** ⚠ **Second time in three batches** that a bad citation of mine hid something worse |

---

## 1. 🔴🔴 `DEBT-AIB-021` — **the generated `ParseParams` must honour the incoming JSON**

> 📄 **`DESIGN_Parameter_Model.md` §3.2**, including its **CORRECTION block** — *"runtime wins" is the
> design and is **true of the curated path**; it is **FALSE of the generated managed-asset path."*

### ⭐ What I measured, so you do not have to

📐 `BTreeBridgeEmitCore.EmitParseParamsLocal` *(≈`:1198`)* — ⭐⭐ **there are TWO defects, not one:**

| # | measured | consequence |
|---|---|---|
| 🔴 **(a)** | the emitted lambda body writes **only** `JsonSerializer.Deserialize<Dto>("<baked default>")` at each `field.ByteOffset`. The comment says it outright: *"The lambda body ignores the incoming json arg"* | ⛔ **a per-assignment override is discarded** |
| 🔴🔴 **(b)** | ⭐⭐ **the EMIT GUARD**: `if (defaults.Count == 0) return false;` — and `defaults` counts only variables with a **non-null `DefaultValueJson`** | ⛔⛔ **an asset whose variables have NO defaults emits NO `ParseParams` at all** ⇒ **fixing (a) alone leaves those assets exactly as broken.** ⭐ **This is the half the debt row does not mention** |

### What to build

⭐ **`DEBT-AIB-021` names the shape and I am not re-deciding it:** *"deserializing a wrapper JSON object
keyed by variable name and dispatching to each variable's deserializer."*

| rule | |
|---|---|
| ⭐ **emit whenever the asset has ≥1 packed managed variable** | ⛔ **not "≥1 default"** — that is defect (b) |
| ⭐ **order: bake defaults FIRST, then overlay from `json`** | *"defaults are baked, scenario JSON overlays them, runtime wins"* — the sequence **is** the ruling |
| **absent key** ⇒ the baked default stands | that is what "overlay" means |
| ⭐ **unknown key ⇒ IGNORED, not an error** | 📐 **because the CURATED path already ignores them** — `JsonSerializer.Deserialize<TDto>` drops unmapped members unless `UnmappedMemberHandling` is set. ⛔ **Ruling 9: one mechanism, one behaviour.** ⚠ **Assert this as a DECISION test**, so a later batch does not "fix" it |
| **empty / null `json`** ⇒ defaults only | the shipped behaviour, and it must stay byte-identical |

### 🔴 STOP conditions

| | |
|---|---|
| ⚠ **the golden corpus** | 📐 **I measured: 43 golden assets, all `.bp.json`** — this item changes a **BTree bridge emitter**, so `persistence-shape` should not move. ⛔ **If it does, stop** — you changed asset shape, not registrar text |
| ⚠ **generated registrar text IS compared somewhere** | if a `.g.cs` snapshot or emission test locks the old body, ⭐ **update it deliberately and say so** — do not regenerate silently |
| ⭐ **`ParseParamsEmissionTests` exists** | `Hrot.AiEditor.Generators.Tests/Bridge/ParseParamsEmissionTests.cs` — ⭐ **start there; it is the file that encodes today's behaviour** |

**rails:** ⭐ **an overlay of ONE variable leaves the others at their baked defaults** · a variable with
**no default and no json entry** is left as `InitDefault` left it · **unknown key does not throw**
*(the decision test)* · ⭐⭐ **an asset with variables but ZERO defaults now gets a working
`ParseParams`** *(the (b) rail — it must fail before your fix)*.

---

## 2. ⭐⭐⭐ The **Instance params seam** — 📄 `DESIGN_Parameter_Model.md` §3.3

> ⭐ **The user ruling this implements:** *"Instances could and should reuse the param parsing and
> resolving."* ⛔ **`BlueprintAssignmentDto.Overrides` is NOT the mechanism and must stay unread.**

### ⭐ Measured ground truth — **use it, do not re-measure it**

| | measured `2026-08-17` |
|---|---|
| ⭐⭐ **the delegate already fits** | `ParseParamsDelegate(string json, byte* memory, EntityRepository world, Entity self, IHostVariableAccess? host)` — ⭐ **a destination POINTER** ⇒ **only the pointer differs between a behaviour and an Instance.** ⛔ **Do not declare a second delegate type** *(ruling 9)* |
| ⭐ **`IHostVariableAccess` already ships**, declared-not-implemented, deliberately | ⛔ **`E7a` populates it later — NOT this batch.** Pass `null`, which is its defined value for a root |
| 🔴 **`BlueprintDefinition` carries neither** | no `ParseParams`, no params size/offset. Fields today: `Name · Kind · StructureHash · StateSize · InitDefault · Tick · EventHandlers · Functions · StateClrType · StateFields · AssetId` |
| 🔴 **`AttachInstanceBlueprintEvent` is `{Entity, BlueprintId}`** | ⛔ **no payload** — and it is a **`struct`** with `[EventId]`, published via `Bus.Publish` |
| ⭐⭐ **the precedent for a payload is `AssignBehaviorEvent`** | *"**Must be a class (not a struct) because it carries managed string fields**"*, published via `PublishManaged`. ⇒ ⭐ **the shape is already decided by precedent** |
| ⚠ **a test asserts the opposite** | `BlueprintEventIngressSystemTests.AttachInstanceBlueprintEvent_IsValueType` — ⭐ **changing it is legitimate here; changing it silently is not** |
| ⭐ **`AttachToEntity` today** | `ChooseTier(def.StateSize)` → `TryAttach(memory, blueprintId, def.StateSize, def.StructureHash, out payloadOffset)` → `def.InitDefault(span of StateSize)` |
| 🔴 **`FieldLayout`** | `LayoutFields(asset.Parameters, startOffset: 0, …)` — ⛔⛔ **for an Instance, offset 0 IS the `BlueprintLatentCursor`.** ⭐ **Safe today only because no Instance has parameters** |

### ⭐⭐ The layout — **params go INSIDE the one struct, after the cursor**

```
payload:  [ BlueprintLatentCursor 16 ][ Params N ][ State M ]
                                       ^ ParamsOffset = 16
```

| | |
|---|---|
| ⭐ **`StateSize` keeps meaning "the whole payload"** | ⇒ `InitDefault`'s span still covers everything and `TryAttach`/`ChooseTier` need no new arithmetic |
| ⭐ **`FieldLayout`** | params base: **`0` for AiPrimitive (unchanged), `16` for Instance**; state base: **`8` for AiPrimitive (unchanged), `16 + N` for Instance** |
| ⭐ **the definition gains `ParamsOffset` + `ParamsSize`, both EMITTED** | ⛔ **do not re-derive `16` at any runtime call site** — that constant already has one home |
| 🔴🔴 **ORDER: `InitDefault` FIRST, then resolve** | ⛔ **the reverse wipes the params** — and it would look like a resolver bug |

### ⭐⭐ Parse-before-commit at attach

⭐ **Mirror `BehaviorIngressSystem` exactly** — *"a failed parse leaves the entity 100% on its old
behaviour"*. ⇒ **resolve into a scratch buffer of `ParamsSize` BEFORE `TryAttach`**, and copy in after
`InitDefault`. ⛔ **A failed resolve must leave NO slot allocated** — not an allocated-then-freed one.

### 🔴 STOP conditions

| | |
|---|---|
| ⭐⭐⭐ **`StructureHash` MUST NOT MOVE — and that is a PREDICTION, not a hope** | 📐 **I measured every `.bp.json` in the tree: 296 Instance assets, of which ZERO carry `Parameters`.** Every asset with parameters is AiPrimitive. ⇒ **`N = 0` everywhere today, so `16 + N == 16`** — byte-identical, exactly like Batch 56's unification. ⛔⛔ **If a hash moves, you shifted something you should not have. STOP and report rather than regenerating goldens** |
| ⚠ **the managed-vs-unmanaged bus** | converting Attach *(and Replace)* to managed events must preserve **`BlueprintEventIngressSystem`'s two-phase drain**: all removes, then all attaches. 📐 **The ordering is enforced by the SYSTEM's loop order, not by the bus** — ⭐ **but confirm `ReadManaged` is non-consuming within a frame** the way `Read<T>()` is, since Replace is drained **twice**. ⛔ **If it is not, STOP** — that is a real semantic difference, not a detail |
| ⚠ **`RemoveInstanceBlueprintEvent` needs no payload** | ⭐ **leave it a struct** unless the bus forces otherwise; say which you did and why |
| ⚠ **`EntityBlueprintsEditModel.AttachEvents`** | an editor list of these events — ⭐ **it must keep compiling and keep its meaning**; a plan built in the editor is the other producer of this event |
| ⭐ **who fills `json` at attach?** | **The event carries it.** ⛔ **Do NOT invent a second source** — no lookup into `BlueprintAssignmentDto.Overrides`, no side table. If a caller has nothing to pass, it passes empty and defaults stand |

**rails — ⛔ THEY ARE ALREADY WRITTEN, `DESIGN_Parameter_Model.md` §8. Use those, do not invent new ones.**
⭐ The four that bite here: **cursor is not overwritten** *(assert the `BlueprintLatentCursor` at
offset 0 is intact after a resolve — this is the `startOffset: 0` trap, caught by a test)* ·
**parse-before-commit** *(a failing resolve at attach leaves the entity WITHOUT the new Instance)* ·
**one supply mechanism** *(exactly one parameter-resolution path exists — a second `Overrides`-style
applier fails it)* · **the tail is untouched** *(no write to `ExpectedThreatLevel` or either interrupt)*.

**impact:** compiler + runtime + editor model. ⛔ **`StructureHash` / `persistence-shape` MUST NOT MOVE.**

---

## 3. ⭐ `G7` + `W10` — **ONE producer picker, specified once**

> ⛔ **Ruling 9 — no two implementations of one concept.** `G7`'s *"parameter resolver: None / Pick /
> Create"* and `W10`'s *"initializer picker"* are both **"pick a named producer from a contributing
> catalog."** 📄 Plan §4c.

| constraint | source |
|---|---|
| ⭐⭐ **identity is the generated FQN, NOT the AssetId** | **architect `AQ2`** — `blueprint-finalize/TASK-DETAIL.md:248`. ⛔ **Non-negotiable** |
| ⭐ **offer over the UNION** | ⛔ not `Variables` alone — 📄 `PLAN_Cross_Host_Sequencing.md:176` |
| ⭐⭐ **the mechanism already exists — REUSE it, do not coin a picker** | 📐 **I measured `BehaviorActionCatalog`**: a contributing catalog, `Id = schema.Fqn` *(already FQN)*, `Source` tagged per contributor, `Changed` event. ⭐ **`AN7-REPORT.md:73–95` is the named precedent**: *"add a source enum member + contributing catalog, not a new picker"* |
| ⚠ **one plan claim of mine is STALE** | I wrote *"`BehaviorActionSource.AiPrimitive` exists but is never assigned."* 📐 **False on `HEAD`** — `BehaviorActionCatalog.cs:175` assigns it. ⭐ **Corrected here; do not spend time on it** |

### ⭐ Scope — **tight, because `G7` in full is six pieces**

📄 The resolver design **§8.2** decomposes `G7` into `E1`–`E6`. ⛔ **This batch is the PICKER only** —
i.e. the *"None / Pick"* control and the catalog behind it.

| in | out |
|---|---|
| ⭐ a **producer catalog** in the `BehaviorActionCatalog` shape · the **picker control** · **persist by FQN** · **"None" is a first-class value** | ⛔ *"Create resolver"* scaffolding (`E5`) · ⛔ *"detach authored shape"* / divergence detection (`E6`) · ⛔ Library-asset authoring (`E1`) |

🔴 **STOP if the picker needs a SECOND catalog** — if resolvers and initializers cannot be served by one
contributing catalog, ⭐ **say why in one paragraph and build only the one this batch can prove.**
⚠ **Two catalogs would be exactly the duplication ruling 9 forbids**, so that is a report, not a choice.

**rail:** ⭐ **headless** — the catalog offers a producer contributed by each source, the picker
round-trips **"None" → a producer → "None"**, and ⭐⭐ **what is persisted is the FQN** *(assert the
stored string, not just that reload works — an AssetId would also round-trip)*.

---

## 4. ⛔ NOT in this batch

⭐⭐ **Blueprint multi-occurrence is PULLED** — 📄 **[`Architect_Question_34_Blueprint_Occurrence_Identity.md`](Architect_Question_34_Blueprint_Occurrence_Identity.md)**.
📐 **Why:** `BlueprintSlotEntry` is **exactly 16 bytes with no spare**, `InstanceVersion` is taken, and
the header is the wrong granularity ⇒ **the discriminator needs a decision I owe the user first.**
⚠ **It does not block item 2** — the seam changes the **payload**, the key changes the **slot entry**.

Also out: **`E0`** *(the HSM golden harness — its own batch, ruled)* · `E3` · `E5` · `E6` ·
`E7a`/`E7b` *(⛔ including populating `IHostVariableAccess`)* · the `InspectorWindow`
"STATIC PARAMETERS" retirement · the Track C **visual check**.

---

## 5. Gates

**Baseline — coordinator-verified at `72f24d326`:** build **0 / 69** · Blueprints **3657 / 3647 / 0 / 10** ·
AiShared **1280** · BTree **615** · Breakpoints **134** · Generators **203** · Hsm.Editor **531** ·
AiEditor.Persistence **136** · Toolkits **1964** · NodeEdit **208 / 131** · tracker **open 61 / done 148**.

| | |
|---|---|
| ⭐ **add any suite the diff reaches** | ⭐ **you added `AiEditor.Persistence` unprompted last batch — keep doing that.** Items 1–2 reach Generators, Persistence, Toolkits and Blueprints |
| ⭐⭐ **`Fdp.Toolkits.Tests`** | ⛔ **a full-suite red is not signal by itself; a full-suite green is not evidence either.** `DEBT-AIB-030` |
| 🔴🔴 **`StructureHash` unchanged · `persistence-shape` UNCHANGED** | ⭐ **stated FIRST in the report, as you have been doing.** For item 2 this is a **prediction with a measured reason** (§2) — ⛔ **if it fails, that is the finding, not an inconvenience** |
| **per-item revert-goes-red** · `tracker-counts.py --check` · ⚠ **the two NodeEdit gates take NO `--no-build`** | |

---

## 6. Reporting

⭐⭐ **The gate table — one row per gate, verbatim command, result.**

**Per item:**
⭐ **item 1** — **did the (b) rail fail before your fix?** *(it should: an asset with variables but no
defaults gets no `ParseParams` at all)* · what you did with any locked registrar text.
⭐⭐ **item 2** — **`StructureHash` unchanged, stated first** · **is `ReadManaged` non-consuming within a
frame?** · which lifecycle events you converted and which you left · ⭐ **where `ParamsOffset` lives so
`16` is written once**.
⭐ **item 3** — **one catalog or two**, and if two, why.
**Always:** ⭐ **every id you allocated** · ⭐ **which rows on the `DEBT-AIB` partition list this batch
touched** *(item 1 is `-021`; item 2 plausibly reaches `-001` / `-002` / `-008` / `-011`)*.

⭐⭐⭐ **One standing ask, unchanged and it keeps paying:** when a premise of mine fails, **STOP and
report it** rather than working around it. **Three of the last four batches turned up a design error of
mine that way**, and each was worth more than the item it interrupted.
