<!--STATUS
state: LIVE
updated: 2026-08-22
current-answer: §1's per-item ledger + §7's gate table.
stale-below: nothing.
known-rot: none.
known-conflict: none.
not-delivered: VC-5 (BP-399, the L3 remainder) — see §6. Stated as a deviation, not omitted quietly.
design-basis: DESIGN_Details_Panel_View_Switching.md §6 L3/L4.4 · §2/§2b · UX_Feature_Curated_Scenarios.md
  · HANDOFF_VisualCheck_Details_And_Menus.md.
-->
# ⭐⭐⭐ REPORT — **the visual-check findings** *(Details views, float/pin, menus)*

> **Handoff:** 📄 [`HANDOFF_VisualCheck_Details_And_Menus.md`](HANDOFF_VisualCheck_Details_And_Menus.md)
> **dispatched at** `4e346705e` · **started at** `6749c26d` *(marker `14661b3a`)* ·
> **branch** `claude/hrot-implementation-j1jvin`

## §1 — PER-ITEM LEDGER

| # | item | state | note |
|---|---|---|---|
| **VC-1** | float + pin unreachable | ✅ **fixed — THREE separate causes** | §2 · `BP-420` `BP-421` `BP-422` |
| **VC-2** | `File ▸ Layout` → `Settings` | ✅ **done, and measuring first changed the fix** | §3 · `BP-423` |
| **VC-3** | curated-scenarios item "missing" | ✅ **fixed — the handoff's diagnosis was wrong** | §4 · `BP-424` `BP-425` |
| **VC-4** | graph-signature + Runtime don't appear | ⚠ **NO CHANGE — both refuse correctly; one stated cause is FALSE** | §5 · `BP-426` |
| **VC-5** | `BP-399` — the `L3` remainder | ⛔⛔ **NOT STARTED — a batch, and the design gates 2 of its 5 rows** | §6 · `BP-427` |

⭐ **IDs allocated:** **`BP-420`**…**`BP-427`**. ⭐ **`BP-403` CLOSED** by `BP-422`.

---

## 2. ⭐⭐⭐ `VC-1` — **three causes, and fixing any two still leaves it unreachable**

### 2.1 The manager never arrived *(`BP-420`)*

📐 `DetailsWindow.AttachWindowManager` had **exactly one caller** — `PerspectiveWorkspaceRegistrar:636`.
The **Scenario** host is built at the composition root instead ⇒ it never got a manager ⇒ ⛔
`DrawToolbar`'s `if (_windowManager != null)` was false for ever.

⚠⚠ **That was my omission in `L6.1c`** — the `2026-08-16` silent-default shape, tenth instance.

🛠 **`ManagedWindow.OnRegistered(WindowManager)`**, raised by `WindowManager.RegisterWindow`.
⭐⭐ Registration is the one event **both** roots already perform ⇒ forgetting is unrepresentable —
📌 `R-126`'s PULL argument one floor up. ⛔ `AttachWindowManager` deleted *(ruling 9)*.

### 2.2 The switch's rule swallowed float/pin *(`BP-421`)* — **and my first rail was vacuous**

📐 `DrawToolbar` opened `if (offered.Count < 2) return;` — written for the **switch** — with float/pin
**below** it ⇒ a single-view context had neither.

⛔⛔ **The finding that matters more than the fix.** My first rail asserted `OpenFloat(wm) != null` and
**stayed green through a probe that put the old guard back** — 📌 `BP-402` ①. The **gesture** was never
guarded; the **draw** was, and `R-21`/`R-62` leave the draw unrailed ⇒ ⭐⭐⭐ **a fix living only in the
draw is a fix no probe can redden.**

⭐ So the decision moved into the **model** — `ShowsViewSwitch(frame)` / `ShowsFloatAndPin`, named and
independent — and the rail asserts they **disagree at one offer**. Re-probed: reddens.
⚠ **Limit:** proves the DECISION, ⛔ not that a button is on screen.

### 2.3 `BP-403` CLOSED — the View menu *(`BP-422`)*

📄 §6 `L4.4`: *"+ the View menu, so a float is reachable with Details closed."* 🛠 Contributed from
`OnRegistered`, so it reaches every perspective with no root line.

⭐⭐⭐ **ONE pair of items for ALL perspectives, resolved at click time from `CurrentPerspective`.**
⛔ Not one pair per window: three perspectives host one each, so a per-window path clutters the bar and
a shared path would have **the last registration win** — ⚠ silently pointing the menu at the wrong
perspective *(`R-78`; railed as such)*. ⚠ Greyed **with a reason**; the enabled label names the view.

---

## 3. ⭐⭐⭐ `VC-2` — **the obvious reading was a trap**

📐 The menu bar draws **two independent models**: `RenderGlobalMenu(GlobalMenu.Root)` and
`ImGuiMenuRenderer.DrawMenus(BuildHostMenuDtos())` — and **`Settings` lived in the DTO list**.
⇒ ⚠ registering `"Settings/Layout/…"` through `GlobalMenu` would have drawn **two top-level menus both
called `Settings`**, side by side — worse than the `File ▸ Layout` it replaced.

🛠 The framework's own `UI Scale & Fonts…` moved **into `GlobalMenu`** *(`R-13`)*; the DTO block is gone.
⭐ Railed through the public menu model, because a rail that only counted children would stay green
while the bar drew two.

⚠ **Visible consequence, stated:** `Settings` now sits **left of `Windows`**, not between `Windows` and
`Help`. ⚠ **No design record existed** *(searched `docs/` and `.dev/`)* — a user preference, recorded at
the registration site.

---

## 4. ⭐⭐ `VC-3` — **the item was never missing; it was silent**

⛔ **The handoff's lean was false on both clauses.** 📐 ① registered unconditionally; ②
`RenderGlobalMenu:707` passes `enabled` to `ImGui.MenuItem` ⇒ a disabled leaf **is drawn, greyed**;
③ `<repo>/scenarios` holds **3** curated scenarios, so the probe is clean here.

⭐⭐ **The gap:** `RegisterCommand` never set `DynamicDisplayName` ⇒ a **null** `DynamicLabel` ⇒ a greyed
item with no explanation, indistinguishable from an unimplemented one. 🛠 It now names the cause, in the
layout feature's own shape.

⚠ **What I could not do:** reproduce the user's run location headless ⇒ ⭐ the fix makes the item
self-explaining **wherever** it runs. ⛔ Stated rather than claimed fixed.

### ⚠ `BP-425` — two rails had been RED since the coordinator's curated-scenarios commit

`Expected: 5, Actual: 6`. Confirmed pre-existing by stashing my own change. ⭐ **Direction established
first** *(`B101c`)*: the sixth command is designed, documented and reachable ⇒ the count records reality.
⚠ `FiveCommands…` renamed — **a count in a test NAME lies the moment the count moves.**
📌 `Hrot.Editor.Tests` **is** in the solution: this was gateable and simply not run.

---

## 5. ⚠⚠ `VC-4` — **no predicate changed, and one stated cause is FALSE** *(`BP-426`)*

📐 **The wiring hypothesis dies first:** both descriptors ARE registered in production — Runtime via
`RegisterPane` *(the registrar does pass the catalogue)*, graph-signature via `RegisterExtraWindow` →
`ContributeDetailsViews`.

| view | its real predicate | verdict |
|---|---|---|
| **Runtime** | `Mode != Planning` ∧ its asset kind — §6 `L3` **verbatim** | ⭐ a Planning editor declines **by design** |
| ⛔⛔ **Graph signature** | `Asset.Kind == Blueprint` ∧ `EditableGraphs(asset).Count > 0` | ⛔ **the handoff said *"needs a graph row selected"* — the predicate says NOTHING about selection** |

⇒ ⚠ clicking empty canvas does **not** make graph-signature decline. Being on a non-Blueprint document,
or on a blueprint with no Function/Event/Macro graph, does.

⭐⭐ **A third suspect, named because it is the same disease as `L6.4`/`UXI-11`:** `AppliesTo` reads
`_asset ?? _selectionStore.SelectedAsset` — the **LEGACY** store, ⛔ not the one the `DetailsContext` is
built from.

⇒ **The rails PIN the predicates rather than change them** — 📌 the handoff's own instruction was
*"argue it against §6 `L3`"*, and §6 `L3` states both rules in those words. ⛔ Loosening either puts a
view on screen that then has to apologise *(`R-117`, one level down)*.

⚠ **Needs a running editor to finish:** which perspective was the user in, and does the open blueprint
have a Function/Event/Macro graph?

---

## 6. ⛔⛔ `VC-5` — **NOT STARTED, and that is a deviation I am surfacing, not burying** *(`BP-427`)*

📄 §6 `L3`'s table, decomposed:

| row | state |
|---|---|
| ⛔ **Node properties** | the design's own words: ***"do not delegate this one"*** — a **697-line** `InspectorWindow`, 4 arms |
| ⛔ **Parameter sync** | ***"LAST — after the orchestrator wiring"*** *(`R-99`)* |
| ⭐ **Layout / byte budget · Asset settings** | buildable — ⚠ but `BlackboardAuthoringWindow` **already** contributes `details.blackboard`, so this row may be partly or wholly DONE and needs measuring first |
| ⭐ **Diagnostics** | buildable *(`VariablesPanelControl`'s host)* |
| ⭐ **Utility** | buildable — ⚠ but it is the **same 697-line window** as the row the design forbids delegating |

⇒ ⭐⭐ **The design gates two of the five itself, and the remaining three are three adapters with three
predicates and their rails.** That is a batch, on top of four items already landed here.
⛔ **I did not half-build it**, because a half-migrated `L3` leaves the tracker ambiguous about which
surfaces are live — and scaling the work down is the user's call, not mine.

⭐ **Recommended split:** measure the Blackboard row first *(it may already be satisfied)* → Diagnostics
→ then **Utility + Node properties as one `InspectorWindow` batch with its own design pass.**

---

## 7. ⭐⭐⭐ THE GATE TABLE — **run ONCE, at the end**

> **base** `6749c26d` *(the rule-7 ff-merge point)* · **dispatch** `4e346705e`

| # | gate | result | `--no-build`? | Δ |
|---|---|---|---|---|
| **1** | `dotnet build IOS-IG-SimHost.sln --no-restore` | ⭐ **0 errors** | ⛔ builds | — |
| **2** | `Hrot.Editor.AiShared.Tests` | ⭐ **1866 pass / 0 fail / 1 skip — 1867 total** | ✅ in solution | **+8** *(1859 → 1867, `VC-1`)* |
| **3** | `Hrot.Blueprints.Tests` | ⭐ **3901 / 0 / 18 skip — 3919 total** | ✅ in solution | **+3** *(3916 → 3919, `VC-4`)* |
| **4** | `Hrot.Editor.Tests` | ⭐ **214 / 0 / 0** | ✅ in solution | **+5** *(`VC-3`)* · ⭐⭐ **and 2 PRE-EXISTING REDS FIXED** *(`BP-425`)* |
| **5** | ⛔ `Fdp.Presentation.Tests` | ⛔ **whole suite un-gateable — `BP-419`** *(host crash, pre-existing)* | — | — |
| **5b** | …by filter *(`~WindowManager\|~EntityInspector\|~MenuCommandAdapter`)* | ⚠ **187 / 3 / 190** | ✅ | ⭐ **the 3 are `GetFilteredEntities_{FiltersById, RespectsLimit, InvalidSearch…}`** — baselined RED at `f968e693` during `L6`, untouched here |
| **6** | `tracker-counts.py --check` | ⭐ **OK — open 91 / done 271** | — | — |
| **7** | `rulings-check.py` | ⭐ **22/22** | — | ⚠ 1 staleness WARN on `.claude/CLAUDE.md` *(pre-existing)* |
| **8** | goldens | ⭐ **none moved** — only `.cs`/`.md` | — | zero |

### ⭐ Revert probes

| probe | reddens |
|---|---|
| `RegisterWindow` stops calling `OnRegistered` | **5 of 8** `VC-1` rails |
| `ShowsFloatAndPin` re-fused with `Offered.Count >= 2` | the one-offer rail *(after it was made non-vacuous — see §2.2)* |
| the `Settings` DTO block restored | `TheHostMenuDtos_NoLongerCarryASettingsBlock` |
| `DynamicDisplayName` → `null` | `WhenItCannotRun_TheLabelSaysWhy` |

### ⛔ What this batch does NOT close

| ⛔ | |
|---|---|
| **the visual check** | every item here is on-screen behaviour and I ran headless — ⭐ delivered rail-green **to** visual-check |
| **`VC-5` / `BP-399`** | §6 — not started, decomposed, recommended split given |
| **`VC-4`'s reproduction** | §5 — needs a running editor; the two candidate causes are named |
