<!--STATUS
state: LIVE
build-state: DESIGN — decision-shaped, RECOMMENDED LEANS below; ⛔ NOT ready-to-build. This is the BASIS
  FOR A DISCUSSION with the user (parallel session extends the MCP server with scenario + AI-asset
  authoring). Resolve the sub-questions WITH the user, THEN it earns UML + a handoff.
updated: 2026-08-25
current-answer: the sub-questions Q56-A..F + their recommended leans. Awaiting the user's resolution.
design-basis: DESIGN_Cgf_Editor_Sharing_Slice3_Editing_HotReload.md §8 (the parallel-track collision plan) ·
  CE-009 slice-2 design (the /assets + /documents MCP surface this extends) · HN-030/R-133 (routes
  self-document; SKILL.md generated) · DESIGN_Deterministic_Network_Ids.md (determinism discipline).
known-conflict: ⚠ shares the DebugApi surface with CE-011 — the collision plan is in the slice-3 design §8
  and restated in §7 here.
-->
# Architect Question 56 — **the MCP authoring surface** *(scenario + AI-asset authoring over MCP)*

> 🎯 **User, `2026-08-25`:** *"keep extending the MCP server by the scenario authoring capabilities
> including the AI asset authoring."* ⇒ turn MCP from a **read/drive** surface *(open, inspect, switch tabs)*
> into an **authoring** surface *(create an asset · add and connect wiring nodes · edit params · save)*.
> ⛔ **This is a DISCUSSION doc** — the leans are mine to propose; you approve.

## INVENTORY — measured `2026-08-25` *(seam law: the authoring vocabulary EXISTS)*
```
grep INewAssetService / *NewAssetService · GraphCommand.AddNode/AddLink · IGraphModel · *CommandSink
grep DebugApiHost POST routes (scenario/*, entities/command, variable stage-write)
```
| ✅ exists | where | ⭐ role |
|---|---|---|
| **`INewAssetService`** + per-kind impls *(`BlueprintNewAssetService` · `HsmNewAssetService` · `BTreeNewAssetService`)* | `Hrot.*.Editor` | ⭐ **create-asset seam** — reuse per kind |
| **`GraphCommand.AddNode` / `AddLink`** over **`IGraphModel`**, applied by a **command sink** *(`BlueprintCommandSink.CreateAssetNode` is the real one)* | `NodeEditor.Core` · `Hrot.Blueprints.Editor/Host` | ⭐⭐ **the graph-authoring vocabulary already exists** — MCP translates calls into these commands |
| **scenario ops over MCP** — `POST /scenario/load/{edit,live}` · `/scenario/save` · `/entities/command` · `POST /entities/{id}/variable` *(stage-write)* | `DebugApi/*` | ⭐ scenario-level authoring PARTLY exists; ⛔ **graph-node authoring does NOT** |
| **save→hot-reload path** *(CE-011)* · **open/switch tabs** *(CE-009)* | `DebugApi/*` · `QuickReloadService` | ⭐ authoring writes the ASSET FILE then hot-reloads — the SAFE path, ⛔ not the live-value staged write *(R-52)* |
| ⚠ **node/link id generation** — `IdGenerator.NewNodeId()` | `NodeEditor.Core` | 🔴 **non-deterministic** ⇒ un-replayable authoring; the one real new concern *(Q56-D)* |

⇒ ⭐⭐ **Authoring is largely "translate MCP → existing `GraphCommand`s + `INewAssetService`, then CE-011's
save→reload."** The genuinely new parts are the **route surface**, **deterministic ids**, and **validation**.

## ✅ RECOMMENDED LEANS — resolve WITH the user

| # | sub-question | ⭐ recommended lean |
|---|---|---|
| **Q56-A** | Build a new authoring API, or reuse the `GraphCommand` sink? | ✅ **RESOLVED with the user `2026-08-25`: REUSE the SAME mechanism as human editing** — `IGraphModel` + `GraphCommand.AddNode`/`AddLink`/`SetNodeProperty`/`RemoveNodes` via the command sinks. ⭐ MCP edits ARE human edits over the wire ⇒ **one path, one validation, and undo/inverse for free** *(`CommandBuilder` returns forward+inverse)*. ⛔ NOT a parallel authoring model. ⭐⭐ **New primitive: `GET /assets/{id}/graph`** — the whole asset as JSON with guids/labels/pins/links/params, the thing that makes read-then-edit-by-guid work. 🔒 **User `2026-08-25`: REUSE the existing asset JSON** *(assets are already expressed as JSON — no new shape to invent)*. ⚠ the read and the edit must share ONE guid space; `AQ10`'s rehydration already keeps ids stable across load/save. ⭐ Layering: **open (CE-009) → read-as-JSON → edit via commands → save+reload (CE-011)** |
| **Q56-B** | Create-asset? | ✅ **reuse `INewAssetService` per kind** — one MCP route resolves kind→service |
| **Q56-C** | Scenario authoring vs AI-asset authoring — one surface or two? | ✅ **RESOLVED with the user `2026-08-25`: TWO DISTINCT surfaces, they share little.** ⭐⭐ **① AI-ASSET (graph) authoring = DOCUMENT editing** — `GET /assets/{id}/graph` *(reuse the existing asset JSON — Q56-D)* + a `/assets/{id}/graph/*` edit-by-guid group via the command sink → save/reload *(CE-011)*. ⭐⭐ **② SCENARIO authoring = WORLD manipulation, NOT file editing** — 🔒 *"the scenario FILE is a reduced SNAPSHOT of the world at save time, nothing more; there is nothing like editing a scenario file."* ⇒ scenario authoring is the **ENTITY operations on the live world** *(place/configure/assign/spawn/delete + attribute edits — mostly the EXISTING `/entities/*`)*, and `scenario/save` snapshots, `scenario/load` reconstitutes. 🔒 **ONE way, no distinct modes** — *"same UI operations, just at a different point in the scenario lifecycle (not-yet-running vs running exercise)."* ⇒ the two surfaces do NOT converge; scenario authoring extends the entity ops, AI-asset authoring is the new graph surface |
| **Q56-D** | Node/link IDS — is `Guid.NewGuid()` a problem? | ✅ **RESOLVED with the user `2026-08-25`: NO problem — GUIDs as-is, no human-naming layer.** 📌 It is a **READ-THEN-EDIT** loop, so the agent never PREDICTS an id: it **reads the whole asset as JSON with the in-memory guids** *(new `GET /assets/{id}/graph`)*, edits **by guid** *(add-node returns its new guid; add-link by pin guid, or pin LABEL where labeled — execution pins are hardcoded/stable)*, and the server returns any new guids. ⛔ The "randomization" concern was a TEST concern, not an authoring one — the harness already normalizes ids *(conformance ignores `panelId`; goldens treat ids as storage keys)*. ⇒ **no deterministic-id scheme, no key derivation.** A uuid→name dictionary is queried where it already exists *(assets: `GET /assets`; nodes/pins: the read-as-JSON itself)* |
| **Q56-E** | Validation & safety | ✅ **reuse the asset validators** *(the `IAssetValidator` set `PerspectiveWorkspaceServices` already carries)* + go through CE-011's save→reload *(so a structure change is a classified Hard reload, confirmed)*. ⛔ no unvalidated write |
| **Q56-F** | Relationship to the write-path / R-52 | ✅ **AI-asset authoring writes the ASSET FILE → hot reload — the SAFE path, distinct from the live `Blackboard1024` value write (R-52)**. ⇒ ⛔ authoring does not touch the frozen variable-model write path |

⭐⭐ **The one that needs YOU:** Q56-D *(id determinism)* and Q56-C *(how far the scenario vs asset surfaces
converge)* are the decisions with blast radius. The rest is reuse.

## 7. ⚠ PARALLELISATION — **it must not collide with CE-011** *(slice-3 design §8)*
| | |
|---|---|
| ⭐ **own route file** | authoring lives in `DebugApiService.Authoring.cs`; CE-011 owns `DebugApiService.Assets.cs` *(save/reload)* — ⛔ neither edits the other's |
| ⚠ **shared, coordinator-serialized** | `DebugApiHost` registration · `DebugApiRouteDocs` · `tool-catalog.mjs` · `SKILL.md` · `CapabilityManifest` ⇒ ⭐⭐ **the authoring session branches from a base that ALREADY contains CE-011**, adding on top — ⛔ not concurrently from the same base |
| ⭐ **sequencing** | dispatch CE-011 now → design AQ56 with the user WHILE it runs → dispatch authoring from a CE-011-inclusive base |

## ⛔ NOT part of the first authoring cut *(name so scope stays honest)*
⛔ a visual node-palette equivalent over MCP *(the palette is a UI affordance)* · undo of MCP authoring
*(AQ25-A is editor-side; MCP authoring is scriptable ⇒ re-issue, not undo)* · live variable-VALUE write
*(R-52)* · map/Axis-B authoring.

> ⭐ **Next step:** resolve Q56-A..F with the user, then this doc earns a `classDiagram` + `sequenceDiagram`
> and becomes READY-TO-BUILD for the separate session.
