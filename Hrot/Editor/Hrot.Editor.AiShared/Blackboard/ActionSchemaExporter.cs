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
        bool isCondition = false;
        bool isAiPrimitive = false;

        // I4: Blueprint-compiled AiPrimitive — a distinct discovery attribute on the generated
        // TickCore. Maps its hosting flags to ActionHosting exactly as the compiler emitted them,
        // and tags the entry so catalogs/UI can present it as blueprint-authored (not hardcoded).
        // DtoType is taken from the first ref param (ref Params) below, like the other attributes.
        var aiPrimitive = method.GetCustomAttribute<GeneratedAiPrimitiveActionAttribute>(inherit: false);
        if (aiPrimitive != null)
        {
            isAiPrimitive = true;
            if (aiPrimitive.BTreeAction)    hosting |= ActionHosting.BTree;
            if (aiPrimitive.BTreeCondition) { hosting |= ActionHosting.BTree; isCondition = true; }
            // ⭐⭐ CE-386 — the ROLE bit is set beside Hsm. 🔴 Both flags used to fold into Hsm
            //    alone, so nothing downstream could tell an HSM activity from an HSM guard and the
            //    two HSM pickers could not be filtered apart. The attribute always carried them
            //    separately; only the schema lost the distinction.
            if (aiPrimitive.HsmAction)      hosting |= ActionHosting.Hsm | ActionHosting.HsmActivity;
            if (aiPrimitive.HsmGuard)       hosting |= ActionHosting.Hsm | ActionHosting.HsmGuard;
            if (aiPrimitive.BlueprintCall)  hosting |= ActionHosting.Shared;
        }

        // ⛔ CE-504 residue (2026-10-02) — a BARE [BTreeAction]/[BTreeCondition] is no longer offered. Since CE-504 a
        //   BTree node binds only the shared forms ([SharedAiAction]/[SharedAiCondition], below) or a blueprint; binding a
        //   bare-attributed method reports BTREE0002. Listing it in the BTree picker offered a choice the build refuses.
        //   📐 No production method carries one; the kernel-form registrations (FDP/Examples, the analyzer's 4-param arm)
        //   register directly and never went through this picker. 📄 DESIGN_BTree_Node_Call_Shapes.md §5 slice 4.

        // HSM attributes — CE-386: the role bit beside Hsm, exactly as for the AiPrimitive above.
        if (method.IsDefined(typeof(HsmActionAttribute), inherit: false))
            hosting |= ActionHosting.Hsm | ActionHosting.HsmActivity;

        if (method.IsDefined(typeof(HsmGuardAttribute), inherit: false))
            hosting |= ActionHosting.Hsm | ActionHosting.HsmGuard;

        // Shared AI attributes -- AllowMultiple, gather all instances
        foreach (SharedAiActionAttribute attr in
            method.GetCustomAttributes<SharedAiActionAttribute>(inherit: false))
        {
            // CE-386: a shared ACTION is an HSM activity, not a guard.
            hosting |= ActionHosting.BTree | ActionHosting.Hsm | ActionHosting.HsmActivity
                     | ActionHosting.Shared;
            _ = attr; // DtoType is on the attribute but we take DtoType from the ref param
        }

        foreach (SharedAiConditionAttribute attr in
            method.GetCustomAttributes<SharedAiConditionAttribute>(inherit: false))
        {
            // ⭐ CE-386: a shared CONDITION is exactly what an HSM transition guard needs, so it
            //   earns the guard bit. ⚠ IsCondition stays too — it is the BTree node-kind signal
            //   (BTreeNodeCatalog:104) and means something different.
            hosting |= ActionHosting.BTree | ActionHosting.Hsm | ActionHosting.HsmGuard
                     | ActionHosting.Shared;
            isCondition = true;
            _ = attr;
        }

        // ⛔⛔ CE-327 (2026-09-23) — the [SharedAiHeavyAction]/[SharedAiHeavyCondition] branches are
        //   DELETED with the attributes they read. 📄 DESIGN_Occurrence_Scoped_Storage.md §30.29.
        // ⭐ CE-330 (2026-09-23) completed the removal §30.29.5 deferred: `ActionSchemaEntry
        //   .HeavyDtoType` and `ActionHosting.Heavy` are GONE, so there is no longer a
        //   permanently-null parameter to carry. 62 call sites across 19 files were rewritten.

        // No relevant attribute found -- skip this method.
        if (hosting == ActionHosting.None)
            return;

        // Extract DtoType from the first ref parameter.
        // If the method has no ref parameter, fall back to the DtoType property on the HSM
        // attribute (DEBT-01 fix for void* unsafe interop signatures).
        Type? dtoType = ExtractFirstRefParamType(method);
        // ⭐ CE-504 C-3 — a shared param-less node (Entity, EntityRepository) binds no variable: typeof(void) says so, and keeps
        //   it pickable (it would otherwise be skipped for having no ref parameter).
        if (dtoType == null && (hosting & ActionHosting.Shared) != 0)
            dtoType = typeof(void);
        if (dtoType == null)
        {
            dtoType = ExtractHsmAttributeDtoType(method);
            if (dtoType == null)
                return;
            // Force Hsm-only hosting for the attribute-based fallback path.
            // ⚠ CE-386: KEEP the role bits. This arm is only reached because an [HsmAction]/
            //   [HsmGuard] supplied the DtoType, so the role is already known — a bare
            //   `hosting = Hsm` would throw it away and put the entry in neither picker.
            hosting &= ActionHosting.HsmActivity | ActionHosting.HsmGuard;
            hosting |= ActionHosting.Hsm;
        }

        // Read access annotation from the first parameter.
        var access = ExtractAccess(method);

        // Build FQN
        string declaringTypeName = method.DeclaringType?.FullName ?? method.DeclaringType?.Name ?? "<unknown>";
        string fqn = $"{declaringTypeName}.{method.Name}";

        // Enumerate public instance fields of the DTO type.
        var dtoFields = ReflectDtoFields(dtoType);

        // Last-write wins for duplicate FQNs (can happen with AllowMultiple across overloads).
        collected[fqn] = new ActionSchemaEntry(
            fqn, dtoType, hosting, access, isCondition, dtoFields, isAiPrimitive,
            WorkingStateType: isAiPrimitive ? null : ExtractSecondRefParamType(method));   // ⭐ CE-2099
    }

    /// <summary>
    /// Returns the public instance fields of <paramref name="dtoType"/> in declaration order.
    /// </summary>
    private static IReadOnlyList<DtoFieldDescriptor> ReflectDtoFields(Type dtoType)
    {
        FieldInfo[] fields;
        try
        {
            fields = dtoType.GetFields(BindingFlags.Public | BindingFlags.Instance);
        }
        catch
        {
            return Array.Empty<DtoFieldDescriptor>();
        }

        var result = new DtoFieldDescriptor[fields.Length];
        for (int i = 0; i < fields.Length; i++)
            result[i] = new DtoFieldDescriptor(fields[i].Name, fields[i].FieldType);
        return result;
    }

    /// <summary>⭐ <c>CE-2099</c> — the working-state type of the stateful shared form: the SECOND leading <c>ref</c>
    /// parameter when the method is <c>(ref P, ref WS, Entity, EntityRepository)</c>; otherwise null.</summary>
    private static Type? ExtractSecondRefParamType(MethodInfo method)
    {
        var ps = method.GetParameters();
        return ps.Length == 4 && ps[0].ParameterType.IsByRef && ps[1].ParameterType.IsByRef
            ? ps[1].ParameterType.GetElementType()
            : null;
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
