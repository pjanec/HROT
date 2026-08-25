<!--STATUS
state: LIVE
build-state: BUILT (2026-08-25, backend/CGF lane, ids CE-019..CE-024). Carries classDiagram +
  sequenceDiagram (§4/§5). Slice 3 of cgf==editor (CE-011): editing + hot reload on CGF. Take the windows'
  native editing WHOLESALE (per the 2026-08-25 steer); wire the reload pipeline + save path; add a MINIMAL
  MCP save/reload trigger so it is testable headlessly.
updated: 2026-08-25
current-answer: §4/§5 (the diagrams, TRUE as built) + §10 (AS-BUILT — what landed, and the two premises
  that MEASURED FALSE). Read §10 before quoting §1/§6's "Soft keeps state, Hard resets" acceptance or
  §6 item ③'s Hard-reload confirm: neither is observable through the path this slice wires.
known-rot: §1, §2 and §6 present the Cosmetic/Soft/Hard classification as something this slice's reload
  produces. MEASURED FALSE (§10.3): QuickReloadResult carries only Succeeded/ErrorMessage/DurationMs, and
  AiHotReloadCoordinator.OnHardReloadCompleted is documented "NOT fired for Quick Reloads" — the
  classification belongs to the ALC file-watcher path, which this slice does not wire (CE-023).
  §6 item ③ says "route the Hard-reload confirm to the interactive node". Ruling 53 says the OPPOSITE for
  a headless origin: it never pre-flights, and the origin-side LOG is the safety net (§10.4).
design-basis: DESIGN_Cgf_Editor_Sharing_Slice2_Open_Asset.md §11 (CE-009 gave CGF OPEN documents — the
  reload trigger's missing precondition — so editing is now reachable) · STEER_Cgf_Shell_Adoption_Slice1.md
  (take editing wholesale; keep the live value-write OFF) · AI_Editor_Shared_Infrastructure.md §17
  (Cosmetic/Soft/Hard classification) · ruling 67 / AssetRoots (write roots) · ruling 53 (Hard-reload
  confirm resolves at the interactive node).
known-conflict: ⚠ SHARES the DebugApi surface with the PARALLEL MCP-authoring track (§8). Partitioned by
  route file; the coordinator serializes the generated catalog/SKILL.md. CONSUMES Hrot.Editor.AiShared;
  must NOT modify it beyond additive wiring — coordinate with the variable-model lane.
-->
# DESIGN — **cgf==editor slice 3 (CE-011): editing + hot reload on CGF**

> 🎯 CE-009 made CGF **open** assets; this makes CGF **edit** them. Take the windows' native editing
> **wholesale** *(the steer — no artificial gating)*, wire the **reload pipeline** so a saved edit
> hot-applies to the running brain, and add a **minimal MCP save/reload trigger** so it is provable
> headlessly. ⛔ Rich MCP AUTHORING *(create assets, add/wire nodes)* is the **separate parallel track** — §8.

## 1. ⭐ SCOPE
| ✅ IN | ⛔ NOT |
|---|---|
| **Take the windows' native editing wholesale** on CGF *(no gating — the steer)* | ⛔ **Rich MCP AUTHORING** *(create asset, add/wire nodes, edit params over MCP)* — §8, the parallel track |
| **Wire the reload pipeline** — `QuickReloadService` + the per-host quick-reload triggers + `ApplyQuickReload` — so a saved edit **hot-applies** *(Soft = patch, Hard = generation bump + reset, §17)* | ⛔ **Live variable-VALUE write** *(R-52 staged blackboard write — variable-model lane, a DIFFERENT path from asset save→reload)* |
| **Wire the save path** — `SaveAllAiDocumentsCommand`/`SaveDelegate` + asset→path via `AssetRoots` *(ruling 67)* | ⛔ **Map / Axis B** |
| **The main-toolbar hot-reload/save button on CGF** *(per §7 of the slice-2 design — a toolbar-controlled feature)* | ⛔ Modifying `Hrot.Editor.AiShared` internals *(additive wiring only)* |
| **A MINIMAL MCP trigger** — `POST /assets/{assetId}/save` · `POST /assets/{assetId}/reload` — so editing is testable headlessly | |
| **Hard-reload confirm routes to the interactive node** *(ruling 53 — a Hard reload on a live cluster is a confirmed cluster-wide reset)* | |

## 2. ⭐⭐ INVENTORY — measured `2026-08-25`
| exists? | thing | where | note |
|---|---|:--:|---|
| ✅ | `QuickReloadService` *(compiles from the in-memory asset)* + `_aiCoordinator.ApplyQuickReload` *(commits into the registry)* | `EditorSubsystem.cs:351,444,3242` | ⭐ editor wires per-host **triggers** `_blueprintQuickReloadTrigger`/`_btreeQuickReloadTrigger`/`_hsmQuickReloadTrigger` *(`:344-349`)*; ⛔ **null on CGF** |
| ✅ | the reload TRIGGER precondition — **a dirty OPEN document** | — | ⭐⭐ **now met** — CE-009 gave CGF open documents *(the exact thing slice-1's CE-011 note said was missing)* |
| ✅ | save — `SaveAllAiDocumentsCommand` + `SaveDelegate(asset, path)` · `AiDocument.IsDirty` · `AppExitPromptController` | `AiShared/Documents/*` | ⭐ wire the `SaveDelegate` on CGF |
| ✅ | write roots — `AssetRoots` *(Assets/ save destination · Recipes/ sources)* | `AiShared/Identity/AssetRoots.cs` | ⭐ added in CE-009; ruling 67 partly done — ⚠ confirm deployed-node resolution |
| ✅ | hot-reload classification Cosmetic/Soft/Hard | `AI_Editor_Shared_Infrastructure.md` §17 · `BTreeHotReloadManager`/`HsmHotReloadManager` | ⭐ Soft = patch *(state kept)*; Hard = generation bump *(state reset, R-24 — intended, confirmed)* |
| ✅ | MCP today: `/assets/*`, `/documents/*`, `/panels/*` *(CE-009)* | `DebugApi/DebugApiService.Assets.cs` | ⭐ **add save/reload beside them; each carries a `RouteDoc`** |

⇒ ⭐⭐ **The reload + save machinery EXISTS and is per-host-triggered; CGF just does not wire the triggers.**
This is wiring + a minimal MCP trigger, not new capability. ⚠ **The asset save→reload path is DISTINCT from
the live value-write (R-52)** — editing a param changes the ASSET FILE and hot-reloads; it does NOT go
through the staged `Blackboard1024` write. That is what keeps R-52 out of this slice.

## 3. ⭐⭐ EDITING IS WHOLESALE, BUT TWO WRITE PATHS STAY DISTINCT
| path | this slice? | why |
|---|---|---|
| ⭐ **asset/graph authoring** *(edit nodes/params → save file → hot reload)* | ✅ **wholesale** | the steer; the reload pipeline is its runtime effect |
| 🔴 **live variable-VALUE edit** *(watch/Details → staged `Blackboard1024` write)* | ⛔ **OFF** | R-52 clobber; variable-model lane's frozen path |

## 4. ⭐⭐⭐ CLASS DIAGRAM
```mermaid
classDiagram
    direction LR
    class CgfSubsystem {
        <<exists · wires the reload triggers + save delegate>>
        +WireReloadPipeline()
    }
    class QuickReloadService {
        <<exists · compiles from the in-memory asset>>
        +TriggerAsync(asset) Task
    }
    class AiReloadCoordinator {
        <<exists · ApplyQuickReload commits into the registry>>
        +ApplyQuickReload(result) void
    }
    class SaveAllAiDocumentsCommand {
        <<exists · SaveDelegate(asset, path)>>
    }
    class AssetRoots {
        <<exists · CE-009 · asset to path>>
    }
    class HotReloadClassifier {
        <<exists · Cosmetic / Soft / Hard · §17>>
    }
    class DebugApiService {
        <<exists · gains save+reload routes · each a RouteDoc>>
        +SaveAsset(assetId)
        +ReloadAsset(assetId)
    }
    class MainToolbar {
        <<exists · gains the hot-reload/save button on CGF>>
    }
    CgfSubsystem ..> QuickReloadService : constructs + wires the per-host triggers
    CgfSubsystem ..> SaveAllAiDocumentsCommand : wires the SaveDelegate
    SaveAllAiDocumentsCommand ..> AssetRoots : asset to path
    QuickReloadService ..> AiReloadCoordinator : ApplyQuickReload
    QuickReloadService ..> HotReloadClassifier : Cosmetic/Soft/Hard
    DebugApiService ..> QuickReloadService : reload trigger (headless test hook)
    DebugApiService ..> SaveAllAiDocumentsCommand : save
    MainToolbar ..> QuickReloadService : hot-reload button
    note for DebugApiService "new routes: POST /assets/{id}/save · POST /assets/{id}/reload — the parallel authoring track adds its OWN route file (§8)"
```

## 5. ⭐⭐⭐ SEQUENCE DIAGRAM
```mermaid
sequenceDiagram
    autonumber
    participant U as operator or MCP
    participant Win as graph window
    participant Save as SaveAllAiDocumentsCommand
    participant QR as QuickReloadService
    participant Co as AiReloadCoordinator
    participant Brain as running brain on CGF

    U->>Win: edit a node or param (wholesale — no gating)
    Note over Win: AiDocument.IsDirty is true
    U->>Save: save (toolbar button, or POST /assets/{id}/save)
    Save->>Save: write the asset file via AssetRoots
    U->>QR: reload (toolbar, file-watcher, or POST /assets/{id}/reload)
    QR->>QR: compile from the in-memory asset, classify Cosmetic Soft or Hard
    QR->>Co: ApplyQuickReload
    Note over Co,Brain: Soft patches lookup tables, state kept. Hard bumps generation, instances reset (confirmed at the interactive node, ruling 53)
    Co-->>Brain: the running brain reflects the edit
```

## 6. ⭐⭐ THE ITEMS
| # | task | the one thing not to get wrong |
|---|---|---|
| ⭐ **①** | **Wire `QuickReloadService` + the three per-host triggers + `ApplyQuickReload`** on CGF, mirroring the editor `:344-3266` | ⛔ don't gate the window editing; ⭐ Soft keeps state, Hard resets *(intended, §17)* |
| ⭐ **②** | **Wire the `SaveDelegate`** + asset→path via `AssetRoots` | ⚠ deployed-node roots = ruling 67; report if it bites, ⛔ no silent save-to-nowhere |
| ⭐ **③** | **Hard-reload confirm at the interactive node** *(ruling 53)* — a Hard reload on a live cluster is a confirmed cluster-wide reset | ⛔ never pop a modal on a headless CGF; the confirm resolves where the operator sits |
| ⭐ **④** | **Main-toolbar hot-reload/save button on CGF**, and it publishes to the toolbar `PanelKind` *(CE-009's §7 rule)* | ⭐ assert the affordance is present + SAME on CGF |
| ⭐ **⑤** | **Minimal MCP trigger** — `POST /assets/{id}/save` · `POST /assets/{id}/reload`, each a `RouteDoc` | ⛔ **keep to save/reload — do NOT add node-authoring routes here** *(that is §8's track — collision boundary)* |

## 7. GATES
⭐ rule 8 + build/test rules. **Row 8 — the rails:**
- ✅ **the headline:** edit a param on an open asset *(Soft)* → save → reload → assert the running brain reflects it AND retains state; then a topology edit *(Hard)* → reset + confirmed. Shown RED by reverting the trigger wiring.
- a rail: `POST /assets/{id}/save` persists *(file changes)*; `POST /assets/{id}/reload` hot-applies.
- a rail: the toolbar hot-reload button publishes + is SAME on both hosts.
- a rail: **the live value-write path is still OFF** *(R-52 not reachable via this slice)*.
- ⛔⛔ conformance suite as the integration gate; `gen:catalog:check`/`gen:skill:check` green for the two new routes.

## 8. ⭐⭐⭐ THE PARALLEL MCP-AUTHORING TRACK — **planned so it does NOT collide** *(user, `2026-08-25`)*
🔮 **A SEPARATE session extends the MCP server with SCENARIO AUTHORING incl. AI-asset authoring** *(create
an asset, add/connect wiring nodes, edit params, delete — over MCP)*. 📄 Its design is
**[`Architect_Question_56_Mcp_Authoring_Surface.md`](blueprints/Architect_Question_56_Mcp_Authoring_Surface.md)**
*(to be resolved WITH the user — the discussion)*. ⭐ **The collision plan:**

| shared surface | ⭐ the rule |
|---|---|
| **DebugApi route FILES** | CE-011 owns `DebugApiService.Assets.cs` *(save/reload)*; the authoring track adds its **own** `DebugApiService.Authoring.cs`. ⛔ neither edits the other's |
| **`DebugApiHost` registration · `DebugApiRouteDocs` · `tool-catalog.mjs` · `SKILL.md` · `CapabilityManifest`** | ⚠ **shared, collision-prone** ⇒ ⭐⭐ **the coordinator SERIALIZES these** — the authoring track branches from a coordinator state that already contains CE-011, so it adds on top of CE-011's route registration rather than concurrently. ⛔ **Do NOT run both editing the generated catalog from the same base** |
| **lane** | CE-011 = CGF/backend lane; authoring = its own session on a lane that branches AFTER CE-011 merges *(or a clearly disjoint file set + coordinator-owned regen)* |

⇒ ⭐ **Sequencing recommendation:** dispatch CE-011 now; **design the authoring track (AQ56) WITH the user while CE-011 runs**; dispatch authoring from a base that includes CE-011. ⛔ CE-011 stays strictly save/reload on the MCP side so the boundary is clean.

## 9. ⭐ WHEN DONE
Fold the as-built into this file; flip the gap-map Axis-A "editing" rows; close `CE-011`; report whether the
deployed-node asset-root *(ruling 67)* bit. State the `CE-` ids; the report points here.

## 10. ⭐⭐⭐ AS-BUILT *(`2026-08-25`, backend/CGF lane — obligation ⑤)*

### 10.0 ⭐ Obligation ③ — **the diagrams vs what was built**

> §4 carries **8 classes**, §5 carries **1 sequence**. ⭐ **Every class is constructed** and the sequence
> runs in the drawn order. ⚠ **TWO of the sequence's claims are not observable through the wired path**
> — §10.3 and §10.4 — and both are folded in as `known-rot` rather than left to be quoted.

| §4 box | as built |
|---|---|
| `CgfSubsystem.WireReloadPipeline` | ✅ as `WireSaveAndReload` |
| `QuickReloadService` + `AiReloadCoordinator.ApplyQuickReload` | ✅ constructed with the **same registry instances the kernel ticks** — that instance-sharing IS the mechanism |
| `SaveAllAiDocumentsCommand` + the three `SaveDelegate`s | ✅ the shared command, the editor's three delegates mirrored |
| `AssetRoots` — *asset to path* | ⚠ **deviation, §10.2** |
| `HotReloadClassifier` — Cosmetic/Soft/Hard | 🔴 **not on this path — §10.3** |
| `DebugApiService.SaveAsset` / `.ReloadAsset` | ✅ + a `RouteDoc` each; **74 tools** *(was 72)* |
| `MainToolbar` — the hot-reload/save button | ✅ **two entries on CGF**, and §7 of the slice-2 design is discharged for the first time |

### 10.1 ⭐ Editing was already wholesale — **this slice added its RUNTIME EFFECT**

⛔ There was never gating code to remove *(the `2026-08-25` steer, already honoured in slice 1)*. ⭐ What
was missing was what an edit **DOES**: a save path, and a reload that commits the recompiled definition
into the registry the kernel ticks. 📌 Slice 1 reported this un-wireable because its trigger — a dirty
OPEN document — could not exist on CGF; ⇒ `CE-009` removed that blocker and this is the follow-through.

⚠ **The two write paths stay distinct, and a rail now enforces it**
*(`The_live_variable_value_write_is_still_off_on_the_cluster`)*: the asset path writes a FILE and
recompiles; the live variable-VALUE path stages a `Blackboard1024` write and stays **OFF** *(`R-52`)*.

### 10.2 ⚠ Deviation ① — **save resolves the path from `SourceFilePath`, not from `AssetRoots`**

§6 ② says *"asset→path via `AssetRoots`"*. 📐 **Measured:** `SaveAllAiDocumentsCommand.Execute` already
resolves `asset.SourceFilePath` and **skips with a WARNING** when it is empty. ⇒ ⛔ a second
`AssetRoots`-based mapping would be a competing answer to *"where does this asset live"*, when the
catalog recorded the real one as it indexed the file. ⭐ `AssetRoots` still resolves the reload
**catalog root** — a different question, and it stays.

⇒ ⭐ **Ruling 67 did NOT bite in a dev run** *(the walk-up finds the source tree; 72 assets index)*. ⚠ On a
deployed node the path is empty and the shared command reports *"Skipped … no source path"* — ⛔ a
warning, not a silent save-to-nowhere, which is what §6 ② asked for.

### 10.3 🔴🔴 Premise FALSE — **Cosmetic/Soft/Hard is NOT observable on the QuickReload path**

⛔⛔ §1/§2/§6 all present *"Soft = patch (state kept), Hard = generation bump (state reset)"* as something
this slice's reload produces, and §7's headline rail was to assert exactly that. 📐 **Measured:**

| what was measured | consequence |
|---|---|
| `QuickReloadResult` is `(bool Succeeded, string? ErrorMessage, long DurationMs)` — ⛔ **no classification field at all** | the reload cannot report which it was |
| `AiHotReloadCoordinator.OnHardReloadCompleted`'s own doc: *"**NOT fired for Quick Reloads** (`ApplyQuickReload`), which do not replace working-state slot layouts"* | the Hard signal belongs to the **ALC file-watcher** path |

⇒ ⭐⭐ **The Cosmetic/Soft/Hard classification lives on a DIFFERENT mechanism than the one this slice
wires**, and §17's `BTreeHotReloadManager`/`HsmHotReloadManager` are its home. ⛔ **The headline rail was
therefore built to assert the CYCLE — open → save → reload → the compiler's own verdict — and NOT the
state-retention claim**, because asserting it would be asserting a fact the code cannot produce.
📌 Filed as **`CE-023`**; ⚠ the event is subscribed anyway, so the log exists the moment that path lands.

### 10.4 🔴 Premise INVERTED — **ruling 53 says a headless origin never pre-flights**

§6 ③ says *"Hard-reload confirm at the INTERACTIVE node"*. 📐 **Read the ruling:**
`UX_Feature_Modal_Surfaces.md` §2.0b — *"**Headless never pre-flights** — MCP/script/replay dispatch the
authorized request directly *(ruling 53)*. ⚠ The origin still **logs** what it skipped"* — and its risk
table: *"Headless proceeds silently on destructive work — deliberate — but … **the origin-side log is the
whole safety net, so it is a requirement, not a nicety**."*

⇒ ⭐⭐ **There is no confirm to route.** CGF pops no modal *(correct — a modal on an unattended node is a
hang)*, and what the ruling actually requires is the **LOG**, which is now written on **every** reload,
plus a `Warn` on the Hard event. ⛔ Building a confirm route would have been building the thing the
ruling forbids. 📌 The cross-node confirm *(`UXI-16`'s `IProgressSink` egress)* remains unbuilt and is
**`CE-024`**.

### 10.5 ⭐⭐ §7 of the slice-2 design, DISCHARGED — **and the hand-off worked exactly as designed**

⭐ Slice 2 asserted CGF's toolbar had **zero** entries and wrote the rail to REDDEN the day it gained one.
📐 It did, on this batch's first conformance run. ⇒ ⭐⭐ the rail now asserts the affordances **by id**
*(`SaveAllAiDocuments`, `QuickReloadAiAsset`)* **and that they are VISIBLE** — ⛔ an entry bound to a
perspective CGF never shows would satisfy an id check and offer the operator nothing.

⚠ `main-toolbar` stays a DECLARED divergence: the editor registers many more entries. ⭐ Its reason is
narrowed — it is no longer *"CGF has none"*.

### 10.6 ⭐ The MCP surface, and the collision boundary held

`POST /assets/{assetId}/save` · `POST /assets/{assetId}/reload`, in the **existing**
`DebugApiService.Assets.cs` *(§8's partition)*. ⛔ **No authoring route was added** — that is AQ56's file.
⚠ **Two honesty points written into the `RouteDoc`s:**
- **save writes EVERY dirty open document**, because the shared command is all-documents by construction;
  ⛔ a per-asset save would duplicate its dirty/path/clean-marking logic.
- **reload compiles from the IN-MEMORY asset**, so it reflects unsaved edits and a failed compile is a
  **200 with a failure status** — ⛔ not an HTTP error, because it is a legitimate outcome of editing.
