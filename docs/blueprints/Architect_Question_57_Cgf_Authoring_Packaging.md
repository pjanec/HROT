<!--STATUS
state: LIVE
build-state: DESIGN — DISCUSSION. Carries a RECOMMENDED ANSWER per sub-question (coordinator "I analyse/
  suggest, user approves"). Awaiting user approval. ⛔ Nothing is built from this until the answer is recorded here.
updated: 2026-08-25
current-answer: §"RECOMMENDED ANSWERS". The one genuinely-open decision for making CGF a full AUTHORING node:
  where the create-asset packaging (catalog + per-kind INewAssetService) lives, so CGF can CREATE — not just
  open/edit — AI assets. The authoring SHELL / undo / role-gating / Q25-C are already recommended in AQ25 (§0).
design-basis: PROGRAMME_Cgf_Equals_Editor_Gap_Map.md (the two rows "Hrot.Editor catalog/NewAssetService
  packaging" + "Behavior/scenario authoring on CGF") · Architect_Question_25_Scenario_Authoring_Golden_Path.md
  (the authoring-shell decisions, recommendations awaiting approval) · AQ51 Project_Consolidation / AQ53
  Gizmo_Pack_Home (the precedent family: "where does a shared capability's home live") · ruling 66 (editor =
  one-node cluster) · MA-011..018 (MCP create-asset already reuses INewAssetService per kind).
known-conflict: none. ⚠ Number 57 taken as the next free across all active branches (rule 3a).
-->
# Architect Question #57 — **CGF authoring packaging: where does create-asset live?**

> 🎯 CGF now OPENS, EDITS and hot-reloads AI assets, and drives all of it over MCP *(CE-001..036, MA-001..018)*.
> The remaining gap to a **full authoring node** is **CREATE** — a new asset from a template. The create-asset
> machinery *(the catalog + per-kind `INewAssetService`)* is wired behind **`Hrot.Editor`**, which CGF does not
> reference. ⛔ **This is the one packaging decision the autonomous CGF run flagged and routed around** *(it
> avoided it for ruling 67 because `AssetRoots.Configure` takes a plain path — but create-asset cannot dodge it)*.

## 0. ⭐ WHAT IS ALREADY DECIDED *(do NOT re-draft — approve in place)*
📄 The authoring **shell / recoverability / role-gating / problems-list / behavior-affinity** decisions are
already recommended in **[`Architect_Question_25_Scenario_Authoring_Golden_Path.md`](../UX/Architect_Question_25_Scenario_Authoring_Golden_Path.md)** §"RECOMMENDED ANSWERS" *(added `2026-08-25`, awaiting your approval)*:
| AQ25 sub-q | recommendation *(awaiting approval)* |
|---|---|
| Q25-A undo/recoverability | autosave + Revert-to-Saved + **bounded single-step undo** of the gizmo gestures *(runtime host registers no inverses ⇒ Ctrl+Z correctly absent)* |
| Q25-C behavior affinity + **schema-driven param UI** | ✅ **C′ MEASURED feasible by REUSE** — a schema-driven sibling of the existing `BlueprintFieldDescriptor` renderer, ⛔ not `BehaviorUiCompiler` codegen *(which is strictly CLR-type-driven)* |
| Q25-D role/mode gating | per-host composition *(Q26)* — an SME host simply never composes the engineer-only controls |
| Q25-E problems list | one host-agnostic contract *(severity · message · source-ref · navigate)* |
⇒ ⭐ **They are postponable FEATURES** *(AQ25's own scoping: missing on the editor too, shared for free once built)* — ⛔ **not blockers for CGF authoring.** This AQ covers only the packaging that IS a blocker for CREATE.

## 1. ⭐⭐ INVENTORY — measured `2026-08-25` *(where create-asset lives today)*
```
search_graph name_pattern=".*NewAssetService" · ".*(Asset)?Catalog"
```
| ✅ exists | assembly | CGF references it? |
|---|---|---|
| **`INewAssetService`** *(the seam)* + `IAssetCatalog` / `IAssetCatalogContributor` | ⭐ **`Hrot.Editor.AiShared`** *(the SHARED assembly)* | ✅ **yes** — CGF built the shell on it |
| `BlueprintNewAssetService` *(182 ln)* | `Hrot.Blueprints.Editor` | ⚠ partly — CGF references it for the command sink/document factory *(slices 2–3)* |
| `BTreeNewAssetService` *(169)* · `HsmNewAssetService` | `Hrot.BTree.Editor` · `Hrot.Hsm.Editor` | ⚠ same |
| the **catalog wiring + New-Asset dialog** that assembles the per-kind services into one create surface | 🔴 **`Hrot.Editor`** *(the editor SUBSYSTEM/app assembly)* | ⛔ **NO** — this is the gap |

⇒ ⭐⭐ **The interfaces are already shared; the IMPL classes live in the per-subsystem AI editor assemblies; only the ASSEMBLY that stitches them into a create surface is `Hrot.Editor`, which CGF does not (and per ruling 66's spirit should not) take a dependency on.**

## 2. ⭐ THE SUB-QUESTIONS

### Q57-A — Where does the create-asset wiring live so CGF can reach it?
| option | what it means | trade |
|---|---|---|
| **A1** | ⭐ **Move the create wiring into `Hrot.Editor.AiShared`** *(a small `NewAssetRegistry` the per-kind services register into)* — CGF already references AiShared | ⭐ smallest new dependency surface; matches the AQ53 "shared home" precedent. ⚠ AiShared is under the variable-model FREEZE — the addition must be **additive + coordinated** with that lane |
| **A2** | CGF **references `Hrot.Editor`** directly | ⛔ pulls the whole editor SUBSYSTEM into a runtime node — contradicts ruling 66's clean seam; large surface |
| **A3** | a **new shared `Hrot.Editor.Authoring` assembly** the per-kind services register into; both `Hrot.Editor` and CGF reference it | ⭐ cleanest long-term seam; ⚠ a new project *(the AQ51 consolidation cost)* |

### Q57-B — How is the per-kind service discovered?
| option | | trade |
|---|---|---|
| **B1** | ⭐ **a registry the per-kind services register into at composition** *(like `IAssetCatalogContributor` already does for the catalog)* | ⭐ mirrors an existing, working pattern; measured-not-authored |
| **B2** | reflection over loaded assemblies | ⚠ the static-ctor-can't-see-hot-reload hazard Q25-C already named |

### Q57-C — Does CGF create-asset go through the SAME MCP route as edit? *(MA- already ships `POST /assets` create)*
📌 **Measured:** `MA-003`'s batch shipped **`POST /assets` create → `INewAssetService` per kind** — so the MCP create route EXISTS; it works on the editor because the services are wired there. ⇒ Q57-C is *"wire the same services on CGF via Q57-A/B"*, ⛔ not a new route.

## ✅ RECOMMENDED ANSWERS — *(coordinator; approve or redirect)*
| # | ✅ recommended |
|---|---|
| **Q57-A** | **A1** — move the thin create-wiring into **`Hrot.Editor.AiShared`** as a `NewAssetRegistry`. ⭐ CGF already depends on AiShared; ⛔ A2 pulls in the whole editor subsystem *(ruling 66)*; A3 is cleaner but costs a new project we don't yet need. ⚠ **The addition is ADDITIVE and MUST be coordinated with the variable-model freeze lane** *(AiShared is frozen)* — a one-file registry + registration calls, no touch to variable/blackboard internals. |
| **Q57-B** | **B1** — the per-kind `INewAssetService` **registers** into the shared `NewAssetRegistry` at composition, exactly as `IAssetCatalogContributor` populates the catalog today. ⛔ no reflection *(Q25-C's hazard)*. |
| **Q57-C** | **reuse the shipped `POST /assets` create route** — CGF gains create the moment Q57-A/B wire the services at its root; ⛔ no new MCP surface. |

⇒ ⭐ **Net:** one small additive registry in AiShared + registration at CGF's composition root = CGF creates assets, over the same MCP route the editor already uses. **The blast radius is the freeze coordination, nothing structural.**

## ⛔ NOT this AQ
- The authoring **shell / undo / role-gating / problems-list** — **AQ25**, awaiting approval *(§0)*.
- **Q25-C** schema-driven param UI — **AQ25**, resolved feasible-by-reuse, awaiting approval.
- **Axis B** map/entity authoring — gated on **UXI-30** *(separate; see the gap map)*.
