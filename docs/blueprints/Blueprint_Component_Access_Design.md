# Blueprint Component Access — implementation design (READ + WRITE)

**Status: cleared to build.** Consolidates the two approved architect rounds — **Q#15 (component READ)** and
**Q#16 (component WRITE)** — into one implementation design. Branch: `claude/blueprint-component-read`.
Companion: `Blueprint_Component_Access_TASK_TRACKER.md` (batches + checkboxes).

## What we're building

Two designer nodes that read/write an entity's real **ECS components** (distinct from blackboard Variables /
Shared slots), mirroring the shipped `GetShared`/`SetShared` multi-pin machinery:

| Node | Direction | Entity | Field mode |
|------|-----------|--------|-----------|
| `GetComponent` (extend existing) | **read** | self **or `Target`** | multi-pin: one out-pin per wired field; `Found` pin |
| `SetComponent` (new) | **write** | **self only** | unmanaged: per-field in-pins (wired-only); managed: single whole-value in-pin |

## Constraint model (from Q#15/Q#16 — enforce all)

- **Reads** are RO, all-components, self+`Target`, fail-safe (`Found`, never throw). Exposed via **member
  access** (`view.GetComponentRO<T>(e).Field`) → *no* blittable/offset requirement; managed fields readable.
- **Managed values:** read + pass-to-managed-consumer only; **never persist** (BP1503 — Variable/WorkingState/
  Shared) and **never mutate** (snapshots share the reference — aliasing). Managed read API =
  `view.GetManagedComponentRO<T>(e)` (baked `IsManaged` flag). Reject **managed→unmanaged wiring** at validate-time.
- **Writes** are **self-only**, gated to the **`[BlueprintWritable]`** component set. Unmanaged = direct per-field
  `GetComponentRW<T>(self).F = v` (wired-only; unwired preserved). Managed = **ECB whole-replace**
  `ecb.SetManagedComponent(self, fresh)`; **per-field managed forbidden**. **Write-if-present** (no implicit add;
  absent component → graceful `Found`/error, no ECB add). Race-free: `BlueprintTickSystem` is Simulation-phase
  `[UpdateBefore]` the Locomotion/Weapon dispatchers; system outputs (`SimTransform`, physics) are excluded by not
  marking them `[BlueprintWritable]`.

## Node models (`Hrot.Blueprints.Compiler/Assets/Nodes.cs`)

- **`GetComponentNode`** (extend): keep `ComponentTypeFqn`; **add** `List<ComponentFieldDecl>? Fields`
  (multi-pin; null ⇒ legacy single-field via `FieldName`), `bool IsManaged`. `Target` + `Found` are projected
  pins (Stage0 / NodePinSchema), not stored fields. Additive JSON (`[JsonIgnore(WhenWritingNull/Default)]`).
- **`SetComponentNode`** (new): `ComponentTypeFqn`, `List<ComponentFieldDecl>? Fields` (unmanaged per-field),
  `bool IsManaged`. JsonDerivedType `"SetComponent"`.
- **`ComponentFieldDecl`** (new): `{ string Name; string TypeId; }` — **no `Offset`** (component access is typed
  member access, not a byte read; contrast `SharedFieldDecl`).
- **`[BlueprintWritable]`** (new attribute): marks a component type as blueprint-writable. Co-locate with the
  component/`[BlackboardDtoStruct]` contracts (confirm assembly at build — likely `Fbt.Kernel`/`Fdp.Core`).

## IR & emit

| Op | New? | Emits |
|----|------|-------|
| `IrOp_GetComponentRO(fqn, e, T)` | exists | `view.GetComponentRO<global::T>(e)` |
| `IrOp_GetManagedComponentRO(fqn, e, T)` | **new** | `view.GetManagedComponentRO<global::T>(e)` (managed read) |
| `IrOp_FieldRead(src, name, T)` | exists | `__t{src}.{name}` (per read field) |
| `IrOp_HasComponent(fqn, e)` | exists | `view.HasComponent<global::T>(e)` (drives `Found` + write-if-present) |
| `IrOp_GetComponentRW(fqn, self)` | reuse `IrOp_GetComponent` | `ref var __c = ref view.GetComponentRW<global::T>(self)` |
| `IrOp_WriteComponentField(target, name, val)` | **new** | `__c.{name} = __t{val};` (per wired field) |
| `IrOp_SetManagedComponent(fqn, self, val)` | **new** | `ecb.SetManagedComponent(self, __t{val});` (whole-replace) |

**Concrete emit shapes:**
```csharp
// READ unmanaged multi-field (Found + 2 fields)
var __c = view.GetComponentRO<global::T>(target);        // once
bool found = view.HasComponent<global::T>(target);       // Found pin
var __f0 = __c.Health;  var __f1 = __c.Armor;            // per field

// READ managed field (read-and-pass only; not persistable/mutable)
var __m = view.GetManagedComponentRO<global::T>(target);
var __f0 = __m.Name;

// WRITE unmanaged per-field (write-if-present; wired-only, unwired preserved)
if (view.HasComponent<global::T>(self)) {
    ref var __c = ref view.GetComponentRW<global::T>(self);
    __c.Health = __t{v0};                                 // only wired fields
}

// WRITE managed whole-replace (ECB, deferred)
ecb.SetManagedComponent(self, __t{wholeValue});           // never per-field
```
`ecb` is already a `Tick(span, view, ecb, entity, time, dt, version)` parameter (Instance) — confirm the
AiPrimitive path carries an ECB too; confirm the exact `HasComponent`/`GetManagedComponentRO`/`SetManagedComponent`
API names on the view/ECB at build.

## Compiler stages

- **Stage0_Rehydrate** — `EnrichGetComponentPins` (Target? in, Found out, per-field Value outs when `Fields`
  baked; legacy single `Value` when not) and `EnrichSetComponentPins` (exec In/Out, per-field data-ins for
  unmanaged / single managed value-in, `Written` out). Mirror `EnrichGet/SetSharedPins`. **Keep in lockstep with
  `NodePinSchema`.**
- **Stage2_Validate** — `V_ComponentAccessRules` (new, ~BP2060–BP2067): write component must be `[BlueprintWritable]`;
  reject `Target` on a write (self-only); reject per-field **managed** write (must whole-replace); reject
  **managed→unmanaged** wiring (read side); component/field FQNs non-empty & well-formed.
- **Stage4_TypeResolve** — resolve component field types (AN2 trust-string; managed types allowed for read pins;
  BP1503 already blocks persisting managed into state).
- **Stage5_Schedule** — `GetComponentNode` multi-field: read-once (`IrOp_GetComponentRO`/`…ManagedRO`) + N×
  `IrOp_FieldRead`; `SetComponentNode`: `HasComponent` guard + `IrOp_GetComponentRW` + N× `IrOp_WriteComponentField`
  (unmanaged) **or** `IrOp_SetManagedComponent` (managed).
- **Stage7_Emit / StatementEmitter** — cases for `IrOp_GetManagedComponentRO`, `IrOp_WriteComponentField`,
  `IrOp_SetManagedComponent`.

## Editor (`Hrot.Blueprints.Editor`)

- **`ComponentFieldReflector`** (new) — reflects a component type's public fields → `(Name, TypeId, IsManaged)`,
  **no offset**, **does not reject managed fields** (unlike `SharedStructFieldReflector`). Flags each field
  managed/unmanaged for the persistence caveat.
- **Discovery / palette** — `ComponentTypeProvider`: reflect **all** component types (read picker) and
  **`[BlueprintWritable]`** types (write picker) at editor startup. `GetComponentPaletteEntries` /
  `SetComponentPaletteEntries` in `BlueprintEditorBootstrap.CreatePaletteRegistry`.
- **`NodePinSchema`** — `GetComponentPins` (Target?, Found, per-field outs) + `SetComponentPins` (per-field ins,
  Written) — **parity with Stage0**.
- **Drawer** (`ComponentNodeDrawers`, mirror `SharedNodeDrawers`) — component picker + field-expand toggle;
  managed fields shown with the **persistence caveat**; write drawer restricted to the writable set.
- **`BlueprintNodeModel`** — titles `Get Component [T]` / `Set Component [T]` (bracketed, per the shipped
  convention); **`NodeState.Error`** + tooltip when a baked component/field no longer resolves (reuse the
  `IsUnresolvedClrCall` path); baked data preserved, never dropped.

## Robustness (reuse Q#15 §Robustness)

Picker fills by reflection at editor **startup** (add component → rebuild → restart → appears). Removed/renamed
component or field → reflector returns null → red **error node** + tooltip; editor refuses Quick Reload / Full
Rebuild on an error node; compiler backstop = Roslyn **CS0246** (unresolved `global::T`) → no runnable assembly.
Editor is the primary guard (netstandard2.0 compiler can't reflect).

## Slices → build order (see tracker for batches)

1. **1a — unmanaged read** (foundation: reflector, discovery, picker, self+Target, `Found`, multi-pin projection +
   `IrOp_FieldRead` lowering, title, stale-ref). *No gate.*
2. **W1 — unmanaged write** (`[BlueprintWritable]`, `SetComponentNode`, per-field write lowering +
   `IrOp_WriteComponentField`, `HasComponent` guard, validator, drawer/palette).
3. **1b + W2 — managed** (`IsManaged`; read `GetManagedComponentRO` + managed→unmanaged rejection + UI caveat;
   write `SetManagedComponent` ECB whole-replace + reject per-field managed).
4. **2 — collections (R1 curated-accessor, A2 UX)** — see the dedicated section below. CA-07a (collection
   pin + GetComponent out-pin) ✅, CA-07b (consumer nodes + IR + emit), CA-07c (wire-bake + palette + drawers),
   CA-07d (managed collections + `Contains`/`Find`, deferred).

## Collections — Slice 2 (R1 curated-accessor, A2 Unreal UX)

**Decided Q17-A = A2 UX (full Unreal collection-pin), R1 mechanism (curated accessors).** The auto-resolve
premise in the Q#17 draft was invalidated by a codebase reality-check (see that doc's ⚠ section): this ECS has
**no `FixedList`/`DynamicBuffer`** — only `[InlineArray]`/`fixed` buffers whose *logical count* is a separate
field, and whose raw access is `unsafe` and architect-ruled **off-graph (Q#5-C)**. So collections are exposed via
**curated accessor pairs**, not reflected off the raw buffer.

**The accessor contract** (`Fdp.Core.BlueprintCollectionAttribute` + `…ItemAttribute`): a component exposes a
virtual collection through two `public static` helpers, mirroring `UnitRosterOps`:
```csharp
[BlueprintCollection(typeof(TComp), "Name")]      public static int      Count(in TComp c);
[BlueprintCollectionItem(typeof(TComp), "Name")]  public static TElement Item (in TComp c, int i);
```
Element type = `Item`'s return type. The pair is grouped by `(ComponentType, Name)`; a lone/malformed accessor
declares nothing. Exactly the reflection-free-bakeable formalization of what `FlowForEach` already bakes.

**Data model.** `ComponentFieldDecl` gains (byte-stable, `JsonIgnore`d when default): `IsCollection`,
`ElementTypeId`, `CountAccessorFqn`, `ItemAccessorFqn`. A collection decl sets `TypeId=""`.

**Pin (A2 UX).** `GetComponent` projects **one collection out-pin** per collection decl —
`BlueprintTypeRef { TypeId = ElementTypeId, IsArray = true }` (reuse the existing `IsArray` flag; no new pin
kind), in `Fields` order between the scalar field pins and `Found`. Stage0 `EnrichGetComponentPins` ⇄ editor
`NodePinSchema.GetComponentPins` kept in exact parity (real-Stage0 parity test). **The pin carries only the
entity at runtime** — each consumer re-reads `GetComponentRO<Comp>(e)` (can't carry a `ref`/unmanaged buffer
across nodes reflection-free).

**Discovery + bake (editor).** `ComponentFieldReflector.TryReflectCollections(fqn)` scans loaded assemblies for
attributed static methods, validates signatures, pairs by name, sorts ordinal; the GetComponent picker
(`ApplyComponentTypeFqn`) appends the discovered collection decls to `Fields`.

**IR & emit (CA-07b — the novel core).** New consumer nodes (`ComponentForEach` / `ComponentItemGet` /
`ComponentItemCount`) take a **collection in-pin** and carry baked `(ComponentTypeFqn, CountAccessorFqn,
ItemAccessorFqn, ElementTypeFqn)`. New IR ops lower to emit that reads the component once then calls the baked
FQNs — `global::{Count}(in c)` / `global::{Item}(in c, i)` — **identical to `FlowForEach`'s emit**, so the
collection *kind* (`fixed`/`[InlineArray]`/managed) never reaches the compiler. `IrOp_ForEach` left untouched
(FlowForEach goldens stay byte-identical). Author-time accessor baking hooks
`BlueprintCommandSink.ApplyAddLink` (CA-07c). `AccessorFqn` uses `DeclaringType.FullName` — guard nested ops
classes (the `+` separator) at emit.

**Stage5 (CA-07a).** The GetComponent multi-field loop **skips** collection decls (`if (f.IsCollection)
continue;`) — no `IrOp_FieldRead`; the collection pin is consumed by CA-07b, not read here.

## Gate

Clean-rebuilt **`Hrot.AiEditor.Generators.Tests`** proof suite (**184 byte-identical**, run **SERIAL** via the
`xunit.runner.json` in `bin`) must stay green — component nodes are **additive** (new JSON fields
`[JsonIgnore(WhenWritingNull/Default)]`), so existing goldens round-trip unchanged. Plus per-slice new tests
(pin projection, lowering, emit shape, validator diagnostics, stale-ref, managed rejection). Editor tests for
reflector/discovery/drawer/title.

## Build-time details to confirm (not blockers)

- `[BlueprintWritable]` assembly location (co-locate with component contracts).
- Exact view/ECB API names: `HasComponent<T>`, `GetManagedComponentRO<T>`, `SetManagedComponent`.
- AiPrimitive tick has an ECB in scope for managed writes (Instance does).
