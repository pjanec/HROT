# BCP-BATCH-04 Review — wire-drop auto-connect (honor PinIds) + sample fix + pin audit
**Status:** ✅ APPROVED   **Date:** 2026-06-03

## Verification (ran myself)
- `dotnet build IOS-IG-SimHost.sln` **0 errors**; no warnings in touched projects (the ~26 full-rebuild warnings are pre-existing unrelated test projects — DEBT-BCP-004).
- `Hrot.Blueprints.Tests` **1120 / 10 / 8** (10 = DEBT-006; +new wire-drop tests). `Hrot.Editor.AiShared.Tests` **761/0**, `Hrot.BTree.Editor.Tests` **382/0**, `Hrot.Hsm.Editor.Tests` **333/0**, `EditorSubsystemBoot` **10/0**. Byte-stability green (the PinIds change affects only newly-created in-memory nodes; loaded assets still project).

## Code read
- **Auto-connect (root cause confirmed earlier):** `BlueprintCommandSink.CreateAssetNode` now honors `AddNode.InitialProperties["PinIds"]` via `ApplyPinIds` — builds the node's canonical pins (`NodePinSchema.GetCanonicalPins(node, _catalog.KindRegistry, _asset)`, same source as `DescriptorToEntry`), reorders **inputs-then-outputs** to match `CanvasInput`'s pinIdx walk + `DescriptorToEntry`'s split, and stamps the supplied GUIDs (min-count guarded). So the new node owns the link-referenced GUID → `ApplyAddLink.FindPin` resolves → wire connects. Wired into both the registry/fallback and Get/Set paths.
- **SampleWiredDemo:** node #4 `CombatChannel`/`Fire` → real `WeaponChannel`/`AimAndFire` (node #2 already `LocomotionChannel`/`MoveTo`).

## Test quality (gold standard)
`BcpBatch04WireDropTests` reproduces the exact `Batch(AddNode{PinIds}, AddLink→pinIds[k])` canvas sequence and asserts a **real connection** — link in `graph.Links` with correct From/To/ToNodeId, both endpoints `FindPin != null`, target pin **owned by the new node** with correct Kind/Direction/(Type for data), and `FindLink` resolves — for an exec drop (EventEntry→ChannelCommand) AND a data drop (GetVariable Int32 → SetVariable Value-In, asserting `System.Int32`). Plus a **regression guard** proving the link is rejected WITHOUT PinIds (pins null check). Exemplary.

## Pin-coverage audit
`reports/PIN-COVERAGE-AUDIT.md` classifies every kind: data-pinned vs exec-only with reason (by-design / config-needed / data-limited / deferred). Confirms ChannelCommand richness is a **data** gap (placeholder params in `BuiltInChannelCommandCatalog`), FunctionCall needs configuration, Return/squad are by-design.

## Debt logged
- **DEBT-BCP-005:** wire-dropped nodes carry populated in-memory `node.Pins` (to honor PinIds). Loaded assets unaffected; but if SAVE persists pins, audit round-trip + compiler implications before enabling save.
- **DEBT-BCP-006:** ChannelCommand params are a single placeholder type per action — rich per-arg pins need catalog/schema data enrichment (runtime/content effort).

## Verdict
APPROVED. Wire-drop now auto-connects (exec + data), the sample uses real actions, and the pin landscape is documented. Remaining: fonts (S4), mini-editors, and the data-side enrichment (DEBT-BCP-006) / ReadRankedResult+squad if wanted.

## Commit Message
```
fix(editor): wire-drop auto-connect (honor AddNode PinIds) + real sample channel actions + pin audit (BCP-BATCH-04)

CreateAssetNode now honors AddNode.InitialProperties["PinIds"] (the pre-generated pin GUIDs NodeEdit's
CanvasInput wire-drop references in its auto-connect AddLink): it builds the node's canonical pins
(NodePinSchema, same source as DescriptorToEntry), reorders inputs-then-outputs to match CanvasInput +
DescriptorToEntry, and stamps the supplied GUIDs so ApplyAddLink.FindPin resolves and the wire connects.
Previously the link was rejected (new node's pins had different GUIDs) → no connection.

SampleWiredDemo: CombatChannel/Fire -> WeaponChannel/AimAndFire (real catalog action) so it resolves.
PIN-COVERAGE-AUDIT.md added: per-kind data-pin status + why (by-design/config-needed/data-limited/deferred).

Loaded-asset byte-stability + compiler golden unchanged (PinIds only affects newly-created in-memory
nodes — DEBT-BCP-005). Build 0 errors. Blueprints 1120/10 (DEBT-006), AiShared 761/0, BTree 382/0,
Hsm 333/0, Boot 10/0. DEBT-BCP-005/006 logged.
```
