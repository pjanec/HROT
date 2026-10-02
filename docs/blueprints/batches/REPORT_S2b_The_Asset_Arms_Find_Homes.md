<!--STATUS
state: LIVE
updated: 2026-08-22
current-answer: this is a BATCH REPORT (ephemeral). The durable record is
  DESIGN_Details_Panel_View_Switching.md §7.4a (the routing + as-built) and the tracker rows
  BP-434..BP-437. Quote THOSE, not this.
stale-below: nothing.
-->
# ⭐ `S2b` — **the asset-scoped arms leave `InspectorWindow`, and none of them becomes a Details view**

> 🔒 **User, `2026-08-22`:** *"go to definition and rename and find references, these all sound like
> context menu items … asset related context menu items then, still nothing for a details panel view."*
> · *"if collision strip is a warning about naming collision or something, it need to be routed to where
> the collision can be seen or fixed."* · *"picker should not have that menu. green to go."*

📄 **Design basis:** [`DESIGN_Details_Panel_View_Switching.md` §7.4a](../DESIGN_Details_Panel_View_Switching.md)
*(written this batch — obligation ⑤)* · `AI_Editor_Shared_Infrastructure.md` §16.1/§16.2 ·
`docs/designs/blueprint-integ-1/DESIGN.md` §5.7.

**Base sha:** `5d1fd44d`. **IDs allocated:** `BP-434` `BP-435` `BP-436` `BP-437`; **closed** `BP-431`.

---

## 1. ⭐ What was built

| # | move | id |
|---|---|---|
| ⑥ | **collision strip → `DiagnosticsWindow`** as `AIE053` rows at `Info` | `BP-435` |
| ① | **Rename… · Find References → the Asset Browser's row context menu**, opt-in per host | `BP-436` |
| ① | **Go to Definition → DELETED** *(an empty placeholder body)* | `BP-437` |
| — | the umbrella: `InspectorWindow` **401 → 302** lines, `S5` unblocked except for `S3` | `BP-434` |

### ⛔⛔ Two dead-code findings, and one is load-bearing

1. ⭐⭐⭐ **The collision strip could never draw.** `DrawCollisionDiagnosticStrip` called
   `SubElementCollisionDetector.GetBindingAmbiguities`, which returns `Array.Empty`
   **unconditionally** — by its own doc comment. ⇒ ⚠ **`S2b` is not a relocation; it is the first time
   this data reaches a designer at all.** A rail pins the deadness so nobody "restores" the old call.
2. ⭐ **`assetBrowserFindResults` was constructed at `EditorSubsystem:2749` and never registered** with
   the WindowManager — the rename preview had nowhere to land. It now has a caller and a home.

### ⭐⭐ The design decisions worth naming

- **Severity is `Info`, not Error.** BTree/HSM resolve bindings by **full FQN** ⇒ a shared short name is
  never ambiguous at runtime; an `Error` would be the exact false positive `GetBindingAmbiguities`
  refuses. The message says the fix is a rename **in C#**, because the editor cannot make it.
- **`RowCommands` defaults to EMPTY.** One `AssetBrowserPanel`, two hosts *(`AssetBrowserDockedWindow`,
  `AssetPickerModal`)* ⇒ a menu on the panel appears in both. Opt-in makes the picker correct **by
  omission**, not by someone remembering to opt out — the silent-default shape pointed the safe way round.
- **`AssetRenameModal` is EXTRACTED, not rewritten** *(§7.4's `..>`)*, keeps §16.2's split *(OK previews,
  never applies)*, and is drawn as a **frame overlay** *(`BP-327`, fourth occurrence guarded)*.
- ⚠ **`DiagnosticsWindow`'s early-return hid schema rows** whenever there were no per-asset validators.
  Fixed; `Collect()` is now the railable seam and `DrawClientArea` a thin renderer *(`R-21`/`R-62`)*.

---

## 2. ⭐ Revert probes

| probe | expected | measured |
|---|---|---|
| drop `RowCommands = assetRowCommands` from the docked browser's options | only the row-command composition rail | ✅ **1 red** — `TheDockedAssetBrowser_OffersTheRefactorRowCommands`; 17 pass |
| pass `schemaDiagnostics: null` in the registrar | only the schema-source composition rail, all 3 perspectives | ✅ **3 red** — `EveryPerspectivesDiagnosticsWindow_HasItsSchemaSource(btree|hsm|blueprint)`; 15 pass |

⛔ Both un-applied with the **inverse edit**, never `git checkout --`.

---

## 3. ⭐ Gates *(the rule-8 contract)*

| # | gate | command | result | Δ vs `5d1fd44d` |
|---|---|---|---|---|
| 1 | solution build | `dotnet build IOS-IG-SimHost.sln --no-restore` | ✅ **0 errors** | — |
| 2 | AiShared | `dotnet test … --no-build` | ✅ **1904 pass / 0 fail / 1 skip** | **+16** *(8+8 new rails)* |
| 3 | Blueprints | `dotnet test … --no-build` | ✅ **3908 pass / 0 fail / 18 skip** | **+2** *(2 composition-root rails)* |
| 4 | Hrot.Editor | `dotnet test … --no-build` | ✅ **214 / 0** | 0 |
| 5 | BTree.Editor | `dotnet test … --no-build` | ✅ **622 / 0** | 0 |
| 6 | Hsm.Editor | `dotnet test … --no-build` | ✅ **555 / 0** | 0 |
| 7 | Smoke | `dotnet test … --no-build` | ✅ **4 / 0** | 0 |
| 8 | Fdp.Presentation *(filtered — `BP-419`)* | `--filter "…~Windows\|…~Docking"` | ✅ **11 / 0** | 0 |
| 9 | StructEdit | `dotnet test … --no-build` | ⚠ **191 / 1** | ⛔ **PRE-EXISTING** |
| 10 | tracker | `tracker-counts.py --check` | ✅ open 91 / done 281 | table corrected |
| 11 | rulings | `rulings-check.py` | ✅ **22/22** | ⚠ 1 staleness WARN on `.claude/CLAUDE.md` *(pre-existing)* |
| 12 | design docs | `design-digest.py --check` | ⛔ **4 fail — none mine** | 0 |

**`--no-build` column:** gates 2–9 ran `--no-build` **after** gate 1 built the whole solution in the same
tree. ⚠ All are **in-solution**, so none reports a stale bin. *(The `NodeEditor.*` / `Fhsm.Tests`
out-of-solution hazard does not apply — this diff touches neither.)*

**Gate 9, confirmed pre-existing against the base commit.** `StructEdit.Tests.Reflection.
DocumentBuilderTests.Build_CircularReference_CircularFieldIsUnsupported` fails identically in a clean
worktree at `5d1fd44d`. This diff touches zero StructEdit files.

**Gate 12, all four pre-existing and in the coordinator's lane, not mine:**
`docs/DESIGN_Stride_Port.md` *(no INVENTORY)* · `docs/UX/UX_Feature_Curated_Scenarios.md` ·
`docs/MCP_Integration.md` · `docs/Editor_Headless_Xvfb.md` *(all `build-state: BUILT`, no UML)*.
⛔ Reported, not fixed.

**Row 8 of the contract — the integration suite.** ⚠ **Not applicable, stated rather than omitted:** this
diff is editor-UI only *(`Hrot.Editor.AiShared` + the composition root)*. It touches no clock, kernel
schedule, orchestrator, transport or cross-node code, so no cross-node invariant is in its blast radius.
`Hrot.ClusterRunner.Integration.Tests` was **not** run — and it remains **un-gateable** for the reason
Batch 101 filed *(the pre-existing DDS-allocator crash)*.

**Quarantine:** 1 skip in AiShared, 18 in Blueprints — **unchanged**; ⛔ no new skips.
**Working tree clean after every suite run** *(no golden regenerated)*. **No golden files moved.**

---

## 4. ⭐ Obligation ③ — **the design's diagrams vs what I built**

📄 §7.4's `classDiagram` and §7.5's `sequenceDiagram` describe the **node-properties** extraction (`S2`),
which this batch does not touch. ⭐ **`S2b` deviates from the design by ADDING a case it did not have** —
arms that leave the window without becoming views. ⇒ **§7.4a is new, written this batch**, and §7.4's
*"`S5` cannot delete it until the header and the strip have a home"* is marked **SUPERSEDED** in §7.7.

⚠ **The premise `BP-431` reasoned from was wrong, and the design corpus already said so** — §16.1 calls
Find References *"Used by the right-click menu"* and §5.7 puts collisions *"in the shared windows"*.
📌 `R-129`: I measured the code's shape and inferred a home; the intent was in two documents I had not
read until the user's ruling sent me to them.

---

## 5. ⚠ What this does **not** prove

⛔ That the menu appears on screen, or that a diagnostic row is legible. 📌 `R-21`/`R-62`: the draw is
unrailed by construction. ⭐ The rails prove the **model** — which host offers commands, that a command
reaches its asset, that rename previews and never applies, that the Diagnostics aggregate contains the
schema rows and does **not** when no source was supplied. **The pixels stay with the visual check.**

---

## 6. ⭐ Next

| | |
|---|---|
| **`S3`** | `details.utility` from `InspectorWindow`'s utility arm — ⚠ **ported honestly as the stub it is** *(§7.6 ③)* |
| **`S5`** | retire `InspectorWindow`, drop `ai_inspector_*` from the shipped default layout — ⭐ **blocked on `S3` alone now** |
| **`S4`** | `details.parametersync` — ⛔ deferred by design *(`R-99`, after the orchestrator wiring)* |
