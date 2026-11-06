using System;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Core.Tkb;
using Hrot.Common.Diagnostics.Gizmos;
using Hrot.Map.Common.Components;
using ImGuiNET;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>
/// ⭐⭐ <b><c>CE-1247</c> — what the Summary view shows of ONE entity</b>, read from the components every host
/// has. 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §9. Each part is null when the entity lacks its component
/// (⛔ no zero-filled lines for absent data).
/// <para>⭐ Side and health come from <see cref="EntityInfo"/> and <see cref="Health"/>, NOT from
/// <see cref="ResolvedStyle"/>: the style is derived from them and only the IG and the Editor run its system.
/// <see cref="ShownAs"/> carries the style's affiliation only where it DIFFERS (an IG symbol override, Force Hostile).</para>
/// </summary>
public sealed record EntitySummary(
    string? Name,
    int? NetworkId,
    TkbTypeId? TkbType,
    Vector3? Position,
    ForceId? Side,
    ForceId? ShownAs,
    float? HealthCurrent,
    float? HealthMax,
    string? Kind = null,
    DISEntityType? Dis = null)
{
    /// <summary>Health as 0..1, or null when the entity has none or no meaningful maximum.</summary>
    public float? HealthFraction => HealthCurrent is { } c && HealthMax is > 0f and var m ? Math.Clamp(c / m, 0f, 1f) : null;

    /// <summary>⭐ CE-4257 — the DIS type as its seven numbers, colon-separated: <c>Kind:Domain:Country:Category:Subcategory:Specific:Extra</c>.</summary>
    public static string DisText(DISEntityType t)
        => string.Create(CultureInfo.InvariantCulture, $"{t.Kind}:{t.Domain}:{t.Country}:{t.Category}:{t.Subcategory}:{t.Specific}:{t.Extra}");

    /// <summary>⭐ CE-4257 — the DIS type's levels by name (the row's tooltip), e.g. <c>Platform › Land › United States › Tank</c>.</summary>
    public static string DisNames(DISEntityType t) => string.Join(" › ", DisNameTable.Default.Path(t));

    /// <summary>⭐ CE-4257 — a position as the Details show it: CULTURE-INVARIANT (a decimal comma would collide with the separator).</summary>
    public static string PositionText(Vector3 p)
        => string.Create(CultureInfo.InvariantCulture, $"{p.X:F1}, {p.Y:F1}, {p.Z:F1} m");

    /// <summary>⭐ CE-4257 — health as shown, culture-invariant.</summary>
    public static string HealthText(float current, float max)
        => string.Create(CultureInfo.InvariantCulture, $"{current:F0} / {max:F0}");

    /// <summary>Reads the entity's summary; null when it is not alive.</summary>
    public static EntitySummary? Read(ISimulationView view, Entity entity)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (entity == Entity.Null || !view.IsAlive(entity)) return null;

        string? name = null; ForceId? side = null;
        if (Has<EntityInfo>(view, entity))
        {
            ref readonly var info = ref view.GetComponentRO<EntityInfo>(entity);
            if (!info.Name.IsEmpty) name = info.Name.ToString();
            side = info.ForceId;
        }

        ForceId? shownAs = null;
        if (Has<ResolvedStyle>(view, entity))
        {
            var drawn = view.GetComponentRO<ResolvedStyle>(entity).Affiliation;
            if (drawn != side) shownAs = drawn;
        }

        float? current = null, max = null;
        if (Has<Health>(view, entity))
        {
            ref readonly var health = ref view.GetComponentRO<Health>(entity);
            current = health.Current;
            max = health.Max;
        }

        // ⭐ CE-4257 — WHAT KIND of entity: the TKB template's display name, else the DIS category's name; the DIS type is the entity's own,
        //   else its template's (as DisEntityTypeTranslator reads it)
        TkbTypeId? tkbType = Has<TkbIdentity>(view, entity) ? view.GetComponentRO<TkbIdentity>(entity).TkbType : null;
        string? kind = null; DISEntityType? dis = null;
        if (view is EntityRepository repo)
        {
            var own = repo.GetDisType(entity);
            if (own.Value != 0) dis = own;
            if (tkbType is { } tt && repo.HasSingletonManaged<ITkbDatabase>() && repo.GetSingletonManaged<ITkbDatabase>() is { } db
                && db.TryGetByType(tt.Value, out var template))
            {
                var display = template.GetDescriptor<TkbMasterDto>()?.CustomName;
                kind = !string.IsNullOrWhiteSpace(display) ? display : !string.IsNullOrWhiteSpace(template.Name) ? template.Name : null;
                if (dis is null && template.DisType.Value != 0) dis = template.DisType;
            }
            if (kind is null && dis is { } d)
                kind = d.Category != 0 ? DisNameTable.Default.CategoryName(d.Kind, d.Domain, d.Category) : DisNameTable.Default.KindName(d.Kind);
        }

        return new EntitySummary(
            name,
            Has<NetworkIdentity>(view, entity) ? view.GetComponentRO<NetworkIdentity>(entity).Value : null,
            Has<TkbIdentity>(view, entity) ? view.GetComponentRO<TkbIdentity>(entity).TkbType : null,
            Has<SimTransform>(view, entity) ? view.GetComponentRO<SimTransform>(entity).Position : null,
            side, shownAs, current, max, kind, dis);
    }

    // ⚠ A host may never register a type (no ResolvedStyle on SimHost) — asking HasComponent of it would throw.
    private static bool Has<T>(ISimulationView view, Entity entity) where T : unmanaged
        => (view is not EntityRepository repo || repo.IsComponentTypeRegistered<T>()) && view.HasComponent<T>(entity);
}

/// <summary>The Summary view's dump — what a frame showed, for <c>GET /panels</c> and the rails.</summary>
public sealed record EntitySummaryViewPanelViewModel(
    string PanelId, string PanelKind, bool HasTarget, int EntityIndex,
    string? Name, int? NetworkId, TkbTypeId? TkbType, float? X, float? Y, float? Z,
    string? Side, string? ShownAs, float? HealthCurrent, float? HealthMax,
    string? Kind = null, string? Dis = null, string? DisNames = null, string? PositionText = null) : IPanelViewModel
{
    public JsonNode Dump() => PanelDump.Of(this);
}

/// <summary>
/// ⭐⭐ <b><c>CE-1247</c> — THE SUMMARY VIEW</b>: the IG's former "Entity Properties" card as a Details view on every
/// host (<see cref="Windows.EntityDetailsBundle"/>). 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §9.
/// </summary>
public sealed class EntitySummaryView : IDetailsViewInstance
{
    private readonly Func<EntityRepository?> _world;

    public EntitySummaryView(Func<EntityRepository?> world)
        => _world = world ?? throw new ArgumentNullException(nameof(world));

    private (EntitySummaryViewPanelViewModel Vm, EntitySummary? Summary) BuildAndPublish(DetailsContext context, string idScope)
    {
        var panelId = $"{idScope}/{EntitySummaryViewDescriptor.ViewId}";
        PanelSnapshot.DeclareInstrumented(panelId);

        bool hasTarget = context.Entities is { Count: 1 };
        var entity = hasTarget ? context.Entities[0] : Entity.Null;
        var summary = hasTarget && _world() is { } world ? EntitySummary.Read(world, entity) : null;
        var vm = new EntitySummaryViewPanelViewModel(
            panelId, EntitySummaryViewDescriptor.ViewId, hasTarget, hasTarget ? entity.Index : -1,
            summary?.Name, summary?.NetworkId, summary?.TkbType,
            summary?.Position?.X, summary?.Position?.Y, summary?.Position?.Z,
            summary?.Side?.ToString(), summary?.ShownAs?.ToString(), summary?.HealthCurrent, summary?.HealthMax,
            summary?.Kind, summary?.Dis is { } d ? EntitySummary.DisText(d) : null, summary?.Dis is { } dn ? EntitySummary.DisNames(dn) : null,
            summary?.Position is { } pos ? EntitySummary.PositionText(pos) : null);

        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return (vm, summary);
    }

    internal EntitySummaryViewPanelViewModel SimulateDraw(DetailsContext context, string idScope) =>
        BuildAndPublish(context, idScope).Vm;

    public void Draw(DetailsContext context, string idScope)
    {
        var (_, s) = BuildAndPublish(context, idScope);
        if (s is null) return;

        string title = s.Name ?? (s.NetworkId is { } n ? $"#{n}" : "(unnamed)");
        if (s.Name is not null && s.NetworkId is { } id) title += $"  #{id}";
        ImGui.TextUnformatted(title);
        ImGui.Separator();

        // ⭐ CE-4257 — what KIND of entity this is: its name, the TKB number, the DIS type's seven numbers (their names on hover)
        if (s.Kind is { } kind) Row("Kind", kind);
        if (s.TkbType is { } tkb) Row("TKB type", tkb.Value.ToString(CultureInfo.InvariantCulture));
        if (s.Dis is { } dis) Row("DIS type", EntitySummary.DisText(dis), EntitySummary.DisNames(dis));
        if (s.Side is { } side)
        {
            var c = AffiliationColors.Of(side);
            ImGui.TextUnformatted("Side");
            ImGui.SameLine(LabelWidth);
            ImGui.TextColored(new Vector4(c.R / 255f, c.G / 255f, c.B / 255f, 1f), side.ToString());
        }
        if (s.ShownAs is { } shown) Row("Shown as", shown.ToString());
        if (s.Position is { } p) Row("Position", EntitySummary.PositionText(p));
        if (s.HealthCurrent is { } hc && s.HealthMax is { } hm)
        {
            ImGui.TextUnformatted("Health");
            ImGui.SameLine(LabelWidth);
            if (s.HealthFraction is { } f) ImGui.ProgressBar(f, new Vector2(-1f, 0f), EntitySummary.HealthText(hc, hm));
            else ImGui.TextUnformatted(hc.ToString("F0", CultureInfo.InvariantCulture));
        }
    }

    private const float LabelWidth = 90f;

    private static void Row(string label, string value, string? tooltip = null)
    {
        ImGui.TextUnformatted(label);
        ImGui.SameLine(LabelWidth);
        ImGui.TextUnformatted(value);
        if (tooltip is { Length: > 0 } && ImGui.IsItemHovered()) ImGui.SetTooltip(tooltip);
    }

    public void Dispose() { }
}

/// <summary>The Summary view's descriptor. 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §9.</summary>
public static class EntitySummaryViewDescriptor
{
    public const string ViewId = "details.summary";

    /// <summary>⭐ Above Components (30), below the Editor's Mission / AI / Runtime (40 / 45 / 50) — §9's rank rule.</summary>
    public const int Rank = 35;

    public static DetailsViewDescriptor For(Func<EntityRepository?> world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var instance = new EntitySummaryView(world);
        return new DetailsViewDescriptor(
            Id:        ViewId,
            Title:     "Summary",
            Rank:      Rank,
            AppliesTo: ctx => DetailsViewPredicates.ExactlyOneEntity(ctx) && world() is not null,
            Create:    () => instance);   // ⚠ no per-window state, as the Components view
    }
}
