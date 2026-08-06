# Architect question #25 — Scenario-authoring UX: the five structural decisions

> **Scope.** The scenario-authoring **shell** (the outer loop), not the graph canvases. Programme:
> [`docs/UX/`](README.md). This is the first architect round of that programme and it batches every
> `OPEN` structural decision into one relay, because each one gates a milestone.
> Requirements: [UX_Requirements.md](UX_Requirements.md) · Design register:
> [UX_Design.md](UX_Design.md) · Journey spec: [UX_Golden_Path.md](UX_Golden_Path.md).
>
> **Nothing is built from this document until the answers are recorded in it.**

**The problem being solved.** HROT's authoring infrastructure works; the authoring experience does not.
An ordinary scenario author cannot walk *new scenario → place entity → assign behavior → run → author
a behavior → debug → hot-reload → iterate → save → reload → run* without knowing which ImGui window to
open, in what order, and without hitting controls that silently do nothing.

**Two audiences, settled by the user 2026-08-06** — this frames every question below:

| | Path A — Authoring | Path B — Runtime intervention |
|---|---|---|
| Surface | editor (`--mode editor`, offline) | distributed **ExCon**, live exercise |
| Audience | engineers / advanced military SME | **ordinary SME** |
| Bar | learnable, no tribal knowledge | **walk-up usable**, no engine vocabulary |
| Gestures | the full golden path | add entity · retask a unit · see the effect |

**Already ruled by the user, so *not* asked below:** no general undo stack (Q25-A asks only *how* to
spend the cheap budget); prefabs are wanted (Q25-B asks *how* to represent them); blueprint authoring
stays on Path A rather than hiding behind a designer mode.

---

## Ground truth (verified against code, 2026-08-06)

### What already exists and is good

| Fact | Evidence |
|---|---|
| **Play/stop is semantically correct** — ECS snapshot on enter, rewind on exit | `EditorPreviewAdapter.EnterPreviewMode/ExitPreviewMode` → `PreviewClusterOpHandler.TriggerLoadingPreview/TriggerUnloadingPreview` |
| **A behavior-affinity vocabulary already exists** | `BehaviorContractAttribute(string behaviorName, BehaviorCategory categories)`; `BehaviorCategory { Civilian, MilitaryApc, Infantry, Insurgent, AllMilitary, Commander }` |
| **Affinity is already resolved per entity type** | `BehaviorCatalog.GetValidBehaviors(tkbType)`, built by reflecting `[BehaviorContract]` types in a static ctor |
| **Param UI is already generated from the DTO** | `BehaviorUiCompiler.Compile<TDto>()` → cached ImGui draw delegate; `BehaviorSchemaDiscovery.AutoRegister` wires every `[BehaviorContract]` type into both the UI registry and the scenario remapper |
| **Scenario persistence is per-concern and pluggable** | `IEntityScenarioTranslator` implementations: `MissionPlanTranslator`, `BlueprintStateTranslator`, `UnitSubordinateTranslator`, `EditablePolylineTranslator`, `TargetMemoryTranslator`, … composed by `HrotScenarioSerializerFactory` |
| **Blueprint assignments already persist as a portable DTO list** | `BlueprintStateTranslator` extracts a `"BlueprintAssignments"` array of `BlueprintAssignmentDto` and injects an `InitialBlueprintsIntent` on load |
| **A TKB type already carries defaults, and the code calls it a *template*** | `ITkbDatabase.TryGetByType(cmd.TkbType, out var template)` in `EditorSpawnAdapter` |
| Commands already carry the metadata a palette needs | `EditorCommandDescriptor(Id, DisplayName, Category, Description, IconKey, DefaultKey, IsEnabled)` |
| Headless-testable seams are the house pattern | logic in public `Handle*` methods, ImGui confined to the composition root |

### What blocks the golden path

| Fact | Evidence |
|---|---|
| **No undo, and no safety net of any kind, on the scenario side** | zero `Undo` matches in `Hrot.Editor` / `Hrot.Presentation` / `Hrot.UI.Common`; `Hrot/Subsystems/Hrot.Editor/Commands/` contains one file (`CenterOnEntityCommand.cs`) |
| **No autosave anywhere** | zero `AutoSave`/`Autosave` matches in `Hrot/` or `FDP/Engine/` |
| **The outliner is a stub** | `EditorOrbatPanel.DrawContent` — 27 lines, prints `• [entityId]` |
| **`BehaviorCatalog` can only see one assembly, at static-ctor time** | `foreach (var type in typeof(BehaviorContractAttribute).Assembly.GetTypes())` — i.e. `Hrot.Core` only, once |
| **⇒ an asset-authored behavior has no way to declare its affinity** | which is exactly why `EditorMissionService.AppendEditorBTreeBehaviors` appends **all** BrainTierBTree assets to **every** entity type, with `TODO (option c): gate by per-asset DisEntityType affinity mask` |
| **Params degrade to a raw JSON textbox** | `MissionPanel.cs:481-492` → `DrawRawJsonEditor` when no DTO is registered |
| **Params are persisted as an escaped JSON string inside the scenario JSON** | `scenarios/hill-attack/scenario.json` — `behaviorParams` is a `string` |
| **Two unrelated assignment models** | `MissionPanel` (mission tasks, OCC commit) vs `EntityBlueprints` (attachment) |
| **The allocator is in the author's face** | `EntityBlueprintsEditModel`: `Projection(Slots, Bytes, Tier, Status)`, `UsageStatus.OverCeiling`, `CommitPlan.UpgradeToTier`, Reality/Staging |
| **Distributed protocol mechanics are exposed unconditionally** | `MissionPanel` renders an `ERR_VERSION_CONFLICT` modal and a **Force Commit** button regardless of host |
| **No problems/diagnostics list** | `NextError`/`PrevError` declared, never registered |
| **No audience/role/expert-mode concept exists anywhere** | zero `OperatorRole`/`UserRole`/`IsExpertMode`/`AdvancedMode` matches in `Hrot/` or `FDP/` |

### Unverified — flagged rather than asserted

- What the ExCon operator's console actually shows today (all of Path B is code-inferred).
- Whether deletion of an entity confirms anything.
- Whether a *catalog picker* for entity placement is reachable, or whether placement always uses
  `LastSelectedTkbType`.
- Whether the OS window title carries the scenario name.
- Whether `behaviorParams`' escaped-string form is also the **DDS wire form** — this decides whether
  [UXD-22](UX_Design.md#uxd-22) is editor-local or protocol-wide.

---

## Q25-A — How do we spend a cheap recoverability budget?

**Ruled:** no general undo stack. **User's reasoning, which we must not undermine:** *the same editor
code is reused inside the simulation runtime, and there real undo is not feasible anyway* — you cannot
un-send a command to a running distributed simulation, nor un-run the frames it produced.

So the question is what the safety net *is*, and how it stays honest across both hosts.

- **A1 — Autosave + *Revert to Saved* + confirm-on-destructive. Nothing else.**
  *Reuse:* the existing save path; `ScenarioFileService` already round-trips.
  *Build:* an autosave timer//dirty hook, a revert command, confirm dialogs.
  *Cost:* the most-wanted single gesture (Ctrl+Z after a misplaced unit) still absent.
- **A2 — A1 plus *bounded single-step* undo of the few spatial gizmo gestures** (place, drag, rotate,
  delete), implemented as an explicit inverse published by the gesture itself — no general stack, no
  command bus, no coverage promise.
  *Reuse:* gizmo completion callbacks already produce discrete intents (`SpawnEntityCommand`, drag/
  rotate gizmos).
  *Build:* an inverse per gesture (4), a one-slot history, and the Ctrl+Z binding.
  *Cost:* an author may expect Ctrl+Z to work everywhere and be disappointed unevenly — mitigate by
  labelling the affordance ("Undo Place Unit") rather than offering a bare Ctrl+Z.
- **A3 — Checkpoint/rollback reusing `PreviewClusterOpHandler`'s ECS snapshot** as a manual
  "authoring checkpoint" the author can return to.
  *Reuse:* the snapshot machinery is proven — it is what makes Stop correct today.
  *Build:* author-facing checkpoint/restore commands + memory-cost policy.
  *Cost:* coarse-grained; snapshot cost at authoring scale unknown; restoring loses *everything*
  since the checkpoint, which is a different mental model from undo.
- **A4 — An authoring-intent journal** (append-only log of author gestures, replayable/truncatable).
  *Reuse:* nothing directly.
  *Build:* an intent vocabulary covering all authoring mutations — i.e. most of the general model the
  ruling rejected.
  *Cost:* contradicts the ruling; listed for completeness.

> **Claude's lean: A2 (which contains A1).** A1 alone leaves the single most-felt gesture missing; A2
> buys it for ~4 inverses because the gizmo gestures are already discrete and already produce
> command objects. Crucially A2 **keeps the two hosts honest**: the runtime host composes the same
> panels and simply *registers no inverses*, so Ctrl+Z is correctly absent there rather than lying.
> A3 is attractive later as "checkpoint before I try something", not as the primary net.
>
> **Sub-question A′ for the architect:** is `PreviewClusterOpHandler`'s snapshot cheap enough at
> authoring scale to make A3 a cheap add-on to A2, or is it sized for the preview transition only?

---

## Q25-B — How is an entity template (prefab) represented?

**Ruled:** wanted. **User's lean:** build on what the scenario format already saves, which may make it
relatively easy. That instinct is supported by the code — a scenario entity is already a
self-contained component bag produced by a pluggable translator set, and blueprint assignments already
persist as a portable `BlueprintAssignmentDto` list.

**Two independent axes.** Please answer both.

### B-i — Representation

- **B1 — A template is a *scenario fragment*: one entity's translator output, saved standalone.**
  *Reuse:* `HrotScenarioSerializerFactory`'s whole translator set, unchanged, extract-one-entity.
  *Build:* a fragment file kind + asset-browser entry + place-from-template.
  *Cost:* fragments inherit every scenario-schema concern (migration, versioning) — which is also the
  point: one format, one migration path.
- **B2 — A new first-class asset kind with its own schema.**
  *Reuse:* the asset browser / New… / document machinery.
  *Build:* a second schema and a second migration story for the same data.
  *Cost:* two representations of "an entity's initial state" that must not drift.
- **B3 — Templates embedded in the scenario** (`"$templates"` section; entities reference by key).
  *Reuse:* one file, one migration.
  *Build:* reference resolution on load, plus editor UI.
  *Cost:* not shareable across scenarios — which is most of the value.

### B-ii — Override semantics

- **B4 — Copy-on-place (a "stamp").** The instance keeps no link to the template.
  *Reuse:* everything; this is nearly free once B1 exists.
  *Cost:* editing a template does not update existing instances.
- **B5 — Live link with per-field overrides** (Unity/Unreal prefab semantics).
  *Build:* per-instance override tracking, an override-visualising inspector, conflict rules on
  template change — and **entity identity in the saved form** to hang it on.
  *Cost:* the expensive part of every engine's prefab system.

> **Claude's lean: B1 + B4 now, B5 only if demanded later.** B1 is genuinely cheap because the
> extract/inject seam already exists per concern; B4 delivers most of the authoring win ("place ten
> configured tanks") at almost no cost. B5 is where prefab systems get hard, and it needs a stable
> per-entity identity decision that B1+B4 does not.
>
> **Sub-question B′:** if B5 is wanted eventually, should B1's fragment carry a template id/version
> from day one so instances *can* be relinked later, even though nothing reads it yet? (Claude's lean:
> yes — it is one field now and a migration later.)

---

## Q25-C — Where does an asset-authored behavior declare its affinity and its parameters?

**This is the sharpest question in the round**, because it explains a defect rather than proposing a
feature. Affinity already works — for C#-declared behaviors only:

```
[BehaviorContract("MoveToLocation", BehaviorCategory.AllMilitary)]   ← C# attribute
   → BehaviorCatalog (reflects ONE assembly, in a static ctor)
   → GetValidBehaviors(tkbType)                                      ← the mission-panel list
   → BehaviorUiCompiler.Compile<TDto>()                              ← the typed param form
```

An editor-authored BTree / HSM / Blueprint asset is a **JSON file**. It has no attribute, so it is
invisible to `BehaviorCatalog`, so `AppendEditorBTreeBehaviors` appends *all* such assets to *every*
entity type with a `TODO`, and it has no DTO, so its params fall back to a raw JSON textbox.
**Two golden-path requirements ([UXR-22](UX_Requirements.md#uxr-22),
[UXR-23](UX_Requirements.md#uxr-23)) fail from this single cause.**

- **C1 — Declare it in the asset.** The `.json` carries a category mask and a param schema; the editor
  authors both.
  *Reuse:* the asset already has a header/`$meta` convention and a migration path.
  *Build:* schema fields, authoring UI, and a loader that feeds the registry.
  *Cost:* two sources of truth for affinity (attribute + asset) unless C3 unifies the read side.
- **C2 — Generate the C# DTO from the asset** so an authored behavior joins the existing reflection
  path exactly as a hand-written one does.
  *Reuse:* `Hrot.AiEditor.Generators` / `Hrot.Blueprints.Generators` already exist; the hot-reload
  coordinator already swaps generated assemblies.
  *Build:* the generator + the build/reload timing.
  *Cost:* an authoring gesture now requires a compile before the behavior is assignable — directly
  hostile to [UXR-41](UX_Requirements.md#uxr-41) ("appears without a restart").
- **C3 — Make `BehaviorRegistry` the single source of truth**: it carries name + category + param
  schema per registered behavior, from *either* origin, and `BehaviorCatalog` consults the registry
  instead of reflecting one assembly.
  *Reuse:* the registry is already the thing both `EditorMissionService` and `MissionEditorService`
  consult, and it is already populated at load/hot-reload time.
  *Build:* extend registry entries; invert `BehaviorCatalog`'s data flow; keep the attribute as one
  *contributor*.
  *Cost:* touches a static, reflection-time class that other code may assume is immutable — needs a
  survey. **Also fixes a latent bug independent of UX: a static-ctor reflection snapshot cannot see
  hot-reloaded behaviors at all.**
- **C4 — Leave it ungated; gate in the UI only**, via a per-asset affinity list edited in the editor
  and stored editor-side.
  *Reuse:* trivial.
  *Cost:* the runtime and ExCon still see the ungated list, so Path B inherits the defect.

> **Claude's lean: C3 for the read path + C1 for where the data lives and is authored.** C3 is the
> unification that makes one list correct everywhere (editor *and* ExCon, since both go through the
> registry) and it retires a real staleness bug; C1 is where an asset-authored behavior states its own
> affinity. C2 is rejected for the golden path because requiring a compile breaks the iterate loop —
> though it may still be right for *param DTOs* if C1's schema proves too weak for
> `BehaviorUiCompiler`.
>
> **Sub-question C′:** can `BehaviorUiCompiler` be driven by a **runtime schema** (asset-declared
> fields) rather than only by a CLR type via `Compile<TDto>()`? If not, C1 needs either C2's codegen
> for the param half specifically, or a schema-driven sibling of the compiler. **This is the pivotal
> technical unknown of the question.**

---

## Q25-D — Two audiences, one set of shared panels. What is the mechanism?

`MissionPanel`, the entity inspector and the ORBAT panel are consumed by the editor **and** ExCon/IG/CGF.
Path A may show OCC internals to an engineer; Path B must never show them to an SME
([UXR-73](UX_Requirements.md#uxr-73)). **No role/mode/profile concept exists in the codebase today.**

- **D1 — A shell-level *surface profile*** (e.g. `Authoring` / `Operations`) that panels query to
  decide what to render.
  *Reuse:* nothing; new concept.
  *Build:* the profile, its propagation, and a discipline for every panel to honour it.
  *Cost:* a new global mode is exactly the kind of implicit state that produces "why is this button
  missing" support calls; risks becoming a second perspective system.
- **D2 — Per-host composition.** Each subsystem constructs the shared panels with different options —
  the pattern this codebase already uses everywhere (`PerspectiveWorkspaceRegistrar` per perspective,
  `EditorMissionService` vs `MissionEditorService` behind one interface, adapters per host).
  *Reuse:* the dominant existing pattern; no new concept.
  *Build:* options on the affected panels; each host passes its own.
  *Cost:* option sprawl if undisciplined; the constraint must be "options describe *capabilities*, not
  *audiences*".
- **D3 — Separate simplified ExCon panels.** Explicitly a
  [non-goal](UX_Requirements.md#non-goals) — listed only so the architect can overrule it.
- **D4 — Progressive disclosure only.** Same UI everywhere; engine detail behind *Advanced* sections.
  *Reuse:* trivial.
  *Cost:* insufficient alone — "Force Commit" behind a disclosure is still reachable by an SME, and
  conflict *resolution* is a service-layer behaviour, not a rendering choice.

> **Claude's lean: D2 for structure + D4 within a panel, and reject D1.** D2 matches how this codebase
> already differentiates hosts and needs no new global state; D4 handles engine detail *within* a
> surface. D1's global mode duplicates the perspective system with different semantics.
>
> **The OCC point is separate and, Claude believes, not a UI decision at all:** conflicts should be
> **resolved in the service** (`IMissionEditorService` retry-on-conflict with a plain-language failure)
> so no host renders a version modal. Force Commit then becomes an engineer-only escape hatch that
> ExCon simply never composes.
>
> **Sub-question D′:** is a conflict on a live retask ever *semantically* safe to auto-retry, or does
> "someone else changed this unit's orders" always require a human decision? If the latter, the SME's
> plain-language surface must still convey a choice — and we need the architect's wording for it.

---

## Q25-E — Where does the one problems list live?

[UXR-X2](UX_Requirements.md#uxr-x2) and [UXR-34](UX_Requirements.md#uxr-34) need validation failures,
compile diagnostics, load warnings and runtime faults in **one** clickable list. Nothing like it exists
today. ⚠ Reuse candidates below are **not yet traced** — the architect's steer decides how deep to look.

- **E1 — A new editor-side diagnostic sink** with a navigable source reference per entry.
  *Build:* the sink, the panel, publishers at each source.
  *Cost:* editor-only ⇒ Path B needs its own answer later.
- **E2 — Extend the existing alert manager** (`_alertManager.OnScenarioLoaded/OnScenarioCleared`).
  *Reuse:* it already receives load results.
  *Cost:* ⚠ its breadth is untraced; may be scenario-lifecycle-only.
- **E3 — One cross-host diagnostic model reusing the distributed diagnostics/GizmoMap path**, so an
  ExCon operator and an editor author see the same kind of list.
  *Reuse:* the cluster diagnostics layer already carries per-node diagnostics.
  *Cost:* heavier; couples an authoring affordance to the distributed layer.
- **E4 — Per-source panels** (status quo plus polish).
  *Cost:* fails the requirement by construction.

> **Claude's lean: E1 with a host-agnostic contract** — build it editor-side first, but define the
> entry type (severity, message, source reference, navigate action) so ExCon can publish into the same
> model without a rewrite. Defer E3 unless the architect says the distributed diagnostics layer is the
> intended home.

---

## Answers

*To be filled in by the user after the architect round. Record the chosen option per sub-question,
the architect's reasoning where it differs from Claude's lean, and any sub-question the architect
reframes rather than answers.*

| Question | Decision | Notes |
|---|---|---|
| Q25-A — recoverability budget | — | |
| Q25-A′ — snapshot cost at authoring scale | — | |
| Q25-B-i — template representation | — | |
| Q25-B-ii — override semantics | — | |
| Q25-B′ — carry a template id from day one? | — | |
| Q25-C — affinity + param declaration | — | |
| Q25-C′ — can `BehaviorUiCompiler` be schema-driven? | — | |
| Q25-D — shared-panel audience mechanism | — | |
| Q25-D′ — is auto-retry on live-retask conflict ever safe? | — | |
| Q25-E — problems-list home | — | |

**On recording the answers:** update the corresponding `UXD` rows in
[UX_Design.md](UX_Design.md#3-design-decisions-uxd) from `OPEN`/`LEAN` to `DECIDED` with a pointer back
to this document, then unblock the affected milestones in
[UX_Task_Tracker.md](UX_Task_Tracker.md).
