# Architect question #21 — fixed-list fields in ACTION DTOs (recognition, authoring, inspection)

**Status: ✅ APPROVED (architect, 2026-08-03) — all recommended leans ratified as-is:**

| Decision | Ratified answer |
|---|---|
| A — recognized shape | **A1** — wrapper-struct pattern only (`int Count` + one `[InlineArray(N)]` buffer) → `EditorManaged`, `List<T>[N]` display; loose twin-field DTOs stay passthrough |
| B — pipeline scope | **B1** — wire the classifier into the existing Variables-panel path, display-only in v1 |
| C — JSON authoring | **C3 via C1** — plain-array form through a `JsonConverterFactory` (Count = length clamped to `[0,N]`, G6-zeroed tail; write emits used window; elements recurse through the enclosing options), registered in BOTH `FdpJsonOptionsRegistry` singletons + the one-line `__paramJsonOpts` switch in `BTreeBridgeEmitCore` |
| C-e — Entity elements | allowed; **author `Entity.Null` only** (documented), handles are runtime-written |
| D — inspector | **D3** — StructEdit-based (`FixedListViewProvider`, `min(Count,N)` window, collapsed row = shared summary formatter) |
| D-e — edit vs display | **display-only v1** |
| D-p — placement | **P1** — StructEdit gains only the generic InlineArray provider-hook parity; provider + formatter live host-side, dependencies flow host → StructEdit only |

Cleared to build FC-3a (classifier + panel) → FC-3b (JSON) → FC-3c (inspector + F2 verify) → docs.

The third and final home of the Fixed Collections
umbrella (FC-3). The component home (Q#20, FC-0/FC-1/FC-1b shipped) and the blueprint-variable home
(Q#19, FC-2 shipped: LV-1…LV-6) are done. Review R4 of the umbrella
(`Blueprint_Fixed_Collections_Design.md` §Second review) ruled this home "not just recognition" —
this doc asked the resulting decisions.
See `Blueprint_Fixed_Collections_Design.md` (umbrella) + `Blueprint_Fixed_List_Variables.md` (FC-2 result).

## The need

A hand-authored C# behavior action (BTree/HSM) wants a **fixed-capacity list in its Params or
working-state DTO** — the same `[InlineArray(N)] Items + int Count` shape the other two homes use.
Runtime read/write by ref is **already free and verified** (true `ref` all the way through the
delegate chain — umbrella review "confirmed sound"). What is missing is the *tooling* around it:

1. the behavior editor's blackboard tooling doesn't recognize the shape (classifier has no array concept);
2. an initial value can't be authored in behavior JSON (`System.Text.Json` cannot populate an
   `[InlineArray]`'s private backing field);
3. the live inspector is composite-blind (renders primitives only);
4. the F2 reused-slot zeroing hazard applies to a stateful action's working state.

## What is already settled / verified (inherited, not re-asked)

| Fact | Evidence |
|---|---|
| Runtime by-ref access is free — no graph/compiler work in this home | umbrella 2nd review, "confirmed sound" |
| **F2 zero-on-attach is ALREADY fixed at the choke point for actions too** — `BehaviorIngressSystem.AttachSlotsToMemory` funnels every fresh/resized attach through `BlueprintBlackboardPartitions.TryAttach`, which zeroes the slot since FC-2/LV-1b (`Unsafe.InitBlock`, poison-byte tested) | `BehaviorIngressSystem.cs:683` → `TryAttach`; `SlotAttachZeroingTests` |
| The canonical DTO shape + curated accessor/ops convention exists and is generator-backed | FC-0/FC-1b: `[BlueprintCollectionField(nameof(Count))]` → `{Component}{Field}Ops` (`CollectionOpsGenerator`) |
| G6 invariant (slots ≥ `Count` always `default`) and the Span-form write pattern are engine-wide rules | Q#20 G6/R3; pinned by tests |
| A reflection-based watch formatter for the wrapper shape exists (`List<T>[N] Count=k {…}`, F2-clamped) | FC-2/LV-5 `BlueprintDebugSession.TryFormatFixedList` |
| `BlackboardFieldClassifier` six-condition rule; **no live production caller today** | `Hrot.Editor.AiShared/Blackboard/BlackboardFieldClassifier.cs`; umbrella R4(a) |

## Sub-questions

### A — What shape does the classifier recognize, and as what classification?

A DTO fixed list is two coupled fields (`[InlineArray(N)] Buf Items; int Count;`) or a wrapper
struct. The classifier's six-condition rule currently forces ReadOnlyPassthrough on ANY attribute
(condition 4) — so an `[InlineArray]` field is passthrough today by accident, not by design.

- **A1 (lean):** recognize the **wrapper-struct pattern only** (a struct whose fields are exactly
  `int Count` + one `[InlineArray(N)]` buffer — the FC-2 `__List_…` shape, hand-authorable too) as a
  first-class known type → `EditorManaged` with a `List<T>[N]` display type. Loose
  `Items+Count` twin-field DTOs stay passthrough (preserved byte-for-byte, documented pattern).
- **A2:** also recognize the loose twin-field pattern by name convention (`X` + `XCount`?) —
  more surface, fragile matching.
- **A3:** keep everything ReadOnlyPassthrough; recognition = display-only (no Variables-panel
  editing), i.e. drop the "EditorManaged" ambition entirely.

**Claude's lean: A1.** One canonical recognizable shape, shared with the other two homes; the
loose pattern keeps working at runtime but isn't editor-managed. A3 underdelivers (the Variables
panel is where capacity/initial-length authoring should live, mirroring the LV-4 declare-UX); A2
is name-matching fragility for little gain.

### B — Recognition pipeline: who calls the classifier, and how far does FC-3 stand it up?

R4(a): the classifier has **no live production caller** — the "pipeline" (parse fields → classify
→ drive the Variables panel / round-trip writer) exists as tested library code only.

- **B1 (lean):** FC-3 wires the classifier into the **existing blackboard Variables panel path
  only** (the panel that already lists DTO fields for stateful actions), so recognized list fields
  display with the `List<T>[N]` type and capacity badge — read-only in v1 (no visual re-type /
  re-capacity of C# source).
- **B2:** full stand-up — classifier drives visual add/rename/re-type of DTO fields including
  lists (the round-trip source writer). A much larger, mostly-orthogonal workstream.
- **B3:** no editor recognition at all; document the pattern + rely on the inspector (D) only.

**Claude's lean: B1.** B2 is a source-round-trip editor feature that predates and exceeds
fixed-lists — wrong slice to smuggle it into. B3 leaves designers blind at author time.

### C — Initial values: custom JSON converter, or constructor-seeded only?

STJ cannot populate `[InlineArray]` private backing fields, so `ParseParams` silently loses an
authored initial list.

- **C1 (lean):** ship a **generic `JsonConverter` for the wrapper shape** (writes/reads
  `{"Count":k,"Items":[…]}`, clamps `k` to `[0,N]` on read — BP1504's runtime twin, G6-zeroes the
  tail) registered where behavior-JSON `ParseParams` builds its options.
- **C2:** no JSON authoring; initial values come only from C# (`static Params Default` /
  constructor). Simplest, but diverges from every other Params field being JSON-authorable.
- **C3:** flatten to a plain JSON array (`"Waypoints":[1,2]`, Count implied by length) — prettier
  authoring, but asymmetric with the byte image and needs the same converter machinery anyway.

**Claude's lean: C3 for authoring ergonomics, implemented via the C1 converter** (read accepts the
array form, infers Count = length ≤ N, zeroes tail; write emits the array of the used window).
The designer never sees `Count` in JSON; the byte image stays canonical.

**C addendum — element types + reuse (verified 2026-08-03).** The platform already has canonical
STJ machinery the converter should ride, not duplicate:

| Fact | Evidence |
|---|---|
| Scenario save/load + DDS round-trips use ONE canonical options singleton with per-type converters: Vector2/3/4 + Quaternion (compact `[x,y,z]` form), FixedString32/64, strict string enums, `IncludeFields=true` | `Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed` |
| Custom unmanaged struct elements serialize field-wise through those options with NO extra work (`IncludeFields=true`) | STJ default behavior |
| The generated `ParseParams` today builds its own bare `new JsonSerializerOptions { IncludeFields = true }` — it does NOT use the registry | `BTreeBridgeEmitCore.cs:157` (`__paramJsonOpts`) |

**Design consequence:** implement the list converter as a `JsonConverterFactory` whose per-element
read/write recurses via `JsonSerializer.…<TElem>(…, options)` — element support is then *inherited*
from the enclosing options (primitives, vectors, FixedString, enums, and arbitrary unmanaged structs
all work day one). Register it in `FdpJsonOptionsRegistry` (both singletons) and switch the emitted
`__paramJsonOpts` to `FdpJsonOptionsRegistry.DefaultRelaxed` — one line in `BTreeBridgeEmitCore` —
so behavior JSON, scenario save/load, and diagnostic dumps all gain list support at once.

**Entity elements:** structurally fine (unmanaged `(Index, Generation)`; a converter exists but is
private to `RecordingExportService` — hoist or rely on `IncludeFields`). *Semantically*, an authored
non-null handle is meaningless — generation/index don't survive across runs, and state snapshots
round-trip as the byte image, never through JSON. Sub-decision **C-e:** allow `Entity`-element lists
but document "author `Entity.Null` only" (lean), or reject Entity lists in JSON authoring outright.

### D — Inspector marshal: reuse the LV-5 formatter, or structured rows?

`LiveBlackboardPanel` renders primitives only; a list field shows nothing useful.

- **D1:** reuse the **LV-5 reflection formatter** (`TryFormatFixedList`) — one string row
  `List<T>[N] Count=k {…}`, F2-clamped, zero new UI. Move/share the helper so the BTree editor can
  call it without referencing the Blueprints editor.
- **D2:** hand-rolled structured expandable rows (one row per element, editable). Real UI work;
  editing live bytes raises write-safety questions (who owns the write? tick races).
- **D3 (StructEdit-based — verified viable 2026-08-03):** the in-repo **StructEdit** library
  (`FDP/ExtDeps/StructEdit`, already consumed by the HSM/BTree/IG editors) **already supports
  `[InlineArray]` end-to-end over native bytes**: reflection discovery
  (`DetermineKind → EditNodeKind.InlineArray`), `InlineArrayBinding` with per-element offsets into
  a `NativeStructEditBuffer`, per-element child nodes — and struct elements recurse into full
  per-field node trees (a `Float3`-element inline array is test-covered, J001-T5). What it does
  NOT have is **list semantics**: all N slots render (Count is just another int field), no
  Count-clamped window, no bounded add/remove. The library's `IBufferViewProvider` hook (custom
  views that "claim" a buffer — already used for `FixedBuffer`) is the designed extension point:
  a `FixedListViewProvider` recognizing the A1 wrapper shape would present `min(Count,N)` element
  rows + a bounded resize driven by `Count`. Two small upstream tweaks needed: consult providers
  on the InlineArray path too (today only FixedBuffer does), and — only if StructEdit.Json is ever
  wanted for lists — fix its container serializer's `ToString()` fallback for struct elements.

**Claude's lean: D3.** The support is largely built and battle-tested elsewhere in the repo; the
`FixedListViewProvider` is a genuinely reusable addition (BTree/HSM inspectors AND
`[InlineArray]` component fields get it too), and it delivers element-level display instead of a
string. The LV-5 string formatter stays where it already ships (the Blueprints debugger watch) —
no conflict. D1 remains the fallback if FC-3 must stay minimal; write-safety for *editing* live
working-state (vs displaying) is a sub-decision — **D-e:** display-only v1 (lean) vs editable
rows from day one.

**D addendum — why NOT unify the blueprint watch onto StructEdit (asked 2026-08-03).** The
blueprint debugger is a genuine special case, not an oversight:

| Constraint | Blueprint watch | StructEdit |
|---|---|---|
| Type source | **collectible ALC** generated types (`State`, `__List_…`) — full unload on hot-reload is a tested invariant (`VerifyAlcUnloadOnDispose`) | statically compiled types (components, HSM/BTree DTOs) — the only StructEdit consumers today |
| Retention | transient `byte[] + Type → string`; nothing rooted, ALC-safe by shape | persistent `EditDocument` whose node metadata holds `ClrType`/binding refs — pins a collectible ALC unless documents are rebuilt/discarded on every reload |
| Memory model | renders a **snapshot copy** (also works on dead bytes) | binds **live native memory** (that's the point — editing) |
| Purpose | display-only grid cell | structured display + editing machinery |

**Unify the FORMATTING, not the mechanism:** relocate the LV-5 helper (`TryFormatFixedList` +
element primitives) so ONE definition of the summary string and the F2 clamp exists; the watch
keeps calling it transiently, and the `FixedListViewProvider` uses the SAME helper for its
collapsed/summary row (per-element nodes on expand). Two hosts, each keeping the mechanism its
constraints demand. If the blueprint debugger ever wants expandable/editable state rows,
StructEdit is the destination — as its own workstream with an explicit ALC lifecycle design
(rebuild-on-reload, discard-on-unload), not inside FC-3.

**D-p — placement (StructEdit is an INDEPENDENT library in ExtDeps; it must not reference
non-StructEdit code).** `IBufferViewProvider` is a public extension interface — providers are
handed in by the host, so the HROT-specific pieces need not enter the library at all:

- **P1 (lean):** StructEdit gains ONLY a generic, convention-free change — consult providers on
  the `InlineArray` path as the `FixedBuffer` path already does (an upstream gap regardless).
  The `FixedListViewProvider` (our Count+buffer wrapper convention, F2 clamp, G6 semantics) AND
  the shared summary formatter live side-by-side in a host-side shared editor lib, referenced by
  both the blueprint watch and the provider. Dependencies flow host → StructEdit only.
- **P2:** upstream "bounded fixed list" (`int Count` + one `[InlineArray(N)]` buffer) as a
  first-class GENERIC StructEdit node kind, formatter included, maintained to library standards
  (own tests, zero HROT references); the watch then calls into StructEdit (also a legal
  direction). Cleaner if the pattern is deemed general C#12, not HROT convention; P1's provider
  is exactly the code that would later promote, so P2 can be deferred without waste.
- **P3:** duplicate the formatter — rejected (the clamp rule would exist twice and drift).

## Reuse-vs-build summary

| Piece | Reuse | Build |
|---|---|---|
| Zero-on-attach (F2) | ✅ LV-1b `TryAttach` fix already covers `AttachSlotsToMemory` | verify-only test on the action path |
| Inspector rendering | ✅ StructEdit `InlineArrayBinding` + native-byte editing (D3); LV-5 formatter stays for the Blueprints watch | `FixedListViewProvider` + provider hook on the InlineArray path + `LiveBlackboardPanel` wiring |
| DTO shape + ops | ✅ FC-0 convention + FC-1b generator | — |
| Classifier | extend known-type set (A) | the panel wiring (B1) |
| JSON element types | ✅ `FdpJsonOptionsRegistry` converters (vectors, FixedString, enums) + `IncludeFields` for custom structs | the list `JsonConverterFactory` (C) + point `__paramJsonOpts` at the registry (1 line) |

## Proposed slice plan (post-approval)

FC-3a classifier + panel display (A/B) → FC-3b JSON converter (C) → FC-3c inspector row (D) +
action-path F2 verify test → docs fold-in. Each slice gates on: clean build · new tests green ·
full Blueprints + AiShared suites' failure sets unchanged · goldens 184/184.
