<!--STATUS
state: LIVE
doc-type: THE resumption doc for the `behaviors` lane — programme: **HSM SUBTREE AUTHORING + THE SHARED ASSET PICKER**.
  ⚠ A STATE doc, not canon. Every "green"/"pushed"/"HEAD" line is a snapshot dated below.
  ⛔ VERIFY against git before acting ("THE LEDGER MAY NOT ASSERT WHAT THE CODE IS").
updated: 2026-09-26
build-state: ✅ **BUILT `2026-09-26` — all four items done, plus a FIFTH the build discovered (`CE-361`).**
  ⭐⭐⭐ **START AT §1a — what was built and what it cost.** ⛔ **§2 is the measurement; DO NOT RE-DERIVE IT.**
  Branch `behaviors`. ⚠ Verify HEAD against git — this line is a snapshot.
current-answer: ⭐⭐⭐ §1 is the build list · §2 the measurements already made · §3 the corrections that
  must not be re-inherited · §4 standing constraints · §5 gate baselines.
known-rot: ⚠ §1's four rows say ☐; they are all DONE — §1a carries the as-built and is authoritative.
known-conflict: ⚠ `RESUME_Occurrence_Storage.md` is the PREVIOUS programme's doc. That programme is
  COMPLETE; that doc's §0 now points here. ⛔ Two `RESUME_*` files exist for this one lane — this is
  the live one.
related-designs:
  - AI_Editor_Shared_Infrastructure.md — ⭐⭐ §7.1a OWNS the shared `[AiAssetPicker]`, its drawer and
    THE HEAL RULE (class + sequence diagrams live there).
  - HSM_Editor_NodeEditor_Host_Design.md — ⭐⭐ §11.1a OWNS the HSM `StateFacet` field,
    `HsmSubtreeResolver` and the dangling validator rule.
  - BTree_Editor_NodeEditor_Host_Design.md — ⭐ §S1 OWNS BTree's picker adoption and its resolver defect.
  - DESIGN_Occurrence_Scoped_Storage.md — §32 owns the RUNTIME that consumes the authored reference
    (`E5`); §32.28 is a pointer here. ⛔ NOT the authoring owner.
  - Architect_Question_36_Subtree_Hosting_Runtime.md — APPROVED; `Q36-B = A` = name BESIDE a Guid.
-->

# RESUMPTION — **HSM subtree authoring + the shared asset picker**, `behaviors` lane

RELEARN

---

## 0. 🔒 WHAT THE USER APPROVED, VERBATIM

> *"now hsm subtree authoring. pls let's discuss the design. **The tree asset must be pickable**"*
>
> *"go with **keep both and heal from guid**; same would be good for btree (to fight the "BTree
> persists only the name"), **no differences, consistency**."*
>
> *"i think we already crossed the 'Occurrence scoped storage' … there are much better **owning
> designs** to put the diagram into."* ⇒ ✅ design written to §7.1a / §11.1a / §S1, **not** to §32.
>
> *"yes, go."*

⭐ **The goal in one line:** `E5` built the whole runtime for *"an HSM state hosts a BTree"* and
**nothing can author the reference** — `StateFacet` has no subtree field ⇒ rules 8/8b/10 are
unreachable on a real asset. This programme makes the reference **authorable by PICKING**.

---

## 1. ⭐⭐⭐ THE BUILD — four items, in order

| # | item | where | done? |
|---|---|---|---|
| **1** | ⭐⭐ **`AiAssetPickerAttribute(AssetKind)` + `AiAssetPickerDrawer`** — `IImGuiFieldDrawer` + `IPickerListSource`, items = `IAssetCatalog.All` filtered by `Kind`. ⚠ Subscribe `IAssetCatalog.Changed` or read live; ⛔ do not snapshot at construction *(the `CE-343` capture lesson)* | `Hrot.Editor.AiShared/Inspector/` | ☐ |
| **2** | ⭐⭐ **`SubtreeReferenceResolver`** — the shared DECISION: `Resolve(catalog, name, guid) → (Name, AssetId, IsResolved, Healed)`. 📄 **The heal rule is §7.1a's sequence diagram — follow it exactly** | `Hrot.Editor.AiShared/References/` | ☐ |
| **3** | ⭐⭐⭐ **HSM** — `StateFacet.SubtreeName` + `[AiAssetPicker(AssetKind.BTree)]`, read-only `SubtreeAssetId` + `IsSubtreeResolved`; `StateNode.IsSubtreeResolved` **derived, NOT persisted**; `HsmSubtreeResolver` walking `asset.AllStates`; facet mapper BOTH directions; a dangling validator rule mirroring BTree's Rule 6 | `Hrot.Hsm.Editor/` | ☐ |
| **4** | ⭐ **BTree** — `[AiAssetPicker(AssetKind.BTree)]` on `BTreeSubtreeFacet.SubtreeName`; **fix `BTreeSubtreeResolver` to HEAL instead of erasing** | `Hrot.BTree.Editor/` | ☐ |

⛔⛔ **THE FOUR BOXES ABOVE ARE ALL TICKED — see §1a.** They are left unticked as the dispatched
scope; §1a is the as-built and wins.

---

## 1a. ✅ AS-BUILT `2026-09-26` — **four items shipped, and a FIFTH the build found**

| id | what | rails |
|---|---|---|
| **`CE-358`** | ⭐⭐ the shared picker — `AiAssetPickerAttribute` + `AiAssetPickerDrawer` + `SubtreeReferenceResolver` | `SubtreeReferenceResolverTests` **8** |
| **`CE-359`** | ⭐⭐⭐ HSM authoring — `StateFacet.SubtreeName`, the Guid captured **at pick time**, `HsmSubtreeResolver`, the `SubtreeReferenceDangling` rule | `HsmSubtreeAuthoringTests` **8** |
| **`CE-360`** | 🔴 BTree's resolver **erased the persisted Guid** on a missed name — routed through the shared rule | `BTreeSubtreeResolverTests` **6** |
| ⭐⭐ **`CE-361`** | 🔴🔴 **the item the BUILD found: `BTreeSubtreeResolver` had ZERO PRODUCTION CALLERS.** Step 0 added to **both** document factories; the HSM dangling rule now asks the resolver and **skips with no catalogue** | `BTreeDocumentFactoryTests` **+2**, `HsmSubtreeAuthoringTests` **+1** |

### 🔒 The one thing worth carrying forward from this build

⛔⛔ **`CE-361` was found because MY OWN DESIGN'S PROSE was an unmeasured principle.**
`HsmSubtreeResolver`'s header said *"call it after load and after a hot reload, **exactly like
`BTreeSubtreeResolver`**"* — describing a convention that **did not exist**. ⇒ ⭐⭐ **`R-139`'s claim
table applies to the prose written INTO the code you are building, not only to the lean handed to
the user.**

⭐ **What caught it: an EXISTING rail, not a new one.** `CE-338`/`CE-339`'s control arms
(*"with no catalogue, nothing is badged"*) reddened, because the new dangling rule read a derived
flag that nothing set. 🔒 **T-1 ③ inverted:** the rails went red for a TRUE reason ⇒ **the product
side moved, not the rails.**

⭐ **Registration:** both hosts already wire facet pickers through
[`AiFacetPickerBinder`](https://github.com/pjanec/HROT/blob/behaviors/Hrot/Editor/Hrot.Editor.AiComposition/AiFacetPickerBinder.cs)
*(built `2026-09-26` as `CE-347`)*. ⛔ **Do not add a per-host registration** — that is the fifth
duplicate this programme keeps undoing.

⭐⭐ **Rails to write** *(T-1: find the feature's own suite FIRST — `HsmValidatorTests`,
`BTreeValidatorTests`, `HsmFacetMapperTests`, `TheViewsCameFromTheirOwnWindowsTests`)*:
the picker lists only `AssetKind.BTree`; the heal branch heals **and marks dirty**; ⛔⛔ **the
never-erase branch — a red-proof that a dangling reference KEEPS both fields** *(this is the shipped
BTree defect; it deserves the strongest rail)*; the HSM dangling diagnostic fires.

---

## 1b. ✅ WHAT CAME AFTER — **two follow-on programmes, both COMPLETE `2026-09-27`**

⭐⭐ **This lane did not stop at authoring.** ⚠ A session resuming here should know these landed, and
⛔ **read their owning designs, not this summary**, before touching either area.

| programme | ids | owning design | one-line state |
|---|---|---|---|
| ⭐⭐⭐ **`E6` — BTREE-HOSTS-BTREE** | `CE-362`…`CE-369` | [`DESIGN_Occurrence_Scoped_Storage.md`](DESIGN_Occurrence_Scoped_Storage.md) **§33**, `build-state: BUILT`; ⭐ **§33.11 is the AS-BUILT and WINS over §33.6/§33.7/§33.9** | ✅ **a BTree node now hosts another BTree, through the real kernel.** `ISubtreeHost` in `Fbt.Kernel`, `OccurrenceSubtreeHost` + `BTreeHostedSites` in `Fdp.Toolkits`, wiring in BOTH registrar routes, `BEH010`/`BEH011` analyzers, 7 end-to-end rails red-proved |
| ⭐⭐ **BEHAVIOUR SELF-REGISTRATION** | `CE-370`…`CE-375` | [`DESIGN_Behavior_Self_Registration.md`](DESIGN_Behavior_Self_Registration.md), §10a is the as-built | ✅ **`CgfCuratedBehaviorRegistrar` is DELETED** — a generator emits it from `[BTreeDefinition]`/`[HsmDefinition]`/`[BehaviorResolver]`. ⭐ `HsmDefinitionGenerator` → `FhsmMachineCatalog` is the twin that was missing |

### ⚠⚠ The three things that would be re-derived otherwise

| | |
|---|---|
| ⛔⛔ **`ExecuteSubtree` MUST mirror `ExecuteAction`'s `RunningNodeIndex` bookkeeping** | 📐 my first kernel arm returned the host's status straight from the switch ⇒ the node never entered the path the post-tick sweep diffs, so **`F14` was silently dead** while every shape rail stayed green |
| ⛔⛔ **A `Selector`/`ObserverSelector` RESUMES into a running child** | ⇒ the obvious *"a higher-priority branch preempts the hosted child"* rail **cannot be written** — only a `Parallel` child sweep and the out-of-bounds path reset abandon a still-`Running` child. Two drafts failed before this was understood |
| ⛔ **`StateMachineGraph.Compile()` ALREADY sets `blob.Metadata`** | ⇒ `CE-370` was a **one-line carry-across** in `HsmBridgeEmitCore`, not a build. ⚠ Both the tracker row's lean and the design's §8 ② said the catalog was "the one place that knows both" — **both were wrong**, and both reasoned from what the emitter did rather than what the blob held |

📋 **Named gaps, deliberately not built:** a `Parallel`-hosted end-to-end · a `BrainTickSystem`-driven
end-to-end · **no runtime cycle guard** *(a hand-written ring recurses until the stack dies)* · the
*"child name resolves to no registered behaviour"* analyzer, which **cannot** be written per-compilation
without false positives and needs a boot-time cross-assembly check instead.

---

## 2. 📐 MEASURED ALREADY — **⛔ DO NOT RE-DERIVE**

| # | fact | evidence |
|---|---|---|
| ① | ⛔⛔ **NO ASSET PICKER EXISTS.** All **eleven** picker attributes pick a SYMBOL — method, event, guard, state, blackboard field, marker, montage, property path, working slot | full enumeration `2026-09-26`; ⇒ **this is a BUILD, not an adoption** |
| ② | ⭐ `BTreeSubtreeFacet.SubtreeName` is labelled *"Referenced asset"* and is **plain free text** | `BTreeFacets.cs:155-175`; Guid + `IsResolved` already `[EditReadOnly]` — **copy that shape** |
| ③ | 🔴 **`BTreeSubtreeResolver` ERASES the persisted Guid** — `payload.SubtreeAssetId = Guid.Empty` in the else branch, and a failed name lookup **is** the rename case | `BTreeSubtreeResolver.cs`; item 4 fixes it |
| ④ | ⭐⭐ **BOTH hosts already persist the full reference.** `BTreeSubtreePayloadDto` carries `SubtreeAssetId` + `SubtreeName` + `IsResolved`, round-tripped both ways; `StateNodeDto` carries Guid + name since `E5` | ⇒ **NO persistence-format change is needed for either host** |
| ⑤ | ⭐ `IAssetCatalog` already exposes **`All`** and a **`Changed`** event | ⇒ the picker needs **no new catalog member** |
| ⑥ | ⭐ `IPickerListSource` *(`Hrot.Editor.AiShared.Inspector`)* is the existing testable picker seam | every picker drawer implements it — headless-testable item lists |
| ⑦ | ⭐ `BTreeSubtreeResolver` filters `referenced.Kind == AssetKind.BTree` | the picker must filter the same way |
| ⑧ | ⭐ BTree validator **Rule 6** = `IsResolved == false` ⇒ *"references '{x}' which no longer exists — reselect or remove"* | item 3's HSM rule mirrors it |
| ⑨ | ⭐ `BTreeValidator.cs:179` architect ruling — *"Identity is by FQN, **never by a persisted AssetId**"* | ⚠ that is the **composed-blueprint** path; consistent with ours: **author a string, derive the id** |

---

## 3. ⚠⚠ CORRECTIONS — **do not re-inherit these**

| 🔴 I claimed | 📐 the truth |
|---|---|
| *"BTree persists only the name"* | **FALSE at the authoring layer.** True only of `BehaviorTreeBlob`, which is explicitly *"not persisted (runtime-only)"*. The authored DTO carries the full triple ⇒ **the two hosts' persistence shapes already agree** |
| *(earlier, other programme)* *"`SAME: 11 · DIFFERENT: 0`, editor⇄cluster parity INTACT, one pre-existing red"* | **RETRACTED.** The log said `Failed: 14, Passed: 103`; the `SAME:` figure exists in **no log**; the rail that answers that question was failing (`CE-357`, since fixed). 🔒 **Quote the `Failed: N, Passed: N` line verbatim, or say the run was not read** |
| *"`entity-blueprints` has two production hits so it survives"* | **FALSE** — the sweep counted TEST files. 🔒 **A matching NET count is not an explanation; diff the SETS** |

---

## 4. 🔒 STANDING CONSTRAINTS

- **Branch `behaviors` only.** `git push -u origin behaviors`; retry network errors 2s/4s/8s/16s.
- ⛔⛔ **`git stash@{0}` holds *"EXPERIMENT: RootParamsBytes always 100 — probe only"* — a diagnostic
  that must NEVER be committed.** Leave it stashed.
- Ask questions in **plain chat text**; ⛔ never the `AskUserQuestion` widget.
- Render docs and task ids as **GitHub blob links on `behaviors`**; gloss every id on first mention
  *(the user reads on mobile)*.
- Run builds/tests/searches **in the background**; ⛔ never a foreground blocker.
- ⛔ Do **not** "fix" `RW-S` tracker rows (known gap `CE-259at`).
- ⛔ No model identifier in commits, PR titles/bodies, code comments or any repo artefact.
- Commit trailers: `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>` and
  `Claude-Session: https://claude.ai/code/session_01Y7DZG7BBQ8rNTesibu9NXo`.
- ⛔ No PR unless explicitly asked.
- ⚠ **Cross-lane:** `Hrot/Runner/Hrot.SystemTests/` is the BACKEND lane's harness — **ask before
  editing it** *(the user has authorised specific edits before; each one was asked for)*.

---

## 5. 📐 GATE BASELINES *(`2026-09-26`, all green unless noted)*

| suite | baseline |
|---|---|
| `Hrot.Editor.AiShared.Tests` | **2077 / 0 / 1 skip** |
| `Hrot.Editor.Tests` | **424 / 0 / 1 skip** ⚠ `AiHotReloadCoordinatorTests.TwoReloadCycles_OldAlcIsCollected` is a GC-timing **flake** under parallel load — 19/19 in isolation |
| `Hrot.BTree.Editor.Tests` | **629 / 0** |
| `Hrot.Hsm.Editor.Tests` | **579 / 0** |
| `Hrot.Blueprints.Tests` | **4036 / 0 / 18 skip** |
| `Hrot.Diagnostics.Breakpoints.Tests` | **164 / 0** |
| ⚠ `T3` (`scripts/run-system-tests.sh`) | **`Failed: 13, Passed: 104, Total: 117`** before this session's harness fixes; **four rails were then fixed** (`CE-353`/`354`/`355`/`356`/`357`) ⇒ expect ~**8**. ⛔ **NOT re-run since** — do not quote a number, run it |

⭐ **Doc gates** *(all sub-second, run before every push)*:
`python3 scripts/tracker-counts.py --check` · `python3 scripts/design-digest.py --check` ·
`python3 scripts/rulings-check.py` · `MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs <file>`

⭐ **Build discipline:** build the **affected project** (~8 s), ⛔ never the solution (~115 s); build
the **TEST** project you are about to `--no-build` test *(the stale-binary trap)*.
