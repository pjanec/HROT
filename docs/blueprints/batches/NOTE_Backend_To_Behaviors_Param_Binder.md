<!--STATUS
state: LIVE — backend → behaviors: the shared param binder (macros S3a item ①) is pushed — rules S2 builds on it
updated: 2026-11-02
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Scenario_Macros.md — §5.1.3 the as-built of this piece (classes, deviations)
  - ../../DESIGN_Scenario_Rules.md — rules S2: the trigger record whose When / Do takes this base
  - HANDOFF_Coordinator_To_Behaviors_Objectives_Triggers.md — "merge backend's item-① commit before editing the predicate DTOs"
  - HANDOFF_Coordinator_To_Backend_Scenario_Macros_S3a.md — item ①
-->
# NOTE — backend → behaviors: **the param binder is in** (macros S3a ①)

**Merge `backend` at `3683f1487`** (or any later `backend`) before you edit the predicate DTOs in rules S2 ③.

| what you get | where |
|---|---|
| `BindableNode` — abstract CLASS with `Dictionary<string,string>? Bind` (property name of THIS node → param name; not written to JSON when null). ⭐ `SearchPredicateDto` already derives from it, so every predicate node is bindable | `FDP/Toolkits/Fdp.Toolkits/Scenario/ParamBinding.cs` |
| ⚠ **your `do` entry derives from `BindableNode` too** — and since a C# `record` cannot derive from a class, make it a `sealed class` with `init`-only members (CLAUDE.md *NEW COMPONENTS* allows that form; it is still replaced whole). `TriggerDefinition` itself may stay a `record` (it HOLDS nodes, it is not one) | design §5.1.3 |
| `ParamBinder.Apply(value, IParamSource)` / `ApplyJson(type, json, src)` — binds every `"^param"` leaf and every node's `Bind`, at ANY depth of a predicate tree (the walker now follows a `SearchPredicateDto` member by its RUNTIME type, `$type` in JSON) | same file |
| ⭐ the walker change also means `RefRemap` / `RefResolve` now reach references INSIDE a predicate tree (they never did: the list element was planned as the abstract base) — an `EntityLink` in a trigger's `When` is remapped / resolved at load like any other | `EntityRefRemap.cs` |
| `IParamSource` + `JsonParamSource(values, resolveOutside)` with `Overlay(defaults, args)` — the flat overlay | same file |
| ⛔ do NOT write a second base or binder (handoff); a `Bind` key must name a SETTABLE property of the node (loud otherwise) | rails `ParamBinderTests` |

_backend lane, 2026-11-02_
