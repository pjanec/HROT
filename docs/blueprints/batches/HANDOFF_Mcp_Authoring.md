<!--STATUS
state: LIVE
build-state: DISPATCH
updated: 2026-08-25
current-answer: dispatch pointer for the MCP AUTHORING surface (AI-asset editing + scenario authoring) — a
  SEPARATE session. Carries NO design: cites DESIGN_Mcp_Authoring.md (classDiagram + sequenceDiagram + §3
  the serialization caution + §8 the collision plan).
known-conflict: ⚠ shares the DebugApi surface with the CE-* editing slices — CE-011 is MERGED, so this
  branches from a CE-011-inclusive base and adds on top (§8). No live CE-slice runs concurrently on DebugApi.
-->
# HANDOFF — **the MCP authoring surface** *(AI-asset editing + scenario authoring — separate session)*

> 📌 **Dispatched at `8cf450cec`** *(CE-011-inclusive — the open/read/save/reload it layers on are all in)*.
> ⛔ **Scope FROZEN at that sha.** ⭐ **Branch fresh from `claude/blueprint-authoring-status-6sr5ld`**
> *(rule 7)*; **rule 1b: started-marker naming `8cf450cec` BEFORE any code.** ⛔ **No PR.** ⭐ **You allocate
> the ids** *(rule 3)* — use a **`MA-`** prefix *(MCP Authoring)* in a **new tracker area** so it stays clear
> of `BP-`/`HN-`/`CE-`; state every id *(rule 5)*.

## 0. ⛔⛔ THE DESIGN IS THE SOURCE — this file is a POINTER
📄 **[`DESIGN_Mcp_Authoring.md`](../../DESIGN_Mcp_Authoring.md)** *(READY-TO-BUILD)* — §1 the two surfaces,
§2 inventory, **§3 the read-shape caution** *(reuse the JSON FORMAT, NOT the save serialization — two id
spaces)*, §4 determinism dropped, §5 classDiagram, §6 sequenceDiagram, §7 the items, §8 the collision plan,
§9 gates. ⭐ Build what §5/§6 draw; report the match *(obligation ③)*; fold deviations into the design
*(obligation ⑤)*. 📄 Decision trail: `Architect_Question_56` *(resolved)*.

## 1. ⛔⛔ NEW BUILD/TEST RULES APPLY
`.claude/CLAUDE.md` → THREE TEST TIERS → the `2026-08-24` rule. Build the AFFECTED PROJECT, never the whole
solution in the fix loop. E2E/system suite is T3 — async. Prove each fix through the rail that reddens;
pre-existing reds proven by `git diff`, not rebuild.

## 2. ⭐⭐⭐ WHAT TO BUILD *(design §7)*
| # | task | the one thing not to get wrong |
|---|---|---|
| 🔴 **①** | **`GET /assets/{id}/graph`** — an **in-memory-faithful** serialization *(nodes/pins/links/params with the IN-MEMORY guids)* | ⛔⛔ **NOT `SaveActiveBlueprintCommand`** — it rewrites to deterministic name-derived pin ids + strips pins for on-disk persistence *(§3, two id spaces)*. ⭐ reuse the JSON FORMAT; `BlueprintClipboard` *(in-memory subgraph serialize)* is the closer analog. **Read and edit MUST share one id space — the in-memory guids** |
| ⭐ **②** | **AI-asset edit routes** — `POST /assets/{id}/graph/{nodes,links,params,remove}` → `CommandBuilder` → the **same command sink human editing uses**; add-node RETURNS its guid | ⭐ one path + undo/inverse free; ⛔ don't build a parallel graph-mutation model |
| ⭐ **③** | **`POST /assets` create** → `INewAssetService` per kind | reuse |
| ⭐ **④** | **Scenario authoring** — extend the existing `/entities/*` world-manipulation ops; `scenario/save` snapshots | ⛔ no "edit a scenario file"; one way, no modes |
| ⭐ **⑤** | validation via the existing `IAssetValidator` set; edits hot-apply via CE-011's save→QuickReload | ⚠ the §17 Soft/Hard classification is NOT on the QuickReload path *(CE-023)* — ⛔ don't assert a classification it can't produce |
| ⭐ **⑥** | **every route: a `RouteDoc` + a handler in `src/index.mjs`** | 📌 CE-009 §4c caught six advertised-but-unreachable tools — ⛔ don't repeat; `test-catalog` gates it |

## 3. ⭐ HOW TO TEST *(design §9)*
✅ conformance/rails. Headline: **round-trip** — `GET /assets/{id}/graph` → add a node+link over MCP → the
re-read shows them *(by the returned guids)* → save+reload → the running brain reflects it. Plus: create-asset
appears in `GET /assets`; a scenario-authoring entity op is snapshot by `scenario/save`. `gen:catalog`/
`gen:skill`/`test-catalog` green for every new route+handler; conformance suite as the integration gate.
⭐⭐⭐ **You MAY extend the harness/MCP** for testing; ⛔ don't fake a pass; ⚠ AiShared changes beyond additive
⇒ STOP and coordinate with the variable-model lane.

## 4. ⭐ LANE, SCOPE & COLLISION *(design §8)*
⭐ **Yours:** a **new** `Hrot.Editor/DebugApi/DebugApiService.Authoring.cs` · the in-memory graph serializer
*(reuse/extend clipboard-style)* · `Hrot.SystemTests/**` · `tools/ai-debug-mcp/**` *(routes + handlers)*.
⛔⛔ **Do NOT edit `DebugApiService.Assets.cs`** *(the CE-slices' file)*. ⚠ the shared registration/generated
files *(`DebugApiHost`, `DebugApiRouteDocs`, `tool-catalog.mjs`, `SKILL.md`, `src/index.mjs`,
`CapabilityManifest`)* — you own the additions since no CE-slice runs concurrently, but ⭐ **rule 4: re-pull
coordinator before the final commit** in case a next CE lands. ⛔ Reuse the command sink — do NOT modify
`Hrot.Editor.AiShared` beyond additive.

## 5. GATES *(rule 8 contract)*
one row per gate · verbatim command · counts · delta vs `8cf450cec` · `--no-build` column · pre-existing reds by diff · `tracker-counts.py --check` · `rulings-check.py` · `design-digest.py --check` · the `MA-` ids. **Row 8 rails:** the round-trip headline *(RED by reverting the edit routes)* · add-node returns a resolvable guid · create-asset appears in `GET /assets` · a scenario entity op snapshots · `gen:catalog`/`gen:skill`/`test-catalog` green.

## 6. ⭐ WHEN DONE
Fold the as-built into `DESIGN_Mcp_Authoring.md`; mark `AQ56` BUILT; state the `MA-` ids; the report points at the design. Report per obligation ③.
