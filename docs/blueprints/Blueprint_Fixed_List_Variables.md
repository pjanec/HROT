# Fixed-List Blueprint Variables (FC-2)

Fixed-capacity, blittable lists as blueprint variables — declared like any variable, mutated
in place, zero heap, zero reflection. Capacity is fixed at declare time; `Count` is the live
logical length.

![Fixed-list state layout](img/fixed_list_layout.svg)

## At a glance

| What | How | Where |
|---|---|---|
| Declare | My Blueprint ➕ → Container: **List (fixed)** → capacity + initial length (budget line shows state bytes) | `VariableCreateModal` → `BlueprintTypeRef.Capacity/InitialLength` |
| Read | The **same five collection consumers** as component collections: For Each / Get Item / Item Count / Contains / Find, wired from the list's GetVariable pin | Stage5 binds `ref s.MyList` (no entity, no component read) |
| Write | Six **`ListWrite`** verbs: Add / Set At / Insert At / Remove At / Clear / Resize (palette: *Variable → Add (List)* …) | `IrOp_ListWrite` → in-place Span-form mutation |
| Clone | `SetVariable(listB ← GetVariable(listA))`, **identical shape only** (same element type + capacity) | flat struct copy — no loop, no Span |
| Debug | Watch renders `List<Int32>[4] Count=2 {5, 7}` | `StateFields` descriptor + `BlueprintDebugSession` |
| Demo | `ListVariableDemo.bp.json` (Tutorial) — Add per tick, fills to capacity, 5th Add rejects | `Hrot.AI.Behaviors/Recipes/Blueprints/` |

## Write-verb contract

| Verb | Operands | Ok=false when | Zeroing (G6) |
|---|---|---|---|
| Add | Value | list full | — |
| Set At | Index, Value | index ∉ [0, Count) | — |
| Insert At | Index, Value | full, or index ∉ [0, Count] | — |
| Remove At | Index | index ∉ [0, Count) | vacated last slot |
| Clear | — | never (no Ok pin) | whole used prefix |
| Resize | Length | length ∉ [0, Capacity] | dropped tail on shrink |

Failed ops write nothing (probe-reported in Debug builds). Unwired required operands degrade
to a safe no-write at compile time.

## Diagnostics

| Code | Fires when |
|---|---|
| BP1504 | declared `InitialLength` outside `[0, Capacity]` |
| BP1505 | `ListWrite` target is not a declared fixed-list variable (empty binding flagged once exec-wired) |
| BP1506 | list value wired to a pin that can't take a list — anything but a consumer's `Collection` pin or an identical-shape `SetVariable` clone |
| BP1507 | fixed-list type declared on a `Parameter` — lists live on Variables / WorkingState / action DTOs, never Parameters or Shared (the Shared side is fenced at the wire by BP1506) |
| BP2066 | consumer wired to a list but the wire-bake state is missing (Kind-aware) |

## The action-DTO home (FC-3)

Hand-authored C# actions (BTree/HSM) use the same wrapper shape in their Params/working-state DTOs
— runtime access is plain `ref` C#; the tooling around it:

| What | How | Where |
|---|---|---|
| Declare | `struct WaypointList { public int Count; [InlineArray(4)]-backed Items; }` — the **one canonical shape** (`FixedListShape`); loose `Items`+`Count` twin fields stay read-only passthrough | `Fdp.Core.FixedListShape` (single definition; classifier + converter + inspector all delegate) |
| Editor recognition | classifier marks the wrapper field EditorManaged, Variables panel shows `List<T>[4]` (display-only v1) | `BlackboardFieldClassifier` / `BlackboardTypeHelper` |
| JSON authoring | plain array — `"Stops": [10, 20]`; Count = length clamped to `[0,N]`, tail zeroed (G6), writes emit the used window; element types inherited from the canonical options (vectors, FixedStrings, enums, custom unmanaged structs; `Entity` → author `Null` only) | `FixedListJsonConverterFactory` in `FdpJsonOptionsRegistry` (both singletons; generated `ParseParams` uses them) |
| Mutate from C# | direct `ref` + the Span-form write pattern, or the `[BlueprintCollectionField]` generator's ops class | FC-0/FC-1b convention |
| Inspect live | `LiveBlackboardPanel` renders `List<Int32>[4] Count=2 {10, 20}` (shared formatter); any StructEdit host gets a count-bounded element view via `FixedListBufferViewProvider` | `Fdp.Core.FixedListFormatter` + `Hrot.Editor.AiShared.Inspector` |
| F2 safety | every working-state attach path (fresh, free-list reuse, hard-reload re-provision) hands out zeroed payload | `BlueprintBlackboardPartitions.TryAttach` (pinned by `SlotAttachZeroingTests`) |

One formatter, every surface: the Blueprints debugger watch, the behavior blackboard panel, and
StructEdit collapsed rows all render the identical summary string.

## Limits (v1)

- Element must be unmanaged (no `String`); no nested lists.
- Self-state only: instance `Variables`, AiPrimitive `WorkingState`, and action DTOs (zero-on-attach guaranteed).
- Not accepted by `GetShared`/`Parameters` — enforced: BP1507 on a list-typed Parameter declaration, BP1506 on a list wired into Shared.
- Editor capacity UI clamp: 1–256.
- Action-DTO editor recognition and inspection are display-only (no visual re-type/resize of C# source).
