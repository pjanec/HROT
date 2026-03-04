using System.Reflection;
using FDP.Toolkit.DER;
using ImGuiNET;

namespace Bagira.IOS.Panels;

/// <summary>
/// A single display row emitted by <see cref="InspectorPanel"/>.
/// </summary>
/// <param name="Category">The descriptor type name (e.g. "EntityInfo").</param>
/// <param name="Field">The public field name within that descriptor.</param>
/// <param name="Value">String representation of the field value.</param>
public sealed record InspectorLine(string Category, string Field, string Value);

/// <summary>
/// IOS UI panel that shows the raw field values of every ECS descriptor
/// attached to the currently selected entity.
///
/// <para><b>Reflection discipline (batch pitfall):</b>
/// <see cref="BuildDescriptorLines"/> is called only inside
/// <see cref="NotifySelectionChanged"/>, i.e. once per selection change —
/// never inside <see cref="Draw"/>.  Field reflection results for each
/// descriptor <see cref="Type"/> are memoised in a process-wide static
/// cache (<c>s_fieldCache</c>) so every subsequent selection of an entity
/// that carries the same descriptor layout is allocation-free on the hot
/// path (CODE-STANDARDS §4 / BATCH-05 pitfall note).</para>
///
/// <para><b>IOS-DEBT-029:</b> <see cref="IDerEntity.GetAllDescriptorTypes"/>
/// enumerates only types for which a descriptor is actually stored, so the
/// call to <c>GetDescriptor&lt;T&gt;</c> is implicitly guarded.  An
/// additional explicit <c>HasDescriptor</c> check is still performed to
/// satisfy the debt invariant and defend against any future change to
/// <c>GetAllDescriptorTypes</c> semantics.</para>
///
/// <para><b>Testing:</b> <see cref="BuildDescriptorLines"/> is public and
/// static; tests call it directly with a real or stub
/// <see cref="IDerEntity"/> without requiring an ImGui render frame.</para>
/// </summary>
public sealed class InspectorPanel
{
    // ── Static reflection cache ───────────────────────────────────────────────

    // Resolved once per process; a lock is only needed during the first
    // encounter of each descriptor type.
    private static readonly Dictionary<Type, FieldInfo[]> s_fieldCache = new();
    private static readonly object s_fieldCacheLock = new();

    // Pre-resolved generic method definitions from IDerEntity.
    private static readonly MethodInfo s_getDescMethodDef =
        typeof(IDerEntity).GetMethod("GetDescriptor")!;
    private static readonly MethodInfo s_hasDescMethodDef =
        typeof(IDerEntity).GetMethod("HasDescriptor")!;

    // ── Per-instance state ────────────────────────────────────────────────────

    private int _cachedEntityId = PanelConstants.InspectorNoSelection;
    private List<InspectorLine> _cachedLines = new();

    // ── Public read-back ──────────────────────────────────────────────────────

    /// <summary>
    /// The entity ID whose descriptor lines are currently cached.
    /// Equal to <see cref="PanelConstants.InspectorNoSelection"/> when nothing
    /// is selected.
    /// </summary>
    public int CachedEntityId => _cachedEntityId;

    /// <summary>
    /// The flattened descriptor field rows for the currently cached entity.
    /// Empty when nothing is selected or the entity has no descriptors.
    /// </summary>
    public IReadOnlyList<InspectorLine> CachedLines => _cachedLines;

    // ── Selection change ──────────────────────────────────────────────────────

    /// <summary>
    /// Called by <see cref="IosMock.Update"/> whenever the selected entity
    /// changes.  Rebuilds the descriptor-field cache for the new entity so that
    /// <see cref="Draw"/> performs zero reflection allocations during normal
    /// rendering.
    /// </summary>
    /// <param name="entity">
    /// The newly selected entity, or <c>null</c> to clear the selection.
    /// </param>
    public void NotifySelectionChanged(IDerEntity? entity)
    {
        if (entity is null)
        {
            _cachedEntityId = PanelConstants.InspectorNoSelection;
            _cachedLines    = new List<InspectorLine>();
            return;
        }

        // Skip the rebuild if the selected entity has not changed.
        if (entity.EntityId == _cachedEntityId) return;

        _cachedEntityId = entity.EntityId;
        _cachedLines    = BuildDescriptorLines(entity);
    }

    // ── Descriptor line builder ───────────────────────────────────────────────

    /// <summary>
    /// Enumerates all descriptors on <paramref name="entity"/> via
    /// <see cref="IDerEntity.GetAllDescriptorTypes"/>, reads their public
    /// instance fields through reflection, and returns a flat list of
    /// <see cref="InspectorLine"/> records.
    ///
    /// <para>This is the only allocation site in the panel.  It is called once
    /// per selection change, not per frame.  Per-type field reflection results
    /// are memoised in <c>s_fieldCache</c>.</para>
    /// </summary>
    /// <remarks>
    /// The returned list is bounded by
    /// <see cref="PanelConstants.InspectorMaxTotalLines"/>.
    /// </remarks>
    public static List<InspectorLine> BuildDescriptorLines(IDerEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var lines = new List<InspectorLine>();

        foreach (var descType in entity.GetAllDescriptorTypes())
        {
            // IOS-DEBT-029: guard via HasDescriptor before calling GetDescriptor.
            bool hasDesc = (bool)s_hasDescMethodDef
                .MakeGenericMethod(descType)
                .Invoke(entity, new object[] { 0 })!;

            if (!hasDesc) continue;

            object? descriptor = s_getDescMethodDef
                .MakeGenericMethod(descType)
                .Invoke(entity, new object[] { 0 });

            if (descriptor is null) continue;

            foreach (var field in GetCachedFields(descType))
            {
                if (lines.Count >= PanelConstants.InspectorMaxTotalLines) return lines;

                object? raw = field.GetValue(descriptor);
                lines.Add(new InspectorLine(
                    Category: descType.Name,
                    Field:    field.Name,
                    Value:    raw?.ToString() ?? "null"));
            }
        }

        return lines;
    }

    // ── Draw stub ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders the inspector panel via ImGui.
    /// Called once per frame from the application shell (Phase P10).
    ///
    /// <para>All rendering code is commented out pending Raylib/rlImGui
    /// linkage.  The panel's business logic (<see cref="NotifySelectionChanged"/>,
    /// <see cref="BuildDescriptorLines"/>, <see cref="CachedLines"/>) is fully
    /// testable without an ImGui context.</para>
    /// </summary>
    public void Draw(IIosLogic logic)
    {
        if (ImGui.GetCurrentContext() == IntPtr.Zero) return;
        IosPanelColors.Push();
        ImGui.Begin("Inspector");
        IosPanelColors.Pop();

        if (_cachedEntityId == PanelConstants.InspectorNoSelection)
        {
            ImGui.Text("No entity selected");
            ImGui.End(); return;
        }

        ImGui.Text($"Entity ID: {_cachedEntityId}");
        ImGui.Separator();

        string? lastCategory = null;
        foreach (var line in _cachedLines)
        {
            if (line.Category != lastCategory)
            {
                if (lastCategory is not null) ImGui.TreePop();
                lastCategory = line.Category;
                ImGui.SetNextItemOpen(true, ImGuiCond.Once);
                ImGui.TreeNode(line.Category);
            }
            ImGui.Text($"  {line.Field}: {line.Value}");
        }
        if (lastCategory is not null) ImGui.TreePop();

        ImGui.End();
    }

    // ── Field cache helper ────────────────────────────────────────────────────

    private static FieldInfo[] GetCachedFields(Type type)
    {
        lock (s_fieldCacheLock)
        {
            if (!s_fieldCache.TryGetValue(type, out var fields))
            {
                fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
                s_fieldCache[type] = fields;
            }
            return fields;
        }
    }
}
