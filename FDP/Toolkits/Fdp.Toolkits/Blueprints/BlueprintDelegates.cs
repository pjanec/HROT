using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Blueprints;

/// <summary>
/// Initializes the entity's blackboard slot to its asset-declared default state.
/// Generated code writes non-zero variable defaults via this delegate.
/// </summary>
public delegate void InitDefaultDelegate(Span<byte> stateBytes);

/// <summary>
/// Ticks one Instance-dispatch Blueprint for one entity per frame.
/// Per Runtime DD §3.3 and Compiler DD Patch Q-18.1.
/// </summary>
public delegate void TickDelegate(
    Span<byte>             stateBytes,
    ISimulationView        view,
    IEntityCommandBuffer   ecb,
    Entity                 self,
    float                  time,
    float                  deltaTime,
    uint                   instanceVersion);

/// <summary>
/// Invokes a compiled Library-blueprint <b>function</b> (G2). A Library function is stateless and
/// name-addressable; this is the runtime seam through which a blueprint-authored parameter resolver
/// is dispatched. Inputs are marshalled (blittably) into <paramref name="inputs"/> in declaration
/// order; the function's return value (if any) is written to <paramref name="outputs"/>. World
/// context (geo transform, entity map, time) is reached through <paramref name="view"/> (R4).
/// </summary>
public delegate void LibraryFunctionDelegate(
    ReadOnlySpan<byte>     inputs,
    Span<byte>             outputs,
    ISimulationView        view,
    Entity                 self,
    float                  time);

/// <summary>
/// Dispatches a single engine or custom event to one Instance-dispatch Blueprint.
/// Per Runtime DD §3.3 and Compiler DD Patch Q-18.3.
/// </summary>
public delegate void EventHandlerDelegate(
    Span<byte>             stateBytes,
    ISimulationView        view,
    IEntityCommandBuffer   ecb,
    Entity                 self,
    float                  time,
    float                  deltaTime,
    ReadOnlySpan<byte>     payload);

