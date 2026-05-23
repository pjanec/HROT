using System;
using System.Collections.Generic;
using System.Reflection;
using Fhsm.Kernel.Attributes;
using Fhsm.Kernel.Data;
using Hrot.Hsm.Editor.Model;

namespace Hrot.Hsm.Editor.Validation;

// Infers OutputLaneMask for each StateNode from action FQN -> CommandLane mappings.
// The mappings are built by reflecting against the loaded assembly.
public sealed class HsmOutputLaneMaskInferrer
{
    // Reflects all types in the given assemblies and builds a dictionary
    // from action FQN (full method name) to CommandLane.
    // Only methods with [HsmAction] attribute are included.
    // Methods with Lane = CommandLane.None are excluded (contribute no bits).
    public static IReadOnlyDictionary<string, CommandLane> BuildLaneDictionary(
        IEnumerable<Assembly> assemblies)
    {
        var dict = new Dictionary<string, CommandLane>(StringComparer.Ordinal);
        foreach (var asm in assemblies)
        {
            foreach (var type in asm.GetTypes())
            {
                foreach (var method in type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    var attr = method.GetCustomAttribute<HsmActionAttribute>();
                    if (attr == null) continue;
                    if (attr.Lane == CommandLane.None) continue;
                    // Use the full qualified name as the FQN key.
                    var fqn = type.FullName + "." + method.Name;
                    dict[fqn] = attr.Lane;
                }
            }
        }
        return dict;
    }

    // Computes OutputLaneMask for a single state using the pre-built lane dictionary.
    // Considers OnEntry, OnExit, Activity, and Timer actions.
    // Returns a byte where bit N = 1 when CommandLane N is used.
    public static byte ComputeMask(StateNode state,
        IReadOnlyDictionary<string, CommandLane> laneMap)
    {
        byte mask = 0;
        mask |= LaneBit(state.OnEntryAction, laneMap);
        mask |= LaneBit(state.OnExitAction, laneMap);
        mask |= LaneBit(state.ActivityAction, laneMap);
        mask |= LaneBit(state.TimerAction, laneMap);
        return mask;
    }

    // Returns the bit contribution of a single action FQN.
    private static byte LaneBit(string? fqn, IReadOnlyDictionary<string, CommandLane> laneMap)
    {
        if (fqn == null) return 0;
        if (!laneMap.TryGetValue(fqn, out var lane)) return 0;
        if ((byte)lane >= (byte)CommandLane.Count) return 0;   // None or unknown
        return (byte)(1 << (byte)lane);
    }

    // Applies inferred OutputLaneMask to all states in the asset.
    public static void ApplyToAsset(HsmAsset asset,
        IReadOnlyDictionary<string, CommandLane> laneMap)
    {
        foreach (var s in asset.AllStates)
            s.OutputLaneMask = ComputeMask(s, laneMap);
    }
}
