# Slice: `Action_ReverseToBaseline` (design)

> Migration slice 2. Rebuild the oracle `HillAttackTankNodes.Action_ReverseToBaseline` as a blueprint.
> Uses only **shipped** capabilities (`ChannelCommand`, `WaitForChannel`, `GetParameter`, `PublishEvent`,
> `Return`) + **one small curated helper** for vector construction. No new compiler node/IR. No architect
> gate (authoring slice of shipped nodes); this note is the in-repo design gate.

## Oracle (ground truth — `HillAttackTankNodes.cs:456-508`)

Latent action on `HullDownAttackParams`:
1. No `LocomotionChannel` → `Failure`.
2. (Sync `loco.BehaviorInstanceId = behav.InstanceId`.) ← **DROP** — architect Q5-B: `BehaviorState`/arbitration bookkeeping is the `ChannelCommand` node's job, not a raw brain write.
3. If `ActiveAction == MoveTo` and `Status == Success` → `publish ClearBehaviorEvent{self}`, return `Success`; if `Status == Failure` → `publish ClearBehaviorEvent{self}`, return `Failure`.
4. Else issue once: `MoveTo { Destination = (BaselineX, BaselineY, 0), ArrivalRadius = 5, Speed = 12, ReverseAllowed = 1 }`, return `Running`.

The "issue once + poll terminal + return Running meanwhile" loop is exactly what **`ChannelCommand` +
`WaitForChannel`** encapsulate (`WaitForChannel`'s AiPrimitive lowering: while channel `Running` return
`Running`; on `Failure` return `Failure`; on `Success` continue on `Out` — see
`WaitLowering_AiPrimitive.cs`).

## Blueprint graph

```
EventEntry
  → ChannelCommand(LocomotionChannel / MoveTo)          // issues the retreat command
       Destination   ← VectorOps.Vec3(GetParameter(BaselineX), GetParameter(BaselineY), Literal 0f)
       ReverseAllowed ← PinDefault "1"
       Speed          ← PinDefault "12"
       ArrivalRadius  ← PinDefault "5"
  → WaitForChannel(LocomotionChannel)                    // suspends; Running→Running, Failure→Failure(auto)
  → PublishEvent(ClearBehaviorEvent, Target=self)        // success terminal
  → Return(Success)
```

- **Parameters:** `BaselineX`, `BaselineY` (`System.Single`) — declared to mirror `HullDownAttackParams`; read via `GetParameter` (GAP-11).
- **Dispatch:** AiPrimitive, Intent=Action, Hostings=[BTreeAction].

## The two design points

1. **`Vector3` construction (new curated helper).** `MoveToParams.Destination` is a
   `System.Numerics.Vector3`; the graph has no vector-literal/constructor. Add a curated, reflection-free,
   contextless pure helper — the architect-blessed "keep the fiddly bit in reviewable C#, call it via
   `FunctionCall`" pattern (like `UnitRosterOps`/`HillAssault2NavOps`):
   ```csharp
   public static class VectorOps           // new public blueprint API
   {
       public static Vector3 Vec3(float x, float y, float z) => new Vector3(x, y, z);
       public static Vector3 Vec2(float x, float y)          => new Vector3(x, y, 0f); // convenience (round-out)
   }
   ```
   Broadly useful beyond this slice (any position/direction authored from scalars). `FunctionCall`
   (IsPure, TrailingContext=None) wired: `x ← GetParameter(BaselineX)`, `y ← GetParameter(BaselineY)`,
   `z ← Literal(0f)`; `Return` → `ChannelCommand.Destination` pin.

2. **`ClearBehaviorEvent` on the FAILURE terminal (documented deviation).** The oracle publishes
   `ClearBehaviorEvent` on **both** Success and Failure. `WaitForChannel`'s lowering auto-returns
   `Failure` on channel failure **without** running the post-wait chain, so the blueprint publishes
   `ClearBehaviorEvent` **only on Success**. Accepted simplification: on failure the BTree selector moves
   on regardless, and there is no blueprint primitive to inject a publish into the latent auto-failure
   path. **Record as DEVIATION in the migration log** (mirrors how slice-1 recorded its deviations). If
   fidelity is later required, options: a `WaitForChannel` variant exposing an explicit failure exec-out,
   or poll the channel status manually with `GetComponent(LocomotionChannel).Status` + `Compare` + `Branch`
   (heavier; deferred).

## Proof

`HillAssault2_ReverseToBaseline.bp.json` + `*_ProofTests.cs` (real generator). Source-inspection: generated
`TickCore` contains the `MoveTo` channel write with `ReverseAllowed`, the `VectorOps.Vec3(` destination
build from the two params, the latent wait, and `world.Bus.Publish(new ...ClearBehaviorEvent` on success.
Behavioral (if feasible headlessly): drive a `LocomotionChannel` to `Success` → asset returns `Success` +
one `ClearBehaviorEvent`. Coverage/gates as usual. Oracle untouched.
