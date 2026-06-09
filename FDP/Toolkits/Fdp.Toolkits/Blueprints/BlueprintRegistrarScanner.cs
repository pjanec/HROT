using System;
using System.Reflection;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Blueprints.Attributes;
using Fhsm.Kernel;

namespace Fdp.Toolkit.Blueprints;

/// <summary>
/// Shared reflection scanner that discovers <see cref="BlueprintRegistrarAttribute"/>-decorated
/// classes in an assembly and invokes their <c>Register</c> or <c>RegisterAll</c> entry point,
/// injecting <see cref="BlueprintRegistryStaging"/> and/or <see cref="BehaviorRegistry"/> arguments.
///
/// <para><b>Contract (caller obligations):</b>
/// <list type="bullet">
///   <item>The caller owns <see cref="BlueprintRegistryStaging"/> and must call
///         <c>BlueprintRegistry.CommitStaging</c> after the scan.</item>
///   <item>The caller owns any required <c>HsmActionDispatcher.ClearAll()</c> ordering;
///         the scanner does not touch the dispatcher.</item>
///   <item>Registrars that request a live <see cref="BlueprintRegistry"/> directly (instead
///         of <see cref="BlueprintRegistryStaging"/>) violate the RCU contract and cause a
///         <see cref="HotReloadRegistrarException"/> to be thrown.</item>
///   <item>Registrars that request <c>HsmActionDispatcher</c> as a parameter cause a
///         <see cref="HotReloadRegistrarException"/> to be thrown (it is a static class
///         and cannot be injected).</item>
/// </list>
/// </para>
/// </summary>
public static class BlueprintRegistrarScanner
{
    /// <summary>
    /// Scans <paramref name="assembly"/> for all <see cref="BlueprintRegistrarAttribute"/>-decorated
    /// classes, resolves each one's <c>Register</c> or <c>RegisterAll</c> static method, and invokes
    /// it with the supplied staging buffers as arguments.
    ///
    /// <para>
    /// Supported parameter types for registrar methods:
    /// <list type="bullet">
    ///   <item><see cref="BlueprintRegistryStaging"/> — blueprint definitions accumulate here.</item>
    ///   <item><see cref="BehaviorRegistry"/> — behavior definitions accumulate here.</item>
    /// </list>
    /// Forbidden parameter types (throw <see cref="HotReloadRegistrarException"/>):
    /// <list type="bullet">
    ///   <item><see cref="BlueprintRegistry"/> — violates the atomic RCU contract.</item>
    ///   <item><c>HsmActionDispatcher</c> — static class, cannot be injected.</item>
    /// </list>
    /// Any other parameter type also throws <see cref="HotReloadRegistrarException"/>.
    /// </para>
    /// </summary>
    /// <param name="assembly">Assembly to scan. Must not be null.</param>
    /// <param name="blueprintStaging">
    /// Staging buffer that receives all blueprint definitions.
    /// Must not be null. Commit it after this call via <c>BlueprintRegistry.CommitStaging</c>.
    /// </param>
    /// <param name="behaviorStaging">
    /// Staging registry that receives all behavior definitions.
    /// Must not be null. Merge or promote it as needed after this call.
    /// </param>
    /// <param name="skipOnUnknownParam">
    /// When <c>true</c>, a registrar whose method has an unresolvable (unknown or forbidden)
    /// parameter type is silently skipped instead of throwing.  Use this in contexts where the
    /// assembly contains registrars with parameters not supported by this scanner (e.g.
    /// <c>IGeographicTransform</c> on <c>AiBehaviorFactory</c>).  Defaults to <c>false</c>
    /// (throw on unknown params) to preserve the existing strict contract.
    /// </param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="HotReloadRegistrarException">
    /// A registrar method requests a forbidden or unresolvable parameter type
    /// and <paramref name="skipOnUnknownParam"/> is <c>false</c>.
    /// </exception>
    public static void Scan(
        Assembly assembly,
        BlueprintRegistryStaging blueprintStaging,
        BehaviorRegistry behaviorStaging,
        bool skipOnUnknownParam = false)
    {
        if (assembly        == null) throw new ArgumentNullException(nameof(assembly));
        if (blueprintStaging == null) throw new ArgumentNullException(nameof(blueprintStaging));
        if (behaviorStaging  == null) throw new ArgumentNullException(nameof(behaviorStaging));

        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Gracefully handle partial assembly loads (e.g., collectible ALCs with missing deps).
            types = ex.Types.Where(t => t != null).ToArray()!;
        }

        foreach (var type in types)
        {
            if (type.GetCustomAttribute<BlueprintRegistrarAttribute>() == null)
                continue;

            // Prefer the more specific "Register" overload; fall back to "RegisterAll".
            var method = type.GetMethod("Register",    BindingFlags.Public | BindingFlags.Static)
                      ?? type.GetMethod("RegisterAll", BindingFlags.Public | BindingFlags.Static);

            if (method == null)
                continue;

            var paramInfos = method.GetParameters();
            var args = new object[paramInfos.Length];

            bool skipThisRegistrar = false;
            for (int i = 0; i < paramInfos.Length; i++)
            {
                var paramType = paramInfos[i].ParameterType;

                if (paramType == typeof(BlueprintRegistryStaging))
                    args[i] = blueprintStaging;
                else if (paramType == typeof(BehaviorRegistry))
                    args[i] = behaviorStaging;
                // BlueprintRegistry direct — violates the RCU contract.
                else if (paramType == typeof(BlueprintRegistry))
                {
                    if (skipOnUnknownParam) { skipThisRegistrar = true; break; }
                    throw new HotReloadRegistrarException(
                        "Registrar requests BlueprintRegistry as a parameter, but only " +
                        "BlueprintRegistryStaging may be injected. Direct access to the live " +
                        "registry would violate the atomic RCU contract. " +
                        "Change the registrar's parameter to BlueprintRegistryStaging.");
                }
                // HsmActionDispatcher is a static class — cannot be injected.
                else if (paramType.FullName == "Fhsm.Kernel.HsmActionDispatcher" ||
                         paramType == typeof(HsmActionDispatcher))
                {
                    if (skipOnUnknownParam) { skipThisRegistrar = true; break; }
                    throw new HotReloadRegistrarException(
                        "Registrar requests HsmActionDispatcher as a parameter, but it is a " +
                        "static class and cannot be injected. " +
                        "Call HsmActionDispatcher.RegisterAction statically from inside Register.");
                }
                else
                {
                    if (skipOnUnknownParam) { skipThisRegistrar = true; break; }
                    throw new HotReloadRegistrarException(
                        $"Unknown registrar parameter type: {paramType.FullName}. " +
                        "Supported: BlueprintRegistryStaging, BehaviorRegistry.");
                }
            }

            if (!skipThisRegistrar)
                method.Invoke(null, args);
        }
    }
}
