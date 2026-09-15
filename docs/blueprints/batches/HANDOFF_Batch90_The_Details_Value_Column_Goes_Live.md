<!--STATUS
state: LIVE
updated: 2026-08-19
current-answer: this whole file - the Batch 90 dispatch.
stale-below: nothing.
known-rot: none.
known-conflict: it REFINES Batch 88's BP-334 lean (a "formatted-value arm"). Section 2
  states the measurement that changed it from a STRING arm to an OBJECT arm. Where the
  two differ, this file wins.
-->
# HANDOFF — Batch 90: **the Details Value column goes live** *(`BP-334`)*

> 📌 **Dispatched at `67a6376e4`.** ⭐ **Branch from THIS commit** *(rule 7)* — the handoff itself.
> ⛔⛔ **YOUR SCOPE IS FROZEN AT THIS SHA.** ⭐ Documents changing after it are **FYI ONLY**.
> ⚠ **If a later document INVALIDATES an item — STOP AND REPORT.** ⛔ **Do NOT adapt, do NOT revert.**
> ⭐ **Rule 3: allocate your own ids and state them.** ⭐ **Rule 1b: push
> `chore: started batch 90 at 67a6376e4` FIRST, before any code.**
>
> ⭐⭐⭐ **User, `2026-08-19`:** *"is the value monitoring in the detail panel finally working? this is
> the one i am waiting for — no useless `(pending)`"* ⇒ ⛔ **it is not.** ⭐ **This batch is that.**

---

## 1. ⭐⭐⭐ THE DEFECT — **measured `2026-08-19` on the post-Batch-89 tree**

```
grep -rn "readRaw:" --include=*.cs Hrot/ | grep -v Tests      → NOTHING
```

⭐ **Three production sites build the Details row sources and NOT ONE passes a reader** —
`BlueprintMyBlueprintWindow:350` · `:378` · `PerspectiveWorkspaceRegistrar:363`.

📐 **What that produces, exactly** *(both row sources spell it identically — this is not a bug in the
rendering, it is the honest output of a source with no reader)*:

```csharp
ReadValue:          reader == null ? () => Array.Empty<byte>() : () => reader(v.Name),
HasEverBeenWritten: reader != null,          // ⇒ false ⇒ the Value cell renders "(pending)"
```

### ⭐⭐ TWO live-value seams exist, and the Details table reads NEITHER

| seam | shape | consumed by |
|---|---|---|
| **`ILiveBlackboardValueProvider`** | `GetLiveVariableValues(asset)` → name → **`string`** | ⛔ **exactly ONE surface** — `BlackboardAuthoringWindow:524` *(the standalone **Blackboard Variables** window)* |
| **`readRaw`** on the row sources | name → **bytes**, decoded by the ONE formatter | ⭐ **the Details table** — ⛔ **and nothing supplies it** |

⇒ ⭐ **That is why `88a` made the *Blackboard Variables* window live on Blueprint and Details stayed
`(pending)`.** ⛔ **Neither `88` nor `89` claimed otherwise** — both fenced `BP-334` out explicitly.

---

## 2. ⭐⭐⭐ THE DESIGN DECISION — **an OBJECT arm.** ⚠ *This REFINES Batch 88's lean; read why*

📄 **Design basis:** `REPORT_Batch88…md` §2.2 *(the three options and the `(b)` lean)* · **ruling 9**
*(no two implementations of one concept)* · `R-49` *(**never** generate per-variable code)* ·
`Q32` ruling 3 *(ONE Value column, meaning switched by run state)*.

⭐⭐ **Batch 88's `(b)` said *"give `IVariableRowSource` a formatted-value arm."*** ⛔ **Measuring the two
sources says the arm should carry an `object`, not a `string`** — ⭐ **and that BTree/HSM does not need
the arm at all:**

| host | 📐 what its live source actually holds | ⇒ ⭐ what it should supply |
|---|---|---|
| **Blueprint** | `BlueprintStateSnapshot.FieldValues` is **`IReadOnlyDictionary<string, object>`** — ⭐ **already-decoded CLR values** | ⭐⭐ **the OBJECT arm.** ⛔ Re-encoding decoded values to bytes *(option `(a)`)* is the absurdity Batch 88 rejected — ⭐ **and formatting them to a string throws away the formatter's job** |
| **BTree / HSM** | `LiveBlackboardValueProvider` projects **`(BrainBlackboard, Type, ByteOffset)`** — ⭐⭐ **it HAS the bytes**, and only formats them at the very end | ⭐⭐⭐ **RAW BYTES — the existing `readRaw`.** ⛔ **No new arm needed here at all** |

### ⭐⭐⭐ Why `object` and not `string` — **the reason is the formatter**

📐 The pipeline today is **bytes → `RawValueDecoder` → `object` → `VariableValueFormatter` → text**, and
the formatter is constructed with a `DecodeRawValue` delegate *(`(byte[], Type) → object?`)*.
⇒ ⭐⭐ **An `object` arm enters that pipeline exactly one step in.** The formatter keeps ownership of
**notation · elision · `<unreadable>` · `(pending)` · the struct tooltip**.

⛔⛔ **A STRING arm hands notation to the provider** ⇒ **two notations for one value** — 📌 the exact
class of defect `BP-01`/`C8` closed *(raw hex is a regression)*, and the struct-notation wart already
filed against `C7` is a live warning that this seam is easy to split.

### ⚠ THE HONEST COST — **state it, do not hide it**

📐 **§4a's change highlight diffs BYTES.** ⇒ ⛔ **a row fed through the object arm has no bytes and its
highlight is INERT.**
⭐ **That is the SAFE direction and the codebase already chose it once** — 📌 `ReadAssetTick`'s own doc:
*"a row with no tick source has an INERT highlight (never lights) rather than a WRONG one."*
⇒ ⭐⭐ **Blueprint: values live, highlight inert. BTree/HSM: values live AND highlight live**, because
they supply real bytes. ⛔ **Do NOT fake bytes to light the highlight.**

---

## 3. 🛠 **`90a` — the object arm on the row** ⭐⭐⭐ *(the enabling change)*

| ⭐ | |
|---|---|
| **add** | an optional **`ReadValueObject`**-style delegate *(name yours)* beside `ReadValue` on `VariableRow` — ⭐ **`null` by default**, so every existing construction is unchanged |
| **the formatter** | ⭐ **prefers the object arm when present; falls back to bytes.** ⛔ **ONE `Cell`/`Tooltip` implementation** — 📌 ruling 9. ⚠ **Both `Cell` overloads and both `Tooltip` overloads** — a Value cell that is live while its tooltip is `(pending)` is worse than neither |
| ⛔ **do NOT** | add a **string** arm · add a second formatter · touch `RawValueDecoder` · generate anything per variable *(`R-49`)* |

### ⭐⭐ `(pending)` must stay HONEST — **this is the row that decides whether the user is satisfied**

⛔⛔ **`HasEverBeenWritten` must NOT become an unconditional `true` because a provider exists.**
⭐ **The rule: the name is PRESENT in this frame's value map ⇒ written; ABSENT ⇒ `(pending)`.**
📌 Both providers already return **only** the variables they could project ⇒ ⭐ **absence is
meaningful and free.**
⚠ **Guide row `C9` depends on this** — *"a variable declared but never written by the run reads
`(pending)`"* ⇒ ⛔ **a zero where `(pending)` belongs is a REGRESSION, not a fix.**

---

## 4. 🛠 **`90b` — Blueprint supplies objects**

⭐ `BlueprintLiveValueProvider` *(Batch 88a)* already resolves the snapshot through
`BlueprintRuntimeInspectorPane.ResolveInspectorSnapshot`, ⭐ **which owns the paused-pointer-vs-live
decision** — ⛔ **do not re-decide it.**
⇒ ⭐⭐ **Expose the `FieldValues` map** *(objects)* **alongside the string map it already returns**, and
wire it into `SectionVariableRowSource` at `BlueprintMyBlueprintWindow:350`/`:378`.

⚠ **`R-67`, and the Blueprint registrar is the one that has forgotten a service FOUR times** ⇒
⭐⭐ **rail the CONSTRUCTED object, not the call site.**

---

## 5. 🛠 **`90c` — BTree / HSM supply BYTES**

⭐⭐ `LiveBlackboardValueProvider` already computes the projection and then formats it.
⇒ ⭐ **Split the PROJECT step from the FORMAT step** and expose the bytes as a `readRaw`, wired at
`PerspectiveWorkspaceRegistrar:363`.
⭐⭐⭐ **This host therefore needs NO new arm** — it fills the seam that was designed for it and has been
`null` since it was built. ⛔ **Do not route BTree/HSM through the object arm** — it would throw away a
working change highlight.

⚠ **Keep the existing string path intact** for `BlackboardAuthoringWindow` — 📌 *"no rush removals"*.
⭐ **If the string formatting can be expressed as `format(project(...))` with no behaviour change, say
so**; ⛔ **do not force it.**

---

## 6. ⛔ SCOPE FENCE

| ⛔ not this batch | |
|---|---|
| **retiring `LiveBlackboardPanel`** | ⭐ `Q38` ruled: **the fixed-list formatter arm FIRST**, then retire |
| **`BP-337`** *(`Fdp.Presentation.Tests` crashes its host)* | ⭐ real, and a Vis2D defect |
| **anything from `Q38`–`Q44`** | ⛔⛔ **`R-27`** |
| **task group `D`** *(the orchestrator emitters)* | ⭐ ruled, ⛔ separate batch |
| **watch pinning · the `⋮` button** | unbuilt, elsewhere |

---

## 7. ⭐ GATES — **the rule-8 contract, plus the three this batch owns**

| # | report |
|---|---|
| **1–7** | the standard contract — verbatim commands · **`--no-build` column** *(⛔ `NodeEditor.Core`, `NodeEditor.UI`, `Fhsm.Tests` report a STALE BIN)* · golden movement as a **diff shape** · every red confirmed **pre-existing vs `67a6376e4`** · clean tree after every suite · both quarantine counts · every id you allocated |
| 🔴🔴 **7b** | ⛔⛔ **RUN `tracker-counts.py --check` **LAST**, AFTER your final tracker edit, and PASTE ITS OUTPUT.** ⚠⚠ **Batches 88 AND 89 both reported this gate GREEN while it was RED** — the ROWS were right and the **derived summary table was stale**, both times. 📌 **Fifth and sixth instance of one mechanic.** ⭐ **`python3 scripts/tracker-counts.py` prints the correct table — paste it in, do not hand-count** |
| ⭐⭐ **8** | ⭐ **THE ENUMERATION: every `IVariableRowSource` implementer and every production construction site of one**, by `search_graph`, with the query and its `total`. 📌 **`R-74`** — ⚠ **Batch 87's gate 8 found a FOURTH table host nobody knew about** |
| ⭐⭐⭐ **9** | ⭐ **What each rail ASKS.** ⛔⛔ **A rail on the provider's return value proves NOTHING** — 📌 Batch 88's own words. ⭐⭐ **ASK THE ARTEFACT: the CELL TEXT the control would draw**, for four cases — **live value · `(pending)` for a name absent from the map · `(pending)` with no provider at all · the TOOLTIP agreeing with the cell** |
| ⭐⭐ **10** | ⭐ **REVERT-GOES-RED**, un-applied with the **INVERSE EDIT** — ⛔ never `git checkout --`. ⚠ **At minimum: drop the wiring at each of the three construction sites, separately** — ⭐ **three sites, three distinct reds**, or one of them is unrailed |

⭐ **Baseline** *(post-Batch-89)*: AiShared **1457** · Blueprints **3767/3777/10** · BTree.Editor **615** ·
Hsm.Editor **551** · Hrot.Editor **201** · Breakpoints **143** · NodeEditor.Core **211** ·
NodeEditor.UI **135** · Fhsm **300** · `Fdp.Presentation.Tests` **146 FILTERED** *(⛔ `BP-337`: the full
suite crashes its host — **filter, and say so**)* · tracker **open 67 / done 205** · rulings **65/65**.
⛔ **`Fdp.Toolkits.Tests`: do not run it** — 📌 `DEBT-AIB-030`.

## 8. ⭐⭐ If you must stop

⭐ **`90a` + `90b` is a complete batch** — it is the host the user asked about first.
⭐ **`90a` + `90c` is also complete.** ⛔ **`90b`/`90c` without `90a` is not** — the arm is the enabler.
⚠ **If the object arm turns out to need a change to `VariableValueFormatter`'s CONSTRUCTOR contract,
STOP and report** — ⛔ that is a shared type and a design question, not a wiring one.

## 9. ⭐⭐⭐ WHAT THIS UNLOCKS — **state it in the report**

⭐ **Guide rows `C7` and `H9` INVERT**: they currently say *"expect `(pending)` — a live value would be
a surprise."* ⇒ ⭐⭐ **after this batch a live value is the PASS condition**, and I will rewrite them.
⭐ **Say which hosts you made live**, so the guide names the right ones.
