<!--STATUS
state: LIVE
updated: 2026-10-12
doc-type: HANDOFF — behaviors lane → a second session (Track B of the orders programme). Dispatched at 7dca4d807 (branch `behaviors`).
current-answer: §1 the scope · §2 the tasks in order · §3 traps · §4 gates
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - docs/DESIGN_Orders_Params_Entity_Classes.md — THE design (rules §1, classes §2, sequences §3, modules §4, task list §6); this file only points at it
  - docs/blueprints/Architect_Question_90_Unit_Orders_Carry_Capacity.md — why (R-273..R-281); read §2.3e, §2.3g–§2.3j, §2.3m B
  - docs/blueprints/DESIGN_Parameter_Model.md — the start pipeline you change; update its §1 / §P.7 when B3 lands
-->

# Handoff — orders programme, Track B: params internals

⭐ This file points at the design; it is not one. Draw no UML here; extend
[DESIGN_Orders_Params_Entity_Classes.md](../DESIGN_Orders_Params_Entity_Classes.md).
⛔ Your scope is FROZEN at the dispatch sha; documents that change after it are FYI. A later document that invalidates an
item ⇒ STOP that item and report it (R-106), do not adapt.

## 1. Scope

The params pipeline: generators (BTree, HSM, blueprint), `BehaviorRegistry`, `BehaviorIngressSystem`, the editor's variable
sections, the params-DTO registry, the params DTO asset. NOT the order behaviours, the class table or the menu (other tracks).

## 2. Tasks, in order *(design §6)*

| id | task | note |
|---|---|---|
| **CE-2133** B1 | a single struct-typed Input parses FLAT; an asset may name its params DTO (`ParamsDtoTypeId`, FQN string) | ⭐ FIRST: the behaviors lane's CE-2132 (A2) waits for it. Precedent for a type named by text: variable `TypeId` → `global::` FQN (`BTreeBridgeEmitCore.cs:1874`), blueprint `DtoTypeFqn` (`Nodes.cs:1390`) |
| **CE-2134** B2 | `[Behavior(Params=…)]` replaces `[BehaviorContract]`; `ParamsDtoRegistry` in `Hrot.Core` (one scan of LOADED assemblies; order DTOs carry their order name as a string); palette de-duplicated by DTO type; load check P3 | consumers to move: `BehaviorSchemaDiscovery` (bind, UI schema, remapper), `BehaviorCatalog` (3 callers), `IntentContractPaletteEntries`, `BTreeBridgeEmitCore.cs:1720`. Coordinate with Track D's CE-2140 (it defines `OrderAttribute` in `Hrot.Core`) |
| **CE-2135** B3 | no Input / State roles; reserved `Params` only without a resolver; `NoParams` stores nothing | ⚠ the trap: `InputBytes` = 1 while `RootParamsBytes` = 0 makes `HostedSubtree.Supply` throw (`RootParamsAccess.cs:270-274,323-324`, `HostedSubtree` :439-443). Role is read only at generate time (`BTreeBlackboardPackHelper.cs:129,177`, `BTreeEmitCore.cs:214-223`, `BTreeJsonGenerator.cs:229`) |
| **CE-2136** B4 | `*.params.json` asset + generator + editor | `VariablesPanelControl` takes any `IEditableAsset` + type choices |
| **CE-2129** B5 | stateful slots salted by behaviour, detached on every change except a paused task's | `BehaviorIngressSystem.cs:310,589-598` |

## 3. Traps

- Build the TEST project, never the production one; `--no-build` after (stale-binary trap).
- Goldens move: report the diff SHAPE (files, added / removed lines), never just "regenerated".
- Generators are `netstandard2.0` and see only JSON text: a DTO is referenced by FQN string, never by `typeof` at generate time.
- The conformance rail (CE-2130, behaviors lane) keeps an allow-list; when a task removes a violation, remove its entry.

## 4. Gates

`python3 scripts/tracker-counts.py --check` · `python3 scripts/design-digest.py --check` · `python3 scripts/rulings-check.py` ·
the feature suites you touch (T-1 first) · report per the gate-report contract (CLAUDE.md rule 8).
