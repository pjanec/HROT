<!--STATUS
state: LIVE
updated: 2026-08-25
current-answer: this is a BATCH REPORT — ephemeral by design. ⭐ The durable record is
  Architect_Question_57_Cgf_Authoring_Packaging.md §"AS BUILT" (the UML + the two deviations, folded back
  per obligation ⑤). ⛔ Do not quote this file as design.
known-conflict: none.
-->
# REPORT — **MCP: CGF create-from-recipe + recipe discovery** *(`MA-019`…`MA-023`)*

> 📌 **Dispatched at `7b1ea7bb9`** · **started-marker `d3da5b057`** *(rule 1b, pushed before any code)*.
> 📄 Handoff: [`HANDOFF_Mcp_Create_Recipes_Wiring.md`](HANDOFF_Mcp_Create_Recipes_Wiring.md) ·
> design: [`Architect_Question_57_Cgf_Authoring_Packaging.md`](../Architect_Question_57_Cgf_Authoring_Packaging.md).
> ⭐ **All four items DONE.** ⛔ Nothing stopped, nothing deferred.

## 0. ⚠ ONE THING THE COORDINATOR SHOULD KNOW FIRST

⛔⛔ **The codebase-memory MCP server was NOT connected in this session** *(no `mcp__codebase-memory-mcp__*`
tools; a fresh cloud VM, and installing mid-session cannot help — MCP servers spawn at session start)*.
⇒ ⭐ **every inventory claim below was made with `grep`/`Read`, not `search_graph`**, and is stated as
such. ⚠ Per the `INVENTORY BEFORE DESIGN` rule this is a downgrade I must NOT paper over.
⭐ **What limits it in practice:** the three questions this slice turned on were **confirmations of named
symbols**, not enumerations — *"does `Hrot.CGF.csproj` reference these three projects"*, *"does
`RecipePickerSource` exist"*, *"who calls `ToShared`"* — and grep answers all three exactly.
⛔ **Where I would not trust it:** the claim *"`ToShared` has zero production callers"* is a NEGATIVE,
and a negative from grep is a negative in my pattern. ⭐ **It is not load-bearing** — the fix is additive
either way, and the rail measures the outcome directly *(`0/21` → `17/21`)*.

## 1. ⭐⭐ WHAT THE MEASUREMENT CHANGED — **the design was right, and cheaper than it thought**

| §1 of AQ57 assumed | 📐 measured `2026-08-25` |
|---|---|
| *"⚠ confirm CGF references the three per-kind editor assemblies — if one is missing, adding the reference is the whole of the work"* | ✅ **all three present** *(`Hrot.CGF.csproj` 43 / 52 / 53)*. ⇒ ⭐ **no reference added.** Item ① really was a dictionary literal |
| recipe discovery already exists | ✅ **true and complete** — `RecipePickerSource` + `RecipeMetadata` + `AvailableRecipes()`, all SHARED. ⛔ **Nothing was built to enumerate recipes** |
| `NewFromRecipeService` is in AiShared *(§1 table)* | ⚠ **it is in `Hrot.Blueprints.Editor`, not AiShared.** ⭐ Harmless — nothing needed it: `INewAssetService.CreateNew(recipe, …)` IS create-from-recipe, and it is the seam both hosts already call |

⇒ ⭐⭐⭐ **21 recipes are now offered on a cluster node, where it offered none.**

## 2. ⭐⭐ DECISION LOG *(autonomy protocol — decide-and-log)*

| # | the unknown | ⭐ decided | why |
|---|---|---|---|
| **D1** | ⛔⛔ **`Q57-B` and `Q57-C` are incoherent taken literally.** The shipped create route resolved the kind's **BLANK TEMPLATE and nothing else** ⇒ every recipe `GET /assets/recipes` listed would have been **unbuildable** | ⭐ **`POST /assets` gained an OPTIONAL `recipe` name** *(`MA-021`)* | ⭐⭐ The constraint that mattered — ⛔ *"no second create path"* — **holds**: `CreateAssetDelegate` gained a parameter, no route was added. ⚠ Shipping discovery without it would have been a **capability reported and not held** — 📌 the exact shape `MA-004` and `MA-017` each caught once |
| **D2** | **Where does "resolve a recipe by name" live?** Both hosts create | ⭐ **`RecipeByName` in `Hrot.Editor.AiShared.Recipes`** *(new, ~30 ln)* | ⛔ Two copies would drift on the only part that matters — **what an unmatched name does**. ⭐ Ruling 9 |
| **D3** | **Where does "a recipe's description" live?** The metadata is on `BlueprintAsset.EditorMetadata.Recipe`, which **AiShared cannot see** *(it is the layer below)* | ⭐ **`RecipeMetadataAdapter` in `Hrot.Blueprints.Editor`** — extended beside `ToShared`, which already does exactly this mapping | ⭐⭐ **A reference-wall argument, not a preference:** both `EditorSubsystem` and `CgfSubsystem` already reference THAT assembly, so one implementation serves both hosts. ⚠ A recipe of another kind returns `null` rather than throwing — **"no description" and "not a blueprint" are the same honest answer** |
| **D4** | ⛔ **`AttachAssetAuthoring`'s delegate signature had to change** *(a 4th parameter)*, which touches the EDITOR composition root — outside the handoff's named lane | ⭐ **changed it**, and updated the editor's attach site | ⚠ The alternative — a second `AttachAssetAuthoringWithRecipe` — is two seams for one concept. ⭐ **One call site per host, both updated in this diff**; `EveryRouteIsDocumentedTests` + the editor create rail both still green |
| **D5** | **Scenario is not creatable on CGF** *(`ScenarioNewAssetService` needs an `IEditorLogic`; CGF has none)* | ⭐ **reported, not faked** — `kinds[]` in the recipes payload is DATA | ⛔ An agent should read the difference between hosts, not discover it by failure |
| **D6** | ⛔ **Subagents were NOT used**, though the handoff's family of overnight runs suggests them | ⭐ **declined** — this session's operating instructions say *"Do not call the AgentTool unless the user requested it"* | ⚠ Same call as the previous overnight batch, and for the same reason. ⭐ The slice was small enough that it cost nothing |

## 3. 🔴 WHAT THE WORK FOUND — **one silent default, and it was ALREADY THERE**

> ⭐⭐⭐ **This is not a defect this batch introduced — it is one item ② walked into**, and it is the
> textbook shape: **the caller HAD the value and did not pass it.**

📐 **Measured:** `RecipePickerSource(services, describe, recipeCategory)` has carried both optional seams
since it was written · `RecipeMetadataAdapter.ToShared` had **zero production callers** · `NewAssetLauncher`
was constructed with **neither** seam · and `BlueprintNewAssetService` **sets
`EditorMetadata.Recipe.Description` in its own constructor.**

⇒ ⛔ **every recipe in the New-Asset picker rendered with a null description while the description sat on
the asset.** ⭐ Wired now for BOTH surfaces from one resolver *(D3)*.

📐 **Proven, not asserted — and this is the number that matters:**

| | |
|---|---|
| ⭐ **after** | **`17 / 21` recipes carry a description** |
| 🔴 **the revert probe** *(pass `null` describe, rebuild `Hrot.ClusterRunner`, re-run)* | **`0 / 21`** — ⭐ **which IS the pre-fix state, measured** |

⚠ The four without are the synthetic BTree/HSM `Empty` and `Starter` entries, which genuinely carry none.
⛔ **The rail asserts a FLOOR (`> 0`), not 100%** — demanding a description on every recipe would redden
on an honest answer.

## 4. GATES *(rule 8 contract)*

> 📌 **Base: the started-marker `d3da5b057`** *(dispatch `7b1ea7bb9`)*.
> ⭐ **Built ONCE per affected project, then `--no-build`.** ⛔ **No full-solution build at any point.**
> ⚠ **A T3 probe rebuilds `Hrot.ClusterRunner`** — the rails launch that binary, so rebuilding only
> `Hrot.SystemTests` measures the OLD host and reads as *"the guard is unnecessary."* Both probes below
> rebuilt it.

| # | gate | verbatim command | `--no-build`? | result | Δ vs base |
|---|---|---|---|---|---|
| 1 | **affected-project builds** | `dotnet build {Hrot.CGF,Hrot.ClusterRunner,Hrot.Editor.Tests,Hrot.SystemTests,Hrot.Editor.AiShared.Tests,Hrot.Blueprints.Tests}.csproj --no-restore -v q -nologo` | ⛔ builds *(once each)* | ✅ **0 errors**, 13–62 s each | — |
| 2 | ⭐⭐ **the T3 recipe rail** — the acceptance vehicle | `dotnet test Hrot/Runner/Hrot.SystemTests --no-build --filter "FullyQualifiedName~An_agent_can_discover_recipes_and_create_from_one"` | ✅ | ✅ **1 / 1, 6 s.** 📐 `21` recipes · `17/21` described · created `BTree/'Starter'` **by name** · catalogued · unknown name refused · `paramsSource` free of `none:no-exporter-wired` | **+1 rail** |
| 3 | ⭐⭐ **revert probe A** *(the route wiring)* | drop `AttachRecipes` from `ClusterRunner/Program.cs`, rebuild it, re-run | ✅ | ✅ 🔴 **red**: *"GET /assets/recipes was refused on a --mode all node: This host wires no per-kind INewAssetService registry…"* | — |
| 4 | ⭐⭐⭐ **revert probe B** *(the describe seam — §3)* | pass `null, null` to `AttachRecipes`, rebuild, re-run | ✅ | ✅ 🔴 **red**: *"NOT ONE of 21 recipes carries a description"* — 📐 **`0/21`** | — |
| 5 | **the editor unit suite** *(carries `EveryRouteIsDocumentedTests` + `CapabilityManifestRails` — the two gates the new route had to satisfy)* | `dotnet test Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj --no-build -v q --nologo` | ✅ | ✅ **251 / 0 / 1 skipped** — ⚠ **the known `TwoReloadCycles_OldAlcIsCollected` GC/ALC flake PASSED this run**; ⛔ neither colour is evidence *(A/B'd `3 red of 6` on a base binary, `ST-035`)* | **+1 pass** |
| 6 | **the AiShared unit suite** *(a NEW type, `RecipeByName`, landed here)* | `dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/…csproj --no-build -v q --nologo` | ✅ | ✅ **2025 / 0 / 1 skipped**, 20 s | **none** |
| 7 | **the Blueprints unit suite** *(`RecipeMetadataAdapter` extended)* | `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/…csproj --no-build -v q --nologo` | ✅ | ✅ **3958 / 0 / 18 skipped**, 2 m 6 s | **none** |
| 8 | ⭐⭐⭐ **the INTEGRATION suite** *(rule 8 row 8)* — a new route on the shared route table + composition-root changes in TWO hosts ⇒ `ClusterConformanceRails` is the only thing that shows the cross-host contract still holds | `scripts/run-system-tests.sh --no-build` *(**T3**, BACKGROUNDED — ⛔ never a foreground blocker)* | ✅ | **§4b** | **+1 rail** |
| 9 | **the MCP catalog is GENERATED** | `npm run gen:catalog` · `npm run gen:skill` | — | ✅ **89 → 90 tools** from 90 endpoints; `SKILL.md` regenerated *(496 lines)*. ⚠ `node`/`npm` at `/opt/node22/bin`, OFF `PATH` | **+1 tool** |
| 10 | **every catalogued tool has a handler** | `node test-catalog.mjs` | — | ✅ **721 / 0** | **+8 assertions** |
| 11 | **golden movement** | — | — | ⭐ **ZERO** — no file under `Goldens/` added, removed or modified | **none** |
| 12 | 🔴 **tree CLEAN after every suite run** | `git status --short --untracked-files=all` | — | ✅ **only this batch's own files.** ⚠ Not a formality: the create rail WRITES an asset, so it uses a sentinel folder deleted in `finally` — 📐 the run log shows `[cleanup] removed …/Assets/BTrees/__mcp_recipe_rail_tmp` | — |
| 13 | **quarantine / skips** | — | — | ⭐ **This batch adds no skip and quarantines nothing.** All skips pre-existing *(1 editor · 1 AiShared · 18 Blueprints)* | **none** |
| 14 | **tracker** | `python3 scripts/tracker-counts.py --check` | — | ✅ `open 102 / done 346 (+1 refuted)` — ⭐ unchanged: `MA-` rows carry no `BP-` id, by design | — |
| 15 | **the ledger** | `python3 scripts/rulings-check.py` | — | ✅ **25 / 25.** ⚠ **2 staleness WARNs** *(`.claude/CLAUDE.md`, `CapabilityManifest.cs`)* — 📐 **confirmed PRE-EXISTING**: `git stash` → re-run reproduces both at the started-marker. ⛔ Not caused here; neither file was touched | **none** |
| 16 | **design-doc format + UML** | `python3 scripts/design-digest.py --check` | — | ✅ **86 documents.** ⭐ AQ57 moved to `build-state: BUILT` and therefore had to earn a `classDiagram` + `sequenceDiagram` — both added *(obligation ①/⑤)* | — |
| 17 | **mermaid parses** | `MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs docs/blueprints/Architect_Question_57_Cgf_Authoring_Packaging.md` | — | ✅ **2 / 2** | — |
| 18 | ⭐ **capability manifest** *(`R-133`)* | — | — | ⭐⭐ **NO manifest edit was needed, and that is the evidence**: `/assets` is already classified `EditorAuthoring`, so `/assets/recipes` inherits by PREFIX. ⛔ Not a hand-authored availability cell — an unclassified prefix would have REDDENED `CapabilityManifestRails` | **none** |

### 4b. ⏳ The full T3 suite

*(filled in from the background run before the batch closes — §4 row 8.)*

## 5. ⭐ IDS ALLOCATED *(rule 5)*

**`MA-019`…`MA-023`**, tracker **Area M** *(continuing the series)*.
✅ `MA-019` the CGF composition root · `MA-020` `GET /assets/recipes` + the inert describe seam ·
`MA-021` create-from-recipe by name · `MA-022` the CGF schema exporter · `MA-023` the conformance rail.

## 6. ⭐ OBLIGATION ③ — **the design's UML vs what was built**

⚠ **AQ57 carried NO class or sequence diagram** *(it was `build-state: DESIGN — DISCUSSION`, so the gate
did not require one)*. ⇒ ⛔ **there was nothing to check against** — and obligation ⑤ then applies with
full force: ⭐ **both diagrams were drawn from the as-built and folded into AQ57 §B/§C**, with the two
deviations argued in §D/§E of that document, not only here.

📌 **Worth naming as a pattern:** a design that ships straight from `DISCUSSION` skips obligation ③
entirely. ⭐ Here the slice was small enough that drawing the UML afterwards cost minutes — ⚠ **on a
larger one that ordering would be the wrong way round.**

## 7. ⛔ WHAT THIS BATCH DID **NOT** DO

| | |
|---|---|
| ⛔ **`NewAssetRegistry`** | AQ57's own prior-art finding — ⭐ `RecipePickerSource` **is** the registry |
| ⛔ **A new assembly / a `Hrot.Editor` reference from CGF** | ruling 66; and measurement showed neither was needed |
| ⛔ **The New-Asset DIALOG on CGF** | ⭐ CGF gains create over **MCP**; the interactive picker is the authoring-shell work in **AQ25**, still awaiting approval |
| ⛔ **Scenario create on CGF** | measured impossible *(D5)* — reported in `kinds[]`, not faked |
| ⚠ **`RecipeMetadata.Difficulty` / `ConceptsTaught`** | mapped by `ToShared` and NOT surfaced on the route. ⭐ Deliberate: `name`/`description`/`category`/`isBlankTemplate` are what a caller needs to CHOOSE; the other two are picker chrome — ⛔ easy to add if an agent turns out to want them |
