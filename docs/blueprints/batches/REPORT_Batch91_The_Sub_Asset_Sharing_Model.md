<!--STATUS
state: LIVE
updated: 2026-08-19
current-answer: this whole file - the Batch 91 report.
stale-below: nothing.
known-rot: none.
known-conflict: none.
-->
# REPORT — Batch 91: **the sub-asset sharing model** *(two items landed, three stopped)*

> 📌 **Dispatched at `3868c29e5`** · **started at `ec51e56`** *(rule 1b marker)*.
> ⭐ **Ids allocated** *(rule 3/5)*: **`BP-339`** *(done)* · **`BP-340`** *(open)* · **`BP-341`** *(open)*.
> **`BP-337` amended.**
> ⛔⛔ **THREE ITEMS STOPPED, each on a MEASUREMENT, none on a guess — §3, §4, §5.**

---

## 1. ⭐ What landed, and what did not

| item | verdict |
|---|---|
| ⭐⭐⭐ **`91b`** — aliases must PERSIST *(`M-20`)* | ✅ **BUILT**, both hosts, real-JSON rails, golden unmoved |
| ⭐⭐ **`91d`** — `BP-337`'s crashing suite | ⚠ **HALF** — the question answered decisively, the fixture fixed, **34 → 83 passing**; ⛔ a **different, native** cause remains |
| ⛔⛔ **`91a`** — wire the orchestrator emitters | 🛑 **STOPPED** — `R-99` settled *that*, the design does not settle *where*, and the natural seam is documented dead |
| ⛔ **`91c`** — pass `subAssetResolver` | 🛑 **HELD with `91a`**, on the handoff's own reasoning |
| ⛔ **`91e`** — a readable auto-name | 🛑 **BUILT, THEN REVERTED** — it breaks `Promote`'s idempotence, which comes *from* the GUID |

⭐ **Per §9's seams:** `91b` is *"complete on its own"*. ⛔ `91a`+`91c` are the pair that must land together, and `91a` cannot land without a ruling.

---

## 2. 🛠 `91b` — the aliases persist *(`BP-339`)*

📄 **Design basis:** `…Persistence_Detailed_Design.md:132` · §7 · `R-83`.

### 2.1 ⭐ The defect, re-measured and conclusive

The only writers to `_aliases` were **rename**, **`AddAlias`** *(the drag-drop)* and **prune**.
⛔ **Nothing on the load path touched them**, and the persistence assembly had **no alias field at
all**. ⇒ ⭐⭐ **every alias a designer authored died with the editor session** — with the badge, the
type-match decision and the cross-region refusal that guarded it.

⚠⚠ The DTO header even filed `_aliases` under *"runtime hydration"* — ⭐ **a hydration that was never
written.** 📄 And `:132` names three things in one breath; **two were built.** ⇒ an **omission**.

### 2.2 ⭐⭐ What was built — one shape, not a third

| ⭐ | |
|---|---|
| **`BlackboardAliasBindingDto`** | in the **ROOT** persistence namespace ⇒ ⭐ **ONE type, both hosts** *(ruling 9)*, shaped like `SubtreeSyncBindingDto` |
| **`GetAllAliases` / `LoadAliases`** | mirroring `GetAllSyncBindings` / `LoadSyncBindings` on both assets — ⛔ deliberately not a new idiom |
| **both mappers** | save + load beside the sync bindings and suppressions they sit next to in the design |
| ⭐⭐ **`DtoType`** | round-trips as **`Type.FullName`** through each mapper's **EXISTING `ResolveClrType`** — ⛔ **not a second resolver**; it already probes loaded assemblies because DTO structs live in **behavior** assemblies. ⚠ Unresolvable ⇒ `typeof(object)`, never a throw: a behavior assembly can legitimately be absent and the alias is still real |

### 2.3 ⭐⭐⭐ The golden could not move — **and that is a RAIL, not a hope**

⭐ The field is **nullable + `JsonIgnore(WhenWritingNull)`**, following the precedent its own neighbour
states: *"a new ALWAYS-EMITTED list changes the bytes of every asset"* *(`ConcurrentWritesAllowed`)*.
⭐⭐ **No shipped asset can contain an alias** — they never persisted — ⇒ the key is absent everywhere.

⭐ **Asserted three ways**: no alias ⇒ **no key**; an alias ⇒ **the key appears**; an **emptied** list
⇒ **no key again**. ✅ And `MigrationEquivalenceTests` — which round-trips stored JSON verbatim — is
**green, 270/270**.

### 2.4 ⭐⭐⭐ The rails go through REAL JSON

⛔ **An in-process `AddAlias`-then-read is exactly the shape that let this defect live**:
`BlackboardAliasingTests` does precisely that and has been green throughout. ⇒ every rail runs
**model → DTO → JSON text → DTO → a FRESH model**.

⭐ Including **prune AFTER a reload** — the case that only exists now that an alias can arrive from
**disk** rather than from the same session.

---

## 3. 🛑 `91a` — STOPPED, and the measurement that stopped it *(`BP-340`)*

> ⭐ The handoff: *"call the emitter from the asset **save/emit path** so the sidecar is written."*

📐 **The seam exists — and is documented dead.** `AiAssetEmitService` IS constructed in production at
**`EditorSubsystem:3136`**, immediately followed by:

```csharp
// PU-D11 (PU-402): emitService is no longer used by the RegenerationScheduler
// flushAction (which now writes JSON via saveBTreeDelegate/saveHsmDelegate instead
// of C#). … AiAssetEmitService is NOT removed per spec.
_ = emitService; // suppress unused-variable lint
```

📐 **And the REAL save path emits no C# at all** — `saveBTreeDelegate:2480` / `saveHsmDelegate:2491`
serialise a DTO to **JSON** and write it.

⇒ ⭐⭐ **Two candidate hosts, neither obviously right:**

| candidate | ⛔ why not, without a ruling |
|---|---|
| `AiAssetEmitService` | **nothing invokes it** ⇒ hooking it writes nothing |
| the JSON save delegates | makes a path **PU-D11 deliberately moved OFF C# emission** start writing a `.g.cs` sidecar again |

⚠ **The gap is real, not phantom** — `CompanionFileDiscovery:194`/`:208` do hunt
`*.Orchestrators.g.cs`. ⭐ `R-99` settled **that** they should be wired; ⛔ **it did not settle
where**, and the obvious seam carries a spec decision against it. 📌 §9: *"do not invent."*

⚠⚠ **`91c` was HELD with it**, on the handoff's own words: *"`91c` alone opens a panel that authors
dead data."* The `PARAMETER SYNCHRONIZATION` bindings persist; nothing executes them.

⭐ **A correction I owe:** my first sweep read *"zero production constructors"* for
`AiAssetEmitService` — a **false negative** from a grep that missed the fully-qualified call. I
re-checked before concluding. 📌 Exactly the `R-74` shape, caught this time.

---

## 4. 🛠 `91d` — the question answered, half the crash fixed *(`BP-337` amended)*

> ⭐⭐⭐ **The handoff's question: is a null `ctx.Resources` LEGITIMATE, or is the fixture unrealistic?**

⭐ **Answered by three measurements, and it is not close:**

| # | evidence |
|---|---|
| ① | `RenderContext` is built in production at **exactly ONE** site — `MapCanvas.Draw():119` — which sets `Resources = this` **unconditionally** |
| ② | **Three** production readers dereference `ctx.Resources` with **no null check** |
| ③ | The field is `IResourceProvider Resources` — **not nullable** — **one line above** `IDebugDrawBuilder? DrawBuilder`, which IS nullable and is documented *"May be null in headless test contexts"* |

⇒ ⭐⭐ **The author distinguished the two deliberately. The FIXTURE was wrong.** ⛔ A guard would have
contradicted the type's own annotation and diverged from two other readers.

🛠 `HeadlessResourceProvider` at the five test construction sites. ⭐ Returning `null` from `Get<T>()`
**is** realistic — the renderer already handles a null MapCamera. ⛔ **The absent RESOURCE was never
the problem; the absent PROVIDER was.**

📐 **Result: 34 → 83 passing, every `NullReferenceException` gone.**

### ⛔⛔ Still aborting — **and the NRE was MASKING it**

With `Resources` non-null, `DebugGizmoLayer.Draw` proceeds past `:102` into
`_innerTerminal.ExtractMetaPrimitives` and the **real** `GizmoMap.Presentation.DebugPrimitiveRenderer2D`
— which is constructed unconditionally at `:34`/`:52`/`:73` **even when a capturing renderer is
injected**. ⇒ a **NATIVE** crash, no managed exception, contained to `Fdp.Toolkit.Vis2D.Tests`.
⚠ Blame-confirmed: `DebugGizmoLayerHitTests.SC_GZ026_2_BeyondEndpoint_IsMiss`, `Completed="False"`.

⛔ **Not guarded around** — 📌 the handoff's rule: *"a guard that hides a real contract violation is
worse than a red suite you can see."* Making GizmoMap's renderer headless-safe is another subsystem.

⭐ **The WindowManager filter is unaffected — 146 passing, unchanged** — so Batch 89's rails still
gate. ⇒ ⭐⭐ **the remaining question: can `GizmoMap.Presentation`'s renderer run headless, or must
these tests inject one that never reaches it?**

---

## 5. 🛑 `91e` — BUILT, then REVERTED by an existing rail *(`BP-341`)*

⭐ Implemented: a shared `AutoVariableNaming.SeedName`, wired into both picker drawers, 9 unit rails
green. ⛔ **Then `PromoteBindTests.Promote_SecondCallSameId_IsIdempotent_BindingUnchanged_BTree` went
red** — an **existing** rail pinning *"same visualId must always produce the same auto-name."*

⭐⭐ **The insight: that idempotence comes FROM the GUID.** The name *is* the node's id, so a second
`Promote` finds the same name and the duplicate guard returns it. With a readable stem + a uniquifier,
the second call sees `FloatAction_params` taken and mints `FloatAction_params2` ⇒ **a second variable
for one node.**

⚠⚠ **Each obvious repair needs a decision I do not have:**

| repair | ⛔ why it is a guess |
|---|---|
| reuse an existing auto-managed var of the same action+type | a **guess about ownership** — it would steal a sibling node's variable |
| read the node's `ExpressionTargetField` | `Promote` runs **before** `ApplyFacet` *(as the pinning test itself does)* |
| a stable per-node discriminator *(`MoveTo_a1b2c3d4_params`)* | keeps both properties but is **a naming scheme the design has not chosen** — `R-86`'s example is `MoveTo_Advance_params` |

⇒ ⛔ **Reverted clean** — helper, both call sites and the new rail removed; **zero residue in the
tree**. ⭐ The question for the owning batch: *how does `Promote` identify **this node's** variable once
the name is no longer the node's id?* ⚠ That is `B2` *(Guid declaration identity)* one level up.

⭐ **Worth noting the probe earned its keep**: `P3` showed my unit rails stayed green when the call
site was reverted, so I added a **call-site** rail — and that rail is what made the idempotence
conflict visible at all.

---

## 6. ⭐ GATES — the rule-8 contract, plus the four this batch owns

### ⭐ 1 + 2 — per gate, with the `--no-build` column

| # | gate | `--no-build`? | result | Δ vs baseline |
|---|---|---|---|---|
| 1 | **AiShared** | ⛔ built | **1479 / 0 / 0** | **0** |
| 2 | **Blueprints** | ⛔ built | **3773 / 0 / 10 skip** | **0** |
| 3 | **BTree.Editor** | ⛔ built | **622 / 0 / 0** | **+7** *(615 → 622)* |
| 4 | **Hsm.Editor** | ⛔ built | **554 / 0 / 0** | **+3** *(551 → 554)* |
| 5 | **Hrot.Editor** | ⛔ built | **201 / 0 / 0** | **0** |
| 6 | **Breakpoints** | ⛔ built | **143 / 0 / 0** | **0** |
| 7 | ⚠ **NodeEditor.Core** | ⛔⛔ **BUILT — never `--no-build`** | **211 / 0 / 0** | **0** |
| 8 | ⚠ **NodeEditor.UI** | ⛔⛔ **BUILT** | **135 / 0 / 0** | **0** |
| 9 | ⚠ **Fhsm.Tests** | ⛔⛔ **BUILT** | **300 / 0 / 0** | **0** |
| 10 | ⚠⚠ **Fdp.Presentation** *(FILTERED — `BP-337`)* | ⛔ built | **146 / 0 / 0** | **0** |
| ⭐ **11** | ⭐⭐ **AiEditor.Generators** — ⚠ **added because it holds `MigrationEquivalenceTests`, `91b`'s GOLDEN gate** | ⛔ built | **270 / 0 / 0** | **0** |

⚠ **`Fdp.Toolkits.Tests` not run** — 📌 `DEBT-AIB-030`.

### ⭐⭐⭐ 8 — GOLDEN: **three reasons it must not move, and it did not**

| reason | outcome |
|---|---|
| ⭐ **`91a`**: no orchestrator for a binding-less subtree | ⛔ **moot — `91a` did not land** |
| ⭐ **`91b`**: no shipped asset can contain an alias | ✅ **nullable + `WhenWritingNull`**, railed three ways, `MigrationEquivalenceTests` green |
| ⭐ **`91e`**: existing `_auto_` names untouched | ⛔ **moot — `91e` was reverted** |

⭐⭐⭐ **ZERO golden movement.** ⛔ No asset, no `.g.cs`, no emit golden is in the diff.

### 🔴 7b — every gate script UNFILTERED, with `EXIT=$?`

```
$ python3 scripts/tracker-counts.py --check
tracker counts OK — open 68 / done 208 (+1 refuted)          EXIT=0
$ python3 scripts/rulings-check.py
66/66 rulings verified against their sources                 EXIT=0
$ python3 scripts/design-digest.py --check
All 49 recently-changed design documents carry a STATUS header, …   EXIT=0
```

⛔⛔ **And the tracker gate was RED on its first run, as in Batch 90 — pasted verbatim:**

```
TRACKER COUNTS DISAGREE WITH THE ROWS:
  RW-L: table says open=31 done=66, rows say open=32 done=66
  RW-M: table says open=28 done=61, rows say open=29 done=62
  Total: table says open=66 done=207, rows say open=68 done=208
```

⭐ The summary table is a **derived** artefact that no row edit updates; ⇒ ⭐⭐ **it will be red on the
first run of every batch that adds a row.** ⛔ **That is not a reason to filter the output** — it is
the reason the script prints the corrected table, and the reason `tail` was so dangerous.

### ⭐⭐ 9 — THE ENUMERATION: the DTO list at `…Persistence_Detailed_Design.md:132`

```
grep -rn "_aliases" --include=*.cs (production only)                → 28 hits, 2 assets, 0 on the load path
grep -rn "SubtreeSyncBinding" --include=*.cs (production only)      → 20 hits, save + load present
```

| # | the line names | built before? | now |
|---|---|---|---|
| 1 | **subtree sync bindings** | ✅ `SubtreeSyncBindingDto`, save + load | unchanged |
| 2 | ⛔ **alias relationships** | 🔴 **NO — no DTO field, no load call** | ✅ **`91b`** |
| 3 | **conflict/unused suppressions** | ✅ `SuppressionsDto` *(+ `ConcurrentWritesAllowed`)* | unchanged |

⇒ ⭐⭐ **Nothing else in that line is missing.** ⚠ **Asymmetry worth naming:** `SubtreeSyncBindings`
persists on **BTree only** — `HsmAssetMapper` mentions it **zero** times. ⛔ Not a defect this batch can
call: HSM may have no subtree-sync concept. ⭐ **Aliases were added to BOTH**, because both assets carry
`_aliases` and both have `AddAlias`.

### ⭐⭐ 10 — WHAT EACH RAIL ASKS

| rail family | ⭐ asks | ⛔ does not |
|---|---|---|
| **`AliasesSurviveAReloadTests`** *(7, BTree)* | ⭐⭐ a **FRESH model reloaded from JSON TEXT** — the alias, its name, its path, its **`DtoType`**; prune **after** reload; the **key's presence/absence** in the serialised text | ⛔ **never `AddAlias`-then-read in-process** — 📌 the shape that let the defect live |
| **`AliasesSurviveAReloadHsmTests`** *(3, HSM)* | ⭐ the same properties on the other host | ⛔ not a copy of the diagnosis — it cites the BTree file |
| **`HeadlessResourceProvider`** *(`91d`)* | ⭐ nothing asserts it; it makes 18 existing tests reach their real assertions | ⛔ it is a fixture, and the doc says so |

### ⭐⭐ 11 — REVERT-GOES-RED, four probes, **never delegated**

| probe | un-applied | reds |
|---|---|---|
| ⭐ **P1** *(`91b` load)* | the mapper stops calling `LoadAliases` | **4 / 7** — ⭐ the three serialisation-shape rails correctly stay green |
| ⭐ **P2** *(`91b` save)* | the mapper stops filling `dto.Aliases` | **5 / 7** |
| ⭐ **P3** *(`91e` call site)* | `Promote` back to the GUID name | **0 at first** ⇒ ⭐⭐ **the finding: my unit rails could not see the call site.** Added a call-site rail; it then reddened **1** |
| ⭐ **P4** *(`91d` fixture)* | one `MakeCtx` back to no `Resources` | **`NullReferenceException` ×5**, immediately |

⛔ **Every probe un-applied with the INVERSE EDIT** — never `git checkout --`.

---

## 7. ⭐⭐⭐ WHAT THIS UNLOCKS — and the visual-check answer

> ⭐ The handoff asked whether a visual-check row should be added, and said **it** will write them.

| | |
|---|---|
| ⭐⭐ **`91b` — YES, one row is worth adding** | **"Author an alias by drag-drop, save, close the asset, reopen it — the alias and its badge are still there."** ⭐ It is the *only* way a designer sees this fix, and the failure it replaces was silent |
| ⛔ **`91a`/`91c` — NO row yet** | the sub-asset sharing model is **still not end-to-end**: Approach A now remembers ✅, Approach B still **does not execute** ⛔ |
| ⛔ **`91d` — NO row** | a test-suite gate, invisible to a designer |

⚠ **The handoff's §10 framing needs one correction:** *"the sub-asset sharing model works end to end
for the first time"* is **half true**. ⭐ **An alias survives a reload.** ⛔ **A field-sync binding
still does not copy** — that was `91a`, and `91a` is stopped.

---

## 8. ⭐ Carried

| | |
|---|---|
| ⛔⛔ **`BP-340`** | `91a`+`91c` — needs a **ruling on which path owns the `.g.cs` sidecar** |
| ⛔ **`BP-341`** | `91e` — needs `Promote`'s node-identity question answered; ⭐ belongs with `B2` |
| ⚠ **`BP-337`** | half fixed; the remainder is a **native** headless-rendering question in `GizmoMap.Presentation` |
| ⭐ **unchanged** | `BP-325` · row 60 / `U-16` · row 61 · `Q38`–`Q44` *(`R-27`)* · watch pinning · the `⋮` button |
| ⭐ **`DEBT-AIB` partitions touched** | ⚠ **none** |
