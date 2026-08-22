<!--STATUS
state: LIVE
build-state: READY-TO-BUILD
updated: 2026-08-22
current-answer: §1's task table. This is a DISPATCH, not a design — the design is
  DESIGN_Details_Panel_View_Switching.md §7 (approved by the user 2026-08-22) and the UML lives there.
stale-below: nothing.
known-rot: none.
known-conflict: none.
design-basis: DESIGN_Details_Panel_View_Switching.md §7 (target state, approved) · §7.6 (the order) ·
  §7.3's catalogue+ranks · §7.4's classDiagram · §7.5's sequenceDiagram · §6 L3 / L5 · R-98 · R-116.
-->
# ⭐⭐⭐ TASKS — **`BP-399`: one shell, and the Inspector's views become Details views**

> 📄 **THE DESIGN IS [`DESIGN_Details_Panel_View_Switching.md` §7](../DESIGN_Details_Panel_View_Switching.md).**
> ⛔ **Nothing here restates it and no diagram is redrawn** — 📌 *"the diagrams live in the design, never
> in the batch."* Every item below cites the section it builds.
> 🔒 **Approved by the user, `2026-08-22`:** *"good design approved."*

## §1 — THE TASKS

| # | task | design | gate | verdict |
|---|---|---|---|---|
| **S0** | ⭐ **MEASURE before building**: are §6 `L3`'s *Diagnostics* and *Layout/byte-budget · Asset settings* rows already satisfied by `details.blackboard`? | §7.6's closing note | a written answer + `search_graph` totals; ⛔ **no code** | ✅ **YES — both already satisfied.** 📐 `BlackboardDetailsView`'s own header records that §6 `L3`'s **three** rows ship as **ONE** view: `BlackboardAuthoringWindow.DrawClientArea` is one flowing body with **no seam** to split, and `VariablesPanelControl`'s host **IS** that window *(`:509`)*. ⇒ ⛔ **no code**, and adding views for those rows would duplicate `details.blackboard` |
| **S1** | ⭐⭐⭐ **Blueprint gets the real shell** — `DetailsWindow` under the existing id, `BlueprintDetailsWindow` retired, its node arm ported | §7.3 ①③④ · §7.6 ① | ⛔⛔ **STAGE GATE**, see §2 | ✅ **BUILT** — `BP-428` · `BP-429` · `BP-430`. Stage gate ①–⑤ all hold *(§2 below, annotated)* |
| **S2** | ⭐⭐⭐ **`details.nodeproperties` on BTree + HSM** — extracted from `InspectorWindow`'s facet arm, **Rank 20** | §7.3's catalogue · §7.6 ② | selecting a BTree node makes it the **default**; Blackboard Variables is the other toolbar entry | ✅ **BUILT** — `BP-432`. ⚠ **Scope extended by one arm** *(`BP-431`, §6)*: the facet arm and the `B-3` default-value arm shared one cache and moved together |
| **S3** | ⭐ **`details.utility`** — from `InspectorWindow`'s utility arm | §7.6 ③ | offered on a utility-consideration selection; ⚠ **ported as the stub it is** | |
| **S4** | ⛔ **`details.parametersync`** — **NOT THIS BATCH** | §7.6 ④ · `R-99` | — | ⛔ **deferred, by design** |
| **S5** | ⭐⭐ **`L5` retire** — delete `InspectorWindow`, drop `ai_inspector_*` from the shipped default layout | §7.6 ⑤ · §6 `L5` | the layout rail *(`BP-103b`)* stays green; no orphaned ids | |

---

## 2. ⛔⛔ THE STAGE GATE — **after `S1`, before `S2`**

⭐ Same shape as `L6.1a`'s, and for the same reason: `S1` changes a window class that four perspectives
depend on, and piling two new views on an unverified swap is how a structural change hides its blast
radius.

| must hold | | ✅ result `2026-08-22` |
|---|---|---|
| **①** | ⭐⭐ **Scenario · BTree · HSM offer sets UNCHANGED**, ordered-equal — the existing rail `TheAiOfferSetsAreUnchangedTests` already asserts three of them | ✅ **BTree/HSM rows byte-for-byte the pre-`S1` measurement.** ⚠ **The Blueprint row MOVED, by design** — §7.3 ① gives it the shell, so it gains `details.variables`; re-expressed in place with the reason, ⛔ not silently updated *(`B101c`)* |
| **②** | ⭐⭐⭐ **Blueprint's Details is now a `DetailsWindow`** and its id is still `ai_details_blueprint` | ✅ `TheOneShellIsOnEveryPerspectiveTests.{EveryAiPerspectivesDetailsPanel_IsTheOneShellClass, TheShellKeepsThePersistedWindowId}` — the type asserted **exactly**, so a subclass would fail too |
| **③** | ⭐⭐ **Blueprint gains toolbar + float + pin** — `ShowsFloatAndPin` true with a view showing | ✅ `WithADocumentOpen_EveryAiShellOffersFloatAndPin` *(with the negative half in the same test)* + `FloatingFromAnAiShell_RegistersAWindow` |
| **④** | ⛔ **Blueprint's node drawer still renders** — the ported view shows what `BlueprintDetailsWindow`'s node arm showed | ✅ all 8 behavioural cases ported to `BlueprintNodeDetailsViewTests` + `FunctionCallNodeDrawerTests` FC-09; ⚠ **`R-27`: rail-green TO a visual check** — the pixels are the user's |
| **⑤** | ⭐ solution build **0 errors**; the three suites green | ✅ see §5's gate table in the report |

🔴 **Any drift ⇒ HALT and report.** ⛔ Do not self-certify past it.
⭐ **No drift found.** ⚠ The one row that moved *(①'s Blueprint half)* is the approved design's own change, argued above and folded into `DESIGN_…` §7.3.

---

## 3. ⚠⚠ THE ONE SEQUENCING TRAP — **`S1` cannot be split**

📐 **Measured:** `BlueprintDetailsWindow` already claims the id **`ai_details_blueprint`**, and
`PerspectiveWorkspaceRegistrar.RegisterCore` **throws** on a duplicate id *(Batch 81's guard, deliberately
loud)*.

⇒ ⛔⛔ **Adding the shell for Blueprint while the old window still registers CRASHES THE EDITOR AT
STARTUP.** ⭐ So `S1` is necessarily atomic: stand up the shell, port the node arm, retire the old window
— **in one commit**. ⚠ It cannot be staged as *"add the shell now, port later"*.

⭐ **This is the reason §7.6 puts it first**: until Blueprint has the shell, `details.nodeproperties` has
two candidate homes and `S2`'s extraction target is ambiguous.

---

## 4. ⭐ DECISIONS ALREADY TAKEN — **do not re-litigate**

| | |
|---|---|
| ⭐⭐ **`details.nodeproperties` is ONE view id across BTree/HSM/Blueprint** | §7.3's catalogue. ⚠ Each perspective registers its **own instance** — the registries are per-perspective, so one id collides with nothing *(unlike `details.runtime.<kind>`, which needs the kind in the id because one registry holds several)* |
| ⭐⭐ **Rank 20** | above Blackboard (5) and Variables (10) ⇒ a selected node is the DEFAULT. 📌 That is the user's ask, verbatim |
| ⚠ **Runtime stays at 50** | ⇒ during a live session Runtime still outranks node properties. ⭐ Deliberate; `R-98` means one click switches. ⛔ **Flagged to the user as their call** — if they want node properties to win while running, that is a rank change, not a build decision |
| ⛔ **The persisted ids are KEPT** | `ai_details_blueprint` stays. 📌 §5: a bare key rename *"silently resets layouts"* |
| ⭐ **EXTRACT, do not WRAP** | §6 `L3`'s *"do not delegate this one"* — 📌 a thin wrapper would leave the 697-line and 350-line windows standing as the implementation, which is the duplication §7 exists to end |

---

## 5. ⭐ GATES *(the standing rule-8 contract)*

One row per gate · verbatim command · pass/fail/skip · delta vs base · goldens as a diff shape ·
`tracker-counts.py --check` · `rulings-check.py` · `design-digest.py --check` · `mermaid-check` on any
touched design · the **`BP-` ids allocated** · `R-106` verdicts · ⭐⭐ **the STAGE-GATE result**.

⚠ **Known pre-existing, do not re-diagnose:** `Fdp.Presentation.Tests` cannot run whole *(`BP-419`,
test-host crash)* — gate it by filter. Four coordinator design docs fail `design-digest --check`
*(`DESIGN_Stride_Port`, `UX_Feature_Curated_Scenarios`, `MCP_Integration`, `Editor_Headless_Xvfb`)* —
⛔ **not this lane's to fix**; report and move on.

⭐ **`R-27`:** the visible outcome is a visual check. ⛔ Deliver rail-green **to** visual-check; do not
report *"works"* for what a headless run cannot see.


---

## 6. 🛑 `S2`'s SCOPE QUESTION — **measured `2026-08-22`, before building**

📐 **`InspectorWindow.DrawClientArea` is 366 lines and has SIX arms, not four.** §7.6 names **three** of
them. ⇒ ⛔ **`S5` ("retire `InspectorWindow`") would strand the other three**, and two of the six share
one cache.

| # | arm | lines | §7.6 says |
|---|---|---:|---|
| ① | **asset header** — name, *Find References*, *Rename…*, *Go to Definition* | 192–256 | ✅ **Asset Browser row menu** *(`S2b`, `BP-436`/`BP-437`)* — ⛔ **not a view** |
| ② | ⭐ **facet / StructEdit** — the selected node's editable fields | 258–324 | ✅ `details.nodeproperties` *(`S2`)* |
| ③ | ⚠ **default-value editor (`B-3`)** — the default of the variable the selected node WRITES | 326–434 | ✅ `details.nodeproperties` *(`S2`, `BP-431`)* — ⛔ §7.6 ② named only ②; it shares ②'s cache |
| ④ | **parameter synchronization** | 436–466 | ✅ `details.parametersync` — ⛔ deferred *(`S4`/`R-99`)* |
| ⑤ | **utility consideration** *(a stub)* | 468–476 | ✅ `details.utility` *(`S3`)* |
| ⑥ | **sub-element collision strip** | 182 | ✅ **Diagnostics window** *(`S2b`, `BP-435`)* — ⛔ **not a view** |

### ✅ RESOLVED `2026-08-22` — **② and ③ moved together; ① and ⑥ ROUTED OUT OF THE PANEL (`S2b`)**

⭐ **② + ③ built as recommended** *(`BP-432`)*.

⭐⭐⭐ **① and ⑥ are answered, and the answer was NOT a Details view** — 🔒 **user, `2026-08-22`:** *"asset
related context menu items then, still nothing for a details panel view"* · *"it need to be routed to
where the collision can be seen or fixed"* · *"picker should not have that menu."*
📄 **The routing and the as-built live in [`DESIGN_Details_Panel_View_Switching.md`](../DESIGN_Details_Panel_View_Switching.md) §7.4a**;
⛔ **not restated here** — this file is ephemeral, the design is not.

| arm | went to | id |
|---|---|---|
| ⑥ collision strip | **Diagnostics**, `AIE053` at `Info` — ⚠ **the old strip was DEAD** *(`GetBindingAmbiguities` returns empty unconditionally)* | `BP-435` |
| ① Rename… · Find References | **Asset Browser row context menu**, opt-in per host ⇒ the picker gets none | `BP-436` |
| ① Go to Definition | **deleted** — an empty placeholder; `CommandCatalog.GoToDefinition` is the real one | `BP-437` |

⇒ ⭐⭐ **`S5` is no longer blocked on `BP-431`. It is blocked on `S3` alone** *(the utility stub).*

### ⭐⭐ The question — **② and ③ are ONE surface sharing ONE cache**

📐 `GetCurrentFacet()` populates `_currentFacet`; ③ reads **that same field** *(`:356`)*.
⇒ ⛔ **extracting ② alone forces a SECOND facet cache** in the view while the window keeps its own —
📌 ruling 9, two implementations of one concept, which is what §7 exists to end.

⭐⭐⭐ **My recommendation: extract ② and ③ TOGETHER as `details.nodeproperties`.**
📄 Batch 74's own record of ③ *(`InspectorWindow.cs:340`)*: *"the surface earns itself: it is
**NODE-scoped** (you see the default of the variable this node writes) where Track C's table is
**ASSET-scoped**"* ⇒ ⭐ **it IS node properties**, by the design's own words. ⛔ The alternative — a
separate `details.defaultvalue` view — puts two views on one selection that a designer would expect in
one panel, and still leaves the shared cache to solve.

⚠ **Arms ① and ⑥ are a SEPARATE question, and it is `S5`'s, not `S2`'s** — they are **asset**-scoped,
not node-scoped. ⭐ Neither is in §7.6, so `S5` cannot delete the window until they have a home:
`Find References`/`Rename…` are a refactor entry point *(the `FindResultsWindow` already exists)*, and
the collision strip is a diagnostic. 🔒 **Recorded as `BP-431`; this needs a decision, not a guess.**
