# Architect question #30 — parameter preprocessing for editor-authored assets

> **Raised 2026-08-14** by the user: *"the behaviour asset can be complicated and might need complex
> param preprocessing. How is this solved in Platoon Hill Attack — hand-written resolver, or buried in
> actions? For editor-authored assets, maybe we could support an editor-authored resolver, a small
> blueprint mapping the asset input params to working-state vars?"*
>
> ⭐ **Context:** [`PA-13`](PriorArt_Cross_Host_Variable_Model.md) — a managed asset **cannot be
> parametrized per instance today**. The generated `ParseParams` ignores its `json` argument
> (`DEBT-AIB-021`), and HSM emits no `ParseParams` at all.

---

## Ground truth — how Hill Attack ACTUALLY does it

⭐⭐ **Neither "hand-written resolver" nor "buried in actions". BOTH — and the split is principled.**

| stage | where | what it does | cost |
|---|---|---|---|
| **1 · impedance matching** | ⭐ **hand-written resolver** `HillAttackCommanderNodes.ParsePlatoonHillAttackParams:670` | deserializes a **different** DTO (`PlatoonHillAttackParamsJsonDto`: lat/lon, network ids) into the runtime struct; **geo→cartesian** via the `IGeographicTransform` singleton; **entity resolution** via `NetworkEntityMap`; defaulting (`>0f ? : 30f`); computes **attack direction** from baseline→firing-line centres; try/catch → `default` on failure; a cartesian-only fallback for offline tests | ⭐ **cold path — once per assignment.** Allocates freely, uses `JsonSerializer`, reaches managed singletons |
| **2 · derivation & distribution** | ⭐ **inside an action** `:100–125` | reads `p` (params) + `s` (working state), computes each tank's slot along the baseline (`t = i/(count-1)`), **serialises a NEW `MoveToLocationParams` per tank**, publishes `AssignTacticalIntentEvent { IntentId, JsonParams }`, records `BaselineReservedMask` | hot path, but **event-driven and rare** |
| **3 · the subordinate repeats stage 1** | its own resolver | `ParseHullDownAttackParams` / `ResolveMoveToParams` | cold |

⇒ ⭐⭐ **The split is by INPUT TYPE and COST, not by taste:**

| | stage 1 | stage 2 |
|---|---|---|
| **input** | ⭐ **raw text** (`string json`) | ⭐ **typed params + live state** |
| **needs** | world **singletons**, a JSON deserializer, allocation | the asset's own variables, subordinate list |
| **runs** | once, at assignment, before the first tick | when the graph decides |

📌 **This is why stage 1 cannot simply move into a node:** it is the only place that sees the *text*,
and it must complete **before any node reads the params**.

### What the blueprint vocabulary has today

| | |
|---|---|
| ✅ **`GraphKind.Construction`** | exists (`GraphTypes.cs:199`) — the *runs-once, cannot-suspend* kind. ⭐ **Exactly the shape of a preprocessing pass** |
| ⛔ **no world-singleton access** | zero `GetSingletonManaged`/`HasSingletonManaged` anywhere in the blueprint compiler ⇒ **a blueprint cannot reach `IGeographicTransform` or `NetworkEntityMap` today** |
| ⛔ **no JSON vocabulary** | no parse/deserialize node kind exists |

---

## Q30-A — Where does **JSON → typed variables** happen?

| | Option | ⚖️ |
|---|---|---|
| **A1** | ⭐⭐ **Generated** — close `DEBT-AIB-021`: a wrapper JSON object keyed by **variable name**, each dispatched to its packed offset | ✅ ~30 lines; `packedFields` already holds the offsets; **no authoring surface at all** · ⚠ fixed JSON shape (keyed by variable name) |
| **A2** | **Authored** — the user's "editor-authored resolver" also does the parsing | 🔴 **blueprints have NO JSON vocabulary** ⇒ inventing one means *authoring a parser graphically*. ⛔ Nobody wants this, and it is the least interesting part of the job |
| **A3** | Keep requiring a hand-written resolver per asset | 🔴 defeats the purpose of editor authoring |

📐 **Ruling: A1** — deserialization is mechanical and must never be authored.
⚠ **The one real cost, stated:** A1 fixes the JSON *shape* to be keyed by variable name — a scenario
cannot present a flat, human-friendly payload like Hill Attack's `{firingLineStart, tankSpacing}`
without a mapping step.

> ⛔⛔ **SUPERSEDED by [`Q30-D`](#q30-d--the-asset-input-as-a-reserved-variable) — user proposal,
> `2026-08-14`, and it is strictly better.** ⭐ **A1's wrapper-JSON-keyed-by-variable-name convention is
> WITHDRAWN.** A reserved input variable makes the JSON shape the DTO's own shape, needs no invented
> convention, and removes this cost entirely. **A1 survives only as the principle** — *deserialization
> is generated, never authored*.

---

## Q30-B — Where does **typed → derived** happen? *(the user's actual proposal)*

| | Option | ⚖️ |
|---|---|---|
| **B1** | ⭐⭐ **An authored `Construction`-kind graph that runs once at assignment**, after A1, reading input variables and writing derived variables / working state | ✅ **the kind already exists and already cannot suspend** (`BP1650`/`BP1661` rails); it is the editor-authored twin of Hill Attack's stage 1b · ⚠ **needs world access added to the vocabulary** |
| **B2** | **An `OnEntry` / first-tick node** in the asset's own graph | ✅ **zero new machinery** — this is Hill Attack's stage 2, and it already works · 🔴 **params are underived for the first tick**, and every reader must tolerate that. ⚠ **Fragile in exactly the way `BP-224` was** |
| **B3** | Hand-written resolver only, as today | ✅ maximally capable · 🔴 **an editor-authored asset then cannot ship without a programmer** |

📐 **Ruling: B1 — the user's proposal is right, but ONLY for this half.**

⭐ **The reframing that makes it work: it is not a "resolver", it is an INITIALIZER.**

| | resolver | ⭐ initializer |
|---|---|---|
| input | **text** | ⭐ **typed variables** (A1 already ran) |
| must exist because | only it sees the JSON | only it can run *after* parse and *before* tick 1 |
| authorable? | ⛔ no — would mean authoring a parser | ✅ **yes — it is ordinary graph logic** |

⇒ ⭐⭐ **Carving the JSON out is what makes the idea tractable.** B1 without A1 is A2, and A2 is bad.

⚠ **B2 is not wrong, it is what ships** — and it stays the right answer for *derivation that depends on
live state* (Hill Attack's per-tank distribution needs the subordinate list, which does not exist at
assignment). ⇒ **B1 and B2 coexist; they are not alternatives.** State the rule:

> ⭐ **Assignment-time, depends only on input params → initializer (B1).
> Depends on live world state or timing → a node (B2).**

---

## Q30-C — What must the blueprint vocabulary gain?

⛔ **This is the honest cost of B1, and it is not small.**

| | need | why |
|---|---|---|
| **1** | ⭐⭐ **managed world-singleton access** | Hill Attack's stage 1 is *mostly* `IGeographicTransform` and `NetworkEntityMap`. **Without this, B1 cannot express the motivating example** |
| **2** | a **geo→cartesian** node | the single most common preprocessing step in the shipped corpus |
| **3** | an **entity-from-network-id** node | ditto |
| **4** | a defined **failure mode** | the hand-written resolvers `try/catch` → write `default` + log. An initializer needs the same contract, since `BehaviorIngressSystem` treats a parse failure as *"stay on the previous behaviour"* |

📐 **Ruling: 1 is required; 2 and 3 are the round-out and should ship with it; 4 is not optional.**
⚠ **Sequence B1 AFTER `Q28`'s gate work** — it is a new authoring surface, and the key/layout rulings
must be green first.

---

## Q30-D — ⭐⭐ The asset input as a **reserved variable** *(user proposal, and it supersedes A1)*

> *"If the asset input param DTO struct is a variable itself — now C# hardcoded, later editor-authored
> — maybe with a fixed reserved variable name, the serialization/deserialization and blueprint access
> becomes easy."*

⭐⭐ **Ruled: YES. This is better than `A1` and it should replace it.**

| | `A1` (withdrawn) | ⭐ **`D` — reserved input variable** |
|---|---|---|
| JSON shape | ⛔ **an invented convention** — a wrapper object keyed by internal variable name | ✅ **the input DTO's own shape** — nothing invented |
| scenario author must know | ⛔ internal variable names | ✅ **only the asset's declared input type** |
| deserialize | N calls, one per variable | ✅ **ONE `Deserialize<TInput>` + one `Unsafe.Write` at its offset** |
| initializer access | needs a new accessor story | ✅ **it is just a variable** — existing Get/Set nodes, already typed |
| relation to curated behaviours | ⛔ a parallel shape | ⭐⭐ **the SAME shape** — a curated behaviour is exactly "one input DTO at offset 0". **World A becomes the degenerate case of World B** |

### ⭐ The part that makes it more than an implementation shortcut

It gives an asset an explicit **public parameter interface**, distinct from its internal wiring:

```
scenario JSON ──generated deserialize──► [reserved variable  Input : TInput]   ← the asset's PUBLIC contract
                                                  │
                                            initializer  (Q30-B / Q30-E)
                                                  ▼
                            per-action variables  (MoveToParams, EngageParams, …)  ← INTERNAL
                                                  │
                                          bound by nodes / state-slots
```

⚠ **Without `D`, the scenario would have to write the per-action variables directly** — coupling the
scenario to the asset's internal action DTO types, and making any re-wiring a breaking change to every
scenario. ⭐⭐ **`D` is what makes an asset's parameters refactor-safe.**

⭐ **Bonus, and worth taking:** with a typed input variable, a **hand-written** initializer no longer
needs the `(string json, byte* memory, …)` shape either — it can be
`(in TInput input, ref TState vars, EntityRepository world, Entity self)`. **No pointers, no JSON.**
⇒ **keep `ParseParamsDelegate` for the curated behaviours that already use it; new initializers get the
typed signature.**

### ⚠ What `D` must settle

| | |
|---|---|
| **reserved name** | needs an actual reservation rail — ⛔ **`MakeUniqueName` would silently rename a user's clashing variable rather than refuse it.** Refuse, do not rename |
| **type source** | today a C# struct type id, like any struct-DTO variable (`extraSizeResolver`/`StructSizeResolver` already resolves these). ⭐ **Editor-authored later — no model change needed, which is the user's point** |
| ⭐ **which tier** | see `Q30-F` — **it should probably NOT sit in the 100-byte region** |

---

## Q30-E — How is the initializer SELECTED in the editor?

| | Option | ⚖️ |
|---|---|---|
| **E1** | ⭐ **Picker over discovered C# static methods**, attribute-marked (`[BehaviorParamsInitializer]`) | ✅ **the machinery exists** — `IActionSchemaExporter` (shared, in `Hrot.Editor.AiShared`, attribute-driven reflection) → `BehaviorActionCatalog` → picker. ⭐ Same path the action picker already uses · ⚠ needs a new attribute + catalog facet |
| **E2** | ⭐ **Reference a blueprint `Construction` graph** by `(assetId, graphId)` | ✅ **precedent exists** — `BehaviorTreeAssetDto.SubtreeAssetId : Guid` already references another asset · ⚠ needs `Q30-C`'s world vocabulary first |
| **E3** | ⛔ **Forced method name derived from the asset name** | ✅ zero UI · 🔴 **the `BP-224` shape** — see below |

### ⛔ Why E3 is rejected

| | |
|---|---|
| 🔴 **it cannot distinguish "no initializer" from "initializer missing or misspelled"** | both look identical: no method found. **A silent no-op is the worst possible failure here** — params simply stay at their defaults |
| 🔴 **it breaks on rename** | renaming an asset silently unbinds its initializer |
| ⭐ **the codebase already ruled against this class** | `BP-228` closed *"a made-up type id is refused; the type picker is safe by construction"*. ⛔ **A naming convention is the exact opposite of safe-by-construction.** And `DeriveWorkingStateTypeFromMethod` — the one convention that exists — is explicitly a **legacy fallback**, not a primary mechanism |

📐 **Ruling: E1 and E2, as ONE discriminated field — not two nullable fields.**

```
ParamsInitializer = { Kind: None | Method | Graph,  MethodFqn? | (AssetId?, GraphId?) }
```

⭐ **Same reasoning as `Q28-B`'s "a method may not be bound both ways":** *"which initializer"* is **one
question**, and two nullable fields permit a both-set state nobody can interpret. **`Kind` makes the
illegal state unrepresentable.**

⭐ **Sequence: E1 first.** It is cheap, needs **no new blueprint vocabulary**, and immediately unblocks
Hill-Attack-class assets in the editor. **E2 follows `Q30-C`.**

---

## Q30-F — Which tier does the input variable live in?

⚠ **Raised by `D`, and not obvious.**

| | Option | ⚖️ |
|---|---|---|
| **F1** | In the **100-byte inline region**, like any `Input` variable | ✅ simplest; uniform · 🔴 **the input DTO is read ONCE, by the initializer** — then never again. **Spending scarce inline bytes on a cold-path value is backwards**, and Hill Attack's input DTO alone would be a large fraction of 100 B |
| **F2** | ⭐ In the **heavy tier**, with only the derived per-action variables inline | ✅ **the 100 B holds only what the hot path reads** · ⚠ the initializer reads from one tier and writes to another |
| **F3** | **Transient** — deserialized to a stack buffer, passed to the initializer, never stored | ✅ costs **zero** persistent bytes · 🔴 **unreadable after assignment** ⇒ no live inspection, and a re-init cannot re-derive without the original JSON |

📐 **Ruling: F2.** ⭐ **F1 is the trap** — it puts a cold-path value in the hottest, scarcest region, and
`Q-F`'s budget work would then have to count bytes no tick ever reads.
⚠ **F3 is tempting and wrong**: the debug/inspection story matters (the live inspector manifest already
surfaces `Role`/`Scope` per variable), and *"what was this instance actually parametrized with"* is a
question anyone debugging a scenario will ask.
📌 **And note F2 falls out of the existing model for free** — ⭐ **`Pack` already skips `Role == State`
variables** (`BTreeBlackboardPackHelper:140`), so a non-inline tier for a declared variable is an
established pattern, not a new one.

---

## Answers — ⭐⭐ ARCHITECT RULING, `2026-08-14`

> ⭐ **Provenance:** NotebookLM unavailable; this session is architect of record.
> ⚠ **Claude-authored — overturn on evidence, not authority.**

**A → A1. B → B1, plus B2 retained for live-state derivation. C → world access is required, and it is the real cost.**

⭐⭐ **The insight that decides all three: the user's proposal conflates two jobs that Hill Attack
already keeps separate.** Deserialization is *mechanical* and belongs to the generator; derivation is
*logic* and belongs to the author. **Splitting them is what turns "author a resolver" — which would
mean drawing a JSON parser — into "author an initializer", which is ordinary graph work.**

⚠ **The trap to avoid, and it is the one the naming invites:** ⛔ **do not let the initializer take
`string json` as an input pin.** The moment it can, someone will parse in it, and the vocabulary will
grow a JSON sub-language nobody designed. **The initializer's inputs are typed variables. Full stop.**

📌 **Two things this ruling does NOT authorise:**

| | |
|---|---|
| ⛔ **not a general "run a blueprint at assignment" hook** | it is scoped to *initialise this asset's own variables*. A graph that publishes events or mutates other entities at assignment time is stage 2's job, and stage 2 has a tick to run in |
| ⛔ **not a replacement for hand-written resolvers** | ⭐ **Hill Attack should stay hand-written.** It predates this, it works, and its stage 1 does try/catch, logging and an offline fallback that the vocabulary will not have on day one. **B1 is for NEW editor-authored assets** |

**D → yes, and it supersedes A1. E → E1 + E2 as one discriminated field; E3 rejected. F → F2.**

⭐⭐ **`D` is the ruling that reorganises the rest.** A1 was solving *"how does JSON reach N variables"*;
`D` observes that **the asset should have ONE input, and the mapping to N is a separate, authored
step** — which is exactly what Hill Attack does (`PlatoonHillAttackParamsJsonDto` → one struct → the
commander distributes). ⇒ **the user's model is the shipped pattern, generalised.**

⚠ **The strongest argument for `D` is not simplicity, it is coupling:** without it a scenario writes
the asset's *internal* per-action variables, so re-wiring a node breaks every scenario.
⭐ **`D` gives the asset a public parameter contract.** That is worth more than the deserialization saving.

### ⭐ Where this sits in the build order

⭐⭐ **`A1` jumps the queue.** It is the only finding in this programme that **blocks a workflow
outright** rather than risking silent corruption — ⇒ **it belongs beside the gate at step 1**, not
after it. **`B1` is a later, larger piece** and should follow `Q28`'s steps 1–4.

| revised | |
|---|---|
| **1** | the collision gate + corpus asset · ⭐ **+ `D` (the reserved input variable + generated deserialize, closing `DEBT-AIB-021`)** — independent of each other, both headless |
| **1b** | 🔴 kill the counter-allocated stubs |
| **2–4** | explicit layout · FQN re-bake + `SlotKind` · asset-driven HSM thunks |
| **4b** | ⭐ **`E1`** — the initializer picker over attribute-discovered C# methods. Cheap, no new blueprint vocabulary, unblocks Hill-Attack-class assets |
| **5** | ⭐ **`B1`/`E2` the authored initializer + `C`'s vocabulary** |

⚠ **And `A1` has an HSM twin that does not exist at all** — `HsmBridgeEmitCore` emits no `ParseParams`.
⭐ **Do both in one batch**; they are the same ~30 lines against two emitters.

---

## Change log

| Date | Change |
|---|---|
| 2026-08-14 | Raised and ruled (`A`–`C`). |
| 2026-08-14 | ⭐⭐ **`D`/`E`/`F` added from the user's proposal. `A1`'s wrapper-JSON convention WITHDRAWN** — the reserved input variable supersedes it. `E3` (name-derived) rejected as the `BP-224` shape. |
