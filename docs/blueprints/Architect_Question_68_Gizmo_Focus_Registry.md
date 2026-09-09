<!--STATUS
state: LIVE
updated: 2026-09-09
current-answer: §6 — ALL FOUR SUB-QUESTIONS ARE APPROVED (user, 2026-09-09). §6 is the ruling and
  the build order; §4c (68-B settled by measurement) and §4d (68-C keying) are the evidence behind it.
stale-below: §3's "My lean:" lines are now HISTORY — they record how each answer was reached, not
  its standing. ⛔ Quote §6 for what was decided. §3's 68-B framing ("a fallback for an unresolvable
  token") is SUPERSEDED by §4c and must not be quoted at all.
known-conflict: none identified
-->

# Architect Question #68 — One gizmo focus registry, and what happens to the routing fallback

**Raised by:** the `UXI-07` tool-model lane, after step 4b.
**Owning issue:** `CE-259c` — *the two-arbiter defect has an owning design whose answer was never built.*
**Design basis:** [`docs/designs/gizmos-1/gizmo-input-focus-design.md`](../designs/gizmos-1/gizmo-input-focus-design.md) §6.2, §6.2a, §9 ·
[`docs/UX/UX_Feature_Tool_Model.md`](../UX/UX_Feature_Tool_Model.md) §4.8–§4.12.

---

## 1. The situation in one paragraph

The gizmo focus design specified **one registry** holding the exclusive-focus slot
(`ActiveGlobalGizmo` inside a single `GizmoInteractionManager`), and said the ECS system would
shrink to *"a lifecycle bridge plus an event router"* over it. **The interfaces were ported into
FDP; the arbiter was not.** FDP instead grew **two** independent arbiters, each with a private
`_focusedGizmo`, and several adapters grew private one-slot arbiters of their own. Every defect
`UXI-07` has fixed is a consequence.

## 2. 📐 INVENTORY — the enumeration behind this question

Queries actually run (`search_graph` on the re-indexed graph, plus grep; the codebase-memory MCP
was reconnected for these):

```
search_graph(name_pattern=".*(GizmoManager|GizmoSystem|GizmoExecutionController|InteractionHost|FocusHolder).*", label="Class")   → total 12
search_graph(name_pattern=".*(Suspend|Resume|PushModal|PushTool|PopTool).*")                                                      → total 180
grep -rn "ActiveGlobalGizmo"                                                                                                      → 0
grep -rn "GizmoInteractionManager"                                                                                                → 9, ALL in GizmoMap.Example
```

| what holds "who owns the mouse" | where | size |
|---|---|---|
| `GlobalGizmoManager._focusedGizmo` | `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/GlobalGizmoManager.cs` | 14 touches; grant at `:66` |
| `DataDrivenGizmoSystem._focusedGizmo` | same folder | ~40 touches; **4 grant sites** (`:91`, `:291` grant-if-null, **`:452` STEALS**); ~8 release sites |
| `MeasureToolGizmoAdapter._wasActive` | `Hrot.IG/Gizmos/` | ✅ **removed** by `CE-259e` |
| `IgApplication._activeLocationPickerId` · `_activeEntityPickerId` | `Hrot.IG/IgApplication.cs:267-268` | 5 uses each — **still present** |
| `ReplayBrowserSubsystem._activeGizmoId` | `Hrot.ReplayBrowser/` | 7 uses, one public (`IsPickPendingFor:1016`) — **still present** |
| `IgApplication._activeSequenceGizmo` | `Hrot.IG/IgApplication.cs:266` | 8+ sites; area/route authoring, **not a picker** |

⚠ **`check_index_coverage` was NOT run** — the MCP server dropped repeatedly during this session
and the CLI does not expose that tool. ⇒ the enumeration above is best-effort and is **not** claimed
as exhaustive.

## 3. The sub-questions, each with my recommended answer

### 68-A — Should FDP adopt the design's single registry at all?

**My lean: YES.**

⛔⛔ **CORRECTED `2026-09-09` — the original argument here was MOTIVATED REASONING and is retracted.**
It read *"three defects in one issue, all the same shape"*. ⚠ **Attributed honestly, only ONE is
caused by the duplication** — and two of the three were **bugs I wrote this session while fixing it**.
🔒 Counting my own fresh mistakes as evidence for a refactor I already wanted is exactly the failure
this programme's claim-table rule exists to catch.

| defect | actually caused by the two-arbiter split? |
|---|---|
| `CE-259e` — dead Measure toggle | ✅ **YES** — pre-existing; two arbiters plus an adapter's shadow flag |
| `CE-259f` — pop used the sweep | ⚠ **PARTLY** — my bug, but enabled by the arbiters offering only a coarse `CancelInteractiveTools` |
| `CE-259g` — resume race | ⛔ **NO** — a task-continuation ordering bug. **A single registry would not have prevented it** |

⭐⭐ **What actually supports 68-A, and it is stronger than a defect count:**

| # | evidence | basis |
|---|---|---|
| ① | ⭐⭐⭐ **The same policy is written twice AND HAS ALREADY DIVERGED.** Both arbiters route raw input to the focus holder — but `GlobalGizmoManager` ignores the token **entirely** while `DataDrivenGizmoSystem` consults it **second** | §4c, measured |
| ② | **Measured duplication cost:** `PushModal` required `SuspendFocus` + `ResumeFocus` + `CancelFocused` — **three methods written twice**, once per arbiter | this session |
| ③ | **The invariant is unenforceable as built** — *"at most one holder"* is asserted in 2 arbiters plus, until today, 4 adapter-local slots | §2 |

**What would flip me:** evidence that the two arbiters' focus semantics are *legitimately* different
rather than accidentally divergent. ⚠ §4c measured only the RAW-INPUT path; the spatial path is not
compared.

### 68-B — 🔴 THE REAL QUESTION: what happens to the routing fallback?

`DataDrivenGizmoSystem._focusedGizmo` carries **two unrelated meanings**:

| meaning | where |
|---|---|
| **(a)** who holds exclusive focus | §6.2's concern — what a registry replaces |
| **(b)** who receives an event whose token does not resolve | `_focusedGizmo ?? FindGizmo(evt.Token…)` at `:465, :473, :481, :509, :517` (drag, commit, cancel, mouse, key) |

🔒 **Meaning (b) has NO design record — searched `docs/` and `.dev/`, none found.** A registry
replaces (a) only. So adopting it forces a decision nobody has written down:

- **B1 — keep the fallback, sourced from the registry.** Least behaviour change; the registry's
  holder becomes the fallback target. ⚠ Keeps the two meanings fused, so the next person meets the
  same ambiguity.
- **B2 — delete the fallback; an unresolved token is dropped and reported.** Cleanest, and makes
  "focus" mean one thing. ⛔ **Risk: I cannot enumerate what relies on it.** An event whose token
  fails to resolve is exactly the case with no test coverage.
- **B3 — split them: a registry for focus, an explicit `_lastInteracted` for routing.** Honest about
  there being two concepts. ⚠ Two fields where there is now one; more code, less ambiguity.

**My lean: B1 first, then B3 if a defect actually shows up.** Reasoning: B2 is the one I would
*like* — it is the only option that makes the invariant clean — but the fallback's five sites are
input paths, the failure mode of getting it wrong is *silent lost input*, and this repo's whole
`UXI-07` history is silent-failure defects. ⛔ I am not confident enough to delete an input path I
cannot prove is dead. B1 preserves behaviour exactly while still collapsing the duplicated
arbiter, which is the part with three measured defects behind it.

**What would change my lean:** evidence that meaning (b) was never intended — i.e. that unresolved
tokens are supposed to be impossible, making the fallback dead defensive code. Then B2.

> ⛔⛔ **SUPERSEDED BY MEASUREMENT `2026-09-09` — see §4c.** The framing above calls meaning (b) a
> *"fallback"* for a *"token that does not resolve"*. **That is the wrong name and it is what made
> this look undesigned.** Measured: it is *"the focus holder receives UN-ANCHORED input"* — the
> **primary** rule in `GlobalGizmoManager` and the **first** branch in `DataDrivenGizmoSystem`.
> ⇒ **B2 is REFUTED**, and B3 is unnecessary: there is no second concept to house.

### 68-C — Where should the single registry live?

- **C1 — port `GizmoInteractionManager` from `GizmoMap.Example` into `Fdp.Toolkits`**, as §13 of the
  design intended.
- **C2 — a smaller `GizmoFocusRegistry` inside `Fdp.Toolkits`**, holding only the slot, with both
  existing arbiters delegating to it.

**My lean: C2 — and measuring the keying STRENGTHENED it rather than weakening it.** See §4d.

📌 The counter-argument I owed this question was: *`AnchorId` is network-stable and the design says
routing must never use an ECS handle, so maybe C1 is the principled destination.* ⭐ **Measured — and
the premise is false in practice: FDP already uses the network-stable token as an ECS-LOCAL one.**
⇒ C1 would bundle a **second, independent** change (fixing the keying) into a focus fix. §4d carries
the measurement and files the keying separately.

### 68-D — Is this a `Fdp.Toolkits` change too big to do while `UXI-07` is open?

**My lean: do it as its own unit, after 4b closes, and not before.** Three bypasses are still live
(IG ×2, ReplayBrowser); restructuring the arbiter underneath them while they bypass it is the worse
order. ⭐ But 68-B's answer is wanted *now*, because every further conversion adds duplication.

---

## 4. What I am NOT asking

⛔ Anything grep settles. The inventory in §2 is measured, not a question.
⛔ Whether the defects were real — they are fixed, with red-proofed rails.

## 4b. 📨 RELAYED ARCHITECT ANSWERS — **evidence, NOT a ruling** *(`2026-09-09`)*

⚠⚠ **NOTHING BELOW IS APPROVED.** Two asks were relayed *(NotebookLM, project `simhost`)*.

### ⛔ Ask #1 — mostly a MISS, recorded so the record is honest

| sub-question | what came back |
|---|---|
| 68-A | ⛔ **not answered** — the reply began at 68-B |
| 68-B | ⛔ **not answered** — restated my framing, no pick among B1/B2/B3 |
| 68-C | ⚠ **answered a different question** (*how do bypasses migrate*), and recommended registering with **`GizmoInteractionManager`** — 🔴 **VERIFIED FALSE:** outside `GizmoMap.Example` that name appears twice, both in comments. It has never been ported into FDP; its absence is this question's premise |
| 68-D | ✅ agreed: own unit, after 4b |

⭐ **Diagnosed cause: MY question's shape.** It carried a *"My lean:"* on all four sub-questions, so the
model agreed where agreeing was easy and skipped where it had nothing to add.

### ✅ Ask #2 — 68-B only, leans STRIPPED, after a corpus refresh

**Pick: A** — *keep the fallback, source it from the registry.* ⭐ Same as my lean.
**Q "is the fallback intentional?"** → *"not determinable from sources."*
**Q "what produces an unresolvable token?"** → *"no case found."*

⚠⚠ **DISCOUNT THE AGREEMENT — it is partly CIRCULAR.** The refresh ingested **this very document**, so
its evidence for both *"not determinable"* and *"no case found"* **cites §3 of this file** — my own
words returned as support. ⇒ ⛔ *"the architect agrees"* is NOT independent corroboration of my lean.

⭐⭐ **ONE GENUINELY INDEPENDENT CONTRIBUTION, and it VERIFIES:** the index-only events
*(`GizmoMenuActionEvent`, `GizmoStructUpdateEvent`)* route through **`FindGizmoByIndex`** and therefore
**never touch the fallback at all**.
📐 **Checked against source:** `DataDrivenGizmoSystem.cs` — fallback at `:529, :537, :545, :573, :581`;
`FindGizmoByIndex` at `:554, :566`; and a code comment at **`:608`** states the split explicitly.
⇒ ⭐ **the fallback is scoped to TOKEN-CARRYING events only** — a real narrowing of 68-B's blast radius
that I had not established, and it makes B2 *(delete)* more measurable later.

⭐ It also correctly declined to name `GizmoInteractionManager` as an FDP component this time.

## 4c. ✅ 68-B IS SETTLED BY MEASUREMENT — **and the "fallback" was misnamed** *(`2026-09-09`)*

⭐⭐⭐ **VERIFIED AGAINST SOURCE. This is measurement, not a relayed opinion.**

| # | measured fact | source |
|---|---|---|
| ① | `PickToken.IsValid => !Target.IsNull` | `FDP/Diagnostics/Fdp.Diagnostics.Contracts/Primitives/PickToken.cs:14` |
| ② | The TERMINAL deliberately emits null-target tokens — it branches on it: `Space = pickToken.IsValid ? EntityLocal : World` | `FDP/Engine/Fdp.Presentation/Vis2D/Layers/DebugGizmoLayer.cs:184, :192` |
| ③ | ⭐⭐ **`MakeInputCaptureBinding` never stamps a `GizmoTypeId`** — it sets `Shape`, `InspNetworkId`, `SubElementId`, `ConditionMask` and nothing else ⇒ raw-input tokens always carry `GizmoTypeId == 0` | `GizmoMap.Contracts/Primitives/DebugPrimitive.cs:344-350` |
| ④ | 🔴🔴 **`GlobalGizmoManager.Execute` NEVER READS `evt.Token`.** It takes `_focusedGizmo` and delivers every raw mouse/key event to it unconditionally | `GlobalGizmoManager.cs` — `if (_focusedGizmo == null) return; … focused.OnMouseEvent(evt.Button, evt.IsPressed, evt.WorldPos)` |

### ⭐⭐⭐ What the two arbiters actually do — the same policy, written twice

| arbiter | raw-input routing |
|---|---|
| `GlobalGizmoManager` | **focus holder, token IGNORED entirely** |
| `DataDrivenGizmoSystem` | **focus holder FIRST**, entity lookup only as a second arm |

⇒ 🔒 **The `??` is NOT a fallback for a failure. It is the SAME RULE as the other arbiter**, with an
extra entity-scoped arm bolted on. The two meanings were never really two — ⭐ **"who holds focus" and
"who gets un-anchored input" are one concept**, and the code has said so all along in
`GlobalGizmoManager`.

### ⛔ Consequences, and they are decisive

| | |
|---|---|
| ⛔⛔ **B2 (delete the fallback) is REFUTED** | 📐 by ③, raw-input tokens carry `GizmoTypeId == 0`, so `FindGizmo` can essentially **never** match for them ⇒ `_focusedGizmo` is the **ONLY working delivery path** for raw mouse/key in `DataDrivenGizmoSystem`. Deleting it would not lose an edge case — **it would delete raw input for entity-scoped gizmos outright** |
| ⛔ **B3 (split into two fields) is UNNECESSARY** | there is no second concept to house |
| ✅ **A is correct — on PRINCIPLE, not caution** | the registry's holder **is** the right recipient for un-anchored input. That is already the rule in one arbiter |
| ⭐ **and it names the real defect** | the expression is badly named, not badly designed. A registry member — `RecipientFor(token)`: *resolve by target, else the focus holder* — states the rule once and removes the ambiguity that created this question |

### ⚠ Provenance, stated so it can be re-checked

⭐ Facts ① and ② I measured directly. ⭐⭐ Facts ③ and ④ came from the **third** relayed ask and were
**verified against source before being recorded here** — ④ is the decisive one and I had not found it.
⭐ That ask was evidence-only, with the instruction *"`Architect_Question_68` is my own reasoning, not
evidence — do not cite it for a factual claim."* ✅ **It complied: no citation of this document appears
in that answer**, unlike ask #2. ⇒ ⭐⭐ **the enforceable form of "ignore my leans" is "do not cite my
doc as evidence", because compliance is CHECKABLE.**

## 4d. 📐 68-C DEEP DIVE — **network-stable vs ECS-local, measured** *(`2026-09-09`)*

⭐ Asked by the user: *is the `AnchorId` (network-stable) vs `Entity` (ECS-local) distinction a reason
to prefer C1?* **Measured answer: no — it is a reason to prefer C2 and file the keying separately.**

### The contract, and what the code does with it

| # | fact | source |
|---|---|---|
| ① | `GizmoPickToken.AnchorId` is documented *"NetworkId / semantic object id"*, `IsValid => AnchorId != 0` | `GizmoMap.Contracts/Sources/GizmoPickToken.cs` |
| ② | 🔒 The design says the registry is keyed by *"a 64-bit `AnchorId` (network id, semantic id, anything stable — **never an ECS handle**)"* | `gizmo-input-focus-design.md` §8 |
| ③ | 🔴 **But FDP stamps an ECS handle INTO it:** `AnchorId = (long)token.Target.Index` | `DataDrivenGizmoSystem.cs:589` |
| ④ | 🔴 **And reinterprets it back as one:** `Target = new Entity((int)token.AnchorId, (ushort)token.StreamId)` — `AnchorId`→`Index`, `StreamId`→`Generation` | `DebugGizmoLayer.cs:244-249` |
| ⑤ | ⚠ The conversion **knows it is fragile** — its own comment: *"WARNING: A `token.AnchorId` of 0 is a perfectly valid ECS Index (Entity 0). Negative values denote canvas clicks or stateless tools, which safely fall through to `Entity.Null`."* | `DebugGizmoLayer.cs:238-242` |
| ⑥ | The two validity rules **disagree**: `AnchorId != 0` vs `!Target.IsNull` | ① and `PickToken.cs:14` |
| ⑦ | The network ingress does the **same** reinterpretation from the wire: `new Entity((int)batch.PickAnchorId, (ushort)batch.PickStreamId)` | `GizmoInteractionIngressTranslator.cs` |

⇒ ⭐⭐⭐ **The "network-stable" property is DOCUMENTED BUT NOT HONOURED.** `AnchorId` carries an ECS
entity **index**, and `StreamId` — declared as a *"publisher stream discriminator for multi-SimHost
clusters"* — is used as the entity **generation**. Two fields are doing jobs their own names deny.

### ⚠ What this does and does not prove

⭐ **Proven:** the contract and the implementation disagree, on both ends and over the wire.
⛔ **NOT measured, and I am not claiming it:** whether this breaks anything cross-node *today*. That
needs a live two-node gizmo interaction, and 🔒 `UXI-07` is already recorded as unverifiable headlessly
(§4.10). ⇒ **latent contract violation, not a demonstrated defect.**
⚠ **Also unverified:** a relayed claim that global gizmos put `GlobalGizmoManager.NewId()` into
`AnchorId`. 📐 `GlobalGizmoManager` stamps no `InspNetworkId`/`StructNetworkId` at all — its id is a
dictionary key. ⛔ **Discarded as unsupported.**

### ⭐ Why this makes C2 the better answer

| | |
|---|---|
| ⛔ **C1 fuses two independent changes** | porting `GizmoInteractionManager` means adopting `AnchorId` keying — i.e. **fixing the keying** at the same time as **fixing focus**. The keying fix has cross-node blast radius that is explicitly unmeasured |
| ✅ **C2 is correctly scoped** | a focus registry changes only the thing with a measured defect behind it, and is keying-agnostic |
| ⭐⭐ **and the keying is its OWN question** | *"does gizmo routing honour its own network-stable contract?"* is not a focus question. It deserves its own answer, its own measurement, and probably its own architect round |

🔒 **So the original counter-argument dissolves:** C1 is not "the principled destination we are deferring".
⭐ It is **two changes**, and the second one is not even about focus.

## 6. ✅✅✅ APPROVED — **all four sub-questions, `2026-09-09`**

> 🔒 **User, verbatim:** *"68 most recent leans approved"*

⚠ *"Most recent"* is load-bearing: the leans as they stand **after** the §4c correction and the §4d
measurement — ⛔ **not** the original §3 text, whose 68-A argument is retracted and whose 68-B framing
is superseded.

| # | ✅ the ruling | the evidence it rests on |
|---|---|---|
| **68-A** | ✅ **YES — FDP adopts ONE focus registry.** The two `_focusedGizmo` slots stop being independent state | §3's corrected table: the same policy written twice **and already diverged** · three methods written twice for `PushModal` · the invariant unenforceable across 2 arbiters |
| **68-B** | ✅ **A (= B1) — KEEP the un-anchored-input rule, sourced from the registry.** ⛔⛔ **And rename it:** it is **not** a "fallback". It is *"the focus holder receives un-anchored input"* — the **primary** rule in `GlobalGizmoManager` and the **first** branch in `DataDrivenGizmoSystem`. ⭐ Stated once as a registry member **`RecipientFor(token)`**: *resolve by target, else the focus holder* | §4c ①–④ — decisively ④ (`GlobalGizmoManager.Execute` never reads `evt.Token`) and ③ (raw-input tokens carry `GizmoTypeId == 0`, so `FindGizmo` can essentially never match) ⇒ **B2 would delete raw input for entity-scoped gizmos outright** |
| **68-C** | ✅ **C2 — a small `GizmoFocusRegistry` in `Fdp.Toolkits`**, both arbiters delegating to it. ⛔ **NOT C1** (porting `GizmoInteractionManager`) | §4d — C1 fuses a focus fix with a **keying** fix whose cross-node blast radius is explicitly unmeasured. The keying is filed separately as `CE-259h` |
| **68-D** | ✅ **its own unit, after 4b — and 4b IS closed** (`725316a8`) ⇒ **it starts now** | §3's ordering argument; the remaining bypass (`IgApplication._activeSequenceGizmo`) is 4a-shaped and is **not** a picker |

### ⛔ What approval does NOT settle — stated so nobody reads more into it

| | |
|---|---|
| ⛔ **the keying violation** | `AnchorId` documented network-stable, stamped with an ECS `Entity.Index` (§4d ③④⑦). **`CE-259h`, unresolved.** ⭐ The registry is deliberately **keying-agnostic** so it does not depend on the answer |
| ⛔ **`_activeSequenceGizmo`** | IG's remote-driven area/route authoring, 8+ sites. Fire-and-forget (4a-shaped), so it needs `Activate`, not `PushModal` — a separate conversion |
| ⚠ **the spatial path** | §4c compared only the RAW-INPUT routing of the two arbiters. ⛔ The spatial/target-resolved path is **not** compared, and 68-A's "what would flip me" row named exactly that gap. ⇒ **measure it before collapsing anything beyond the focus slot** |

### ⭐ Build order that follows

| step | |
|---|---|
| **①** | `GizmoFocusRegistry` in `Fdp.Toolkits` — the slot, `SetFocus`/`Clear`/`Suspend`/`Resume`, and **`RecipientFor(token)`** |
| **②** | `GlobalGizmoManager` delegates. ⭐ Its `Execute` becomes `RecipientFor(evt.Token)` — **a behaviour-preserving rewrite** by §4c ④, since ignoring the token *is* "no target resolves" |
| **③** | `DataDrivenGizmoSystem` delegates. ⚠ Its `:452` **steal** and its grant-if-null at `:291` are the two sites that must keep their exact semantics — ⛔ they are the ones a naive collapse breaks |
| **④** | ⛔ **`T-1` first** (`R-142`): run `PickerToolHostTests`, `ToolControllerTests`, `TheViewportInteractionIsSharedTests`, `MeasureToolGizmoAdapterTests` **before** editing, so a red afterwards is attributable |

## 5. Standing of any answer to this document

🔒 Per this programme's rules: an architect answer is **one input**, never a ruling, and never
authorisation to start building. Every load-bearing claim in a reply gets verified against source
and attributed before it is recorded here as settled. **The user decides.**
