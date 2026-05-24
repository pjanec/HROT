QUESTIONS

1. **Do the AiPrimitive Wait nodes today react to changes in their wait condition mid-wait, or do they re-evaluate only on completion of their underlying latent operation?** This determines whether the most natural "abort wait on cover invalidation" use case needs any new mechanism.

2. **Is there a planned canonical pattern for "shared per-entity state across Blueprints"?** If not, would it be reasonable to designate one well-known Blueprint name (e.g., `EntityState`) as the convention for cross-cutting blackboard data, with the editor surfacing it specially?

3. **Would a per-slot `Version` field on `BlueprintBlackboard\*` slots (bumped automatically by generated setters) be acceptable, and could ObserverSelector predicates and Wait-node conditions opportunistically read it for change detection?** This is the smallest possible "observer" feature and it composes with what exists.


RESPONSES


Regarding your first question, `AiPrimitive` `Wait` nodes (such as `WaitForChannel` or `WaitForEvent`) evaluate their target conditions continuously every single tick mid-wait. During the lowering phase, the compiler translates a `Wait` node into a phase-byte state machine that returns `NodeStatus.Running` to the host kernel. On each subsequent tick, the generated code performs an unmanaged read (e.g., `GetComponentRO` on the target channel) to check the `Status` field. If the status flips from `Running` to `Failure` or `Success` (for instance, if cover is invalidated and a channel aborts), the `Wait` node detects it immediately on that exact frame and branches accordingly.

For your second question, while we do not enforce a hardcoded "shared blackboard" Blueprint, the architecture natively supports this through the `callablePeers` mechanism. Because Instance Blueprints isolate their state into distinct partition slots within the `BlueprintBlackboard*` components, the canonical pattern is to author an `EntityState` Instance Blueprint and declare it in the `callablePeers` list of other assets. Downstream Instance Blueprints can then make synchronous, in-frame calls to `EntityState`'s pure getter functions to read shared context. Additionally, `AiPrimitive` assets can access these shared states if the target Blueprint exposes a `BlueprintCall` hosting.

As for the third question, introducing a per-slot `Version` field bumped by generated setters is completely acceptable and fits perfectly into the engine's unmanaged memory model. We actually already employ a similar `uint InstanceVersion` on `BlueprintSlotEntry` (currently bumped only on hard reloads to invalidate stale latent cursors). Because all `BlueprintBlackboard*` slot memory accesses are raw pointer operations, incrementing a version integer on mutation would be zero-allocation and practically free on the hot path. 

However, keep in mind that our current architecture already relies heavily on cheap, continuous polling: `Wait` nodes poll their targets every frame, and the FastBTree `ObserverSelector` is specifically designed to re-evaluate its guard children from the root on every tick to guarantee reactivity. While an opportunistic version check would be a brilliant optimization to short-circuit unchanged predicates, the current 60Hz unmanaged polling is already so cache-friendly that you may not even need the versioning check unless profiling shows an actual bottleneck in those specific Blueprint graphs.
