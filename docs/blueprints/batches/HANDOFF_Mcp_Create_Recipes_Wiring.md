<!--STATUS
state: LIVE
build-state: DISPATCH — small WIRING slice. CGF gains CREATE (new asset from recipe) + recipe discovery over MCP.
updated: 2026-08-25
current-answer: pointer + autonomy. Source: AQ57 (revised — recipe discovery already exists via RecipePickerSource).
known-conflict: ✅ PARALLEL-SAFE with the Axis-B session (disjoint: this = DebugApi/catalog + the CGF asset-service
  dict; Axis-B = engine authority gate + gizmo + AttributeIds). ⚠ the ONE shared file is CgfSubsystem.cs — keep to
  the ASSET-SERVICE construction region (near slice-1's AssetCatalog build); Axis-B keeps to gizmo registration.
-->
# HANDOFF — **MCP: CGF create-from-recipe + recipe discovery** *(MCP lane — parallel with Axis-B)*

> 📌 **Dispatched at `<coordinator HEAD>`** *(confirm `git rev-parse origin/claude/blueprint-authoring-status-6sr5ld`)*.
> ⭐ Branch FRESH from the coordinator *(rule 7)*; **rule 1b started-marker before any code.** ⛔ No PR.
> ⭐ Continue the **`MA-`** series *(Area M)* from `MA-019`; state every id *(rule 5)*.
> ⚠ **The variable-model freeze is LIFTED** *(`2026-08-25`)* — AiShared is editable; ordinary coordination still applies.

## 0. AUTONOMY PROTOCOL
Same as the overnight runs: **decide-and-log** on unknowns *(DECISION LOG in the report)*; **stop the item, not the batch**; DONE = §3's rails green. ⭐ Low risk — reuse, not new mechanism.

## 1. ⛔ SOURCE — this is REUSE, not a new registry
📄 **[`Architect_Question_57_Cgf_Authoring_Packaging.md`](../Architect_Question_57_Cgf_Authoring_Packaging.md)** *(revised)*. ⭐⭐⭐ **Recipe discovery ALREADY EXISTS** — `RecipePickerSource` *(`Hrot.Editor.AiShared/Browser`)* enumerates `INewAssetService.AvailableRecipes` per kind; `NewFromRecipeService` creates from a recipe; `RecipeMetadata` carries name/description. ⛔ **Do NOT build a `NewAssetRegistry`.** MA- already shipped **`POST /assets` create → `INewAssetService` per kind**.

## 2. ⭐⭐ WHAT TO BUILD *(three small items)*
| # | task | note |
|---|---|---|
| ⭐ **①** | **Construct the `Dictionary<AssetKind, INewAssetService>` at CGF's composition root** *(the per-kind services from `Hrot.{Blueprints,BTree,Hsm}.Editor`)* and feed `RecipePickerSource` — exactly where slice 1 built `AssetCatalog` *(`CgfSubsystem.BuildAiShell`)* | ⚠ **first confirm CGF references those three editor assemblies** *(it should, post slices 2–3)*; if one is missing, adding the reference is the whole of the work. ⛔ do NOT reference `Hrot.Editor` *(ruling 66)* |
| ⭐ **②** | **`GET /assets/recipes`** — list available recipes per kind from `RecipePickerSource` *(name · description · kind)* — the recipe analog of node-kind discovery *(MA-013)* | own route file `DebugApiService.Authoring.cs`; a `RouteDoc` + handler in `src/index.mjs`; `test-catalog` green |
| ⭐ **③** | verify **`POST /assets` create works on CGF** now the services are wired | ⛔ no new create route — reuse the shipped one |
| ⚠ **④** *(opportunistic follow-up)* | wire an **`IActionSchemaExporter` on CGF** so MCP `paramsSource` stops reporting `none:no-exporter-wired` *(the one-liner the overnight MCP run filed)* | only if trivial at CGF's root; else log and skip |

## 3. ⭐ DONE — rails
- an agent lists recipes over `GET /assets/recipes` on a `--mode all` CGF node, creates one via `POST /assets`, and it appears in `GET /assets` *(round-trip, conformance rail)*;
- `gen:catalog`/`gen:skill`/`test-catalog` green for the new route+handler;
- affected-project builds *(`Hrot.CGF`, `Hrot.Editor`, `Hrot.SystemTests`)* green; system suite named + run *(T3, background)*.

## 4. LANE & COLLISION
⭐ **Yours:** `CgfSubsystem.cs` *(ASSET-SERVICE construction region only)* · `DebugApiService.Authoring.cs` + the generated catalog · `Hrot.SystemTests/**`. ✅ **Parallel-safe with the Axis-B session** — it owns the engine authority gate + gizmo + `AttributeIds`, touches NO DebugApi/catalog. ⚠ **The one shared file is `CgfSubsystem.cs`** — keep your edits to the asset-service dict area; the Axis-B session keeps to gizmo registration. ⭐ Rule 4: re-pull coordinator before the final commit.

## 5. GATES *(rule 8 contract)* + WHEN DONE
One row per gate · counts · Δ vs the started-marker · `--no-build` column · reds by `git diff` · `tracker-counts.py` · `rulings-check.py` · `design-digest.py --check` · the `MA-` ids. **When done:** fold the as-built into AQ57 *(mark BUILT)*; state the ids; the report points at AQ57.
