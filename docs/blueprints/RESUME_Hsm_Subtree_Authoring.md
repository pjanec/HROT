<!--STATUS
state: LIVE
doc-type: THE resumption doc for the `behaviors` lane — programme: **HSM SUBTREE AUTHORING + THE SHARED ASSET PICKER**.
  ⚠ A STATE doc, not canon. Every "green"/"pushed"/"HEAD" line is a snapshot dated below.
  ⛔ VERIFY against git before acting ("THE LEDGER MAY NOT ASSERT WHAT THE CODE IS").
updated: 2026-09-26
build-state: 🟡 **DESIGN APPROVED AND WRITTEN (§7.1a / §11.1a / §S1). BUILD NOT STARTED.**
  ⭐⭐⭐ **START AT §1 — the four build items.** ⛔ **§2 is the measurement; DO NOT RE-DERIVE IT.**
  Branch `behaviors`, HEAD `6c98681f8` at time of writing, tree clean, everything pushed.
current-answer: ⭐⭐⭐ §1 is the build list · §2 the measurements already made · §3 the corrections that
  must not be re-inherited · §4 standing constraints · §5 gate baselines.
known-rot: nothing.
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
