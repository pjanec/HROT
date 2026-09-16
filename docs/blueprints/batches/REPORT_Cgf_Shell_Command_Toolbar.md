<!--STATUS
state: LIVE
updated: 2026-08-26
current-answer: this is a BATCH REPORT — ephemeral. ⭐ The durable record is
  DESIGN_Cgf_Shell_Command_Toolbar_Slice.md §9 AS BUILT (the three argued deviations and what the gate
  caught), folded back per obligation ⑤.
known-conflict: none.
-->
# REPORT — **CGF shell-command + main-toolbar adoption** *(`CE-037`…`CE-040`)*

> 📌 **Dispatched at `cc738017b`** · **started-marker `2fc7de02a`** *(rule 1b, pushed before any code)*.
> 📄 Handoff: [`HANDOFF_Cgf_Shell_Command_Toolbar.md`](HANDOFF_Cgf_Shell_Command_Toolbar.md) ·
> design: [`DESIGN_Cgf_Shell_Command_Toolbar_Slice.md`](../../DESIGN_Cgf_Shell_Command_Toolbar_Slice.md).
> ⭐ **All four items DONE.** ⚠ Item ④ shipped **half-done first** and the full T3 caught it — §3.

## 1. ⭐⭐ OBLIGATION ③ — **the design's UML vs what was built**

⭐ Checked before building: **every box in §4's `classDiagram` exists exactly where it says**
*(graph CLI: `ToolbarCommandAdapter` · `ShellEditorCommands` · `MainToolbarManager` · `SilkIconProvider` ·
`IIconProvider` — 56 nodes, 5 real classes + their tests)*. ⇒ the slice really was adoption, not building.

| the diagram says | built as |
|---|---|
| `CgfEditorShellToolbar.RegisterCommonCore(shell, toolbar, icons, hostServices)` | ✅ **as drawn** — plus a **nullable toolbar**, §2 ⑵ |
| `EditorSubsystem ..> CgfEditorShellToolbar : calls (was inline)` | ✅ |
| `CgfSubsystem ..> CgfEditorShellToolbar : calls` · `..> SilkIconProvider` | ✅ |
| §5's sequence *(icons → RegisterCommonCore → per-command Adapter.Register)* | ✅ ⚠ with a **second pass for separators**, §2 ⑶ |

## 2. ⭐⭐ WHAT THE MEASUREMENT CHANGED

### ⑴ ⛔ "Extract lines 4464-4562" is not a contiguous block

📐 The command **descriptors** were scattered and mostly already shared: `shell.openAsset`/`shell.newAsset`
sat **outside** the `if (MainToolbar != null)` guard, `ShellSaveCommands.Register` is a shared registrar
~900 lines earlier, `AiDebugCommands.Register` likewise. ⇒ ⭐⭐ **the genuinely duplicated thing is the
LAYOUT** — which ids, at which sortOrders, with which separators. That is what the helper owns; the
descriptors that already had a shared registrar are still registered by their own registrar.
⛔ Duplicating them into the helper would be a second definition of one command.

### ⑵ ⚠ The toolbar parameter had to be NULLABLE

📐 `shell.openAsset`/`shell.newAsset` were registered **outside** the toolbar guard, so a bare
`EditorSubsystem` *(window-registration unit tests)* still got them **and their File-menu items**.
⇒ ⛔ folding everything inside the guard would have **silently removed the menu entries** on a
toolbar-less host. ⭐ Descriptors always; entries only when there is a toolbar.

### ⑶ ⭐ Separators need a second pass

A separator can only know whether to appear once the group behind it is resolved ⇒ commands first,
separators second. ⚠ **A separator names the group whose presence justifies it** — it may TRAIL that group
*(`ToolbarSep_OpenAsset`)* or LEAD it *(`ToolbarSep_PerspToAiDebug`)*.

## 3. 🔴🔴 WHAT THE GATES CAUGHT — **three defects, all mine, all before merge**

> ⭐⭐⭐ **Two were caught by the new unit rail; the third by the full T3.** None reached the coordinator.

| # | caught by | the defect |
|---|---|---|
| 🔴 **1** | the byte-identical gate | **`ToolbarSep_OpenAsset` was given a group of its own**, so nothing ever made it live and **it vanished from the EDITOR's toolbar** — a silent regression in the host the extraction was supposed to leave untouched |
| 🔴 **2** | the mirror guard | **the AI-debug ids were GUESSED**: the first draft wrote `"ai.debug.continue"`; the real id is **`"debug.continue"`**. ⛔⛔ **This one is SILENT by construction** — the derived-subset rule means an id nothing registers simply yields no button, so a typo **deletes the whole AI-debug toolbar group and fails nothing** |
| 🔴🔴 **3** | ⭐⭐ **the full T3** | **item ④ shipped HALF-DONE.** I deleted the `main-toolbar` known-divergence entry and **never implemented the subset assertion it was to be replaced by** ⇒ the three-way diff fell back to full-array golden *(editor 17 entries, cluster 5)* and reddened. ⚠ The sibling rail also still demanded `"SaveAllAiDocuments"`/`"QuickReloadAiAsset"` — **the ad-hoc ids this slice DELETES**, i.e. a rail asserting the divergence being removed |

⭐⭐ **The fix for ⑶ is a FIFTH VERDICT, `SUBSET-BY-DESIGN`** — ⛔ deliberately **not** another
`DivergesByDesign` row. That set says *"these differ and we accept it"*, with no shape; here the shape is
the whole point: both hosts register from ONE table, so every cluster entry must be on the editor at the
**same id + sortOrder + visibility**. ⇒ it catches a CGF-invented or renumbered entry that a blanket
exemption would wave through, while allowing the editor its extra buttons.
⚠ **Anti-vacuity: an EMPTY cluster list is a VIOLATION**, not a trivial subset — that is the exact state
this slice ends.

## 4. GATES *(rule 8 contract)*

> 📌 **Base: the started-marker `2fc7de02a`** *(dispatch `cc738017b`)*.
> ⭐ Built per affected project, then `--no-build`. ⛔ No full-solution build at any point.

| # | gate | verbatim command | `--no-build`? | result | Δ vs base |
|---|---|---|---|---|---|
| 1 | **affected-project builds** | `dotnet build {Hrot.Editor.AiShared,Hrot.Editor,Hrot.CGF,Hrot.ClusterRunner,Hrot.SystemTests,*.Tests}.csproj --no-restore -v q -nologo` | ⛔ builds *(once each)* | ✅ **0 errors** | — |
| 2 | ⭐⭐⭐ **the layout rail** — the extraction gate + the mirror guard | `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests --no-build --filter TheToolbarLayoutIsOneList` | ✅ | ✅ **5 / 5, ~30 ms.** 📐 pre-extraction list by id+sortOrder · the derivation · the separator rule · the null-toolbar case · the id mirror | **+5** |
| 3 | ⭐⭐ **the two toolbar T3 rails** | `dotnet test Hrot/Runner/Hrot.SystemTests --no-build --filter "…main_toolbar_is_readable…\|…two_modes_agree…"` | ✅ | ✅ **2 / 2.** 📐 `SUBSET-BY-DESIGN main-toolbar: cluster is a subset of the editor (5 of 17 entries)` · cluster ids `[shell.save, TimeControlGroup, ToolbarSep_OpenAsset, ToolbarSep_AiDebugToBuild, blueprint.compileReload]` | — |
| 4 | **the Blueprints unit suite** | `dotnet test …/Hrot.Blueprints.Tests --no-build -v q --nologo` | ✅ | ✅ **3963 / 0 / 18 skipped**, 2 m 21 s | **+5** |
| 5 | **the editor unit suite** | `dotnet test …/Hrot.Editor.Tests --no-build -v q --nologo` | ✅ | ✅ **251 / 0 / 1 skipped** | **none** |
| 6 | ⭐⭐⭐ **the INTEGRATION suite** *(rule 8 row 8)* — a shared helper both composition roots call, a deleted conformance baseline entry, and a new diff verdict ⇒ nothing smaller shows the cross-host contract holds | `scripts/run-system-tests.sh --no-build` *(**T3**, backgrounded)* | ✅ | ✅ **106 / 0 / 0 skipped**, 6 m 53 s | **+1 rail** *(MD-008's, merged in)* |
| 7 | ⚠ **reds proven** | re-run in isolation | ✅ | 📐 An earlier T3 showed **8 failed**: **2 were MINE** *(§3 ⑶, fixed)*; the other **6** — `VariableAddressingTests` ×3, `CapabilitySmokeTests` ×2, `ScenarioBehaviorTests` ×1, all `hill-attack` scenario-load — **pass 6/6 in isolation AND 0 fail in the clean full run**. ⇒ **CONTENTION**: that run overlapped a heavy unit suite on a 16 GB box | **none** |
| 8 | **golden movement** | — | — | ⭐ **ZERO** | **none** |
| 9 | 🔴 **tree CLEAN after every suite run** | `git status --short --untracked-files=all` | — | ✅ **empty** | — |
| 10 | **quarantine / skips** | — | — | ⭐ **adds no skip**; the 18 Blueprints + 1 editor skips are pre-existing | **none** |
| 11 | **tracker** | `python3 scripts/tracker-counts.py --check` | — | ✅ `open 102 / done 346 (+1 refuted)` — ⭐ unchanged: `CE-` rows carry no `BP-` id | — |
| 12 | **the ledger** | `python3 scripts/rulings-check.py` | — | ✅ **25 / 25** | — |
| 13 | **design-doc format + UML** | `python3 scripts/design-digest.py --check` | — | ✅ | — |
| 14 | **mermaid parses** | `MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs docs/DESIGN_Cgf_Shell_Command_Toolbar_Slice.md` | — | ✅ **2 / 2** | — |

### 4b. ⚠⚠ TWO PROCESS MISTAKES, both mine, both cost time

| 🔴 | what happened | ⭐ the rule it breaks |
|---|---|---|
| **1** | The **previous batch's T3 was ORPHANED** — backgrounded with a bare `&` inside a tool call, killed when the call returned. Lost ~30 min unnoticed, and that gate row was never filled | ⭐ background a long run through the harness, ⛔ never `&` |
| **2** | The replacement run was piped through **`\| tail -25`**, so ⛔ **the exit code I read was `tail`'s, not the suite's** — it reported `0` while the suite had **8 failures**, and I told the user "the same tree passed clean". 🔴 **A false green, self-inflicted, and reported as fact** | ⛔⛔ **never pipe a gate command** — the summary AND the status both live at the end |

## 5. ⭐ IDS ALLOCATED *(rule 5)*

**`CE-037`…`CE-040`**. ✅ `CE-037` the one shared list · `CE-038` the gated extraction ·
`CE-039` the CGF adoption · `CE-040` the divergence entry replaced by the SUBSET verdict.

## 6. ⛔ WHAT THIS BATCH DID **NOT** DO

| | |
|---|---|
| ⛔ **the menu (UXI-05)** | design §8 — its own slice; when built its registration **extends this same helper** |
| ⛔ **a Save-All button** | §9.2 — the editor never had one; adding it breaks `CE-038`'s own gate |
| ⛔ **the AI-debug group on CGF** | §9.3 — the ids mean AI-GRAPH stepping and CGF has no debug session; binding its CLUSTER-TIME controller to them would make one id mean two things |
| ⛔ **Open / New on CGF** | §9.4 — no picker launcher composed. ⚠ CGF can still CREATE over MCP *(`MA-019`…`023`)* |
| ⚠ **`PerspectiveToolbarSection` on CGF** | design §8 left it to the implementer. ⛔ Not added: the dangling separator is gone, so A2 needs no perspective group — ⭐ and adding one is a cheap round-out a later slice can make deliberately |
