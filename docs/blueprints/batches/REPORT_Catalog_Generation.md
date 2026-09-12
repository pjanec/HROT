<!--STATUS
state: LIVE
build-state: BUILT — HN-030 complete, plus the two host holes found on the way.
updated: 2026-08-24
current-answer: §1 what shipped · §2 THE ROUND-TRIP (the verification that found real losses) ·
  §3 where the estimate was wrong · §4 §Gates · §5 the mutation table · §6 the ids · §7 what is still open.
design-basis: 📄 docs/MCP_Integration.md § "BUILT — the catalog is GENERATED from the routes" (written by
  this batch) · Architect_Question_54 § Manifest scope (Q54-1 / charter D4) · tracker HN-030.
known-rot: none. ⚠ EPHEMERAL — the durable record is MCP_Integration.md § BUILT.
-->
# REPORT — **HN-030: the tool catalog is generated from the routes**

> ⛔⛔ **This report is EPHEMERAL.** ⭐⭐⭐ The durable record is
> **[`MCP_Integration.md` § BUILT](../../MCP_Integration.md)** — written before this batch closed
> *(obligation ⑤)*.

⭐⭐ **Headline:** `tool-catalog.mjs` is now **generated**. The catalog's 66 endpoint-backed tools come from
`RouteDoc`s beside their routes, via `--mode dump-api`. ⭐⭐⭐ **And the move was verified by regenerating and
DIFFING against the hand-written catalog — which found real losses that reading would not have.**

## 1. ⭐⭐ WHAT SHIPPED

| piece | where |
|---|---|
| ⭐⭐ **`RouteDoc` / `RouteParam`** | `Hrot.Editor/DebugApi/RouteDoc.cs` — tool name, group, summary, params *(with `enum`/`items`/`properties`)*, returns, notes, example, hint, `NotATool` |
| ⭐⭐ **the 66-entry table** | `Hrot.Editor/DebugApi/DebugApiRouteDocs.cs`, keyed by `(method, path)`, ordered to mirror the route table |
| ⭐⭐ **`--mode dump-api`** | `Program.cs` + `DebugApiHost.DumpManifestJson` — prints the manifest and exits, **booting nothing** |
| ⭐ **`EnumerateRouteTemplates`** | the enabling seam: a throwaway host enumerates the table with no port, world or DDS |
| ⭐⭐ **`gen-catalog.mjs`** + `catalog-supplement.mjs` | `npm run gen:catalog` writes the catalog; **`gen:catalog:check`** fails when it is stale |
| ⭐⭐⭐ **`EveryRouteIsDocumentedTests`** | 4 rails — the part that actually kills the drift |
| ⭐ **two host holes** | `start_simulation`'s `mode` param; `/shutdown` becomes a real route |

## 2. ⭐⭐⭐ THE ROUND-TRIP — **the verification, and why it mattered**

⭐ The content was not rewritten, it was **moved**: the existing catalog was already correct. So the check
that matters is *"does regenerating reproduce it?"* — and it did **not**, three times:

| # | 📐 what the diff caught | why nothing would have failed |
|---|---|---|
| **①** | the first `RouteParam` carried only name/type/required/description/default ⇒ **5 `enum`s dropped** *(`get_event_history.bus`, `start_recording.mode`, `step_replay.dir`, `get_logs.level`, `add_annotation.type`)*, **2 `items`**, **3 `properties`** | ⛔ those reach the agent's `inputSchema`. A missing `enum` does not error — **it widens the contract**, so the tools would silently have accepted anything |
| **②** | the JS transform copied only four param fields | same loss again, one layer further on — ⭐ which is why the check was re-run after each fix rather than once |
| **③** | `patch_attribute.patchJson` deliberately has **no** `type` *(object OR JSON string)*; a non-nullable `Type` rendered it as the literal string `"undefined"` | ⛔ a type of `"undefined"` is not a JSON-schema type; it would have shipped as one |

⭐⭐ **Final: 66 tools, 66 endpoints, `lost: []`, `gained: []`, CONTENT DIFFS: 0.**
⚠ Compared with keys sorted, so a key-order change does not read as a content change.

## 3. ⛔ WHERE THE ESTIMATE WAS WRONG — **in both directions**

| | |
|---|---|
| ⛔ **the row's *"the hard part is already done by the manifest"* was FALSE** | 📐 `endpoints[]` carried **three** fields *(method, path, capability)*; the route table is `RouteEntry(Method, Template, Handler)`. Of 92 params only **14** were derivable *(path params)*; 78 live as string literals inside handler lambdas |
| ⛔ **and my counter-estimate was ALSO wrong** | I called `RW-M` *"mis-sized"* and described a *"route-descriptor refactor"*. ⚠ It is `RW-M`, as the row said — because the docs already existed and moving them was scriptable. ⭐ The user's question *("is the cost just adding the descriptions?")* was the correct reading |
| ⭐ **what was genuinely design, not transcription** | one *(the non-HTTP supplement)*. Of my other three objections: naming-in-C# was a choice I mislabelled as a cost, the alias was settled by ruling *(and retired)*, and *"types aren't free"* was simply wrong |

## 4. ⭐⭐⭐ §GATES

| # | gate | verbatim command | `--no-build`? | result · delta |
|---|---|---|---|---|
| 1 | build | `dotnet build IOS-IG-SimHost.sln --no-restore` | must build | ⭐ **0 errors** |
| 1 · 8 | ⭐⭐⭐ **the integration gate** | `bash scripts/run-system-tests.sh` | builds | ⭐ **83 / 83, 0 fail, 0 skip** *(unchanged — the manifest gained a `doc` field per endpoint and no rail cared, which is the additive shape intended)* |
| 8 | ⭐⭐⭐ **the enforcement rail** | `dotnet test Hrot.Editor.Tests --filter FullyQualifiedName~EveryRouteIsDocumentedTests` | `--no-build` | ⭐ **4 / 4** |
| 8 | the editor suite | `dotnet test Hrot.Editor.Tests --no-build` | `--no-build` | ⚠ **239 / 240, 1 fail** — `AiHotReloadCoordinatorTests.TwoReloadCycles_OldAlcIsCollected`. ⭐ **PRE-EXISTING, proven properly:** at a genuinely clean base *(`git stash push -u` — the first attempt left my untracked files and `--no-build` ran MY binary, so that comparison was void)* the base is **235 / 236 with the same single failure**. ⭐ Passes in isolation ⇒ a GC/ALC flake |
| 8 | ⭐⭐ **the round-trip** | `gen-catalog.mjs --dump …` then a field-by-field diff vs the pre-move catalog | n/a | ⭐ **66 tools, 0 content diffs** — §2 |
| 8 | ⭐ **the JS gates** | `npm run test:catalog` · `gen:catalog:check` · `gen:skill:check` | n/a | ⭐ **529 / 529** · **PASSED (66 tools, 66 endpoints)** · **PASSED** |
| 8 | ⭐ **the dump boots nothing** | `dotnet Hrot.ClusterRunner.dll --mode dump-api` | n/a | ⭐ exits 0 in ~1 s, no DDS/window/world; **66 endpoints, 66 docs, `unclassifiedRoutes: []`, 1 `notATool`** |
| 2 | out-of-solution / stale bin | — | — | ⭐ all gated projects are in the solution; every `--no-build` followed a full build of the same tree |
| 3 | golden movement | `git status --short` | — | ⭐ **ZERO goldens moved.** ⚠ **`tool-catalog.mjs` DID move — and that is the deliverable**: same 66 tools, byte-different because it is now generated *(sorted keys, group headers, a "GENERATED — do not edit" banner)*. ⭐ Content proven identical by §2. **`SKILL.md` is byte-identical in size and content** |
| 4 | every RED pre-existing, by name | *(row 8)* | — | ⭐ one, named and proven at a clean base |
| 5 | working tree clean after every suite | `git status --short` | — | ⭐ clean; all three mutation probes reverted and verified *(`grep -c "MUTATION PROBE"` ⇒ **0**)* |
| 6 | quarantine counts | — | — | ⭐ **0 skips before, 0 after** *(the 1 skip in `Hrot.Editor.Tests` is pre-existing and present at base)*. ⛔ No new filter |
| 7 | doc gates + ids | `tracker-counts.py --check` · `rulings-check.py` · `design-digest.py --check` · `mermaid-check.mjs` | — | ⭐ **OK (open 99 / done 333)** · **24/24, 3 known staleness WARNs** · **designs OK** · **4/4 mermaid blocks parse** |

## 5. ⭐⭐⭐ THE MUTATION TABLE

| # | mutation *(reverted by inverse edit)* | what reddened | expected? |
|---|---|---|---|
| **M4** | ⭐⭐ **add a route with no doc** — `GET /mutation-probe-undocumented` | `EveryRouteCarriesADoc`: *"1 route(s) have no RouteDoc, so the generated tool catalog cannot describe them and no agent will ever discover them"* | ✅ yes — **this is the exact defect that shipped as `switch_perspective`** |
| **M5** | ⭐⭐ **add a doc for a route that does not exist** | `NoDocDescribesARouteThatIsGone`: *"1 RouteDoc(s) describe endpoints the host does not serve, so the generated catalog would advertise tools that 404"* | ✅ yes |
| **M6** | ⭐ **hand-edit the generated catalog** *(change one summary)* | `gen:catalog:check` **FAILED** and named the fix command | ✅ yes |

⭐ Every probe rebuilt before a conclusion was drawn, and all three were restored and re-verified.

## 6. ⭐ RULE 5 — the ids allocated

| id | |
|---|---|
| ✅ **`HN-030`** | **CLOSED** |
| ✅ **`HN-040`** | `RouteDoc`/`RouteParam` + the 66-entry table + `NotATool` |
| ✅ **`HN-041`** | the round-trip's three findings *(enum/items/properties, and the nullable type)* |
| ✅ **`HN-042`** | `EveryRouteIsDocumentedTests` + `gen:catalog:check` + the three mutations |
| ✅ **`HN-043`** | `start_simulation`'s `mode`; `/shutdown` as a real route |
| 🔴 **`HN-044`** | **open** — nothing directly asserts the hand-written supplement |

## 7. 🔴 WHAT IS STILL OPEN — **stated, not smoothed**

| ⛔ | ⭐ |
|---|---|
| **the prose is still hand-written** | 92 param descriptions, 66 `returns`/`hint`/`example`, 57 `notes`. ⭐ Generation moved **where** it is authored and made an undocumented route fail — ⛔ it did not make the writing go away, and claiming otherwise would be the false green `D4` exists to kill |
| **the supplement is unpoliced** *(`HN-044`)* | one entry, `start_simulation`, which genuinely has no endpoint. ⭐ `test-catalog.mjs` covers it indirectly; a direct assertion is cheap and missing |
| ⚠ **the docs are a keyed table, not inline per route** | ⭐ enforcement, not proximity, is what stops drift — but the user asked for adjacency and this is one step short of it. ⭐ Inline-per-route is a legitimate follow-up; it would add ~1000 lines to a 535-line method, which is why it was not done blind |
| ⚠ **`gen:catalog` needs a built runner** | it shells `dotnet … --mode dump-api` *(or takes `--dump <file>`)*. ⭐ Cheap — the dump boots nothing — ⛔ but it is a build-order dependency `gen:skill` did not have |
