# Behavior Parameters & the Resolver — Detailed Design

> **Status:** design draft (2026-07-13) — resolves the "how do JSON-authored behaviors parse/post-process parameters without a curated `AiBehaviorFactory`" question. Describes the **future state**; no code has been written for it yet. Supersedes the ad-hoc, factory-injected `ParseParams` closure model.
> **Scope:** how a behavior (BTree or HSM, hardcoded or JSON-authored) declares its authored parameters, its runtime-usable parameters, and an optional **resolver** that bridges the two; how the resolver runs exactly once at activation; how behaviors register and are referenced **by name**; and the end-to-end authoring workflow. Does **not** re-specify blackboard bin-packing, variable roles/scopes, or blueprint compilation — those are owned by the docs cited below.
> **Audience:** AI-behavior authors (editor users), engine/editor engineers implementing the resolver seam, and reviewers.
> **Related canonical docs:** `BTree_AiActionParameterBinding_Detailed_Design.md` (§4.4 the `Node`/`Behavior`/`Entity` scoped-variable model this builds on; §3 whole-DTO binding & the `[BlueprintRegistrar]` masquerade registrar), `Blackboard_Authoring_Addendum_v3_ActionParamAuthoring.md` (§4 the "runs exactly once at assignment via `BehaviorIngressSystem`" defaults→overlay→write pipeline this generalizes), `Blackboard_Authoring_Detailed_Design.md` (Category-1/2 variables, bin-packing), `Blueprint_Subsystem_Architecture_v1.2.md` (Library/AiPrimitive/Instance dispatch, world singletons), `AI-Behavior-Authoring.md` (§7 registration flow), `docs/AI_DEV_GUIDE.md` (the current hand-written `ParseParamsDelegate`).
> **Companion code lives in:** `FDP/Toolkits/Fdp.Toolkits/Behavior/` (`BehaviorRegistry.cs`, `Systems/BehaviorIngressSystem.cs`, `Events/AssignBehaviorEvent.cs`), `Hrot/Subsystems/Hrot.AI.Behaviors/` (`AiBehaviorFactory.cs`, `Brains/HillAttackCommanderNodes.cs`, `Brains/HillAttackDtos.cs`), `Hrot/Engine/Hrot.Core/MapDefinitions/Behavior/` (authored DTOs + `BehaviorContractAttribute`), `Hrot/Engine/Hrot.Presentation/Behavior/` (`BehaviorUiCompiler.cs` field generation), `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/VariablesPanelControl.cs`.

---

## Table of Contents
1. Goal & motivation
2. The model in one picture
3. Core concepts
4. The authoring workflow
5. Runtime: registration, identity, and the resolver hook
6. Mapping to the current implementation (ground truth)
7. Open implementation gaps & phasing
8. Cross-references / supersedes

---

## 1. Goal & motivation

Today a behavior's parameters are turned into runtime-usable form by a **hand-written `ParseParamsDelegate`** wired inside the curated `AiBehaviorFactory`. For `PlatoonHillAttack` that delegate closes over `geoTransform` and `entityMap` captured at registration time:

```csharp
// AiBehaviorFactory.cs:211-212 — the closure that pins the factory in place
ParseParams = (json, ptr) => HillAttackCommanderNodes.ParsePlatoonHillAttackParams(
    json, ptr, geoTransform, entityMap);
```

This is the single reason the factory cannot be retired: the JSON-generated per-asset registrar supplies **no** `ParseParams` (it can't — it has nowhere to get `geoTransform`/`entityMap`), so a JSON-authored behavior has no way to post-process its parameters. The result is the double-registration bug (a curated record under id `3014` *with* the parser, and a generated record under a GUID-derived id *without* it), where whichever registers last wins the name and may silently skip parsing.

This design removes the factory's reason to exist by making parameter post-processing a **named, first-class part of the behavior** — a **resolver** — that reaches world services (geo transform, entity map) as **world singletons** rather than captured closures, and that any authoring path (hardcoded or JSON/blueprint) can declare identically.

Three principles drive the design:

- **The behavior is the contract.** A behavior, identified by its **name**, owns everything: its authored parameter shape, its runtime-usable variables, its working state, and its resolver. There is no separate reusable "param contract" abstraction (none was needed; see §3.1).
- **One shape by default; two only when they must differ.** Simple behaviors declare a single parameter shape and need no resolver. Only behaviors whose *authored* shape genuinely differs from their *usable* shape (geographic coordinates, entity references, derived fields) pay the cost of a second shape plus a resolver.
- **Any part is independently visual-or-code.** Tree/HSM, authored params, runtime variables, and resolver each slide between "authored in the editor" and "supplied in C#" without the others noticing, because the only seam between them is the **name**.

## 2. The model in one picture

```
                       AUTHORING TIME                         │        RUNTIME (once, at activation)
                                                              │
  ┌───────────────────────┐     ┌────────────────────────┐   │   scenario JSON ("behaviorParams")
  │  Authored params       │    │  Runtime variables      │   │            │
  │  (editor DTO)          │    │  Role=Input  (usable)   │   │            ▼
  │  PickableGeoPoint,     │    │  Role=State  (working)  │   │   1. auto-deserialize ──► Authored DTO
  │  net-id, pick attrs    │    │  Scope=Node/Behavior/   │   │            │
  └───────────┬───────────┘     │        Entity           │   │            ▼
              │                  └────────────┬────────────┘   │   2. RESOLVER (once) ─── reads world
              │  detach (only if shapes differ)│              │      singletons (geo, entity map)
              │                                 │              │            │  writes ▼
              └──────── RESOLVER  ──────────────┘              │       Input variables  (usable params)
              (identity by default; a Library blueprint        │            │
               function, or a hardcoded C# function,           │            ▼
               referenced BY NAME from the behavior)           │   3. provision + zero-init State slots
                                                              │            │
                                                              │            ▼   behavior ticks
```

The behavior name is the join key across the whole diagram and across the authoring/runtime boundary.

## 3. Core concepts

### 3.1 The behavior is the contract (keyed by name)

A behavior owns, under its **name**, a single registry record (`BehaviorDefinition`) carrying: the tree/HSM interpreter, the authored-params type, the runtime variables (as the existing `StatefulWorkingSlots` manifest + inline param region), and an optional resolver reference.

- **Name is the stable external identity.** Scenarios already reference behaviors by name (`"behaviorName": "PlatoonHillAttack"` in the scenario file; resolved via `BehaviorRegistry.TryGetId`). The integer id is a **private registry handle** — it may differ between builds and between the hardcoded and generated producers, and nothing outside the registry may hardcode it.
- **Duplicate name = hard error.** Two registrations of the same name is a mistake (or an intended replacement of a hardcoded behavior by a JSON one) and must fail loudly, not silently overwrite. This is the fix for the double-registration bug (§5.1).
- **No shared param-contract layer.** We considered keying the contract by the authored-param *type* so multiple behaviors could share it. No concrete use case surfaced, and it adds an indirection. If one ever appears it is an **additive** change (a behavior points at a shared named param-type) that does not break the behavior-owns-it model. Reuse of *transform logic* is still available at a finer grain: the resolver is a Library blueprint function, which is itself reusable (§3.3).

### 3.2 The three data shapes

A behavior has up to three shapes, with three distinct lifecycles:

| Shape | Role | Authored? | Populated | Lifetime |
|---|---|---|---|---|
| **Authored DTO** (e.g. `PlatoonHillAttackParamsJsonDto`) | editor fields + JSON schema | **yes** (reflected attributes) | deserialized from scenario JSON — **transient** parse buffer | parse-time only |
| **Usable params** — variables with `Role=Input` (e.g. `PlatoonHillAttackParams`) | hot-path input | no | resolver writes them (or auto-deserialize when identity) | resolved **once at activation** |
| **Working state** — variables with `Role=State` (e.g. `HillAttackMutableState`) | hot-path scratch | no | zero-init at provisioning | **reset at activation**; `Scope` = `Node`/`Behavior`/`Entity` (see `BTree_AiActionParameterBinding_Detailed_Design.md` §4.4) |

> **Vocabulary note.** The variable **role** enum is `{ Input, State }` — there is no separate "Param" role. `Input` *is* the parameter role. This doc uses "usable params" and "`Role=Input` variables" interchangeably.

**One shape by default.** The author defines the runtime `Input` variables (the tree binds to those). The authored DTO is, by default, an **attached mirror** of them — same field names and types, auto-generated, not separately editable. In this state:

- authored shape == usable shape,
- the resolver is the **identity** (plain auto-deserialize straight into the params slot),
- there is exactly one shape to think about, and the behavior is simple in the editor as well as conceptually. (The wingmen in the sample scenario — `{X, Y, Speed, ArrivalRadius}` — are exactly this case.)

**Two shapes only on divergence.** When the authored surface must differ from the usable form — a `PickableGeoPoint` the designer clicks on the map vs. the Cartesian `StartX/StartY` the tree reads, a network id vs. a resolved `Entity`, a derived field like `AttackDir` with no author-facing source — the author **detaches** the authored shape (§4.2). It forks from the usable shape, becomes independently editable, and from then on a resolver bridges the two. `PlatoonHillAttack` is the canonical two-shape behavior (four geo points → four Cartesian points + a derived direction vector + one resolved entity).

### 3.3 The resolver

The resolver is the function that produces the usable `Input` variables from the authored DTO. It is **always present**; its default is the identity (== auto-deserialize). Key properties:

- **Referenced by name from the behavior.** The behavior names its resolver; absent ⇒ identity. There is no "contract default + behavior override" two-level scheme — the behavior is the contract, so there is one level.
- **Two producers, one name.** The named resolver resolves to **either** a hardcoded C# static function **or** a user-authored **Library blueprint** function (a stateless, non-instance blueprint — see `Blueprint_Subsystem_Architecture_v1.2.md`). The behavior cannot tell which produced it and does not care. So a visually-authored behavior may use a hardcoded resolver (perf-critical or fiddly math) and a hardcoded behavior may use a visually-authored one.
- **Reaches world services as singletons, not closures.** This is the linchpin. Today `ParseParams` obtains `geoTransform`/`entityMap` by closure capture inside the factory. The resolver instead reaches them as **world singletons** through the simulation view — the same mechanism blueprint functions already use (`BlueprintRegistry` world-singleton registration). This is what lets the resolver be declared by a JSON asset with no factory in the loop.
- **Logical signature** (authoring view): `resolve(in TAuthored authored, ref TUsable usable, ISimulationView view, Entity self, float time)`. Pure math (normalise a vector) can be a plain Library function; anything needing entity/world context reads it from `view`.
- **Physical signature** (runtime dispatch): evolves today's `ParseParamsDelegate(string json, byte* memory)` into a resolver delegate that also receives the view + self, so it can reach singletons without a closure. The generated code auto-deserializes the JSON into the managed `TAuthored` (using the `ParamsDtoType` already recorded on `BehaviorDefinition`) and then invokes the resolver against the `byte*` usable slot.

### 3.4 Lifecycle: resolve once, at activation

A behavior's lifecycle is **assignment ≡ activation**: there is no re-activation without a fresh assignment (deactivation is brain-death). Consequently the resolver runs **exactly once per lifetime**, at activation, and the double-conversion trap (re-running a geo conversion over already-converted values) is structurally impossible — a second run requires a second assignment, which re-deserializes fresh authored JSON first.

The activation sequence (one pass of `BehaviorIngressSystem.Execute` consuming one `AssignBehaviorEvent`) is:

1. **auto-deserialize** the authored JSON payload → `TAuthored`;
2. **resolver** runs once → writes the `Input` variables (identity resolver ⇒ this is just the deserialize);
3. **commit** `BehaviorState` (active behavior, instance id, tier);
4. **provision + zero-init** the `State` working-state partition slots.

This generalizes the pipeline already documented in `Blackboard_Authoring_Addendum_v3` §4 ("runs exactly once at assignment"), inserting the resolver as an explicit, name-referenced step between deserialize and slot provisioning instead of fusing it into a hand-written parser.

## 4. The authoring workflow

### 4.1 Simple behavior (no resolver)

1. Create the behavior; give it a **name** (this is its identity).
2. Draw the tree/HSM. Nodes reference `Input`/`State` variables by name.
3. Declare the variables in the variables panel (`Role=Input` for params, `Role=State` for scratch with a `Scope`).
4. Done. The authored shape is an attached mirror of the `Input` variables; the resolver stays identity; scenario JSON auto-deserializes straight in. No blueprint, no transform.

### 4.2 Behavior needing a transform (detach + resolver)

1–3. As above, but the `Input` variables are the *usable* shape (`StartX/Y`, `AttackDir…`, `TargetAreaEntity`, …).

4. **Declare the authored fields.** A palette maps authorable field types onto the existing pickable attributes:
   - "World location" → `PickableGeoPoint` + `[MapPickableWorldLocation]`
   - "Entity reference" → `long` + `[RemapNetworkId]` + `[MapPickableEntity("…")]`
   - number / enum / bool → scalar fields.
   These generate the authored DTO (the drawn equivalent of `PlatoonHillAttackParamsJsonDto`).

5. **Detach the authored shape** when it must diverge from the usable shape. It forks (pre-filled from the usable shape) and becomes editable. This promotes the implicit identity resolver to an explicit, editable one (pre-seeded as identity).

6. **Divergence detection surfaces what to fill in.** The editor continuously diffs authored ↔ usable and flags real gaps — an `Input` variable with no authored source and nothing writing it ("`AttackDir` will be zero at runtime — write it in the resolver"), a type mismatch, a dropped field. Identical-but-detached shapes raise nothing (no busywork). A real gap makes the resolver **required**.

7. **Pick or create the resolver.** A "Parameter resolver: [None] · [Pick…] · [Create…]" control on the behavior:
   - **Pick** an existing Library blueprint function (or a registered hardcoded function) by name.
   - **Create** scaffolds a new **Library blueprint function** in a `.bp.json` asset with the signature pre-filled — input = this behavior's authored DTO type, output = its `Input` variables, world singletons (geo transform, entity map) available in scope — and jumps into the blueprint editor with an empty body. The author wires the transform visually (geo-convert ×4, resolve-entity, normalise-vector for `AttackDir`, assign) and saves. The behavior now references it **by name**.

8. Done. At runtime, activation runs deserialize → resolver → commit → provision (§3.4).

### 4.3 Choosing visual vs code, per part

| Part | Authored visually | Supplied by code |
|---|---|---|
| Tree / HSM topology | node graph → `.btree.json` | `BTreeBuilder`/`HsmBuilder` in C# |
| Authored params | typed fields with pickable types → generated DTO | hand-written `…JsonDto` with `[MapPickable…]` attributes |
| Runtime variables (`Input`/`State`) | variables panel (role + scope) | hand-written structs |
| Resolver | a Library blueprint function | a hardcoded C# static function |

Every row is independent; the name is the seam, and the registry is producer-blind (§5.2).

## 5. Runtime: registration, identity, and the resolver hook

### 5.1 Name as identity; hard error on collision

`BehaviorRegistry` is keyed by both int id and name (`_definitions: Dictionary<int, BehaviorDefinition>`, `_nameToId: Dictionary<string,int>`). The design requires:

- **Name is authoritative.** External references (scenarios, behavior-to-behavior comparisons) resolve by name.
- **Collision is a hard error.** `Register(id, name, def)` must reject a name (or id) that is already present, instead of the current silent last-writer-wins overwrite. The **blueprint** registry already throws on id collision (`BlueprintRegistry.RegisterDirect`) — copy that behavior into `BehaviorRegistry`.
- **`ActiveBehaviorHash` becomes name-derived.** `BehaviorState.ActiveBehaviorHash` is today an `int` holding the registry id (the "hash" name is misleading) and is set from a name→id lookup. Under name-as-identity it should be `FNV(name)` (or otherwise stable across registration schemes), so behavior-to-behavior comparisons are stable regardless of which producer minted the record. This kills the magic-int comparison in combat code (§6 item G).

### 5.2 One name table, two producers; retiring the factory

Both the curated `AiBehaviorFactory` and the generated per-asset registrar are `[BlueprintRegistrar]` types discovered and invoked by `AiHotReloadCoordinator.ScanForRegistrars`. Today both run and both register "PlatoonHillAttack" (bug). Once each behavior carries its own resolver **by name**, and world services are reachable as singletons, the factory has nothing left to aggregate:

- the interpreter + variable manifest already come from the generated registrar;
- the resolver comes from the behavior's named reference;
- geo/entity come from world singletons, not a factory closure.

So `AiBehaviorFactory` is deleted. Each behavior self-registers once under its name; a duplicate name is the loud signal that a hardcoded behavior is being replaced by a JSON one (delete the hardcoded one). Hardcoded behaviors remain first-class — they self-register the same way, just from C#.

### 5.3 The resolver hook

The single hook point is the region of `BehaviorIngressSystem.Execute` between the parse-commit and slot provisioning. The resolver call replaces today's fused `ParseParams` invocation, now split into auto-deserialize + resolver, both driven by the behavior's declared resolver name and both reaching world singletons through the simulation view.

## 6. Mapping to the current implementation (ground truth)

Verified against the branch `claude/hill-attack-json-slice-3-7fbaf4` (2026-07-13):

- **A — Blueprint kinds & Library dispatch.** `BlueprintDispatchKind { Library, AiPrimitive, Instance }` exists (`FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintDispatchKind.cs`); `BlueprintRegistry` is name-addressable (`TryGetByName`) and has world-singleton registration. **Caveat:** the callable `TickDelegate`/`EventHandlerDelegate` on `BlueprintDefinition` are for **Instance** dispatch only — a Library-kind definition carries no runtime delegate today (see gap §7-G2).
- **B — Activation seam.** `BehaviorDefinition.ParseParams` is invoked at exactly one production site, `BehaviorIngressSystem.cs:96`, driven by `AssignBehaviorEvent.JsonParams`; slot provisioning (`ProvisionStatefulSlots`) runs immediately after the `BehaviorState` commit. Assignment ≡ activation. The seam for the resolver is `BehaviorIngressSystem.cs:~119`.
- **C — Variables.** Roles `BlackboardVariableRole { Input, State }`; scopes `WorkingStateScope { Node, Behavior, Entity }` (editor) mirrored by `StatefulSlotScope` (runtime). Authored in `.btree.json` (`"Role":"State","Scope":"Behavior"`) and edited in `VariablesPanelControl` (role/scope combos). No "Param" role.
- **D — Registration/identity.** `BehaviorRegistry.Register(int id, string name, def)` keyed by both; **silent overwrite** on duplicate (no guard). `TryGetId(name)` present.
- **E — Two producers.** `AiBehaviorFactory.BuildRegistrationAction` (curated, id 3014, **with** the geo `ParseParams`) and the generated `PlatoonHillAttackRegistrar` (GUID-derived id, **no** `ParseParams`); both discovered by `ScanForRegistrars`; name maps to whichever sorts last.
- **F — DTO field generation.** `BehaviorSchemaDiscovery` scans `Hrot.Core` for `[BehaviorContract]` DTOs; `BehaviorUiCompiler.BuildPropertyRenderers<TDto>` reflects over properties and dispatches on `[MapPickableEntity]`/`[MapPickableWorldLocation]`/`PickableGeoPoint`/scalar types; `[RemapNetworkId]` handled in a second pass (`BehaviorParamRemapperCompiler`).
- **G — `ActiveBehaviorHash`.** `int` field on `BehaviorState` holding the registry id; set in `BehaviorIngressSystem` from `TryGetId(name)`; compared in `HillAttackCommanderNodes.cs` against `const int HullDownAttackRunBehaviorId = 3013` (duplicates `BehaviorIds.HullDownAttackRun_BT`). This magic-int comparison is the code smell the name-based rule removes.
- **H — Resolver concept.** None exists. The only param transform is the hand-written `ParseParams` lambdas (`AiBehaviorFactory`) and the emitted `EmitParseParamsLocal` (`BTreeBridgeEmitCore`). The resolver is a new abstraction on a clean existing seam.

## 7. Open implementation gaps & phasing

These are the pieces that do **not** exist yet and must be built (roughly in order):

- **G1 — Split deserialize from resolve.** Today `ParseParams` fuses them. Add a generic auto-deserializer keyed by `BehaviorDefinition.ParamsDtoType`, and a resolver delegate that runs after it. Evolve `ParseParamsDelegate` to also pass `ISimulationView` + `Entity self` so it can reach world singletons without a closure.
- **G2 — Make Library blueprint functions runtime-invocable.** A Library-kind `BlueprintDefinition` has no callable delegate today (only Instance does). A *blueprint-authored* resolver needs Library functions to be dispatchable at ingress. **Phasing:** ship the **hardcoded** resolver path first (trivial — a named delegate on the behavior record), then add Library-function invocation for the fully-visual path.
- **G3 — Geo transform & entity map as world singletons.** `NetworkEntityMap` is already a world singleton; confirm/one-off the geographic transform as a world singleton reachable from the resolver via `ISimulationView` (it is currently injected at registration). Prerequisite for retiring the factory.
- **G4 — Hard-error on duplicate name in `BehaviorRegistry`.** Copy the blueprint registry's collision-throw. This both enforces name-as-identity and fixes the double-registration bug.
- **G5 — `ActiveBehaviorHash` → name-derived**, and replace the `3013` magic constant in `HillAttackCommanderNodes` with a name-based lookup.
- **G6 — Retire `AiBehaviorFactory`** once G1–G5 land, moving each behavior's resolver reference + params onto its self-registration.
- **G7 — Editor affordances:** the "detach authored shape" action, divergence detection, and the "Parameter resolver: None/Pick/Create" control with Library-function scaffolding. Builds on the existing `VariablesPanelControl` and `BehaviorUiCompiler`.

Each gap is independently landable behind the others except where noted (G6 depends on G1–G5; the blueprint-resolver path depends on G2).

## 8. Cross-references / supersedes

- **Supersedes** the factory-injected `ParseParams` closure model described operationally in `AI-Behavior-Authoring.md` §7.4 and `docs/AI_DEV_GUIDE.md` "Writing the ParseParamsDelegate" — those describe the current state this design replaces.
- **Builds on** `BTree_AiActionParameterBinding_Detailed_Design.md` §4.4 (the `Node`/`Behavior`/`Entity` scoped-variable model; this doc adds the authored↔usable resolver on top of it) and `Blackboard_Authoring_Addendum_v3_ActionParamAuthoring.md` §4 (the once-at-assignment runtime pipeline; this doc names the previously-inline resolve step and makes it pluggable).
- **Coordinates with** `Blueprint_Subsystem_Architecture_v1.2.md` for Library-function dispatch and world singletons (gap G2/G3).
