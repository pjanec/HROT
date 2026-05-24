# Onboarding — AI Authoring Flexibility & Reactivity Discussion

## Why this document exists

The previous chat consolidated the EQS (Environment Query System) design and produced `EQS_Design_v1.3_final.md`. Toward the end, the conversation expanded into broader topics: blackboard architecture, BTree action lifecycles, comparison with AAA engines, and ultimately a meta-question about whether the engine's existing AI authoring stack provides AAA-quality flexibility and designer ergonomics. The EQS-focused chat had become polluted with this broader discussion, so we agreed to move it to a fresh chat.

**This document explains where the previous discussion landed and what should happen next.**

---

## How to start

When you (Claude) start the new chat, read this document first, then read the design documents referenced in section "Project documents you must read" below before engaging with my questions. The conversation is mid-flight; don't propose anything from scratch.

---

## The engine in one paragraph

Hrot/FDP is a 2-node distributed game engine (Brain–Muscle CQRS). Brain runs cognitive AI on networked entities; Muscle runs physics, perception, animation, and the detailed world. The two nodes communicate via CycloneDDS with both component replication and discrete events. The engine is heavily performance-optimized for thousands of entities, but also aspires to AAA-quality designer-friendly authoring for scenarios with fewer high-fidelity entities. The architecture is ECS-based throughout, with zero-allocation hot paths, snapshot-on-demand for background modules, and a sophisticated hot-reload pipeline.

---

## Project documents you must read

In priority order. Read at least the first four before engaging.

### Tier 1 — read carefully

1. **`HROT_architecture.md`** — the foundational engine architecture document. Brain/Muscle split, CQRS pattern, ECS conventions, snapshot-on-demand, channels, DDS replication.

2. **`EQS_Design_v1.3_final.md`** (in project documents) — the completed EQS design. Contains canonical examples of the engine's idioms: `[…]Sensor` component for Brain→Muscle config replication, `[…]ResultEvent` unmanaged-handle-into-pool pattern for upward results, `[…]CognitiveBuffer` for Brain-side cached results, hot-reload pipeline integration, attribute-based authoring with stable AssetId GUIDs.

3. **`Blueprint_Subsystem_Architecture_v1_2.md`** — the architecture of the Blueprint authoring subsystem. **Critical reading.** Three dispatch kinds (Library, AiPrimitive, Instance). AiPrimitive is the key concept: one graph hosted by BTree action, BTree condition, HSM action, HSM guard, and/or BlueprintCall. Channel Command Catalog and Wait Primitive Catalog as visual nodes. Three blackboard tiers (1024/4096/16384) with partition allocator for multi-Blueprint per entity. Hot reload with per-slot soft/hard reconciliation.

4. **`AI_Editor_Shared_Infrastructure.md`** — the shared editor substrate underneath BTree, HSM, and Blueprint editors. Shared selection store, asset browser, inspector, refactor service, find-references, debug session base, runtime overlay control, trace timeline. Three editors share infrastructure; cross-asset refactor is first-class.

### Tier 2 — skim or read selectively

5. **`BTree_Editor_NodeEditor_Host_Design.md`** — BTree-specific editor host. Author writes fluent C#; editor projects from compiled assembly. Decorators render as Unreal-style pills via NodeAttachments. ObserverSelector has distinct visual treatment. Quick Reload ≤ 100ms.

6. **`HSM_Editor_NodeEditor_Host_Design.md`** — HSM editor host with nested composite states via ContainerNodes extension.

7. **`Blueprint_Subsystem_Editor_Detailed_Design.md`** — the Blueprint visual graph editor. ImGui-based. Per-graph node positions in EditorMetadata. Quick Reload pipeline targets ~100ms turnaround. StructEdit drawers for properties.

8. **`Blueprint_Subsystem_Runtime_Detailed_Design.md`** — runtime systems for Blueprint dispatch. Partition allocator for multi-Blueprint blackboards. `BlueprintTickSystem`, `BlueprintMaintenanceSystem`. Zero-allocation hot path.

9. **`Blackboard1024`** (the project doc file) — the heavy-state blackboard component. 1024 bytes inline per entity. Used by AiPrimitive working state. Decorated `[DataPolicy(DataPolicy.NoSave)]` for transient cognitive state.

10. **`Sharing_blackboard_across_whole_behavior__btree_`** (the project doc file) — earlier architect discussion about BTree blackboard sharing. Shows the existing `BrainBlackboard.BehaviorParameters` (100 bytes inline) projection model and the `ReusableActionDelegate<TValue, TContext>` pattern with expression-based field binding.

### Tier 3 — reference only when relevant

11. **`Blueprint_Subsystem_Compiler_Detailed_Design.md`** — Roslyn pipeline that compiles `.bp.json` → C#.
12. **`Blueprint_Subsystem_Hot_Reload_Detailed_Design.md`** — coordinator that handles ALC swaps and per-slot reconciliation.
13. **`Blueprint_Subsystem_Debug_Protocol_Detailed_Design.md`** — debug probes, breakpoints, watch.
14. **`NodeEditor_Extension_NodeAttachments.md`** — visual extension primitive for BTree decorator pills and HSM badges.
15. **`NodeEditor_Extension_ContainerNodes.md`** — visual extension for HSM nested composites.
16. **`NodeEditor_Extension_CustomCanvasRenderer.md`** — visual extension for custom overlays (observer-guard badges, runtime highlighting, heatmaps).
17. **`Responses-to-Claude-1.md` … `Responses-to-Claude-4.md`** — architect's answers from the EQS discussion. Heavy with engine-specific patterns.
18. **`Hrot-project-docs.txt`** — additional engine-wide project documentation.

---

## The state of the previous discussion

### What I confirmed by reading the design documents

The engine is not "missing AAA flexibility" and bolting on a second tier. It already has **three peer authoring surfaces** sharing infrastructure:

- **FastBTree** — hot-path stateless-delegate behaviors. Authors write fluent C#. ObserverSelector provides branch-level reactivity. Sized for thousands of entities. Decorator pills via NodeAttachments. **For: high-entity-count specialized behaviors.**

- **FastHSM** — peer state-machine system with deferred events, transition guards, nested composites. Same authoring substrate. **For: behaviors that are naturally state-driven.**

- **Blueprint subsystem** — visual graph editing on `.bp.json`, Roslyn-compiled. AiPrimitive dispatch hosts one graph as BTree action, BTree condition, HSM action, HSM guard, or BlueprintCall. Channel Command Nodes and Wait Nodes provide visual authoring of the engine's CQRS patterns. Three blackboard tiers (1024/4096/16384) with partition allocator for multi-Blueprint per entity. Quick Reload < 100ms. Full debug protocol with breakpoints/watches/steps. **For: designer-friendly, flexible, reusable AAA-quality behaviors.**

All three share:
- Unified editor with shared selection, inspector, refactor, find-references
- Identity model: GUID in editor, FNV-1a-32 hash in runtime
- Hot-reload pipeline with structure-hash-based soft/hard reconciliation
- The same `AiHotReloadCoordinator`
- Visual extensions (NodeAttachments, ContainerNodes, CustomCanvasRenderer)

**This is past "AAA-quality."** Most shipping AAA games don't have this level of unified authoring polish for their AI stack.

### What's genuinely missing

After reading the designs, my read of the gap is much narrower than I'd initially thought. The big gap that remains is reactivity at the **data layer**:

All three blackboards (`BrainBlackboard.BehaviorParameters`, `Blackboard1024`, `BlueprintBlackboard{1024,4096,16384}`) are passive byte storage. Reactivity is expressed at graph/tree topology (BTree ObserverSelector, HSM transitions with guards), not at the data level (Unreal-style "key X changed → react").

Concrete scenarios where this matters:

1. **Cross-Blueprint reactivity within one entity.** Blueprint A writes to a shared key; Blueprint B must poll, can't be notified.
2. **Cross-tier reactivity.** A BTree wanting to react to a Blueprint's state, or vice versa, must poll.
3. **Conditional decorators on Blueprint sequences.** "Abort this latent wait if cover position becomes invalid" requires explicit polling structure.
4. **Event-driven BTree aborts based on Blueprint state.** ObserverSelector can read Blueprint blackboard slots but isn't notified of changes.

### Three lightweight solutions on the table

**Solution A — Versioned blackboard slots.** Add one `uint Version` field per partition slot. Generated Blueprint setters bump it on write. Decorators, ObserverSelector predicates, and Wait-node conditions read the version on entry and compare each tick. Cheap; high-value polling-based reactivity better than current.

**Solution B — ECS-event-driven Tier-2 reactivity.** Extend the Blueprint subsystem's event polling to subscribe to "blackboard-slot-changed" events emitted by other Blueprints. Per-frame event flush of dirty-slot notifications. Stays event-bus-driven (matches engine pattern). Avoids push callbacks.

**Solution C — Canonical "shared blackboard" Blueprint pattern.** Designate one well-known Blueprint per entity (`EntityState`) or per squad (`SquadState`) as the home for cross-cutting state. Others peer-call into it. ObserverSelectors observe its ECS slot. This is a documented pattern, not new infrastructure.

My recommendation at the end of the previous chat: **A + C, defer B until shipping Slice 1 reveals whether it bites.**

### Three crisp questions queued for the architect

These were drafted at the end of the previous chat but not yet sent. The new chat should send them (or refine first):

1. **Do the AiPrimitive Wait nodes today react to changes in their wait condition mid-wait, or do they re-evaluate only on completion of their underlying latent operation?** Determines whether "abort wait on cover invalidation" needs new mechanism.

2. **Is there a planned canonical pattern for "shared per-entity state across Blueprints"?** If not, would it be reasonable to designate one well-known Blueprint name (e.g., `EntityState`) as the convention, with the editor surfacing it specially?

3. **Would a per-slot `Version` field on `BlueprintBlackboard*` slots (bumped automatically by generated setters) be acceptable, and could ObserverSelector predicates and Wait-node conditions opportunistically read it for change detection?** Smallest possible observer feature; composes with what exists.

---

## What previously happened that's *not* relevant to this discussion

The EQS chat covered a lot of ground that this chat does NOT need to revisit:

- The EQS sensor lifecycle (Option 4: hybrid resource-owning actions with `OnDeactivate`) — user committed to implementing this; it's done.
- The EQS design document (v1.3) — complete; the user has it.
- The EQS solver architecture, snapshot model, raycast caps, four-phase evaluation — all decided.
- The EQS authoring approach (hand-written C# with `[EqsTemplate(AssetId=...)]`) — settled.

If the user references EQS, treat it as background; don't redesign anything.

---

## What this chat is about

The user wants to discuss **AI authoring flexibility and designer-friendliness in the engine**, specifically:

- Whether the existing three-subsystem stack (FastBTree, FastHSM, Blueprint) genuinely meets AAA-quality designer ergonomics
- Whether the reactivity gap at the data layer matters in practice and how to close it cheaply
- Comparison with how AAA engines (Unreal especially) handle these concerns
- Practical patterns for the engine's "few entities, high fidelity" scenarios versus its "thousands of entities" scenarios

The user values:
- Honest comparison with AAA engines, including where the engine's design is *better* than AAA conventions and where it's *worse*
- Architect-quality questions that surface genuine engine concerns
- Pragmatic recommendations that respect the engine's performance commitments
- Concrete proposals when warranted, NOT vague architectural musing

The user does NOT want:
- Reinventing what already exists in the Blueprint subsystem
- Suggesting that the engine needs a "Tier 2" — it already has three peer subsystems
- Long-winded design proposals before the conversation has scoped its actual concern

---

## How to engage

Once you've read at least the Tier 1 documents above, open with a short message that:

1. Acknowledges you've read the context.
2. Briefly states the situation as you understand it (three peer authoring surfaces with shared infrastructure, narrow reactivity gap at the data layer).
3. Asks 2–3 targeted questions to confirm the user's specific concern within this space. The user mentioned wanting to discuss flexibility, friendliness, and feature parity with AAA; let them narrow it.
4. Do NOT propose architecture or solutions yet. Wait for the user to direct the conversation.

A good opener might focus on:
- What scenarios specifically concern the user (cross-Blueprint reactivity? designer onboarding? authoring ergonomics? something not on my list?)
- Whether the user wants to validate the existing roadmap or extend it
- Whether the three queued architect questions should be sent first to ground further discussion

Above all: respect that the user has built a sophisticated stack. The conversation should be high-signal, comparing real options, not re-deriving what's already decided.

---

## Other practical notes

- The user's editor at `EQS_Design_v1.3_final.md` (in outputs) is the EQS reference; don't recreate it.
- The user prefers low-formatting prose for typical messages; reserve headers/bullets for genuine structure.
- The user is responsive to "what AAA engines do" comparisons backed by concrete references (Unreal docs, GDC talks). Web search when useful.
- The user works with an "engine architect" who answers technical questions about the engine codebase. Frame questions for them clearly when you have them.
- The architect questions from the EQS chat went through `Responses-to-Claude-N.md` files; the same pattern continues here.

Good luck.
