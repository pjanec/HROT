<!--STATUS
state: LIVE
build-state: DISPATCH
updated: 2026-08-25
current-answer: dispatch pointer for cgf==editor SLICE 2 (CE-009) — CGF populates its AssetCatalog and
  gains the MCP surface to open an asset, list/switch graph tabs, focus a window, and read the
  focus→Details/toolbar consequence. Carries NO design: cites
  DESIGN_Cgf_Editor_Sharing_Slice2_Open_Asset.md (classDiagram + sequenceDiagram + §3a addressing).
known-conflict: none. CONSUMES Hrot.Editor.AiShared and must NOT modify it (freeze owner = variable-model
  lane). Slice 1 (CE-001..010) is merged; this continues Area L.
-->
# HANDOFF — **cgf==editor slice 2 (CE-009): CGF opens an asset + MCP drive/observe** *(CGF / backend lane)*

> 📌 **Dispatched at `2603adad9`.** ⛔ **Scope FROZEN at that sha.** ⭐ **Branch fresh from
> `claude/blueprint-authoring-status-6sr5ld`** *(rule 7)*; **rule 1b: push an empty started-marker naming
> `2603adad9` BEFORE any code.** ⛔ **No PR.** ⭐ **You allocate the ids** *(rule 3)* — continue the `CE-`
> series in tracker **Area L**; state every id *(rule 5)*.

## 0. ⛔⛔ THE DESIGN IS THE SOURCE — this file is a POINTER
📄 **[`DESIGN_Cgf_Editor_Sharing_Slice2_Open_Asset.md`](../../DESIGN_Cgf_Editor_Sharing_Slice2_Open_Asset.md)**
*(READY-TO-BUILD)* — **§2** inventory, **§3** why the MCP surface is in-slice, **§3a** asset addressing &
folders, **§4** classDiagram, **§5** sequenceDiagram, **§6** the items, **§7** the toolbar reminder, **§8**
the recorded FUTURE, **§9** gates. ⭐ Build what §4/§5 draw; report the match *(obligation ③)*; fold
deviations back into the design *(obligation ⑤)*.
📄 Context: slice 1 as-built *(`DESIGN_Cgf_Editor_Sharing_Slice1_Shell_Adoption.md` §9/§10 — the shell is
built; its open rows CE-009/010/011 all root in "CGF cannot open an asset", which THIS slice fixes)* ·
`Architect_Question_54` + `R-133` *(manifest MEASURED; new routes carry a `RouteDoc`)* · HN-030 *(SKILL.md
+ catalog are GENERATED from routes)*.

## 1. ⛔⛔ NEW BUILD/TEST RULES APPLY
`.claude/CLAUDE.md` → THREE TEST TIERS → the `2026-08-24` rule. ⛔⛔ **Never `dotnet build <the.sln>` in the
fix loop** — build the AFFECTED PROJECT *(`Hrot.CGF` · `Hrot.Editor` DebugApi · `Hrot.SystemTests`)*.
⛔ **The E2E/system suite is T3 — ASYNC, never a foreground blocker.** ⭐ Prove each fix through the rail
that reddens for it; ⛔ do NOT re-run the whole suite "to be sure."

## 2. ⭐⭐⭐ WHAT TO BUILD *(design §6)*
| # | task | the one thing not to get wrong |
|---|---|---|
| ⭐ **①** | **Populate CGF's `AssetCatalog`** — construct `AiAssetCatalogBuilder` with the cluster-appropriate contributors; replace the bare `new AssetCatalog()` | ⭐⭐ **index RECURSIVELY across subfolders** *(§3a — `SourceFilePath` keeps the relative folder path)*; ⛔ **not a single flat folder**; ⚠ deployed-node asset roots = ruling 67, report if it bites, ⛔ no silent-fail |
| ⭐ **②** | **Discover + open** *(§3a)*: `GET /assets` *(list `{assetId, name, kind, sourceFilePath}`)* · `POST /assets/{assetId}/open` *(Guid, URL-safe)* · `POST /assets/open {path}` *(relative path in the BODY → new `AssetCatalog.FindBySourceFilePath`)* → `AiDocumentManager.Open` | ⛔ **never a raw path in a URL segment**; ⛔ `Name` is NOT the address *(collides across subfolders)*; ⭐ each route carries a `RouteDoc`; bad id/path ⇒ typed hint, not a 500 |
| ⭐ **③** | **List + switch graph tabs** — `GET /documents` *(from `OpenDocuments`/`Active`)* · `POST /documents/{assetId}/activate` *(→ `Activate`)* | ⭐ the tab model EXISTS in `AiDocumentManager` — **expose it, ⛔ do not reimplement tabs** |
| ⭐ **④** | **Focus a window/panel** — `POST /panels/{panelId}/focus` → WindowManager focus/open | ⭐ RouteDoc; unknown panel ⇒ safe no-op with a hint |
| ⭐ **⑤** | **Instrument the main TOOLBAR as a readable `PanelKind`** *(a `ToolbarPanelViewModel` → `PanelSnapshot`)* | ⭐⭐ so focus→toolbar is observable *(the user's ask)*; ⛔ don't gate — publish what is there. 📌 This is the groundwork for §7 |
| ⭐ **⑥** | **Conformance: a POPULATED-asset case** — open X of kind K on `--mode all`, activate its tab, read graph + MyBlueprint + Details + toolbar, assert **SAME as the editor** | ⚠ model-level; ⛔ compare to the EDITOR golden, not host-to-host only *(slice 1 §3's caution)* |

## 3. ⭐⭐ HOW TO TEST *(design §9 + slice-1's method)*
✅ **The conformance suite is your acceptance vehicle** *(`Hrot.SystemTests/Conformance/ClusterConformanceRails.cs`,
landed; `--mode all` answers MCP via `PerspectiveScopedDispatcher`; `GET /panels` reads `PanelSnapshot`)*.
| step | do |
|---|---|
| **T0** | confirm the conformance suite is GREEN at `2603adad9` *(baseline)* — name the result |
| **T1** | capture editor-mode goldens for a **POPULATED** asset *(open X, activate, dump graph + MyBlueprint + Details + toolbar)* |
| **T2** | build §2 |
| **T3** *(acceptance)* | conformance **`SAME` per `PanelKind`** for the populated panels, editor vs `--mode all` — ⛔ **not empty state** |
| **T4** | `gen:catalog:check` / `gen:skill:check` green — the new routes' `RouteDoc`s regenerate the catalog + SKILL.md |

⭐⭐⭐ **YOU MAY EXTEND THE HARNESS / MCP** *(user, standing)* — if a panel is unreachable, a `PanelKind`
uncaptured, a route missing, or `--mode all` does not expose what a test needs, **build it** *(route +
`RouteDoc`, the `ClusterConformanceRails` case, the golden, the `PanelSnapshot` registration)*. ⛔ **Do NOT
fake a pass by narrowing the diff**; ⚠ if an extension would touch **AiShared internals**, STOP and
coordinate with the variable-model lane *(§4)*.

## 4. ⭐ LANE, SCOPE & COLLISION
⭐ **Yours (CGF/backend lane):** `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs` · `Hrot/Subsystems/Hrot.Editor/DebugApi/**`
*(new routes + `RouteDoc`s + the DTO shapes)* · `AssetCatalog` *(the new `FindBySourceFilePath` — ⚠ it lives
in AiShared; see below)* · the toolbar `PanelKind` · `Hrot.SystemTests/**`.
⛔⛔ **`FindBySourceFilePath` and the toolbar `PanelKind` are in `Hrot.Editor.AiShared`** — that is the
freeze owner's assembly. ⭐ Adding a **pure lookup method** and a **new read-only ViewModel** is additive and
low-risk, ⚠ **but it IS an AiShared change** ⇒ **coordinate with the variable-model lane before landing**
*(a quick nod; it is not a modification of existing behaviour)*. ⛔ If it turns into more than additive,
STOP and report.
⛔ **Not this slice:** asset EDITING / hot-reload writes *(CE-011 — becomes reachable AFTER this)* · live
variable-VALUE write *(R-52)* · map/Axis B · **MCP authoring** *(design §8 FUTURE — `CE-FUTURE-authoring`)*.
⭐ **Rule 4:** re-pull coordinator before your final commit.

## 5. GATES *(rule 8 contract)*
One row per gate · verbatim command · pass/fail/skip · **delta vs `2603adad9`** · `--no-build` column ·
every RED pre-existing **by name** *(prove by `git diff`, not rebuild)* · goldens as a diff shape ·
`tracker-counts.py --check` · `rulings-check.py` · `design-digest.py --check` · **the `CE-` ids allocated**.
⭐⭐ **Row 8 — the rails:**
- ✅ **the headline:** a conformance case that OPENS a real asset on `--mode all`, activates its tab, and
  asserts graph + MyBlueprint + Details are **SAME as the editor** *(not empty)* — RED by reverting the
  catalog population.
- a rail: `GET /assets` lists a subfoldered asset with its `sourceFilePath`; `POST /assets/open {path}`
  opens it; `GET /documents` shows the tab; `POST /documents/{id}/activate` changes `Active`.
- a rail: the toolbar `PanelKind` publishes and is readable via `GET /panels`, SAME on both hosts.
- a rail: `gen:catalog:check` / `gen:skill:check` green.
- ⛔⛔ name + run the **conformance suite** as the integration gate.

## 6. ⭐ WHEN DONE
⭐⭐ **Fold the as-built into
[`DESIGN_Cgf_Editor_Sharing_Slice2_Open_Asset.md`](../../DESIGN_Cgf_Editor_Sharing_Slice2_Open_Asset.md)**
*(the contributors chosen · the routes + DTOs as built · the toolbar `PanelKind` · whether `CE-011` reload
is now reachable)*; flip the gap-map Axis-A rows from "empty shell" to "populated"; close `CE-009`. ⭐ State
the `CE-` ids; ⛔ design content in the design, the report points at it. ⭐ Report per obligation ③:
*"§4 carries N classes, §5 M sequences; built matches / deviates HERE."*
