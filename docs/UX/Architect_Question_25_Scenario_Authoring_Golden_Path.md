<!--STATUS
state: LIVE
build-state: DESIGN — ✅ RECOMMENDED ANSWERS added 2026-08-25 (coordinator; "I analyse/suggest, user
  approves"). Awaiting user approval. The per-Q sections below are the OPTION SPACE + Claude's lean; the
  "✅ RECOMMENDED ANSWERS" section at the top is the live recommendation.
updated: 2026-08-25
current-answer: the "✅ RECOMMENDED ANSWERS" section.
design-basis: PROGRAMME_Unification_And_Harness.md (editor = one-node cluster) · Q26 (superseded Q25-D,
  mooted Q25-F/F′) · AI_Editor_Shared_Infrastructure.md §17 (hot-reload Cosmetic/Soft/Hard classification).
known-conflict: Q26 supersedes Q25-D and moots Q25-F/F′ — the "new editor exe" lean (F5/UXD-08) is
  WITHDRAWN (2026-08-10). Read §F/§F′ as historical.
-->
# Architect question #25 — Scenario-authoring UX: the five structural decisions

## ✅ RECOMMENDED ANSWERS — awaiting user approval *(coordinator, `2026-08-25`)*

> Per the "I analyse/suggest, user approves" model. Each adopts/sharpens the per-Q "Claude's lean" below,
> informed by the charter *(the editor is a one-node cluster)* and a fresh code measurement of the C′ unknown.
>
> ⚠⚠ **SCOPING, user `2026-08-25`:** **A/B/C/E are authoring FEATURES, not prerequisites for SHARING the
> editing capability with CGF** *(they are missing on the editor too, and get shared for free once built)*
> ⇒ **postponable**; resolve them when the feature is actually built, not before the CGF sharing.
> **F is IGNORED** — keep `ClusterRunner` *(a shim exe passing params may come later)*. The recommendations
> below stand as the answers **for when each feature is built**; none blocks the pure-sharing work.

| # | ✅ recommended answer |
|---|---|
| **Q25-A** *(recoverability)* | **A2** = A1 *(autosave + Revert-to-Saved + confirm-on-destructive)* **plus** bounded single-step undo of the 4 gizmo gestures via an explicit inverse each. ⭐ Keeps the two hosts honest: the runtime host **registers no inverses** ⇒ Ctrl+Z is correctly **absent**, not lying. ⛔ Defer **A3** *(checkpoint via `PreviewClusterOpHandler` snapshot)* — that snapshot is the whole-world preview snapshot sized for the transition; a later "checkpoint before I try something," pending a cost measurement (A′). |
| **Q25-B** *(prefabs)* | **B1** *(a template = a saved scenario-fragment via the existing translator set)* **+ B4** *(copy-on-place "stamp")* now; **B5** *(live prefab link + per-field overrides)* only if demanded. ✅ **B′: YES** — carry a template id/version field from day one *(one field now, a migration later)* so B5 stays possible. |
| **Q25-C** *(behavior affinity + params — the pivotal one)* | **C3** *(`BehaviorRegistry` = single source of truth for the READ path; `BehaviorCatalog` consults it instead of reflecting one assembly — also fixes the static-ctor-can't-see-hot-reload bug)* **+ C1** *(the asset declares its category mask + param schema, authored in the editor)*. ⛔ Reject **C2** for the golden path *(compile-before-assignable breaks UXR-41)*. 📐 **C′ MEASURED `2026-08-25`:** `BehaviorUiCompiler.Compile<TDto>()` is **strictly CLR-type-driven** *(reflects a DTO type; switches on `PropertyType`)* ⇒ cannot consume a runtime schema as-is. ⭐ **BUT the runtime field-schema model already exists** — `BlueprintFieldDescriptor(name,type,offset,size)` + the `.bp.json`-schema path *(`GeneratedBlueprintSchemaCatalog`/`StructSizeResolver`)* — and the variable-model **Details panel already renders rows from field metadata**. ⇒ the param form is a **schema-driven sibling renderer built on the existing field-descriptor model**, NOT C2 codegen. **This closes C′: schema-driven is feasible by reuse.** |
| **Q25-D** *(two audiences, shared panels)* | ✅ **ANSWERED by Q26** *(which supersedes it)*: **D2** *(per-host composition — the descriptor/binding split)* **+ D4** *(progressive disclosure within a panel)*; ⛔ reject **D1** *(a global surface-profile = a second perspective system)*. ⭐ The OCC conflict is resolved **in the service** *(no host renders a version modal)*; **Force Commit** = engineer-only, an SME host simply never composes it. **D′:** a live-retask conflict is **NOT** safe to silently auto-retry — *"someone else changed this unit's orders"* needs a human decision, surfaced in plain language on the SME host. |
| **Q25-E** *(the one problems list)* | **E1 with a host-agnostic contract** *(entry = severity · message · source-ref · navigate action)*. Build editor-side first; define the contract so ExCon/CGF publish into the same model without a rewrite. ⛔ Defer **E3** *(distributed-diagnostics home)* unless we later want cross-node problem aggregation. |
| **Q25-F** *(dedicated editor exe?)* | ✅ **ANSWERED: NO new exe.** The "new shell/exe" lean *(F5 / UXD-08)* is **WITHDRAWN (`2026-08-10`)** and the charter settles it — the editor is a **ONE-NODE CLUSTER** *(a `ClusterRunner` mode, networkless by construction, already running the cluster machinery in-process)*. The seam is **inside shared code**, not a separate host. |

⭐ **Net:** the only sub-question that needed a measurement rather than a judgement was **C′**, and it resolves to **reuse** *(a schema-driven sibling of the existing field-descriptor renderer)*. Everything else adopts the standing lean, with **D** and **F** already settled by later rulings.



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
stays on Path A rather than hiding behind a designer mode; and the editor **should become its own
application with a purpose-built shell, keeping features and init path shared** (Q25-F asks *where the
seam goes*, not whether).

> 📌 **Read [Q25-F](#q25-f--a-dedicated-editor-application-with-a-purpose-built-shell) first.** It came
> last chronologically but is logically prior: it reframes Q25-D, makes Q25-A's argument structural, and
> it addresses the *cause behind the cause* — the editor's UI is currently the emergent output of a
> generic cluster-node window aggregator, not a designed shell.

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

## Q25-F — A dedicated editor application with a purpose-built shell

<a id="q25-f--a-dedicated-editor-application-with-a-purpose-built-shell"></a>

> ⚠ **Answer this one first. It reframes [Q25-D](#q25-d--two-audiences-one-set-of-shared-panels-what-is-the-mechanism)
> and makes [Q25-A](#q25-a--how-do-we-spend-a-cheap-recoverability-budget)'s host-composition argument
> structural rather than a matter of discipline.**
>
> **User's proposal (2026-08-06):** stop shipping the editor as a `ClusterRunner` mode and build a
> **dedicated standalone editor executable** — *fully-fledged feature-wise, with a very much shared init
> path so all the internal machinery still runs*, and a **new UI built step by step** by composing what
> mostly already exists (placing existing windows, or combining their content into new ones) as we walk
> the golden path.

### Why this is not merely cosmetic — verified

**The editor's UI structure was never designed. It is the correct output of a generic cluster-node
window aggregator.** `LocalWindowController.OpenLocalWindow()` is the *entire* editor shell, in ~60
lines:

| Fact | Evidence |
|---|---|
| Every subsystem dumps its windows into one manager | `foreach (var sub in _subsystems) if (sub is IWindowRegistrar r) r.RegisterWindows(wm);` — `LocalWindowController.cs:53-55` |
| The default perspective is *"the second subsystem's name"* | `var first = _subsystems.Skip(1).FirstOrDefault(); string defaultPersp = first?.Name ?? "Default";` — `LocalWindowController.cs:80-82` |
| Perspectives are cluster roles, hardcoded | `perspectiveMap = { IG, SimHost, ExCon, CGF, StrideMock }` — `Program.cs:~243` |
| The window is titled for the cluster, not the product | `"HROT Cluster Runner"` — `LocalWindowController.cs:38` |
| `--mode editor` still pays cluster startup for subsystems it never runs | `ScanForSubsystems().Select(type => { … CreateParticipant(domainId) … })` creates a DDS participant + network factory for **every discovered** subsystem, *then* filters to the requested ones — `Program.cs:181-210` |

> **Therefore:** "a bag of windows with no front door" is not an oversight inside the editor. It is what
> this host is *for*. No amount of panel work changes it; only a curated shell does. That makes Q25-F
> arguably the **highest-leverage question in this round** — it addresses the cause behind the cause
> named in [the briefing](UX_Programme_Briefing.md#2-why-the-work-is-where-it-is).

### Why it is cheap — verified

| Fact | Evidence |
|---|---|
| The whole host is small | `Hrot.ClusterRunner` is **2,217 lines**; `LocalWindowController` 102, `IPresentationShell` 28, `RaylibPresentationShell` 198 |
| **No subsystem depends on the host** | the only `Hrot.ClusterRunner` mentions in subsystem `.csproj` files are `InternalsVisibleTo` test attributes. The dependency arrow already runs host → subsystems |
| The orchestrator is not host-local | `SubsystemOrchestrator` / `ISubsystem` / `RunnerOptions` live in `FDP/Toolkits/Fdp.Toolkits/Runner/` |
| The Raylib/ImGui boundary is already a seam | `IPresentationShell` (InitWindow · SetupImGui · FontService · IconAtlas · GizmoFont) — reusable verbatim |

⚠ **The honest caveat: "standalone" ≠ "no cluster machinery".** The editor's own features route through
the orchestration state machine — `EditorApplication.LoadScenarioByName` publishes a
`TransitionStateIntent` and waits for `ClusterState.Idle` (`EditorApplication.cs:156-167`), and
Play/Stop goes through `PreviewClusterOpHandler`. A dedicated app **still hosts the orchestrator**; it
drops mode parsing, DDS participants, and the other subsystems. This is exactly why the user's
*shared init path* constraint is the right one.

### ✅ Resolved: the editor is networkless by construction, and the host wastes a DDS participant on it

Previously flagged as unverified; now established, and it matters for F-i:

```csharp
// EditorSubsystem.cs:180
private readonly INetworkFactory _networkFactory = new OfflineNetworkFactory();
// EditorSubsystem.cs:557
public EditorSubsystem( INetworkFactory _ )      // ← the injected factory is DISCARDED
```

**The editor discards the network factory the host injects and hardcodes `OfflineNetworkFactory`.** So
the user's stated design intent — *the editor is a networkless, all-in-one, in-process solution* — is
already enforced in code, and the DDS participant `Program.cs` creates for the editor
(`Program.cs:194`) is **built and thrown away**. A dedicated editor app can drop network composition
entirely for its own preset.

> #### ⚡ Sharpened 2026-08-10 — it is stronger than "discards the injected one"
>
> `_networkFactory` is **declared at `:180` and never read anywhere**. `EditorSubsystem` is
> `sealed` and **not `partial`** (`:165`), so the file is the whole class — one grep settles it.
>
> ⇒ **The editor consumes no `INetworkFactory` at all**, not even the offline one. `:180` is a **dead
> field**. So the answer F-i needs is not "how little network composition must shared init keep for the
> editor preset" but **none** — the editor preset can omit network composition entirely, and doing so
> removes nothing the editor reads.
>
> Confirmed on the host side too: the participant and factory are built inside the `Select` at
> `Program.cs:184-207`, which runs for **every discovered subsystem**, *before* the requested-subsystem
> filter at `:213`. So `--mode editor` still pays for a DDS participant it never touches.

⚠ Two riders:

1. `EditorSubsystem( INetworkFactory _ )` is a **dependency that looks injected and is not** — trap #8's
   shape inverted. A future session could "helpfully" wire it and quietly give the editor a network. If
   the app is split, make the intent explicit rather than positional.
2. The **construction-kit nature must survive** (user, 2026-08-06): the system must still compose
   network-distributed variants exactly as `ClusterRunner` does today. So a shared host library must keep
   *generic* subsystem composition with real network factories — **the editor app is one preset of the
   kit, not a replacement for it.** See [F-i](#f-i--where-does-the-seam-between-shared-init-and-new-shell-go).

🔴 **CORRECTED — the MCP server does exist.** *(Established 2026-08-06 and recorded in the RESUME that
day; **this doc lagged until 2026-08-10** and asserted the opposite in the meantime — logged in
[Corrections](UX_Tasks_Detail.md#corrections).)* An earlier revision of this section said it
"does not exist yet"; that was true only of *our line* of history. It was built on
`origin/feat/ai-debug-api` (tip `d7b2a6e1`) — a loopback `HttpListener` control plane (`DebugApiHost`,
`DebugApiService`) hosted **inside `Hrot.Editor`**, plus an external Node MCP proxy. A **parallel session
is porting it forward** ([MCP_PORT_PLAN.md](MCP_PORT_PLAN.md), [MCP_PORT_RESUME.md](../mcp-port/MCP_PORT_RESUME.md)).

⇒ This is **a constraint to design against, not intent**: the editor app acquires a loopback HTTP
interface, so "networkless" means **no DDS / no cluster transport** — it was never a claim about
sockets. Do not architect the shell so that hosting the API later requires reopening it. See the
🔴 [sequencing rule](../SESSION_SYNC.md#sequencing-rule) — the port wires `EditorSubsystem.cs` first.

### F-i — Where does the seam between shared init and new shell go?

The user's constraint: **fully-fledged features, very much shared init.** So the question is not whether
to share, but where to cut.

- **F1 — Extract the composition into a shared host library** (`Hrot.Host.Composition` or similar) that
  both executables call: subsystem construction, orchestrator, console service, render loop, exit
  guards. Each exe supplies its **own shell composition**.
  *Reuse:* all init, verbatim, in one place. *Build:* the extraction + a shell abstraction wider than
  today's `IPresentationShell` (which covers Raylib/ImGui only, not *what gets registered*).
  *Cost:* one refactor of a working host, touching `ClusterRunner`'s two test projects.
- **F2 — The new exe references `ClusterRunner` as a library** and calls its bootstrap, overriding only
  the shell.
  *Reuse:* no extraction needed. *Build:* `LocalWindowController` is `internal` and hardcodes the
  generic loop, so it must be opened up and parameterised.
  *Cost:* the editor product now depends on the cluster host — backwards, and it keeps cluster concerns
  in the editor's dependency graph.
- **F3 — No new exe yet: one exe, a `--shell` selector** choosing between the generic aggregator and a
  new curated editor shell.
  *Reuse:* everything; nothing to extract. *Build:* only the new shell.
  *Cost:* no distinct product identity (title, icon, own settings/layout file), and the shell choice
  stays a developer flag rather than a deliverable.
- **F4 — New exe with its own copied init.**
  *Cost:* divergence. Rejected — it is the failure mode this codebase's shared-panel discipline exists
  to avoid.

> ### 🔒 Two hard constraints on any answer here
>
> **1. `ClusterRunner` stays fully operational, continuously.** Blueprint development runs in parallel in
> other sessions against it. No option may put it in a broken or "will fix after the refactor" state, and
> the extraction in F1 must therefore be mechanical and gated on ClusterRunner's own suites staying green.
>
> **2. The construction kit survives.** The system must still compose network-distributed variants exactly
> as it does today (`--mode orchestrator,simhost,cgf`, `--mode ig`, `--mode excon`, `--mode all`). A
> shared host library must keep **generic** subsystem composition with real network factories; the editor
> app is **one preset of the kit** — networkless, all-in-one, in-process — not a replacement for it.
>
> These two rule out any answer that reshapes the host around the editor's needs. They *favour* F3→F1
> staged: the selector stage cannot break the cluster host at all, and by the time F1 extracts anything,
> the shell it serves is already proven.

> **Claude's lean: F3 → F1, staged.** Build the curated shell **first** behind a selector in the
> existing exe (F3), because that proves the shell with zero refactor risk and keeps every gate green
> while the UI is still churning. Then, once the shell has stabilised around the golden path, extract
> the shared composition and split the dedicated exe (F1) — at which point the extraction is a
> mechanical move rather than a redesign. **F1 is the destination; F3 is how to get there without
> betting the host on an unproven layout.**
>
> **Sub-question F′:** is that staging acceptable, or does the dedicated exe need to exist from day one
> for product/delivery reasons (installer, branding, separate release cadence)? If the latter, go
> straight to F1.

#### ⚠ 2026-08-10 — the lean above was measured, and it does not survive

<a id="f-prime-measured"></a>

**Claude's F3→F1 lean rested on one premise: that the extraction is risky enough to defer.** That premise
was never measured. It has now been, and it is false. **Read the lean above as superseded by this block.**

**F3 and F1 bundle three decisions that are independent:**

| Decision | Real cost |
|---|---|
| Where does the curated shell's code live? | Its own project — **either way**. The shell code is *identical* under F3 and F1; only the entry point differs |
| When is the new exe cut? | Near-free once the shell is a library. A `Main` is small |
| How much host composition is extracted, and when? | **The only expensive decision** — and it can be staged under *either* option |

⇒ F3 does not de-risk the *shell* at all. It defers only the *extraction* — and the extraction turns out
to be small enough that deferring it buys nothing.

**F5 — the option this question did not offer: cut the exe on day one, stage the *extraction*.**
New shell project + thin exe immediately; extract composition into a shared library incrementally, each
step gated on `ClusterRunner`'s suites. The host keeps running throughout, because it gains a library it
also consumes rather than having its behaviour rewired.

**Measured 2026-08-10** (evidence: [baseline index](UX_Tasks_Detail.md#baseline-evidence-index)):

| Measurement | Value |
|---|---|
| Production projects referencing `Hrot.ClusterRunner` | **Zero.** Only its 2 test projects — it is a leaf |
| `internal` types blocking a separate exe | **7 total, but only 2 are needed**: `IPresentationShell` + `RaylibPresentationShell`. `LocalWindowController` is *the generic aggregator we are replacing*, and `PerspectiveUpdateSubsystem` is multi-subsystem cluster coordination a single-subsystem editor does not want |
| ⇒ **The minimum extraction** | **Two types** — the Raylib/ImGui/font/icon bootstrap. Already seam-tested: `LocalWindowControllerTests` drives it through a fake `IPresentationShell` |
| `Program.cs` droppable for an editor-only exe | ~200 of 484 lines — CI mode, migrate mode, DDS participant + factory selection, the perspective/gizmo cluster map, node-id offsets, and the whole reflection scan (→ one `new EditorSubsystem()`) |
| F3's blast radius **inside the working host** | 3 files; `Program.cs:236-370` (135 lines) plus conditionals through most of `OpenLocalWindow`'s 53-line body — then deleted again at F1 |

**Claude's revised lean: F5 — go straight to the dedicated exe, stage the extraction.** F3 spends changes
*inside the host that constraint #1 exists to protect*, to host an experiment, and throws the plumbing away
afterwards. F5 touches `ClusterRunner` only to make two bootstrap types reachable.

> **F′ is therefore re-put to the architect as a three-way choice:** staged **F3→F1**, straight **F1**
> (big-bang extraction), or **F5** (exe now, extraction staged). Claude leans **F5**; the honest residual
> risk is that F5 must guess the shared-library boundary up front, where F3 discovers it empirically —
> but guessing wrong under F5 means refactoring *our own new library*, while guessing wrong under F3
> means having built against host internals that may not extract cleanly.

⚡ **A finding that removes the main argument for "very much shared init":** the scenario-load and
Play/Stop machinery — `TransitionStateIntent`, the wait for `ClusterState.Idle`, `PreviewClusterOpHandler`
— is **not in `Program.cs` at all**. It lives inside `Hrot.Editor`, which builds its **own in-process
`ClusterMaster`** on its own bus (`EditorSubsystem.cs:1352`, with `Mandatory = Array.Empty<string>()`),
ticked per frame. `Program.cs` contributes only *generic hosting*: logging, CLI, orchestrator driver,
window, render loop. **The cluster machinery the editor needs, the editor already builds itself.**

### F-ii — What does the new shell keep of the window machinery?

- **G1 — Keep `WindowManager` and curate what enters it**: an explicit registration list, an explicit
  default layout, and only the editor's own perspectives (Editor / BTree / HSM / Blueprint) — the
  cluster-role perspectives simply do not exist in this app.
  *Reuse:* docking, layout persistence, menu generation, icon atlas, status bar, font pipeline.
  *Build:* the curated list + a default-layout description.
- **G2 — G1 plus a first-class "layout template" concept** so the default layout is data, not code, and
  a user can reset to it.
  *Build:* a small layout-template format + a Reset Layout command.
  *Note:* [UXR-04](UX_Requirements.md#uxr-04) ("delete the layout profile, launch, walk the path — no
  window opened manually") is much easier to satisfy — and to *test* — if the default layout is data.
- **G3 — A new layout mechanism.** Rejected unless the architect sees a reason: `WindowManager` already
  persists layout, groups menus and handles DPI/font scaling.

> **Claude's lean: G2.** G1 is the minimum, but "Reset Layout" plus a data-defined default is what makes
> the working-layout requirement verifiable rather than aspirational, and it is small.
>
> ⚠ **Note the collision to resolve:** two overlapping *perspective* concepts exist today — cluster-node
> perspectives (which collapse to a single one in this app) and the editor's internal
> Editor/BTree/HSM/Blueprint perspectives (which stay). Code that assumes the former exists must be
> found before the shell is cut.

#### 🔴 Found it — and the collision is already a live defect

<a id="f-ii-perspective-restore"></a>

**Searched 2026-08-10 as the pre-seam check the [RESUME §3.5](UX_RESUME.md#next-up) asks for.** The
collision is not latent. It costs the author their place on every restart today:

| Step | Code |
|---|---|
| `SaveSettings` persists the **active perspective id** — `"BTree"`, `"HSM"`, `"Blueprint"` included | `WindowManager.cs:368-382` (`CurrentPerspective` into `WindowManagerSettings`) |
| `LoadSettings` returns it verbatim | `WindowManager.cs:388-411` |
| The shell then accepts it **only if it names a subsystem** | `LocalWindowController.cs:83` — `_subsystems.Any(s => s.Name == persisted)` |
| …otherwise silently falls back | `:84` → `defaultPersp` = `_subsystems.Skip(1).FirstOrDefault()?.Name` (`:81-82`) |

`"BTree"`, `"HSM"` and `"Blueprint"` are **perspective ids registered by `EditorSubsystem`**, not
subsystem names — so `valid` is `false` and the restore is **silently discarded**. Only `"Editor"`
survives, and only by coincidence: `EditorSubsystem.Name => "Editor"` (`:172`).

⇒ **Prediction: quit while editing a blueprint graph, relaunch, and you are back in Scenario.** Nothing
is lost but your place — and nothing tells you it happened. ⚠ **Code-derived; the coordinator cannot run
the editor.** Put it on the [walk](UX_Golden_Path.md#deviation-log) to confirm.

**Three things follow for this sub-question:**

1. It is **evidence for G1's "curate what enters"** over inheriting the aggregator: the bug exists
   because the shell validates perspectives against a list that was never the perspective list.
2. It is **evidence for G2 specifically** — a shell that owns an explicit perspective set can validate
   against *that set*, which is the one-line fix. G1 alone does not force the question.
3. 🔒 **Do not fix it in `LocalWindowController`.** Per [RESUME §3.3](UX_RESUME.md#next-up) this is
   shell-level, the shell is being replaced, and `ClusterRunner` must stay operational — so it is a
   *"the new shell must not reproduce this"* entry, **not** a repair task.

### F-iii — How do we combine the content of existing windows into new composite panels?

The user's plan is to *place existing windows, or combine their content into new ones*. The second half
is the load-bearing part — [UXR-14](UX_Requirements.md#uxr-14) (one inspector) and
[UXR-20](UX_Requirements.md#uxr-20) (one behaviors section) both require merging what are currently
four separate windows, **without forking panels** ([non-goal 4](UX_Requirements.md#non-goals)).

> ### ⚠ Corrected 2026-08-06 — the parallel-development constraint changes the answer
>
> **User constraint:** `ClusterRunner` must stay fully operational because blueprint work is proceeding
> **in parallel in other sessions**. Therefore the new UI must *"be careful about changing the content of
> the windows — rather place them properly into the desired layout"*, and any change **inside** a window
> or to a **shared menu** must be **synchronised/consulted with the other sessions**.
>
> Claude's earlier lean here was **H1 (re-host the view-models) as the rule**. That is now wrong as a
> *starting* position: re-hosting view-models means touching panel internals, which is precisely the
> churn the constraint forbids without consultation. **The lean is revised below.** See also
> [F-v](#f-v--how-do-we-stay-out-of-the-parallel-blueprint-work).

- **H0 — Place only. Change nothing inside a window.** The new shell registers, positions and docks
  existing windows into a designed layout; window *content* is untouched.
  *Reuse:* total — zero panel edits, zero collision with parallel work.
  *Build:* only the shell's registration + layout.
  *Cost:* layout-level requirements are satisfiable ([UXR-04](UX_Requirements.md#uxr-04),
  [UXR-06](UX_Requirements.md#uxr-06)) but *merge*-level ones are not
  ([UXR-14](UX_Requirements.md#uxr-14), [UXR-20](UX_Requirements.md#uxr-20)) — so H0 alone cannot finish
  the programme; it can start it safely.
- **H1 — Re-host the view-models, not the windows.** Compose new panels over the existing headless
  logic (`Handle*` methods, `EntityBlueprintsEditModel`, `BlueprintMyBlueprintModel`, …), leaving the
  old windows intact for their other hosts.
  *Reuse:* the house pattern already separates logic from ImGui in most panels — precisely why this
  codebase is testable. **Reads shared code; does not modify it** when the seam already exists.
  *Build:* a view-model seam wherever one is missing — **and adding a seam *does* modify a shared
  panel, so it needs a consult.**
- **H2 — Embed whole existing windows as child regions / tabs** of a new composite.
  *Reuse:* maximal, immediate, no panel edits. *Cost:* the composite inherits each window's titling,
  padding and scroll behaviour — a stack of windows in a box, i.e. the current problem in a smaller
  frame.
- **H3 — Extract each window's draw body into a callable *section*** (`DrawSection(ctx)`) that both the
  old window and the new composite call.
  *Reuse:* one implementation, two hosts. *Cost:* mechanical but broad — **modifies every shared panel
  and therefore collides hardest with parallel blueprint work.** The right move eventually, the wrong
  move now.

> **Claude's revised lean: H0 first for everything the layout can satisfy; H1 only where the
> view-model seam already exists (read-only reuse); H3 deferred until the blueprint programme's active
> surface is quiet or the change is consulted; H2 only as task-labelled scaffolding.**
>
> Concretely: **the merge requirements are re-sequenced, not dropped.** UXR-14/UXR-20 stop being early
> spine work and move behind the consultation protocol — they are the *first* things to do once
> in-window change is affordable, not the first things to do overall.
>
> **Sub-question F″:** for the *graph canvases* specifically (BTree/HSM/Blueprint — already good, large,
> and **the active surface of the parallel programme**), is the intent that the new app hosts them
> **unchanged in their own perspectives** (Claude's assumption, and doubly so under this constraint), or
> that they eventually move into the new shell's layout? This decides whether "compose what exists" has
> a hard boundary at the canvas.

### F-v — How do we stay out of the parallel blueprint work?

<a id="f-v--how-do-we-stay-out-of-the-parallel-blueprint-work"></a>

Two programmes now edit one repo: the **blueprint** programme (inner loop, active, other sessions) and
this **UX** programme (outer loop). The constraint is not merely git hygiene — a UX change that alters a
shared panel's behaviour can invalidate a blueprint session's visual verification mid-flight.

**One structural advantage worth naming:** a **greenfield shell project is collision-free by
construction** — new files, new `.csproj`, which no blueprint session touches. Under this constraint the
new-app plan and the parallel-work constraint *reinforce* each other; repairing the old shell in place
would have collided continuously.

- **J1 — Declared co-ownership list + consult-before-touch.** Name the shared surfaces (the shared
  panels, the global menu registry, `WindowManager`, `EditorSubsystem`'s composition root, the blueprint
  editor windows). The UX programme may freely **register, place and dock**; altering a co-owned
  surface's internals requires a consult note the other programme can see.
  *Build:* a list + a handoff section + the discipline.
- **J2 — Additive-only rule at the shell boundary.** The new shell may only *add* registrations and
  layout; any edit to a co-owned file is a separate, individually-reviewed task.
  *Build:* nothing; it is a rule. Pairs naturally with J1.
- **J3 — Time-slice it.** Freeze blueprint work while shared surfaces are changed.
  *Cost:* the user is actively developing blueprints in parallel — this defeats the purpose.
- **J4 — Branch isolation with periodic integration.** Each programme on its own branch, merged on a
  cadence.
  *Cost:* does not help at all with *semantic* collisions (a changed panel behaviour), only textual ones,
  and it delays discovery.

> **Claude's lean: J1 + J2.** The co-ownership list and the additive-only boundary are cheap, they make
> the constraint checkable rather than hoped-for, and they compose with the existing shared-panel-hosts
> discipline already in the handoff template. J4 as ordinary practice, not as the mechanism.
>
> **Sub-question F‴:** what is the **consultation channel** in practice? Claude cannot reach other
> sessions. Options: the user relays; or a co-owned `docs/UX/SHARED_SURFACES.md` where a proposed change
> is written down and the other programme's RESUME links to it. Claude's lean: the file, because it
> survives compaction and both programmes' sessions can read it — **but only the user can confirm that
> the blueprint sessions will actually read it.**

### F-vi — The effective map viewport

<a id="f-vi--the-effective-map-viewport"></a>

**The architecture (user, 2026-08-06, confirmed in code):** the 2D symbolic map is **Raylib, rendered
across the whole OS window, behind ImGui** — for **speed**. ImGui runs a dockspace with
`PassthruCentralNode` (`Program.cs:347-349`) so the central node is transparent and the map shows
through; ImGui windows dock along the **screen edges**; the map is visible only where they are not. The
BTree/HSM/Blueprint perspectives show no map at all — their central window is the graph.
🔒 **The map stays Raylib. Hosting it in an ImGui window is a [non-goal](UX_Requirements.md#non-goals).**

**The user's question:** *where is the map centre for a command like "centre map on this entity"?* The
map's screen extent is the window; its **visible** extent is the central node. They are different
rectangles.

#### What is already true

| Fact | Evidence |
|---|---|
| **The anchor already exists.** `Camera.Offset` is the screen point that `Camera.Target` maps to — set it and `FocusOn()` centres there | `MapCamera.cs:23-33`; `ScreenToWorld = Target + (screen − Offset)/Zoom` |
| `CenterOnEntityCommand` is handled by `_camera.FocusOn(entityWorldPos)` | `EditorSubsystem.cs:3898-3914` |
| 🔴 **The editor never sets `Offset`.** The ctor leaves it `Vector2.Zero` | no `Camera.Offset` assignment anywhere in `Hrot.Editor`; `MapCamera.cs:62` |
| ⇒ **prediction: centre-on-entity puts the entity at the window's top-left, under the docked panels** | ⚠ code-derived; the coordinator cannot run the editor. **Confirm on the walk** |
| Other hosts anchor to a full-window or **hardcoded** centre | `IgApplication.cs:617` (`WindowWidth/2, WindowHeight/2`); `CgfSubsystem.cs:577` and `SimHostVisualization.cs:226` both hardcode `1280/2, 720/2` |
| **Nothing anywhere is occlusion-aware** | no `ViewportRect`/`VisibleRect`/dock-inset concept in the presentation layer |
| Culling uses the **whole window**, which is correct for culling | `EditorSubsystem.cs:1598-1607` — `ScreenToWorld(0,0)` → `ScreenToWorld(GetScreenWidth(), GetScreenHeight())` into `MapCameraViewport` (a *world-space AABB*, so that name is taken) |
| There is precedent for headless inset math | `DockspaceLayout.CentralSize(work, toolbar, statusBar)` — pure, unit-tested, no ImGui dependency |

> **So the fix is one assignment, not a rewrite:** give the map an *effective viewport rect* and set
> `Camera.Offset` to its centre. Zero rendering change, zero perf cost, and `FocusOn`/frame/fit all become
> correct at once. The question is only **where that rect comes from**.

#### How is the effective viewport determined?

- **K1 — Ask ImGui for the dockspace central node** each frame (`DockBuilderGetCentralNode(dockspaceId)`
  → `Pos`/`Size`).
  *Reuse:* ImGui already computes exactly this rectangle — it is *the* definition of "not covered by
  docked windows", and it stays correct when the user drags a splitter or floats a panel.
  *Build:* one call + plumbing to the camera.
  ⚠ **Unverified:** no `DockBuilder*` API is used anywhere in this repo, so availability in this ImGui.NET
  binding must be checked first. If it is missing, K1 dies and K2 becomes the answer.
- **K2 — Derive it from the shell's declared layout insets** — the new shell knows its own edge docks, so
  it publishes `left/right/top/bottom` insets and the rect is window-minus-insets.
  *Reuse:* `DockspaceLayout` is the existing precedent, and this composes with
  [F-ii](#f-ii--what-does-the-new-shell-keep-of-the-window-machinery)'s **G2** lean (default layout as
  *data*): the same data that defines the layout defines the viewport.
  *Build:* an inset model + a per-perspective value.
  *Cost:* goes stale if the user drags a splitter, unless the shell tracks resizes.
- **K3 — Subtract the registered window rects** each frame (union of docked ImGui window rectangles).
  *Cost:* fragile and order-dependent; reinvents what ImGui already knows. Rejected unless K1 and K2 both
  fail.
- **K4 — Leave it window-centred and accept the offset.**
  *Cost:* this is today's behaviour, and it is why [UXR-18](UX_Requirements.md#uxr-18) is filed 🔴.

> **Claude's lean: K1 if the binding exposes the central node, else K2 — and expose the result as one
> named concept** (`IMapViewportProvider` or similar) that the camera, hit-testing, frame-all,
> edge-panning and gizmo placement all consult, rather than each recomputing it.
>
> **Also worth deciding here:** the map is meaningless in the graph perspectives, so the provider should
> report *no viewport* there rather than a stale rect — that makes "is there a map right now?" a single
> answerable question instead of an assumption spread across features.
>
> **Sub-question F⁗:** should this land as part of the new shell (clean, but waits on Q25-F-i) or as a
> **standalone fix to the current editor** (⚠ it touches `EditorSubsystem.cs`, which
> [SESSION_SYNC](../SESSION_SYNC.md#sequencing-rule) currently reserves for the MCP port)? It is a 🔴
> correctness defect that is cheap to fix, so the sequencing is a genuine question rather than an obvious
> "wait for the shell".

### F-iv — Does `--mode editor` survive?

- **I1 — Retire it once the new app reaches parity.** One editor, one walk, one answer to "what did you
  test?".
- **I2 — Keep both indefinitely.** *Cost:* two shells, an ambiguous golden-path walk, and doubled
  visual-verification burden — the divergence trap in slow motion.
- **I3 — Keep it as a thin alias** that launches the new shell.

> **Claude's lean: I1, with I3 as a courtesy during transition.** Under the staged F3 → F1 answer this
> resolves naturally: the flag becomes the alias, then goes away.

---

## 🔒 Do not relay yet — 2026-08-10

**Two of this document's questions were overtaken by
[UX_Current_UI_Architecture.md](UX_Current_UI_Architecture.md)** and must absorb it before the architect
sees them. Relaying as-is would have them rule on a question that no longer exists.

| Question | What changed |
|---|---|
| **F′ / F-i** — the exe seam | ⏸ **The exe question largely dissolves.** Every difference the requirement names — layout, main menu, map layers, context menus — is a **seam problem inside shared code**, not a hosting problem. Seams are exercised by whoever composes the panels; a second executable adds nothing a host profile could not express. The user also raised a cost this doc never weighed: **two parallel test paths** during any staged period |
| **D** — *"two audiences, one set of shared panels: what is the mechanism?"* | **Largely answered by measurement.** The mechanism already exists for some surfaces (`IEntityContextMenuHandler`, `MapCanvas.AddLayer`, `ITimeTransportFacade`, the inspector's seams) and is **absent** for others (main menu, ORBAT rows, camera, spawn, selection). D is no longer *"what mechanism?"* but *"do we make the existing contribution-seam pattern mandatory, and who owns the profile?"* — and it generalises from 2 audiences to **5 modes** |

**The finding both rest on:** every UI surface with a contribution seam is shared successfully; every
surface without one has been forked. No counter-example in five scans.

## Answers

*To be filled in by the user after the architect round. Record the chosen option per sub-question,
the architect's reasoning where it differs from Claude's lean, and any sub-question the architect
reframes rather than answers.*

| Question | Decision | Notes |
|---|---|---|
| **Q25-F-i — shared-init / new-shell seam** | — | *answer first; reframes D* |
| **Q25-F′ — staged F3→F1, straight F1, or F5?** | — | ⚠ **now a three-way choice** — the F3→F1 lean was measured and withdrawn; Claude leans **F5** (exe now, stage the extraction). [Why](#f-prime-measured) |
| **Q25-F-ii — what the shell keeps of the window machinery** | — | |
| **Q25-F-iii — how existing window content is combined** | — | *lean revised to H0-first by the parallel-development constraint* |
| **Q25-F″ — do the graph canvases move into the new shell?** | — | |
| **Q25-F-iv — does `--mode editor` survive?** | — | |
| **Q25-F-vi — how the effective map viewport is determined** | — | *lean K1 (ImGui central node) else K2 (declared insets)* |
| **Q25-F⁗ — fix the viewport now, or with the new shell?** | — | *🔴 correctness defect, cheap; but it touches `EditorSubsystem.cs`* |
| **Q25-F-v — staying out of the parallel blueprint work** | — | *lean J1 + J2* |
| **Q25-F‴ — what is the consultation channel in practice?** | — | *only the user can confirm the blueprint sessions will read it* |
| Q25-A — recoverability budget | — | |
| Q25-A′ — snapshot cost at authoring scale | — | |
| Q25-B-i — template representation | — | |
| Q25-B-ii — override semantics | — | |
| Q25-B′ — carry a template id from day one? | — | |
| Q25-C — affinity + param declaration | — | |
| Q25-C′ — can `BehaviorUiCompiler` be schema-driven? | — | |
| Q25-D — shared-panel audience mechanism | — | *may collapse into F: two hosts ⇒ two compositions* |
| Q25-D′ — is auto-retry on live-retask conflict ever safe? | — | |
| Q25-E — problems-list home | — | |

**On recording the answers:** update the corresponding `UXD` rows in
[UX_Design.md](UX_Design.md#3-design-decisions-uxd) from `OPEN`/`LEAN` to `DECIDED` with a pointer back
to this document, then unblock the affected milestones in
[UX_Task_Tracker.md](UX_Task_Tracker.md).
