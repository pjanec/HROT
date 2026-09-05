<!--STATUS
state: LIVE
updated: 2026-08-26
current-answer: this is a BATCH REPORT — ephemeral. ⭐ The durable record is
  DESIGN_Cgf_Menu_Follows_Focus_Slice.md §10 AS BUILT (the four argued deviations, the corrected §4/§5
  UML, and the rail table), folded back per obligation ⑤.
known-conflict: none.
-->
# REPORT — **CGF menu adoption + the follows-focus registry model** *(`CE-041`…`CE-045`, UXI-05)*

> 📌 **Dispatched at `4b31db747`** · **started-marker `762add67a`** *(rule 1b, pushed before any code)*.
> 📄 Handoff: [`HANDOFF_Cgf_Menu_Follows_Focus.md`](HANDOFF_Cgf_Menu_Follows_Focus.md) ·
> design: [`DESIGN_Cgf_Menu_Follows_Focus_Slice.md`](../../DESIGN_Cgf_Menu_Follows_Focus_Slice.md).
> ⭐ **All five items DONE.** ⚠ **Four argued deviations** — §2 — of which **two need a coordinator/user
> call**: `File/Reload` and what CGF actually gained.

## 1. ⭐⭐ OBLIGATION ③ — **the design's UML vs what was built**

⭐ Checked before building. **The design carries 8 classes and 1 sequence.** ✅ Every box exists where it
says; ⚠ **three corrections**, all folded into §4 of the design *(obligation ⑤)* rather than left here:

| the diagram said | built as |
|---|---|
| `MenuBinding` · `MenuItemNode.Bindings` · the three `Register*(…, perspective)` · `MenuCommandAdapter.Register(…, perspective)` | ✅ **as drawn** |
| `WindowManager` resolves by `CurrentPerspective` | ✅ — ⭐ **plus `HasVisibleDescendant`**, the empty-parent skip §3 ② *requires* but §4 never drew. **Added to the diagram** |
| `RegisterCommonCore(shell, toolbar, icons, menu, services)` | ⚠ **`(shell, toolbar, icons, services, menu = null, menuPerspective = null)`** — `services` is required, so an optional `menu` cannot precede it. Cosmetic; **diagram corrected** |
| — | ⭐⭐ **NEW: `GlobalMenuPanelViewModel` / `GlobalMenuItemView`** — §2 ④. **Added to the diagram** |
| §5's sequence *(resolve → draw or skip)* | ✅ **as drawn** |

## 2. 🔴 THE FOUR DEVIATIONS

### ⑴ ⭐⭐ `Slot.MenuOrder` — **the menu's order is not the toolbar's** *(a silent trap, caught by measuring)*

📐 The editor's **toolbar** reads **New · Open · Save** *(sortOrder −11/−10/−9)*. Its File **menu** has
always read **Open Asset… · New Asset… · Save** — `GlobalMenuRegistry` is a **trie with no ordering key**,
so items render in **registration order**.
⇒ ⛔ driving the menu pass off `SortOrder` **silently swaps the editor's first two File items.** Nothing
else in the tree notices. ⭐ `Slot` therefore carries a **separate `MenuOrder`**; one table, two orderings,
both explicit, and gated.

### ⑵ 🔴🔴 **NO `File/Reload`, on EITHER host** — ⛔ *the handoff asked for something structurally impossible*

| the constraint | |
|---|---|
| item ③ / UXI-05 migration step 1 | ⭐⭐⭐ **the editor's menu must be BYTE-IDENTICAL** — the handoff states it **twice** |
| 📐 measured | the editor's File menu has **five** items *(Open Asset…, New Asset…, Save, Save As…, Save All)* and **NO Reload** |
| item ④ | *"CGF gains … `File/Reload`"* |
| ruling **58** | ⭐⭐ **ONE list, no `if (host==…)`** |

⇒ a `MenuPath` on the shared `compileReload` slot **adds `File/Reload` to the EDITOR** *(breaks the
byte-identical gate)*; giving it to CGF alone needs the per-host branch ruling 58 forbids.
⛔ **Both alternatives lose, so it is NOT built** — and
`The_editor_full_shell_yields_exactly_the_pre_extraction_file_menu` **asserts the absence**, so it stays
deliberate rather than accidental.

> ✅✅ **RESOLVED — user, `2026-08-26`: NO menu item, on either host.** 🔒 Verbatim: *"hot reload is now a
> toolbar menu button so no Main menu item is necessary."*
>
> ⚠ **And the premise was corrected in the same breath — worth recording, because the handoff's wording
> hid a real distinction.** *"Reload"* there meant **`blueprint.compileReload`**, which is the **ACTIVE
> document only** *("Compile & hot-reload the active blueprint / BTree / HSM")*. ⛔ It is **not** the
> all-assets command — that is a **separate slot**, `blueprint.fullRebuild` *("Rebuild all AI behavior
> assets")*, which CGF omits because it supplies no handler *(ruling 49)*.
> ⇒ ⭐ `compileReload` is **already a toolbar button on BOTH hosts, same id, same sortOrder**, from the
> one shared table *(`CE-039`)*. A duplicate menu entry would have bought nothing and changed the
> editor's menu. ⭐⭐ **The as-built stands; the question is CLOSED, not deferred.**

### ⑶ ⚠ **CGF gained ONE menu item, not four** — ⭐ the derivation working, ⛔ not a shortfall

📐 **Measured on the running cluster** *(T3, gate 3)*: editor **17** menu leaves, cluster **7**.

| item ④ expected | as built | why |
|---|---|---|
| `File/Save` | ✅ | CGF services save *(`CE-039`)* |
| `File/Open Asset…` · `File/New Asset…` | ⛔ **absent** | ⭐ CGF composes **no asset picker and no new-asset launcher** — the identical ruling-49 absence **`DESIGN_Cgf_Shell_Command_Toolbar_Slice.md` §9.4 already recorded for their toolbar buttons**. No handler ⇒ no descriptor ⇒ no item. ⭐⭐ They appear **with zero menu code written** the day a picker is composed |
| `File/Reload` | ⛔ **absent** | ⑵ |

⇒ ⭐ **the MECHANISM is what shipped**: one table, two surfaces, the subset **derived** from what each
host's shell services. ⚠ **The rail asserts the SUBSET VERDICT, not the list** — so the list growing later
is not a rail edit.

### ⑷ ⭐⭐ **The menu needed a PANEL MODEL before item ⑤ could assert anything**

📐 The toolbar has published `main-toolbar` since slice 2; the **menu published nothing at all**.
⛔ **A conformance verdict on an unpublished surface is not a verdict.** ⇒ new kind **`global-menu`**
*(path · kind · **scopes** · visible)*, published **unconditionally** from the menu-bar block — the same
reason `MainToolbarManager.PublishSnapshot` sits outside its draw guard: *"offers nothing"* must not look
like *"never instrumented"*.

⭐⭐ **And CE-040's subset checker was GENERALISED, ⛔ not copied** — `SubsetShape(ArrayProperty,
KeyProperty, ComparedProperties, Noun)` ⇒ `main-toolbar` and `global-menu` run **one implementation**
*(ruling 9)*. ⛔ The menu is **not compared by order**: the trie has no ordering key, so item order is a
per-host registration-order property the shared table does not promise.

## 3. GATES *(rule 8 contract)*

> 📌 **Base: the started-marker `762add67a`** *(dispatch `4b31db747`)*.
> ⭐ Built per affected project *(8 projects, ~8–17 s each)*, then `--no-build`.
> ⛔ **No full-solution build at any point.** ⛔ **No gate command piped** *(the `2026-08-26` lesson —
> a pipe hands you `tail`'s exit code)*.

| # | gate | verbatim command | `--no-build`? | result | Δ vs base |
|---|---|---|---|---|---|
| 1 | **affected-project builds** | `dotnet build {Fdp.Presentation,Hrot.Editor.AiShared,Hrot.Editor,Hrot.CGF,Hrot.ClusterRunner,Hrot.SystemTests,Fdp.Presentation.Tests,Hrot.Blueprints.Tests}.csproj --no-restore -v q -nologo` | ⛔ builds *(once each)* | ✅ **8 / 8, 0 errors** | — |
| 2 | ⭐⭐⭐ **the resolution rail** *(NEW)* | `bash scripts/quick-check.sh FDP/Engine/Fdp.Presentation.Tests TheMenuFollowsFocus` | ✅ | ✅ **5 / 5, 67 ms** | **+5** |
| 3 | ⭐⭐⭐ **the menu T3 rail** *(NEW)* + the two it extends | `dotnet test Hrot/Runner/Hrot.SystemTests --no-build --filter "The_global_menu_is_readable_on_both_hosts\|The_two_modes_agree_on_every_shared_panel_kind\|The_main_toolbar_is_readable_on_both_hosts"` | ✅ | ✅ **3 / 3, 36 s.** 📐 **editor 17 leaves** `[File/New Asset…, File/Open Asset…, File/Save, File/Save All, File/Save As…, File/Scenario/×6, Settings/×4, View/Details/×2]` · **cluster 7** `[File/Save, Settings/×4, View/Details/×2]` ⇒ **subset holds** | **+1** |
| 4 | ⭐⭐ **the layout/menu gate** *(now 7)* | `bash scripts/quick-check.sh Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests TheToolbarLayoutIsOneList` | ✅ | ✅ **7 / 7, 38 ms** — ⭐ the editor File menu **in order** + **no `File/Reload`** + CGF's derived-and-GLOBAL | **+2** |
| 5 | **the Blueprints unit suite** | `dotnet test …/Hrot.Blueprints.Tests --no-build -v q --nologo` | ✅ | ✅ **3965 / 0 / 18 skipped**, 2 m 3 s | **+2** |
| 6 | ⚠ **the Presentation unit suite** | `dotnet test FDP/Engine/Fdp.Presentation.Tests --no-build --nologo --filter "FullyQualifiedName~Tests.WindowManager"` | ✅ | ⚠ **149 passed / 4 failed** | ⭐ **+5 passed, 0 new reds** |
| 7 | ⚠ **the editor unit suite** | `dotnet test Hrot/Subsystems/Hrot.Editor.Tests --no-build -v q --nologo` | ✅ | ⚠ **250 / 1 / 1 skipped** | ⭐ **0 new reds** — §3b |
| 8 | ⭐⭐⭐ **the INTEGRATION suite** *(rule 8 row 8)* — a shared engine registry + a new PanelKind + a generalised conformance verdict ⇒ nothing smaller shows the cross-host contract holds | `bash scripts/run-system-tests.sh --no-build` *(**T3**, backgrounded, ⛔ unpiped)* | ✅ | ✅ **107 / 0 / 0 skipped**, 7 m 49 s, **exit 0** | **+1 rail** *(106 → 107 — the new menu rail)* |
| 9 | **golden movement** | — | — | ⭐ **ZERO** | **none** |
| 10 | 🔴 **tree CLEAN after every suite run** | `git status --short --untracked-files=all` | — | ✅ | — |
| 11 | **quarantine / skips** | — | — | ⭐ **adds no skip**; the 18 Blueprints + 1 editor skips are pre-existing | **none** |
| 12 | **tracker** | `python3 scripts/tracker-counts.py --check` | — | ✅ `open 102 / done 346 (+1 refuted)` — ⭐ unchanged: `CE-` rows carry no `BP-` id | — |
| 13 | **the ledger** | `python3 scripts/rulings-check.py` | — | ✅ **25 / 25**. ⚠ 2 staleness WARNs *(`DataBreakpointManager.cs`, `CapabilityManifest.cs`)* — **pre-existing, neither file touched here** | **none** |
| 14 | **design-doc format + UML** | `python3 scripts/design-digest.py --check` | — | ✅ 89 docs | — |
| 15 | **mermaid parses** | `MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs docs/DESIGN_Cgf_Menu_Follows_Focus_Slice.md` | — | ✅ **2 / 2** | — |

### 3b. ⚠⚠ THE TWO PRE-EXISTING REDS — **proven against the base sha, not asserted**

> ⭐ Rule 8 row 4. Both were measured by **stashing to the base tree, rebuilding, and re-running** — ⛔ not
> by inspection.

| red | at base | with the changes | verdict |
|---|---|---|---|
| `AiHotReloadCoordinatorTests.TwoReloadCycles_OldAlcIsCollected` | 🔴 **fails 4 / 4 full-suite runs** | fails **2 of 3** | ⭐⭐ **PRE-EXISTING and FLAKY** — a GC/ALC-collection assertion. 📐 **Passes 3 / 3 when run alone**, so it is load-dependent, ⛔ and this slice touches no ALC, hot-reload or blueprint-loading code. ⚠ **A finding worth a row: a rail whose verdict depends on GC timing is a rail that will cry wolf** |
| `PerspectiveMenuTests` ×3 + `WindowManagerMainToolbarTests.Render_InvokesMainToolbar_WithCurrentPerspective` | 🔴 **the SAME 4, by name** | 🔴 the same 4 | ⭐ **PRE-EXISTING, identical set** |

⛔⛔ **And a third, reported plainly:** `dotnet test FDP/Engine/Fdp.Presentation.Tests` **as a whole crashes
the test host** — at base it reported `Failed: 12, Passed: 222` before `Test host process crashed`, and the
abort point moves between runs. ⇒ ⭐ **the full Presentation suite CANNOT gate in this environment**, which
is why gate 6 filters to `Tests.WindowManager` *(the namespace this slice touches)* and states the count.
⚠ **This is a reported finding, not a silent omission** — the crash is pre-existing and orthogonal.

### 3c. T3 — **the full system suite**

`scripts/run-system-tests.sh --no-build`, **backgrounded** *(tier T3 — never a foreground blocker)* and
**unpiped**. ⭐ **The three conformance rails it contains were already proven green in isolation at gate 3**
before it started, with the published menu paths quoted there.

✅ **107 passed / 0 failed / 0 skipped**, **7 m 49 s**, **exit code 0** — ⭐ **106 → 107**, the one new
test being `The_global_menu_is_readable_on_both_hosts`. ⭐⭐ **The tree is CLEAN after the run**
*(`git status --short --untracked-files=all` empty)* ⇒ ⛔ no golden was regenerated by a test.

⚠ **The exit code was read from the script itself, ⛔ not through a pipe** — 📌 the `2026-08-26` mistake
where `| tail -25` handed back **`tail`'s** status and a suite with 8 failures was reported as a green.

⚠ **And it was NOT re-run "to be sure."** 📌 The three conformance rails were already proven at gate 3
*(3 / 3, with the published menu paths quoted)*; this run is the cross-host contract check rule 8 row 8
asks for, and one pass of it is the evidence.

## 4. ⭐ IDS ALLOCATED *(rule 5)*

**`CE-041`…`CE-045`.** ✅ `CE-041` the bindings model + the no-dead-headers skip · `CE-042` one table two
surfaces *(and `MenuOrder`)* · `CE-043` CGF adoption *(and the two absences)* · `CE-044` `global-menu` as a
readable PanelKind · `CE-045` the generalised `SUBSET-BY-DESIGN` verdict.

## 5. ⛔ WHAT THIS BATCH DID **NOT** DO

| | |
|---|---|
| ⛔ **`File/Reload`** | §2 ⑵ — needs a two-host call. ⭐ ~5 minutes once decided |
| ⛔ **the four gizmo-block cleanup** *(UXI-13)* | design §9 — CGF participates in none of them |
| ⛔ **per-perspective enabled / label / shortcut** | UXI-05's own record keeps those node-level; ⭐ the slice is faithful to it |
| ⚠ **a perspective-SPECIFIC menu item** | ⭐ the MODEL is built and unit-railed; ⛔ nothing in the common core differs per perspective yet, so nothing exercises it in a running host. **The first such item is what proves the feature end-to-end** |

## 6. ⭐⭐⭐ HANDBACK TO THE COORDINATOR — **feature parity is what closes the menu gap** *(user, `2026-08-26`)*

> 🔒 **User, verbatim:** *"yes i need the feature parity, and menus to be shown just for the features that
> are actually available."*

⭐⭐ **The second half is ALREADY THE MECHANISM and needs no work** — `CE-041`…`CE-045` make a menu item
appear **only** for a command the host's shell can service, and *absent, not greyed* is ruling 49 by
construction. ⛔ There is no per-host menu list left to maintain. ⇒ **the remaining gap is not menu code
at all**: it is the **features behind the items**.

📐 **Measured on `--mode all` — editor 17 menu leaves, CGF 7.** Here is every missing one with what it
actually costs, so the coordinator can scope rather than estimate:

### 6.1 ⭐⭐ `File/Open Asset…` + `File/New Asset…` — ⚠ **a RELOCATION, not new features**

📐 **CGF already has the hard half**: a **populated `AssetCatalog`** *(72 assets, `CE-009`)*, a per-kind
**`INewAssetService`** map and a working create path *(`MA-019`…`023`)*. ⛔ **What it cannot reach is the
UI shell around them** — and the reason is structural:

| what the editor builds | where it lives | reachable from CGF? |
|---|---|---|
| `AssetPickerLauncher` | `Hrot/Subsystems/Hrot.Editor/Browser/` | ⛔ **NO** — the **editor assembly** |
| `NewAssetLauncher` | `Hrot/Subsystems/Hrot.Editor/Browser/` | ⛔ **NO** |
| `AssetPickActionRouter` | `Hrot/Subsystems/Hrot.Editor/Browser/` | ⛔ **NO** |
| `PickerRegistry` *(the modal host)* | `NodeEditor.UI.Picker` | ✅ yes |
| `ShowNewAssetDialog` | 🔴🔴 **a LOCAL FUNCTION inside `EditorSubsystem.RegisterWindows`** *(`:3740`)* | ⛔ **not even a type** |

⇒ ⭐⭐⭐ **This is the seam-law shape again, and the same shape `CE-037` already fixed once**: the
capability exists and is **in the wrong assembly**, with one half not extracted at all. ⇒ the slice is
*"relocate the three `Browser/` types to `Hrot.Editor.AiShared` and promote `ShowNewAssetDialog` to a
class"*, then CGF composes them — ⛔ **not** *"build a picker for CGF"*.
⭐ **And the payoff is doubled**: the same composition lights up **both** the two toolbar buttons
*(`CE-016` §9.4's declared absence)* **and** the two menu items, from the one table, with **zero menu or
toolbar code written**.
⚠ **Scope honestly** — 📌 the `HN-037` lesson: `ShowNewAssetDialog` is a long local function closing over
`EditorSubsystem` state, so promoting it is **real work, not an `s/old/new/`**. Measure its captures first.

### 6.2 ⚠ `File/Scenario/×6` — ⛔ **a much bigger lift, and possibly not wanted**

`ScenarioMenuCommands.Register` takes **`IEditorLogic`**, and CGF has none — 📌 the same absence that
already made this slice pass `saveScenario: null` and `MA-019` §G record no scenario CREATE on CGF.
⇒ ⛔ **not a wiring slice**; it is *"does CGF host a scenario session at all?"*, which is a **ruling
question, not an implementation one**. ⭐ Recommend it be asked before it is scoped.

### 6.3 ⚠ `File/Save As…` — a **modal browser** CGF does not compose

📐 The editor builds a `SaveAsBrowserDialog`; this slice registered CGF's Save-As as a
**no-op-with-a-reason** *(it logs rather than throwing)*. ⭐ Rides along with §6.1's relocation — the same
`PickerRegistry` composition — ⛔ so it should not be scoped as its own slice.

### 6.4 ⛔ `File/Save All` — **not a gap**

⭐ The editor's **toolbar** never had a Save-All button either *(`CE-016` §9.2)*. It is an editor-only
menu affordance today; adding it is a deliberate two-host decision, ⛔ not parity debt.

### 6.5 ⭐ THE ORDER I'd RECOMMEND

**§6.1 first, alone.** ⭐⭐ It is the only one that is a *relocation of built code*, it closes **four**
declared absences at once *(two toolbar + two menu)*, and 📐 the `SUBSET-BY-DESIGN` rails already in place
mean **it needs no rail edit to prove** — the items simply appear on CGF and the verdict still holds.
⇒ ⛔ §6.2 needs a ruling first; §6.3 is a passenger; §6.4 is not debt.
