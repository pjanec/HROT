<!--STATUS
state: LIVE
updated: 2026-08-19
current-answer: this whole file - the Batch 91 dispatch.
stale-below: nothing.
known-rot: none.
known-conflict: none.
-->
# HANDOFF — Batch 91: **the sub-asset sharing model, finished** *(+ two riders)*

> 📌 **Dispatched at `3868c29e5`.** ⭐ **Branch from THIS commit** *(rule 7)* — the handoff itself.
> ⛔⛔ **YOUR SCOPE IS FROZEN AT THIS SHA.** ⭐ Documents changing after it are **FYI ONLY**.
> ⚠ **If a later document INVALIDATES an item — STOP AND REPORT.** ⛔ **Do NOT adapt, do NOT revert.**
> ⭐ **Rule 3: allocate your own ids and state them.** ⭐ **Rule 1b: push
> `chore: started batch 91 at 3868c29e5` FIRST, before any code.**
>
> ⭐⭐ **User, `2026-08-19`:** *"longer batch is better than few short ones"* ⇒ ⭐ **five items.**
> ⛔ **The visual check has NOT run** — 📌 **`R-27` still gates every `Q38`/`Q44` item.** ⭐ **Nothing
> here touches the watch or breakpoint families.**

---

## 1. ⭐⭐⭐ THE SPINE — **one design, both halves half-built, in OPPOSITE ways**

📄 **Design basis:** `Blackboard_Authoring_Detailed_Design.md` **§7** *(Approach A — whole-DTO
aliasing)* · **§8** *(Approach B — field-level sync)* · `R-83` · `R-99` *(user ruling: WIRE)* ·
`BTree_HSM_JSON_Persistence_Detailed_Design.md:132` *(what persists)*.

| | Approach **A** *(alias)* | Approach **B** *(field sync)* |
|---|---|---|
| authoring UI | ✅ drag-drop, type match, cross-region refusal, badge | ✅ the Inspector's `PARAMETER SYNCHRONIZATION` table |
| **persisted?** | ⛔⛔ **NO — `M-20`, and it is a DEFECT** | ✅ **yes**, JSON + layout round-trip |
| **executes at runtime?** | ✅ free *(the alias IS the shared address)* | ⛔⛔ **NO — the emitters have zero callers** |

⇒ ⭐⭐⭐ **A authors and forgets; B remembers and does nothing.** ⭐ **This batch closes both.**

---

## 2. 🛠 **`91a` — WIRE the orchestrator emitters** *(`R-99`, the user's ruling)*

📄 **Design basis:** ⭐ **`R-99`** — *"WIRE the orchestrator emitters; the `D3` disposition is settled"*
*(user, `2026-08-19`)* · `PLAN_Remaining_Work.md` **task group `D`, row `D-a`** ·
`Blackboard_Authoring_Detailed_Design.md` §8.3.

📐 **Measured:** `BTreeOrchestratorEmitter` *(193 lines)* and `HsmOrchestratorEmitter` *(137)* have
**zero non-test callers**; `WriteOrchestratorFile` has **none at all**. ⚠ **Yet
`CompanionFileDiscovery:194`/`:208` already hunt `*.Orchestrators.g.cs`** *(coordinator-verified `2026-08-19`)* ⇒ ⛔ **the consumer exists and the
producer is never called.**

| ⭐ | |
|---|---|
| **do** | call the emitter from the asset **save/emit path** so the sidecar is written; ⭐ the discovery site already consumes it |
| ⭐⭐ **rail — ask the ARTEFACT** | assert the **emitted TEXT** for a subtree with one `☑↓` and one `☑↑`, in **§8.3's exact order — copy · tick · copy**. ⛔ **Not that the emitter returns a string** |
| ⭐⭐ **second rail** | a subtree with **ZERO active bindings emits NO orchestrator** *(§8.3)* ⇒ ⭐ **golden must not move for any asset with no sync bindings** |
| ⚠ **`R-49` applies** | ⛔ **never generate per-VARIABLE code.** A per-**binding** copy statement is fine — a binding is not a variable — ⭐ **but do not "fix" anything here by emitting an accessor per variable** |

---

## 3. 🛠 **`91b` — Approach A aliases must PERSIST** 🔴🔴 *(`M-20`, a defect I measured)*

📐 **Measured `2026-08-19`, conclusive.** The only writes to `_aliases` are **rename** *(`:409`)*,
**`AddAlias`** *(`:504`, the drag-drop)* and **prune** *(`:536`)*. ⛔ **Nothing on the LOAD path touches
them**, and the persistence assembly has **no alias field at all** — only an unrelated
*"alias shorthand"* comment. ⇒ ⭐⭐ **every alias a designer authors is gone when the asset reopens**,
together with the badge, the type-match decision and the cross-region refusal that guarded it.

### ⭐⭐⭐ The design already says it persists — **this is an OMISSION, not a decision**

📄 **`BTree_HSM_JSON_Persistence_Detailed_Design.md:132`**, verbatim:
> *"**subtree sync bindings**, **alias relationships**, **conflict/unused suppressions** (today
> smuggled in the `[*Layout]` method — promoted to first-class JSON)"*

⚠⚠ **Three things in one list. TWO were built** — `SubtreeSyncBindingDto` and the suppression sets, and
the DTO's own header even cites §5.2 for them. ⛔ **Aliases are the one that was skipped**, and the DTO
header lists `_aliases` under *"Runtime-only fields EXCLUDED … runtime hydration"* ⇒ ⭐ **a hydration
that was never written.**

| ⭐ | |
|---|---|
| **do** | persist alias bindings **in the same shape as `SubtreeSyncBindings`** — a DTO field + the layout round-trip. ⛔ **Do not invent a third shape** *(ruling 9)* |
| ⭐⭐ **what a binding IS** | `BlackboardAliasBinding(RequiringAssetId, RequiringElementId, RequiringAssetName, RequiredByPath, DtoType)` keyed by **variable name** — ⭐ **already a type, do not redesign it** |
| ⭐ **both hosts** | `BehaviorTreeAsset` **and** `HsmAsset` — 📐 both carry `_aliases`, both have `AddAlias`/`PruneStaleAliasBindings` |
| ⭐⭐⭐ **GOLDEN CANNOT MOVE, and say why** | ⛔ **no shipped asset can contain an alias** — they never persisted ⇒ **the field is absent everywhere and every existing asset emits byte-identically.** ⚠ **If a golden DOES move, STOP** — it means something else changed |
| ⭐⭐ **the rail** | ⭐ **round-trip through the REAL save/load**: author an alias → save → **load into a fresh asset instance** → `GetAliasesFor` returns it. ⛔ **Not `AddAlias` then read back in-process** — 📌 **that is exactly the shape that let this survive**, and the existing `BlackboardAliasingTests` do it |
| ⚠ **`PruneStaleAliasBindings`** | ⭐ it exists and is called from `BlackboardAuthoringWindow:404` ⇒ **a persisted alias to a deleted sub-asset must still prune.** ⭐ **Rail it after a reload**, not only in-process |

---

## 4. 🛠 **`91c` — pass `subAssetResolver`** *(task group `D`, row `D-b`)*

📐 **Measured:** `InspectorWindow._subAssetResolver` is **`readonly`, constructor-only, no setter**, and
`PerspectiveWorkspaceRegistrar:241` — the **only** production construction — does not pass it ⇒ the
`PARAMETER SYNCHRONIZATION` section renders its header and then
**`"Sub-asset resolver not configured."`**, on every host.

⭐ **This is the silent-default pattern, 13th instance** — 📌 the checkable rule is *"a production caller
that HAS a dependency must PASS it"*, and ⭐⭐ **the control is a forwarding rail PER DEPENDENCY,
asserted on the CONSTRUCTED object** *(`R-67`)* — ⛔ never on the registrar's source.

⚠ **`91a` + `91c` together are what make Approach B real end to end** — ⛔ neither alone is.

---

## 5. 🛠 **`91d` — `BP-337`: the suite that crashes its host** ⭐ *(a rider, and I sized it)*

📐 **Coordinator-measured `2026-08-19`, reproduced:** **18 tests, ONE cause.**

```
Fdp.Toolkit.Vis2D.Gizmos.DebugPrimitiveRenderer2D.Render → NullReferenceException
  FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/DebugPrimitiveRenderer2D.cs:28
      var mapCamera = ctx.Resources.Get<MapCamera>();      // ⛔ ctx.Resources is null in the fixture
```

⇒ ⭐⭐ **The whole `Fdp.Presentation.Tests` suite aborts**, so its totals depend on ordering and
⛔ **neither a red nor a green is evidence** — ⚠ **and Batch 89's `FrameOverlayTests` live in it**,
observable only under a filter.

> ⭐⭐⭐ **THE QUESTION, and it is a real one — do not guess:**
> **is a null `ctx.Resources` LEGITIMATE, or is the fixture unrealistic?**
> ⭐ **If production always supplies it** ⇒ **fix the FIXTURE**; a guard would paper over a contract.
> ⭐ **If a headless/2-D-only context legitimately has none** ⇒ **guard, and say so in the code.**
> ⛔ **Measure which, state the answer, then fix the right side.**

⭐ **Success condition: the UNFILTERED suite runs to completion** ⇒ ⭐⭐ **it can enter the baseline
table as a real gate**, and Batch 89's rails stop needing a filter. ⚠ **Report its full count** — ⛔ it
has never been in a baseline, so there is no delta to compare; **establish one.**

---

## 6. 🛠 **`91e` — a readable auto-name** *(task group `B`, row `B5`)*

📄 **Design basis:** `R-86` — *"RENAMING IS MANDATORY; the auto-name is not acceptable"* *(user)* ·
plan group `B`, `B5`.

⛔ `_auto_{VisualId:N}` and `bpParams_2` are both opaque. ⭐ **Seed a READABLE name — sanitized and
uniquified from the OWNING NODE** *(`MoveTo_Advance_params`)*. ⭐ **`SanitizeIdentifier` already
exists.**

| ⚠ | |
|---|---|
| ⭐⭐ **why it is worth doing before `B2`** | **it means the common case never needs renaming at all** — ⛔ and renaming is still broken *(`M-15`: `RenameVariable` rewrites no bindings)* |
| ⛔ **NEW variables only** | ⚠ **do NOT rename existing ones** — 📌 `M-15` again: a rename dangles its binding. ⭐ **Seeding at CREATION is safe; migrating is not** |
| ⭐ **collision** | uniquify — ⛔ two nodes of the same action must not collide |
| ⭐⭐ **golden** | ⚠ **existing assets keep their `_auto_` names** ⇒ ⛔ **no golden may move.** If one does, you migrated something |

---

## 7. ⛔ SCOPE FENCE

| ⛔ not this batch | |
|---|---|
| **anything in `Q38`–`Q44`** — the **watch** and **breakpoint** families | ⛔⛔ **`R-27`: gated on the visual check, which has NOT run** |
| **`B2`** *(Guid declaration identity)* · **`B4`** | ⭐ `B4` must come **after** `B2`; `B2` is its own batch |
| **group `C`** *(`C2` the resolve hook, `C1`, `C3`, `C4`)* | ⭐ **the front of a four-item chain — it deserves its own batch** |
| **`LiveBlackboardPanel`'s retirement** | ⭐ `Q38`: the fixed-list formatter arm FIRST |
| **watch pinning · the `⋮` button · `BP-325`** | unbuilt, elsewhere |

---

## 8. ⭐ GATES — **the rule-8 contract, plus the four this batch owns**

| # | report |
|---|---|
| **1–7** | the standard contract — verbatim commands · **`--no-build` column** *(⛔ `NodeEditor.Core`, `NodeEditor.UI`, `Fhsm.Tests` report a STALE BIN)* · every red confirmed **pre-existing vs `3868c29e5`** · clean tree after every suite · both quarantine counts · every id you allocated |
| ⭐ **7b** | ⭐⭐ **Run every gate script UNFILTERED with `EXIT=$?` shown.** 📌 **Your own Batch 90 §7b root cause** — `tail` discarded the failure banner **and** the exit code, and the script's REMEDY table read as its VERDICT. ⭐ **Batch 90 got this right; keep it** |
| ⭐⭐⭐ **8 — GOLDEN, and this batch has THREE reasons it must not move** | ⭐ **`91a`**: no orchestrator for a subtree with no bindings · ⭐ **`91b`**: no shipped asset can contain an alias · ⭐ **`91e`**: existing `_auto_` names are untouched. ⇒ ⛔⛔ **ANY golden movement is a STOP-AND-REPORT**, not a rebase |
| ⭐⭐ **9** | ⭐ **THE ENUMERATION: every persisted field of the DTO listed at `…Persistence_Detailed_Design.md:132`**, and which are built. 📌 **`R-74`** — ⚠ **that one line is how `91b` was found**; ⭐ **check whether anything ELSE in it is missing** |
| ⭐⭐⭐ **10** | ⭐ **What each rail ASKS.** ⛔⛔ **`91b`'s rail MUST round-trip through a real save/load** — 📌 an in-process `AddAlias`-then-read is precisely what let this defect live, and `BlackboardAliasingTests` already does that and is green |
| ⭐⭐ **11** | ⭐ **REVERT-GOES-RED, one probe per item**, un-applied with the **INVERSE EDIT** — ⛔ never `git checkout --` |

⭐ **Baseline** *(post-Batch-90)*: AiShared **1479** · Blueprints **3773/3783/10** · BTree.Editor **615** ·
Hsm.Editor **551** · Hrot.Editor **201** · Breakpoints **143** · NodeEditor.Core **211** ·
NodeEditor.UI **135** · Fhsm **300** · `Fdp.Presentation.Tests` **146 FILTERED** *(⭐ **`91d` should make
this a real number**)* · tracker **open 66 / done 207** · rulings **66/66**.
⛔ **`Fdp.Toolkits.Tests`: do not run it** — 📌 `DEBT-AIB-030`.

## 9. ⭐⭐ If you must stop — **and this batch has clean seams**

| ⭐ complete on its own | |
|---|---|
| **`91b`** | the alias defect — **independent of everything else** |
| **`91a` + `91c`** | ⭐ **together** they make Approach B real. ⛔ **`91a` alone ships an emitter for a panel nobody can open; `91c` alone opens a panel that authors dead data** — ⚠ **land both or say which you landed and why** |
| **`91d`** · **`91e`** | riders, independently droppable |

⚠ **If `91d`'s question has no clear answer from the code — STOP and report it.** ⛔ **Do not guess
which side is wrong**; 📌 a guard that hides a real contract violation is worse than a red suite you
can see.

## 10. ⭐⭐⭐ WHAT THIS UNLOCKS — **state it in the report**

⭐ **The sub-asset sharing model works end to end for the first time**: an alias survives a reload, and
a field-sync binding actually copies. ⭐ **Say whether a visual-check row should be added** for either —
⛔ **I will write it; do not edit the guide.**
