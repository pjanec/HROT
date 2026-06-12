using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fbt;
using Fbt.Kernel;
using Fhsm.Kernel.Attributes;

namespace Hrot.Editor.AiShared.Blackboard;

/// <summary>
/// Reflection-based implementation of <see cref="IActionSchemaExporter"/>.
/// On each <see cref="Rebuild"/> call, scans all assemblies currently loaded into the
/// current AppDomain for methods decorated with the supported AI action/condition/guard
/// attributes and builds the <see cref="All"/> dictionary.
/// </summary>
public sealed class ActionSchemaExporter : IActionSchemaExporter
{
    private Dictionary<string, ActionSchemaEntry> _entries = new();

    /// <inheritdoc />
    public IReadOnlyDictionary<string, ActionSchemaEntry> All => _entries;

    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
    public ActionSchemaEntry? Lookup(string fqn) =>
        _entries.TryGetValue(fqn, out var entry) ? entry : null;

    /// <inheritdoc />
    public void Rebuild()
    {
        var collected = new Dictionary<string, ActionSchemaEntry>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            ScanAssembly(assembly, collected);
        }

        _entries = collected;
        Changed?.Invoke();
    }

    // -------------------------------------------------------------------------
    // Internal helpers
    // -------------------------------------------------------------------------

    private static void ScanAssembly(
        Assembly assembly,
        Dictionary<string, ActionSchemaEntry> collected)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Partial load: process the types that did load.
            types = ex.Types.Where(t => t != null).ToArray()!;
        }

        foreach (var type in types)
        {
            MethodInfo[] methods;
            try
            {
                methods = type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Static | BindingFlags.Instance |
                    BindingFlags.DeclaredOnly);
            }
            catch
            {
                continue;
            }

            foreach (var method in methods)
            {
                try
                {
                    ProcessMethod(method, collected);
                }
                catch (Exception ex) when (ex is TypeLoadException or BadImageFormatException or InvalidOperationException)
                {
                    // Skip methods from assemblies with incompatible type references.
                }
            }
        }
    }

    private static void ProcessMethod(
        MethodInfo method,
        Dictionary<string, ActionSchemaEntry> collected)
    {
        // Determine which AI system(s) host this method.
        var hosting = ActionHosting.None;
        Type? heavyDtoType = null;
        bool isCondition = false;

        // BTree attributes
        if (method.IsDefined(typeof(BTreeActionAttribute), inherit: false))
            hosting |= ActionHosting.BTree;

        if (method.IsDefined(typeof(BTreeConditionAttribute), inherit: false))
        {
            hosting |= ActionHosting.BTree;
            isCondition = true;
        }

        // HSM attributes
        if (method.IsDefined(typeof(HsmActionAttribute), inherit: false))
            hosting |= ActionHosting.Hsm;

        if (method.IsDefined(typeof(HsmGuardAttribute), inherit: false))
            hosting |= ActionHosting.Hsm;

        // Shared AI attributes -- AllowMultiple, gather all instances
        foreach (SharedAiActionAttribute attr in
            method.GetCustomAttributes<SharedAiActionAttribute>(inherit: false))
        {
            hosting |= ActionHosting.BTree | ActionHosting.Hsm | ActionHosting.Shared;
            _ = attr; // DtoType is on the attribute but we take DtoType from the ref param
        }

        foreach (SharedAiConditionAttribute attr in
            method.GetCustomAttributes<SharedAiConditionAttribute>(inherit: false))
        {
            hosting |= ActionHosting.BTree | ActionHosting.Hsm | ActionHosting.Shared;
            isCondition = true;
            _ = attr;
        }

        foreach (SharedAiHeavyActionAttribute attr in
            method.GetCustomAttributes<SharedAiHeavyActionAttribute>(inherit: false))
        {
            hosting |= ActionHosting.BTree | ActionHosting.Hsm | ActionHosting.Shared | ActionHosting.Heavy;
            // Prefer the first non-null HeavyDtoType encountered.
            if (heavyDtoType == null && attr.HeavyDtoType != null)
                heavyDtoType = attr.HeavyDtoType;
        }

        foreach (SharedAiHeavyConditionAttribute attr in
            method.GetCustomAttributes<SharedAiHeavyConditionAttribute>(inherit: false))
        {
            hosting |= ActionHosting.BTree | ActionHosting.Hsm | ActionHosting.Shared | ActionHosting.Heavy;
            isCondition = true;
            if (heavyDtoType == null && attr.HeavyDtoType != null)
                heavyDtoType = attr.HeavyDtoType;
            _ = attr;
        }

        // No relevant attribute found -- skip this method.
        if (hosting == ActionHosting.None)
            return;

        // Extract DtoType from the first ref parameter.
        // If the method has no ref parameter, fall back to the DtoType property on the HSM
        // attribute (DEBT-01 fix for void* unsafe interop signatures).
        Type? dtoType = ExtractFirstRefParamType(method);
        if (dtoType == null)
        {
            dtoType = ExtractHsmAttributeDtoType(method);
            if (dtoType == null)
                return;
            // Force Hsm-only hosting for the attribute-based fallback path.
            hosting = ActionHosting.Hsm;
        }

        // Read access annotation from the first parameter.
        var access = ExtractAccess(method);

        // Build FQN
        string declaringTypeName = method.DeclaringType?.FullName ?? method.DeclaringType?.Name ?? "<unknown>";
        string fqn = $"{declaringTypeName}.{method.Name}";

        // Last-write wins for duplicate FQNs (can happen with AllowMultiple across overloads).
        collected[fqn] = new ActionSchemaEntry(fqn, dtoType, hosting, access, heavyDtoType, isCondition);
    }

    /// <summary>
    /// Returns the CLR type of the first <c>ref</c> parameter, or null if none exists.
    /// </summary>
    private static Type? ExtractFirstRefParamType(MethodInfo method)
    {
        foreach (var param in method.GetParameters())
        {
            var pt = param.ParameterType;
            if (pt.IsByRef)
                return pt.GetElementType(); // strip the & from the ByRef wrapper
        }
        return null;
    }

    /// <summary>
    /// Reads the <c>DtoType</c> property from an <see cref="HsmActionAttribute"/> or
    /// <see cref="HsmGuardAttribute"/> on <paramref name="method"/>.
    /// Returns null when neither attribute is present or both have null DtoType.
    /// </summary>
    private static Type? ExtractHsmAttributeDtoType(MethodInfo method)
    {
        var action = method.GetCustomAttribute<HsmActionAttribute>(inherit: false);
        if (action?.DtoType != null)
            return action.DtoType;

        var guard = method.GetCustomAttribute<HsmGuardAttribute>(inherit: false);
        if (guard?.DtoType != null)
            return guard.DtoType;

        return null;
    }

    /// <summary>
    /// Reads <c>[BlackboardReadOnly]</c> or <c>[BlackboardReadWrite]</c> from the first
    /// parameter of <paramref name="method"/>. Returns <c>Unknown</c> if neither is present
    /// or if the method has no parameters.
    /// </summary>
    private static BlackboardAccess ExtractAccess(MethodInfo method)
    {
        var parameters = method.GetParameters();
        if (parameters.Length == 0)
            return BlackboardAccess.Unknown;

        var first = parameters[0];
        if (first.IsDefined(typeof(BlackboardReadOnlyAttribute), inherit: false))
            return BlackboardAccess.ReadOnly;
        if (first.IsDefined(typeof(BlackboardReadWriteAttribute), inherit: false))
            return BlackboardAccess.ReadWrite;
        return BlackboardAccess.Unknown;
    }
}
