<!--STATUS
state: LIVE
doc-type: batch report (ephemeral — the durable record is the DESIGN)
updated: 2026-08-25
current-answer: the whole file. ⛔ It carries NO design content: the as-built lives in
  docs/DESIGN_Cgf_Editor_Sharing_Slice2_Open_Asset.md §11 (obligation ⑤), and this report POINTS there.
-->
# REPORT — **cgf==editor slice 2 (CE-009): CGF opens an asset + MCP drive/observe** *(backend/CGF lane)*

> 📌 **Dispatch `2603adad9`** · started-marker `f2018bd14` · **ids allocated `CE-012`…`CE-018`** *(rule 5;
> tracker **Area L**, continuing the `CE-` series)*.
> 📄 **The design is the record:**
> [`DESIGN_Cgf_Editor_Sharing_Slice2_Open_Asset.md`](../../DESIGN_Cgf_Editor_Sharing_Slice2_Open_Asset.md)
> — **§11 is new and is the as-built.**

## 1. ⭐⭐⭐ THE RESULT

📐 **`--mode all` indexes 72 AI assets** *(the editor: 73)*, **opens** them over MCP, switches graph tabs,
and renders **real content** — `graph-canvas` and `my-blueprint` are **`SAME` as the editor's, whole
model, no exemption**, with an actual blueprint open on both hosts.

⭐ **`CE-009` is closed.** Slice 1's shell was real but every window could only show its empty state; that
was the root of all three of its open rows.

⭐ **Six MCP routes** *(`gen:catalog` **66 → 72 tools**)*, and the **main toolbar** is now a readable
`PanelKind` on both hosts.

## 2. ⭐ OBLIGATION ③ — **the diagrams vs what was built**

> §4 carries **8 classes**, §5 carries **1 sequence**. ⭐ **The SEQUENCE matches exactly.**
> ⚠ **The class diagram deviates in one box and was MISSING TWO** — both folded into the design (§11.0).

| # | deviation | where |
|---|---|---|
| ① | 🔴🔴 **the DOCUMENT FACTORIES were not in the design at all** — without them `AiDocument.ViewState` is null, so the canvas has an active document while MyBlueprint and Details see nothing | §11.3 · `CE-015` |
| ② | 🔴 **nor was the `ActiveChanged` RETARGET** — Details reads the perspective store's `ActiveAsset` and the outline holds a retargeted model; opening a document does not push either | §11.3 · `CE-015b` |
| ③ | **the toolbar model lives in `Fdp.Presentation`**, not AiShared, and **publishes outside the draw guard** | §11.4 · `CE-016` |
| ④ | **`FindAllBySourceFilePath`** — the design did not say what an ambiguous path should do; it is REPORTED with candidates, ⛔ never resolved by picking the first | §11.2 · `CE-017` |

⭐⭐ **The finding worth carrying forward:** *"the asset opens"* and *"the asset is usable"* are different
claims, and at the canvas level they look identical. 📌 Only the panels that read THROUGH the canvas
context tell them apart — which is why §1's *"prove SAME on a POPULATED asset"* was the right acceptance
criterion and *"the open route returns 200"* would not have been.

## 3. ⭐⭐ THE RAILS

| rail | what it pins |
|---|---|
| ⭐⭐⭐ `The_same_opened_asset_looks_the_same_on_both_hosts` | **the headline** — the same asset opened on both hosts *(picked by `sourceFilePath`, ⛔ never by index)*, its tab active, and `graph-canvas` + `my-blueprint` **SAME**. ⭐ Anti-vacuity FIRST: both hosts must report `hasActiveDocument`, so a regression that stops opening reddens instead of returning to the empty-vs-empty green slice 1 had |
| `The_cluster_can_discover_open_and_switch_graph_tabs` | the four verbs end to end on the cluster, exercising the **path** form *(the Guid form is the headline rail's)*; every asset carries both addresses; a second asset is opened so the tab SWITCH is not trivially true; an unknown id is a **404**, not a 500 |
| `The_main_toolbar_is_readable_on_both_hosts` | `main-toolbar` publishes on both; the editor's entries are non-empty *(anti-vacuity on the reference side)*; ⚠ the cluster's count is asserted **`== 0`** so the first CGF toolbar entry REDDENS it and names design §7 |

⚠ **`details` is asserted for what this slice can honestly claim** — same `assetId`, a real `assetName`,
no empty state on either host. ⛔ Its whole-model verdict stays under the DECLARED divergence, whose
reason is now **two measured causes** *(`$.mode` run state — `CE-003`; `$.offeredViewIds` needs an
`IBlueprintDebugSession` — `CE-004`)*, both of which pre-date this slice.
⭐⭐ **That is not a narrowing:** before this batch the cluster's Details read `assetId: null` and *"No
document is open."* — the new assertion is strictly stronger than anything that existed.

## 4. GATES *(rule 8 contract)*

*(filled in §4b before the batch closes.)*

## 5. ⭐ IDS ALLOCATED *(rule 5)*

**`CE-012` … `CE-018`**, tracker **Area L**.
✅ `CE-012` the two project references · `CE-013` the populated catalog *(closes `CE-009`)* ·
`CE-014` `AttachAssetShell` on both hosts · `CE-015` the document factories **+ the `ActiveChanged`
retarget** · `CE-016` the toolbar `PanelKind` *(and CGF's empty toolbar)* · `CE-017` the three additive
AiShared members.
⚠ Open: **`CE-018`** *(the editor's two inline `.csproj` walk-ups should route to
`AssetRoots.ResolveProjectDir` — another lane's file, so filed not done)*.
⭐ **`CE-011` narrowed:** the reload pipeline's *"no trigger"* blocker is gone now that CGF opens
documents; its other two stand *(§11.7)*.

## 6. 🔴 THE ONE THING THAT NEEDS A DECISION FROM YOU

⛔⛔ **Three additive members landed in `Hrot.Editor.AiShared` — the variable-model lane's frozen
assembly — and the handoff §4 asks for that lane's nod BEFORE landing. It has not been obtained.**

| member | shape |
|---|---|
| `AssetCatalog.FindBySourceFilePath` · `FindAllBySourceFilePath` | **new methods**, nothing existing touched |
| `AssetRoots.ResolveProjectDir` | **new static**, lifted from `EditorSubsystem`'s inline copy because a second host needs it |

⭐ **Evidence they are safe:** no existing member changed, and `Hrot.Editor.AiShared.Tests` is unchanged
at **2016 / 0**. ⚠ **Raised here rather than assumed** — 📌 the handoff calls it *"a quick nod; it is not
a modification of existing behaviour"*, and I could not obtain one from inside this lane.

## 7. ⛔ WHAT THIS SLICE DID **NOT** DO

| | |
|---|---|
| ⛔ **MCP authoring** *(create an asset, add/wire nodes)* | design §8 FUTURE — `CE-FUTURE-authoring` |
| ⛔ **asset editing / hot-reload writes** | `CE-011`, now one blocker lighter |
| ⛔ **live variable-VALUE write** | `R-52`, the variable-model lane's |
| ⚠ **give CGF toolbar ENTRIES** | design §7 hands that to the first slice that ports a toolbar-controlled feature; slice 2 made the toolbar READABLE so that slice can assert it |
| ⚠ **the 73 vs 72 asset difference** | ⭐ **the CAUSE is measured in §4b, not guessed here.** The rail now PRINTS the symmetric difference by path and kind — ⛔ a count gap is exactly what a report is tempted to explain away *("probably the scenario contributor")*, so the rail names it instead |
