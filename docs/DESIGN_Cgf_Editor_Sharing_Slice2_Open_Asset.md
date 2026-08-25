<!--STATUS
state: LIVE
build-state: READY-TO-BUILD — carries classDiagram + sequenceDiagram (§4/§5). Slice 2 of cgf==editor
  (CE-009): CGF POPULATES its AssetCatalog and gains the MCP surface to OPEN an asset, LIST/SWITCH graph
  tabs, FOCUS a window, and READ the focus→Details/toolbar consequence — turning slice 1's empty shell
  into a populated, drivable, observable one.
updated: 2026-08-25
current-answer: the whole file.
design-basis: DESIGN_Cgf_Editor_Sharing_Slice1_Shell_Adoption.md §9 (the shell is built; §6's open rows
  CE-009/010/011 all root in "CGF cannot open an asset") · PROGRAMME_Unification_And_Harness.md (charter) ·
  Architect_Question_54 + R-133 (manifest is MEASURED; new routes carry a RouteDoc) ·
  MCP_Integration.md / HN-030 (routes self-document; SKILL.md is generated) · DESIGN_Regression_Net.md +
  DESIGN_Headless_Testability.md (conformance by PanelKind).
known-conflict: none. CONSUMES Hrot.Editor.AiShared; must NOT modify it (freeze owner = variable-model lane).
-->
# DESIGN — **cgf==editor slice 2 (CE-009): CGF opens an asset + the MCP drive/observe surface**

> 🎯 Slice 1 built the shell but the windows show their EMPTY state — **CGF cannot open an asset**
> *(empty `AssetCatalog`; no MCP route opens an AI asset)*. This slice **populates the catalog** and adds the
> **MCP surface to open an asset, switch graph tabs, focus a window, and read what the focus shows** — so a
> populated blueprint/BTree/HSM graph is visible on CGF and provable headlessly.

## 1. ⭐ SCOPE

| ✅ IN | ⛔ NOT (recorded below) |
|---|---|
| **Populate CGF's `AssetCatalog`** via an `AiAssetCatalogBuilder` with the cluster-appropriate contributors *(index the BTree/HSM/Blueprint assets)* | ⛔ **Asset EDITING / hot-reload writes** *(CE-011; needs the reload pipeline — §7)* |
| **MCP: discover + open an AI asset** — `GET /assets` · `POST /assets/{assetId}/open` *(Guid)* · `POST /assets/open {path}` *(relative folder path, §3a; MX-013)* | ⛔ **Live variable-VALUE write** *(R-52; variable-model lane)* |
| **MCP: list + switch graph tabs** — `GET /documents`, `POST /documents/{assetId}/activate` *(the tab model already exists — §3)* | ⛔ **Map / entity parity** *(Axis B)* |
| **MCP: focus a concrete window/panel** — `POST /panels/{panelId}/focus` | ⛔ **MCP AUTHORING** *(create assets, add/wire nodes — §8 FUTURE, recorded)* |
| **Instrument the main TOOLBAR as a readable `PanelKind`** so focus→toolbar is observable | |
| **Prove SAME on a POPULATED asset**: open X, activate its tab, read graph + MyBlueprint + Details + toolbar, assert editor==cluster | |

## 2. ⭐⭐ INVENTORY — measured `2026-08-25`

| exists? | thing | where | note |
|---|---|:--:|---|
| ✅ | `AiAssetCatalogBuilder` *(contributors → catalog, `RefreshFromAssembly`)* | `EditorSubsystem.cs:1053` | ⭐ editor builds it; **CGF constructs a bare `new AssetCatalog()`** *(`:2571` fallback)* ⇒ empty |
| ✅ | `AssetCatalog.FindByAssetId(Guid) : IEditableAsset?` | `AiShared/Catalog/AssetCatalog.cs:20` | the open lookup |
| ✅✅ | **the TAB model** — `AiDocumentManager.Open(IEditableAsset)` · `Activate(doc)` · `OpenDocuments` · `Active` · `ActiveChanged` | `AiShared/Documents/AiDocumentManager.cs:74-228` | ⭐⭐ **already exists** — slice 1 constructed it; ⛔ **no MCP route drives it** |
| ✅ | MCP today: `GET /panels` · `GET /panels/{id}` · `GET /perspectives` · `POST /perspective` · `POST /entities/{netId}/focus` | `DebugApi/*` | ⛔ **no open-asset, no list/switch-documents, no window-focus route** |
| 🔴 | the **main toolbar** as a `PanelKind` | — | ⛔ **NOT instrumented** — only breakpoint-panel mirrors exist. ⇒ MCP cannot read which buttons a focus shows |
| ✅ | `RouteDoc` / `DebugApiRouteDocs` | `DebugApi/DebugApiRouteDocs.cs` | ⭐ **every new route carries one** ⇒ manifest + SKILL.md auto-generate *(HN-030, R-133)* |

⇒ ⭐⭐ **The tab machinery and the catalog machinery both EXIST** — this slice **populates** the catalog on
CGF and **exposes** the open/switch/focus operations over MCP; the only genuinely new UI code is the toolbar
`PanelKind`.

## 3. ⭐⭐ WHY THE MCP DRIVE/OBSERVE SURFACE IS IN THIS SLICE *(user, `2026-08-25`)*

⛔ You cannot **verify** a populated asset on CGF headlessly without being able to **open** it, **focus** the
window, **switch to the right graph tab**, and **read the resulting panels**. And ⭐ different graph kinds
legitimately drive **different Details content and different toolbar buttons** — so the focus→consequence
must be observable now, even if the consequences are small today. ⇒ open · list/switch tabs · focus ·
read-toolbar are **part of this slice**, not a later nicety.

## 3a. ⭐⭐⭐ ASSET ADDRESSING & FOLDERS *(user, `2026-08-25`)*

📐 **Measured identity model** *(`IEditableAsset`)*: `Guid AssetId` *(the catalog key — `AssetCatalog._byId`
is `Dictionary<Guid,…>`)* · `string Name` · `AssetKind Kind` · **`string SourceFilePath`** · `IsDirty`.

| the concern | ⭐ the resolution |
|---|---|
| ⚠ *"isn't the id a file path, unsafe in a URL?"* | ⛔ **No — the id is a `Guid`** ⇒ `POST /assets/{assetId}/open` **is** URL-safe as written. But a Guid is **not discoverable or memorable**, which is the real problem |
| ⭐⭐ **assets MUST organize into SUBFOLDERS** *(`blueprint/subfolder/blueprint1.bp.json`)* | ⭐ that structure already lives in **`SourceFilePath`**. ⛔ **The catalog contributor MUST index RECURSIVELY** across subfolders *(not one flat folder)*, and `SourceFilePath` preserves the **relative folder path** |
| ⭐ **address by the human PATH, not just the Guid** | ⛔ a raw path in a URL SEGMENT is unsafe *(slashes/dots)* ⇒ ⭐⭐ **two safe forms:** ① `GET /assets` **discovery** returns every asset's `{assetId, name, kind, sourceFilePath}` so a client resolves path→Guid itself; ② `POST /assets/open { path: "blueprint/subfolder/blueprint1.bp.json" }` — the path travels in the **BODY** *(no URL-encoding)*, resolved via a new `AssetCatalog.FindBySourceFilePath`. Open-by-Guid stays the URL-segment form |
| ⚠ `FindByName` is ambiguous across subfolders | ⛔ two folders may hold `blueprint1.bp.json` ⇒ **`Name` cannot be the address; the relative `SourceFilePath` is the human key**, the `Guid` is the stable one |

⇒ ⭐⭐ **Three ways in, all safe:** the `Guid` *(stable, URL-segment)* · the relative `SourceFilePath`
*(human, in the body)* · discovery via `GET /assets`. ⭐ The `RouteDoc`s document all three.

## 4. ⭐⭐⭐ CLASS DIAGRAM

```mermaid
classDiagram
    direction LR
    class CgfSubsystem {
        <<exists · slice 1 built BuildAiShell · now builds the catalog builder>>
        +BuildAssetCatalog()
    }
    class AiAssetCatalogBuilder {
        <<exists · AiShared · contributors to catalog>>
    }
    class AssetCatalog {
        <<exists · AiShared · gains a path resolver>>
        +FindByAssetId(guid) IEditableAsset
        +FindBySourceFilePath(path) IEditableAsset
        +All IReadOnlyList
    }
    class AiDocumentManager {
        <<exists · AiShared · the TAB model — no code change>>
        +Open(asset) AiDocument
        +Activate(doc) void
        +OpenDocuments IReadOnlyList
        +Active AiDocument
    }
    class DebugApiService {
        <<exists · gains routes · each carries a RouteDoc>>
        +ListAssets()
        +OpenAssetById(assetId)
        +OpenAssetByPath(path)
        +ListDocuments()
        +ActivateDocument(assetId)
        +FocusPanel(panelId)
    }
    class ToolbarPanelViewModel {
        <<NEW · AiShared · makes the toolbar a readable PanelKind>>
    }
    class PanelSnapshot {
        <<exists · GET /panels reads it>>
    }
    CgfSubsystem ..> AiAssetCatalogBuilder : constructs with cluster contributors
    AiAssetCatalogBuilder ..> AssetCatalog : populates
    DebugApiService ..> AssetCatalog : FindByAssetId
    DebugApiService ..> AiDocumentManager : Open / Activate / list
    DebugApiService ..> PanelSnapshot : FocusPanel + read
    ToolbarPanelViewModel ..> PanelSnapshot : Register (new instrumentation)
    note for DebugApiService "new routes: GET /assets · POST /assets/{id}/open (Guid) · POST /assets/open {path} · GET /documents · POST /documents/{id}/activate · POST /panels/{id}/focus"
```

## 5. ⭐⭐⭐ SEQUENCE DIAGRAM

```mermaid
sequenceDiagram
    autonumber
    participant M as MCP client
    participant Api as DebugApiService
    participant Cat as AssetCatalog
    participant Doc as AiDocumentManager
    participant WM as WindowManager
    participant Snap as PanelSnapshot

    M->>Api: POST /assets/{assetId}/open
    Api->>Cat: FindByAssetId
    Api->>Doc: Open the asset
    Note over Doc: graph canvas and MyBlueprint now render the real asset, not empty state
    M->>Api: GET /documents
    Api->>Doc: OpenDocuments + Active
    Api-->>M: tabs with kind and assetId
    M->>Api: POST /documents/{assetId}/activate
    Api->>Doc: Activate
    Note over Doc: ActiveChanged fires, Details and toolbar re-publish for the focused kind
    M->>Api: POST /panels/{panelId}/focus
    Api->>WM: focus and open the window
    M->>Api: GET /panels
    Api->>Snap: read graph, MyBlueprint, Details, toolbar view-models
    Api-->>M: the focused panels — conformance asserts editor equals cluster
```

## 6. ⭐⭐ THE ITEMS
| # | task | the one thing not to get wrong |
|---|---|---|
| ⭐ **①** | **Populate the catalog** — construct `AiAssetCatalogBuilder` on CGF with the cluster-appropriate contributors; replace the bare `new AssetCatalog()`. ⭐⭐ **Index RECURSIVELY across subfolders** *(§3a)*; `SourceFilePath` preserves the relative folder path | ⛔ index only what CGF legitimately hosts; ⛔ **not a single flat folder** — subfolders are required; ⚠ asset-root resolution on a DEPLOYED node is ruling 67 — report if it bites, do not silent-fail |
| ⭐ **②** | **Open + discover** *(§3a)*: `GET /assets` *(list `{assetId, name, kind, sourceFilePath}`)* · `POST /assets/{assetId}/open` *(Guid, URL-safe)* · `POST /assets/open {path}` *(relative path in the BODY → new `AssetCatalog.FindBySourceFilePath`)* → `AiDocumentManager.Open` | ⭐ each carries a `RouteDoc`; ⛔ a bad/absent id or path returns a typed hint, not a 500; ⛔ **never put a raw path in a URL segment** |
| ⭐ **③** | **`GET /documents` + `POST /documents/{assetId}/activate`** over `OpenDocuments`/`Active`/`Activate` | ⭐ the model EXISTS — expose it, ⛔ don't reimplement tabs |
| ⭐ **④** | **`POST /panels/{panelId}/focus`** → WindowManager focus/open | ⭐ RouteDoc; make it a no-op-safe on an unknown panel with a hint |
| ⭐ **⑤** | **Instrument the main toolbar as a `PanelKind`** *(a `ToolbarPanelViewModel` registered to `PanelSnapshot`)* | ⭐⭐ this is the observability the user asked for — buttons readable per focus; ⛔ don't gate, just publish what is there |
| ⭐ **⑥** | **Conformance: a POPULATED-asset case** — open X of kind K, activate, read graph + MyBlueprint + Details + toolbar, assert **SAME** editor-vs-`--mode all` | ⚠ still model-level; ⛔ compare to the EDITOR golden, not host-to-host only *(slice 1 §3's caution — both regress identically otherwise)* |

## 7. ⚠ REMINDER FOR ALL LATER FEATURE SLICES — **the main toolbar is a first-class surface** *(user, `2026-08-25`)*
⛔⛔ **When a later slice adds a feature CONTROLLED FROM THE TOOLBAR** *(hot reload, save, run/stop, …)*, its
**toolbar button must be wired AND instrumented on CGF too** — not just the underlying command. 📌 This slice
makes the toolbar a readable `PanelKind` precisely so that *"the button exists on CGF"* is assertable when
those features land. ⭐ Every feature slice's acceptance must include *"its toolbar affordance is present and
SAME on CGF."*

## 8. ⛔ FUTURE — **recorded, NOT this slice** *(user, `2026-08-25`)*
🔮 **MCP AUTHORING — the API gains the ability to AUTHOR, not just observe:** create a new asset, and
**add/connect wiring nodes to a graph** over MCP *(and by extension edit params, delete, save)*. ⭐ This is a
large future capability — it turns MCP from a read/drive surface into an authoring surface, and it will need
its own design *(and it interacts with the editing/write-path work — hot reload, R-52, ruling 67)*. ⛔ **Do
NOT build it here.** 📌 Filed so it is not forgotten; tackle when the read/drive surface and editing slices
have settled. *(Recorded as `CE-FUTURE-authoring`; also noted in the gap map.)*

## 9. GATES *(rule 8 + the build/test rules)*
⭐ Standing contract; affected-project builds *(`Hrot.CGF` + `Hrot.Editor` DebugApi + the SystemTests)*;
T3 async. ⭐⭐ **Row 8 — the rails:**
- ✅ **the headline:** a conformance case that OPENS a real asset on `--mode all`, activates its tab, and
  asserts graph + MyBlueprint + Details are **SAME as the editor** *(not empty state)* — shown RED by
  reverting the catalog population.
- a rail: `GET /documents` lists the opened tab; `POST /documents/{id}/activate` changes `Active`.
- a rail: the toolbar `PanelKind` publishes and is readable via `GET /panels`, SAME on both hosts.
- a rail: `gen:catalog:check` / `gen:skill:check` stay green — the new routes' `RouteDoc`s regenerate the
  catalog + SKILL.md *(HN-030)*.
- ⛔⛔ name + run the **conformance suite** *(`ClusterConformanceRails`)* as the integration gate.

## 10. ⭐ WHEN DONE
Fold the as-built into this file *(the catalog contributors chosen · the routes as built · the toolbar
`PanelKind` · the populated-asset conformance case)*; flip the gap-map Axis-A rows from "empty shell" to
"populated"; close `CE-009` and note whether `CE-011` (reload) is now reachable. State the `CE-` ids;
the report points here.
