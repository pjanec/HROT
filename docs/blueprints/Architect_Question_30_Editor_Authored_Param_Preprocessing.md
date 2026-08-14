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

📐 **Ruling: A1.** ⭐ **Deserialization is mechanical and must never be authored.** ⚠ **The one real
cost, stated:** A1 fixes the JSON *shape* to be keyed by variable name — a scenario cannot present a
flat, human-friendly payload like Hill Attack's `{firingLineStart, tankSpacing}` without a mapping
step. ⇒ **that mapping is exactly what `Q30-B` is for.**

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

### ⭐ Where this sits in the build order

⭐⭐ **`A1` jumps the queue.** It is the only finding in this programme that **blocks a workflow
outright** rather than risking silent corruption — ⇒ **it belongs beside the gate at step 1**, not
after it. **`B1` is a later, larger piece** and should follow `Q28`'s steps 1–4.

| revised | |
|---|---|
| **1** | the collision gate + corpus asset · ⭐ **+ `A1` (`DEBT-AIB-021`)** — independent of each other, both headless |
| **1b** | 🔴 kill the counter-allocated stubs |
| **2–4** | explicit layout · FQN re-bake + `SlotKind` · asset-driven HSM thunks |
| **5** | ⭐ **`B1` the initializer + `C`'s vocabulary** |

⚠ **And `A1` has an HSM twin that does not exist at all** — `HsmBridgeEmitCore` emits no `ParseParams`.
⭐ **Do both in one batch**; they are the same ~30 lines against two emitters.

---

## Change log

| Date | Change |
|---|---|
| 2026-08-14 | Raised and ruled. |
