<!--STATUS
state: LIVE
build-state: DISPATCH — a pointer, not a design. ⛔ Carries NO UML and NO design content by rule.
updated: 2026-09-17
current-answer: §2 is the item list. §1 is the ordering constraint that makes C8 first.
stale-below: nothing — new document.
known-conflict: E1-E4 touch editor/IG UI surfaces; the UI lane (claude/reset-working-branch-qd1qpv) owns
  the variable/blackboard panels, NOT the map gizmos or DetailsWindow views this batch adds. If a file
  turns out to be shared, that is a STOP-and-report, not a judgement call.
related-designs:
  - ../PLAN_Terrain_Zones_Build.md — the plan (C8 + stages E and H; grouping in §5).
  - ../../DESIGN_Terrain_Zones_And_Assets.md — THE owning design. §10 is the AS-BUILT from batch ②b.
  - ../Architect_Question_57_Cgf_Authoring_Packaging.md — owns the recipe registry Stage H reuses.
  - ../../DESIGN_Cgf_Asset_Picker_Shell_Slice.md — owns the New-Asset picker shell Stage H feeds.
  - ../../DESIGN_Node_Roles_And_Policies.md — owns the Map2D role E5 moves authoring into.
-->

# HANDOFF — **Terrain & zones, Batch ③ THE SURFACES** *(C8 + E + H — 10 items)*

**Dispatched at `2500ced2d`.** ⛔⛔ **Scope FROZEN at that sha.** A later document that invalidates an item
⇒ **stop that item and report it**; ⛔ never stop the batch (`R-106`).

⭐ **This is the LAST batch of the programme.** Batches ①, ②a and ②b are merged: the model, the loader,
the 2PC round and the retirement are all real. What remains is the surfaces people touch — and the one
composition task that keeps them from breaking.

## 1. ⭐⭐⭐ `C8` GOES FIRST — and it is a user ruling

🔒 **User, `2026-09-17`: *"IG/CGF get loaders in batch 3."*** ⇒ ⭐ **a REAL `TerrainLoadClusterStateHandler`
on IG and CGF**, ⛔ **not** a scoped-down identity check.

📐 **Why the order is load-bearing, not tidiness.** Batch ②b's `D5` implements §8.3 N4 as written, and
**only SimHost registers a terrain loader today** *(one call site, `NodeBootstrapper`)*. ⇒ **the first
thing that issues a zone op turns `BP-537` from a nil-blast-radius break into a live one — and `E2` is
that thing.** ⛔ **Do not ship `E2` before `C8`.** ⭐ Everything else in `E`/`H` is order-free.

## 2. Before you write a line

| | |
|---|---|
| **①** | `git fetch origin claude/blueprint-authoring-status-6sr5ld` then `git merge --ff-only` it (**rule 7**) |
| **②** | ⭐⭐ push `chore: started terrain batch 3 at 2500ced2d` **immediately**, before any code (**rule 1b**) |
| **③** | ⭐⭐⭐ **T-1 (`R-142`)** — find and run each feature's OWN suite first. ⛔ `DetailsWindow`, the gizmos and the New-Asset shell all have suites; do not open a parallel rail class |
| **④** | ⭐ Success conditions live in **`PLAN_Terrain_Zones_Build.md` §2**, one row per item. ⛔ Not restated here |
| **⑤** | ⛔ coordinator allocated **no** ids. Continue from **`BP-539`**, plain numbers (rule 3a-id) |
| **⑥** | ⭐ **rule 4** — pull the coordinator branch again before your final commit |

## 3. The items — 10

| # | what | note |
|---|---|---|
| **`C8`** | 🔒 real terrain loader composed on **IG and CGF** | ⛔ **FIRST.** Closes `BP-537` |
| **`E1`** | zone gizmo renders load state via `LineStyle` | reads the **local** marker |
| **`E2`** | "Load zone" context menu on a zone entity | ⛔ **always enabled**, never gated on local freshness; publishes the **cluster** op. ⚠ needs `C8` |
| **`E3`** | a **zones view** in the existing `DetailsWindow` | ⛔ progress from `_pendingTransactions`, **not** `HasInFlightTransaction` ⚠ **owns `U5`** |
| **`E4`** | cluster-panel control to trigger a terrain-asset build | per-node outcome visible, never one global OK |
| **`E5`** | move area/tactical-drawing authoring into the shared **Map2D role** | ⛔ not an IG-private path ⚠ resolves `U6` |
| **`H1`** | pass the seeds — the 2-arg `ScenarioNewAssetService` ctor over `AssetRoots.ScenariosRecipesRoot` | |
| **`H2`** | resolve seeds against the **recipes** root | ⚠ **owns `U9`** |
| **`H3`** | Scenario offers **no blank template** | `IsBlankTemplate => false` |
| **`H4`** | ship a seed carrying both a `TkbName` and a terrain name | ⭐⭐ **the rail that proves the stage** |

## 4. ⭐⭐ `H` IS WIRING, NOT NEW MACHINERY

`Q57` already ruled the recipe registry exists *("recipe DISCOVERY already exists … ⛔ do NOT build a new
`NewAssetRegistry`")*, and the picker shell is composed on **both** hosts. ⛔ **No new asset kind, no new
registry, no per-kind dialog fields.** ⭐ The whole point of `H4`: with a seed, the terrain and TKB names
are **carried** by load-then-save-as — ⛔ they are never *chosen* anywhere in that path.

## 5. The two open `U` rows this batch owns

| # | what is missing | who decides |
|---|---|---|
| **`U5`** | the details shell has no *"map background selected"* CONTEXT. `PopulateEmptyMapMenu` proves empty space is a click target for a **menu**; selection-as-context is new | ⭐ **your design call inside `IDetailsContextSource`** — `E3` cannot be asserted until it exists |
| **`U9`** | `FromSeed` must reach the recipes root: widen `IScenarioCreationSession` with a root-aware load, **or** have `AvailableRecipes()` hand back full paths the existing load already accepts | ⭐ **your call.** ⚠ One read of `IEditorLogic.LoadScenarioByName`'s body settles it — if it accepts a rooted path, option two costs no seam change |

⭐ **Report both: what you measured and which way you went.** ⛔ Neither needs the user.

## 6. Three traps

| ⚠ | |
|---|---|
| **`E2` must NOT gate on local freshness** | ⛔ the menu is **always enabled** and always publishes a **cluster-wide** op — 🔒 user ruling. A host whose local copy looks fresh may still be the one node that is stale |
| **`E3` must not use `HasInFlightTransaction`** | 📐 design §9.4: it is a **pre-existing defect** and out of scope to fix. ⭐ Source progress from `_pendingTransactions`, the keyed dict that actually supports concurrent rounds |
| **`E5` is a MOVE, not a copy** | ⛔ ruling 9 — one implementation. If the IG path survives beside a shared one, that is the duplicate the whole programme exists to remove |

## 7. Gates

Rows 1–7 against base **`2500ced2d`**. ⚠ **Row 8 binds for `C8` and `E2`** — a new host composing a
cluster-state handler, and the first surface that issues a cross-node op. Name the integration suite that
would break and report **running** it, or state with base-sha evidence why it cannot gate. ⭐ Batch ②b's
row 9 (`EditorAuthoringIntegrationTests` in isolation, with the `ClusterRunner` DDS-allocator crash named
as the pre-existing un-gateable condition) is exactly the right shape.

⭐ Build the affected project, never the solution. Slow things in the background.

## 8. What to send back

`docs/blueprints/batches/REPORT_Terrain_Zones_Batch3.md`, and **inline** via your report trigger: the gate
table · every id · row 8's named suite and result · `U5` and `U9`'s measurements and your calls · the UML
check (obligation ③) with any deviation **folded back into the design** and the edit cited (obligation ⑤) ·
what the design got wrong · the sha you pushed.

⭐⭐ **And one programme-closing line the coordinator actually needs:** with `E`/`H` shipped, **which of
design §8.4 / §9's OPEN questions are now answered by what exists**, and which remain genuinely open. ⛔ A
programme that ends without saying what it left open is how the next session re-derives it.
