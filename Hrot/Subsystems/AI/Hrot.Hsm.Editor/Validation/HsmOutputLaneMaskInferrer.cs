using System;
using System.Collections.Generic;
using System.IO;
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
    /// <summary>
    /// ⭐⭐⭐ <c>HSM-007</c> — the loaded-assembly overload, and it is what finally FEEDS this class.
    ///
    /// <para>📐 Measured 2026-10-06: before this, <see cref="ApplyToAsset"/> and <see cref="BuildLaneDictionary"/>
    /// had <b>no callers outside their own tests</b>. Every editor-owned asset therefore carried
    /// <c>OutputLaneMask == 0</c>, so <c>HsmValidator.CheckOutputLaneConflicts</c> could never fire, the
    /// <c>hsm.region_conflicts</c> renderer stayed dark despite being correctly fed, and the inspector's
    /// read-only "Output lanes (inferred)" summary was always blank. §10.3 of
    /// <c>HSM_Editor_NodeEditor_Host_Design.md</c> was implemented and unreachable.</para>
    ///
    /// <para>⭐ The assembly set and the failure guard deliberately MIRROR <c>ActionSchemaExporter.Rebuild</c> /
    /// <c>ScanAssembly</c> — the editor's other reflection pass over the same attributes — so the two cannot
    /// disagree about which assemblies count.</para>
    /// </summary>
    public static IReadOnlyDictionary<string, CommandLane> BuildLaneDictionaryFromLoadedAssemblies()
        => BuildLaneDictionary(AppDomain.CurrentDomain.GetAssemblies());

    public static IReadOnlyDictionary<string, CommandLane> BuildLaneDictionary(
        IEnumerable<Assembly> assemblies)
    {
        var dict = new Dictionary<string, CommandLane>(StringComparer.Ordinal);
        foreach (var asm in assemblies)
        {
            // ⚠ HSM-007 — a partial load must not take the whole pass down. Same guard, same reason, as
            //   ActionSchemaExporter.ScanAssembly: process the types that DID load.
            Type[] types;
            try
            {
                types = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = Array.FindAll(ex.Types, t => t != null)!;
            }
            catch (Exception ex) when (ex is TypeLoadException or BadImageFormatException or FileNotFoundException)
            {
                continue;
            }

            foreach (var type in types)
            {
                // ⚠ The three guards below are NOT defensive clutter — ALL THREE levels can throw once this walks
                //   every loaded assembly, and the per-METHOD one is the live case: reading an attribute resolves
                //   the attribute's own type graph, so a method in an assembly compiled against a different
                //   framework surface throws TypeLoadException from GetCustomAttribute, NOT from GetTypes.
                // 📌 Measured 2026-10-06: without it, 15 rails went red with
                //   "Could not load type 'System.Diagnostics.CodeAnalysis.DoesNotReturnAttribute' from assembly
                //   'Microsoft.TestPlatform.CoreUtilities'" — a host assembly that merely happened to be loaded.
                // ⭐ Same three levels, same order as ActionSchemaExporter.ScanAssembly.
                MethodInfo[] methods;
                try
                {
                    methods = type.GetMethods(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                }
                catch
                {
                    continue;
                }

                foreach (var method in methods)
                {
                    HsmActionAttribute? attr;
                    try
                    {
                        attr = method.GetCustomAttribute<HsmActionAttribute>();
                    }
                    catch (Exception ex) when (ex is TypeLoadException or BadImageFormatException or InvalidOperationException or FileNotFoundException)
                    {
                        continue;
                    }

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
    // ⚠ HSM-012 — Timer is still counted on purpose: the mask states what the asset DECLARES, and a state that
    //   still carries a timer binding is reported separately (HsmValidator.CheckTimerActionNotImplemented).
    //   ⛔ Do not silently drop its bit here — that would put "timers never fire" in a second place.
    public static byte ComputeMask(StateNode state,
        IReadOnlyDictionary<string, CommandLane> laneMap)
    {
        byte mask = 0;
        mask |= LaneBit(state.OnEntry?.MethodFqn, laneMap);
        mask |= LaneBit(state.OnExit?.MethodFqn, laneMap);
        mask |= LaneBit(state.Activity?.MethodFqn, laneMap);
        mask |= LaneBit(state.Timer?.MethodFqn, laneMap);
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
