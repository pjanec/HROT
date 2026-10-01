using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Blueprints;

namespace Hrot.Blueprints.Core.Debug;

/// <summary>
/// What a Behavior-dispatch blueprint (<c>BrainTier 3</c>, <c>CE-446</c>) holds right now: its name, where its latent
/// coroutine will resume, its parameters and its working fields.
/// </summary>
public sealed record BehaviorBlueprintState(
    string BehaviorName,
    uint ResumeAt,
    float WaitUntilTime,
    IReadOnlyDictionary<string, object?> Parameters,
    IReadOnlyDictionary<string, object?> Variables);

/// <summary>
/// ⭐ <b>The ONE reader of a Behavior-dispatch blueprint's root block</b> — used by the debug API's trace AND variables
/// routes, on the editor and on a cluster alike. 📄 <c>docs/blueprints/DESIGN_Cluster_Ai_Debug_Surface.md</c> §2 D5.
/// </summary>
/// <remarks>
/// <para>📐 <b>Why a reader of its own.</b> A Behavior blueprint's registrar registers a <see cref="BehaviorDefinition"/>
/// only — <b>no</b> <see cref="BlueprintDefinition"/> — so <see cref="BlueprintDebugSession"/>'s registry lookup misses it
/// and its AiPrimitive reader walks only HSM-kind slots; the root block is kind <c>BlueprintBehavior</c>. Its state was
/// therefore unreadable on every host (<c>CE-476</c>).</para>
///
/// <para>⭐ <b>Typed by the definition, not a debug map.</b> The root block IS the emitted <c>State</c> struct —
/// <c>[Cursor][Params][working fields]</c>, registered as <see cref="BehaviorDefinition.BlackboardLayoutType"/>
/// (Q77 §5). Reading it through that type cannot drift from the compiler and needs no editor-loaded asset.</para>
/// </remarks>
public static unsafe class BlueprintBehaviorStateReader
{
    private static readonly MethodInfo ReadAsOpen =
        typeof(BlueprintBehaviorStateReader).GetMethod(nameof(ReadAs), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    /// Reads <paramref name="entity"/>'s running Behavior blueprint, or returns <c>false</c> when the entity is not running
    /// one, its behaviour is not in <paramref name="behaviors"/>, or its root block is absent.
    /// </summary>
    public static bool TryRead(
        ISimulationView view, Entity entity, BehaviorRegistry? behaviors, out BehaviorBlueprintState state)
    {
        state = null!;
        if (view is null || behaviors is null) return false;
        if (!view.IsAlive(entity) || !view.HasComponent<BehaviorState>(entity)) return false;

        ref readonly var brain = ref view.GetComponentRO<BehaviorState>(entity);
        if (brain.BrainTier != BehaviorConstants.BrainTierBlueprint || brain.ActiveBehaviorHash == 0) return false;
        if (!behaviors.TryGetDefinition(brain.ActiveBehaviorHash, out var def)) return false;

        var layout = def.BlackboardLayoutType;
        if (layout is null || !layout.IsValueType) return false;
        if (!RootParamsAccess.TryGetRootBytesInView(view, entity, out byte* root) || root == null) return false;

        object block = ReadAsOpen.MakeGenericMethod(layout).Invoke(null, new object[] { (IntPtr)root })!;

        uint resumeAt = 0;
        float waitUntil = 0f;
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
        var variables  = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var field in layout.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            object? value = field.GetValue(block);
            if (field.FieldType == typeof(BlueprintLatentCursor))
            {
                var cursor = (BlueprintLatentCursor)value!;
                resumeAt  = cursor.ResumeAt;
                waitUntil = cursor.WaitUntilTime;
            }
            else if (field.Name == "Params" && value is not null)
            {
                foreach (var p in field.FieldType.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    parameters[p.Name] = p.GetValue(value);
            }
            else
            {
                variables[field.Name] = value;
            }
        }

        state = new BehaviorBlueprintState(def.Name ?? string.Empty, resumeAt, waitUntil, parameters, variables);
        return true;
    }

    // Copies the root block into a boxed T — exact for InlineArray and nested unmanaged structs alike.
    private static object ReadAs<T>(IntPtr ptr) where T : unmanaged => Unsafe.Read<T>((void*)ptr);
}
