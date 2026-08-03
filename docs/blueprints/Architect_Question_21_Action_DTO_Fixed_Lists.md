# Architect question #21 — fixed-list fields in ACTION DTOs (recognition, authoring, inspection)

**Status: 🟡 DRAFT — awaiting architect pass.** The third and final home of the Fixed Collections
umbrella (FC-3). The component home (Q#20, FC-0/FC-1/FC-1b shipped) and the blueprint-variable home
(Q#19, FC-2 shipped: LV-1…LV-6) are done. Review R4 of the umbrella
(`Blueprint_Fixed_Collections_Design.md` §Second review) ruled this home "not just recognition" —
this doc asks the resulting decisions.
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

### D — Inspector marshal: reuse the LV-5 formatter, or structured rows?

`LiveBlackboardPanel` renders primitives only; a list field shows nothing useful.

- **D1 (lean):** reuse the **LV-5 reflection formatter** (`TryFormatFixedList`) — one string row
  `List<T>[N] Count=k {…}`, F2-clamped, zero new UI. Move/share the helper so the BTree editor can
  call it without referencing the Blueprints editor.
- **D2:** structured expandable rows (one row per element, editable). Real UI work; editing live
  bytes raises write-safety questions (who owns the write? tick races).

**Claude's lean: D1** for FC-3; D2 only if a concrete workflow demands element-level live editing
(none does today).

## Reuse-vs-build summary

| Piece | Reuse | Build |
|---|---|---|
| Zero-on-attach (F2) | ✅ LV-1b `TryAttach` fix already covers `AttachSlotsToMemory` | verify-only test on the action path |
| Watch/inspector rendering | ✅ LV-5 `TryFormatFixedList` | relocation to a shared home + panel hook |
| DTO shape + ops | ✅ FC-0 convention + FC-1b generator | — |
| Classifier | extend known-type set (A) | the panel wiring (B1) |
| JSON | — | the converter (C) |

## Proposed slice plan (post-approval)

FC-3a classifier + panel display (A/B) → FC-3b JSON converter (C) → FC-3c inspector row (D) +
action-path F2 verify test → docs fold-in. Each slice gates on: clean build · new tests green ·
full Blueprints + AiShared suites' failure sets unchanged · goldens 184/184.
