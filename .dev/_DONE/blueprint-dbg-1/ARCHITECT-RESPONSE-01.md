# Response to architect — re: DEBUG-DD-ADDENDUM review

Thanks for the review — it sharpened the addendum. Two of your points changed the design (we were wrong on
Trace), two we've reconciled differently after checking the current source. Below is where we landed and the
reasoning, so future guidance lands on the same model. The updated addendum is attached.

## The governing principle: editor-on-demand instrumentation

Blueprint debugging in this product is **purely interactive — it only ever happens inside the editor.** That single
fact drives the architecture:

- **The production / Full-Rebuild path stays `Release`: no probes, no debug map.** It is fast and clean, and ships
  nothing debug-related.
- **The editor instruments on demand, in memory.** The moment a breakpoint/watch is placed on an asset (or a saved
  session is restored), the editor sets that asset's compile mode and performs an **in-memory Quick Reload** →
  `DebugProbe.NodeEnter` calls + `DebugMap` go live for that asset only. Zero probe overhead until the user
  actually debugs.

This is deliberately the **best of both worlds**: production builds stay fast, and the developer gets full
interactive debugging — **without any debug state being committed to source.** Please treat this as the baseline
for blueprint debugging guidance going forward.

## Point-by-point

### 1. Full Rebuild / generator — we are NOT baking probes into the build (by design)

Your concern was that hardcoding `Release` in `BlueprintIncrementalGenerator` breaks debugging after a Full
Rebuild. Under our model it does not: **CF-7-rev makes Full-Rebuilt / precompiled assets debuggable via the
on-demand in-memory reload above** — the pause path is restored by the editor, not by the build output. So there is
no "missing pause" regression to re-introduce.

We also can't adopt the specific fix you proposed — "the generator reads `asset.EditorMetadata.CompilerMode`."
`EditorMetadata` is serialized **into the committed `.bp.json`**. Driving the production build off it would bake
**per-asset debug state into shared source control**, which directly contradicts the per-user / never-committed
rule you (rightly) stated for breakpoints. If we ever genuinely needed build-baked probes, the correct lever would
be the **build *configuration*** (`Debug` ⇒ instrument), never per-asset metadata — but for an editor-only feature
we don't need it at all.

### 2. Trace mode for conditions — you were right; corrected

Confirmed in code: `DataBreakpointSystem` evaluates compiled predicates against ECS chunk memory via
`EntityRepository.QueryDelta` at the tick boundary — conditions do **not** flow through `DebugProbe.PinValueChanged`.
So forcing `Trace` for a conditional breakpoint would have been a real per-tick regression (pin-value boxing) for no
benefit. The addendum now reads: **node breakpoints and conditional data breakpoints need only `Debug`; `Trace` is
required *only* for pin-value Watches** (the Watch panel). Thanks for catching this.

### 3. "Orphaned probe calls" — agreed, reworded

You're correct that deterministic regeneration means a **freshly regenerated build never emits a probe for a
deleted node.** We've reworded §5 accordingly. The defensive "ignore unknown node id, never throw" path remains, but
it is scoped to what actually happens: (a) the normal case where every non-breakpointed node still calls the probe
(a dictionary miss), and (b) the transient window where a **stale in-memory build** (running code from before a
re-instrument/reload) emits old node ids. It is not claiming a current build emits phantom calls.

### 4. Predicate DTO serialization — risk valid, mechanism corrected

The polymorphism is **attribute-based**: `[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]` plus
`[JsonDerivedType(...)]` declared directly on `SearchPredicateDto` (Compound / PropertyMatch / BlueprintVariable /
ExternalHitTag / …). System.Text.Json resolves these with **default options** — which is why `WatchPersistence`'s
plain `JsonSerializerOptions` already round-trips conditions today and `SearchPredicateDtoSerializationTests` pass.
So a dedicated `FdpJsonOptionsRegistry` is **not required** for the predicate hierarchy. Your underlying concern is
still valid, though: at least one value DTO is intentionally outside the `[JsonDerivedType]` list, so CF-8 will
round-trip a deeply-nested condition in a test and make an **unresolved derived type fail loudly** rather than
silently drop the condition.

### Where your guidance landed squarely (adopted as-is)

- **`DataBreakpointManager` as the load-independent, durable owner.** Verified: `OnHotReloadCompleted` drops stale
  delegates and re-mounts from the retained DTOs; `IsBroken`/null-delegate gives us the "pending/inert until a clean
  map" state for free. CF-8 reuses this rather than reinventing it.
- **BPF-003 "stale but retained"** (disabled + yellow marker, user re-binds or discards).
- **Pending → auto-bind on load** (breakpoints set before an asset loads bind on `RegisterDebugMap`).
- **Per-user, gitignored persistence**, conditions saved as DTOs and recompiled via `PredicateCompiler` on load.

## Ask going forward

The codebase moves fast, so let's anchor **code-level specifics** (compiler modes, APIs, file paths, current
behavior) on the current source — a couple of the specifics in the review (the generator-mode trigger, the
serializer-registry requirement) didn't match the tree as it stands, and we verify those against code before
acting. Where you add the most value is exactly what this review did well: **design intent and lifecycle**
(ownership, staleness, pending semantics). Keep that coming.

Updated `DEBUG-DD-ADDENDUM.md` attached — would value your read on whether the editor-on-demand model and the
storage/lifecycle sections now hang together.
