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

| # | gate | command | result | `--no-build`? | delta vs `2603adad9` |
|---|---|---|---|---|---|
| 1 | solution build | `dotnet build IOS-IG-SimHost.sln --no-restore` | ✅ **0 errors** | n/a | unchanged |
| 2 | ⭐ **T0 baseline, BEFORE any edit** | `run-system-tests.sh ClusterConformanceRails` | ✅ **10 / 0** | no *(built)* | — |
| 3 | ⭐⭐⭐ **conformance (acceptance vehicle)** | `run-system-tests.sh --no-build ClusterConformanceRails` | ✅ **13 / 0** | yes | **10 → 13**: +3 slice-2 rails, all green |
| 4 | ⭐⭐ **full system suite** | `run-system-tests.sh --no-build` | ✅ **93 / 0**, 5 m 44 s | yes | **90 → 93** *(slice 1's run)*; ⛔ no red, no new skip |
| 5 | ⭐ **revert probe** *(catalog population disabled)* | probe applied, rebuilt, conformance re-run | 🔴 **2 / 13 red, as designed** | yes | — |
| 6 | `gen:catalog:check` | `node gen-catalog.mjs --check` | ✅ **PASSED (72 tools, 72 endpoints)** | n/a | **66 → 72** — the six new `RouteDoc`s |
| 7 | `gen:skill:check` | `node generate-skill.mjs --check` | ✅ **PASSED** | n/a | `SKILL.md` regenerated *(438 lines)* |
| 8 | `test:catalog` | `node test-catalog.mjs` | ✅ **577 / 0** | n/a | **571 → 577.** ⚠ **It caught a real gap** — §4c |
| 9 | `Hrot.Editor.Tests` *(incl. `EveryRouteIsDocumentedTests`)* | `dotnet test … --no-build` | ✅ **248 / 0**, 1 skip | yes | unchanged ⇒ all six routes documented |
| 10 | `Hrot.Editor.AiShared.Tests` *(the frozen assembly)* | `dotnet test … --no-build` | ✅ **2016 / 0**, 1 skip | yes | **unchanged** ⇒ the three additions broke nothing |
| 11 | `Hrot.ClusterRunner.Tests` | `dotnet test … --no-build` | ⚠ **271 pass / 2 fail** | yes | **both PRE-EXISTING — proven by diff** *(§4d)* |
| 12 | `tracker-counts.py --check` | | ✅ **OK — open 102 / done 346** | n/a | unchanged *(`CE-` ids are not `BP-`)* |
| 13 | `rulings-check.py` | | ✅ **25 / 25** | n/a | unchanged |
| 14 | `design-digest.py --check` | | ✅ **clean** | n/a | unchanged |
| 15 | ⭐ `mermaid-check.mjs` | `MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs` | ✅ **2 / 2 blocks parse** *(slice-2 design)* | n/a | ⚠ **and it corrects slice 1's row 12** — §4e |

### 4c. ⭐⭐ Gate 8 caught a REAL gap — **six advertised tools with no handler**

📐 `gen:catalog` regenerated the catalog happily, but `test-catalog.mjs` also asserts *"`src/index.mjs`
registers a handler for every catalogued tool"* — ⛔ it did not. ⇒ the six tools would have been
**advertised over MCP and unreachable**. ⭐ Six handlers added; the hand-maintained expected-tools list is
the one thing generation cannot derive, so it was extended deliberately *(`HN-044`'s shape)*.

### 4d. ⭐ Gate 11 — **the two reds, proven pre-existing by `git diff`, not by rebuild**

`DataDrivenGizmoPredicateTests.D003_*`, both `InvalidCastException: D003NoOpDrawBuilder →
DebugPrimitiveBuffer` inside `DataDrivenGizmoSystem.Execute`. 📐 `git diff --name-only
2603adad9..HEAD` names **19 files**, and **neither `DataDrivenGizmoSystem.cs` nor
`DataDrivenGizmoPredicateTests.cs` is among them**. ⚠ Same pair slice 1 reported; still not this lane's.

### 4e. ⚠ **Gate 15 corrects a wrong claim in the SLICE-1 report**

⛔ Slice 1's row 12 read *"SKIPPED — no npm/node in this container."* **That was wrong.** 📐 `node`/`npm`
are at **`/opt/node22/bin`**, merely off `PATH`; the checker needs a one-off
`npm install mermaid@11 jsdom` into `MERMAID_PREFIX`, which succeeds here. ⭐ The slice-1 report is
amended in place with a dated correction. ⚠ **Its conclusion was right** *(slice 1 added no Mermaid, and
§3/§4 parse)* — ⛔ but *"the tool is unavailable"* was a wrong reason for a right answer, and the next
session would have inherited it.

⚠ **`verify.mjs` is NOT a gate here:** it is a LIVE end-to-end script needing a running host with a
loaded scenario; run cold it fails 32 assertions, all of the *"nothing is running"* shape. ⭐ The design's
T4 names `gen:catalog:check` / `gen:skill:check`, which are gates 6–7.

### 4f. ⚠ The working tree, and one measurement I did NOT take

✅ **Clean after every suite run** — no golden regenerated, ⛔ no new skip *(1 · 1 · 0, all unchanged)*.
⚠ **NOT measured: WHICH asset the editor has and the cluster does not** *(73 vs 72)*. The rail prints the
symmetric difference by path and kind — ⛔ but only on failure, and it passed. ⭐ Stated rather than
guessed: an earlier draft of this report attributed it to the editor's `ScenarioCatalogContributor`, and
📐 that is almost certainly wrong *(there are 3 curated scenarios, which would make the gap 3, not 1)*.
⇒ **one asset, cause unknown, no AI asset affected.**

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
