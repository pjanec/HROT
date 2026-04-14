using Hrot.IG.Components;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using ModuleHost.Core.Abstractions;

namespace Hrot.IG.UI;

/// <summary>
/// Pure-logic state driving the Entity Inspector Panel (IG.5.2).
///
/// Extracts ECS component data for the currently-selected entity so that the
/// ImGui panel can display it without querying the world directly.  Call
/// <see cref="Refresh"/> once per frame with the entity returned by the
/// <see cref="SelectionState"/> query; all public properties are updated
/// synchronously and remain valid until the next <see cref="Refresh"/> call.
/// </summary>
public class EntityInspectorState
{
    // ── Selection ─────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>true</c> when a valid entity is selected and its component data has
    /// been successfully extracted; <c>false</c> when the selection is empty
    /// or the entity has been destroyed.
    /// </summary>
    public bool HasSelection { get; private set; }

    /// <summary>
    /// The ECS entity currently being inspected.
    /// Equals <see cref="Entity.Null"/> when <see cref="HasSelection"/> is <c>false</c>.
    /// </summary>
    public Entity InspectedEntity { get; private set; } = Entity.Null;

    // ── Network identity ──────────────────────────────────────────────────────

    /// <summary>Raw network / DIS entity identifier from <see cref="NetworkIdentity.Value"/>.</summary>
    public int EntityId { get; private set; }

    /// <summary>TKB template type key from <see cref="TkbIdentity.TkbType"/>.</summary>
    public long TkbType { get; private set; }

    // ── SimTransform ──────────────────────────────────────────────────────────

    /// <summary>World-space X position (metres) from <see cref="SimTransform.Position"/>.</summary>
    public float PositionX { get; private set; }

    /// <summary>World-space Y position (metres) from <see cref="SimTransform.Position"/>.</summary>
    public float PositionY { get; private set; }

    /// <summary>World-space Z / altitude (metres) from <see cref="SimTransform.Position"/>.</summary>
    public float PositionZ { get; private set; }

    // ── ResolvedStyle ─────────────────────────────────────────────────────────

    /// <summary>
    /// Resolved force affiliation from <see cref="ResolvedStyle.Affiliation"/>.
    /// Defaults to <see cref="ForceId.Unknown"/> when no <see cref="ResolvedStyle"/> is present.
    /// </summary>
    public ForceId Affiliation { get; private set; }

    /// <summary>
    /// Damage level in the range [0, 100] from <see cref="ResolvedStyle.DamageLevel"/>.
    /// 0 = healthy, 100 = destroyed.
    /// </summary>
    public float DamageLevel { get; private set; }

    // ── Refresh ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads ECS components for <paramref name="entity"/> from <paramref name="view"/>
    /// and updates all public properties.
    ///
    /// If <paramref name="entity"/> is <see cref="Entity.Null"/> or no longer alive,
    /// <see cref="HasSelection"/> is set to <c>false</c> and all other properties
    /// retain their previous values.
    /// </summary>
    /// <param name="view">Read-only ECS view (typically the current frame's <see cref="EntityRepository"/>).</param>
    /// <param name="entity">Entity to inspect; pass <see cref="Entity.Null"/> to clear.</param>
    public void Refresh(ISimulationView view, Entity entity)
    {
        if (entity == Entity.Null || !view.IsAlive(entity))
        {
            HasSelection    = false;
            InspectedEntity = Entity.Null;
            return;
        }

        HasSelection    = true;
        InspectedEntity = entity;

        // Network identity and TKB type.
        if (view.HasComponent<NetworkIdentity>(entity))
        {
            ref readonly var identity = ref view.GetComponentRO<NetworkIdentity>(entity);
            EntityId = (int)identity.Value;
        }

        if (view.HasComponent<TkbIdentity>(entity))
        {
            ref readonly var tkbId = ref view.GetComponentRO<TkbIdentity>(entity);
            TkbType = tkbId.TkbType;
        }

        // SimTransform — world-space position.
        if (view.HasComponent<SimTransform>(entity))
        {
            ref readonly var t = ref view.GetComponentRO<SimTransform>(entity);
            PositionX = t.Position.X;
            PositionY = t.Position.Y;
            PositionZ = t.Position.Z;
        }

        // ResolvedStyle — rendered affiliation and damage.
        if (view.HasComponent<ResolvedStyle>(entity))
        {
            ref readonly var style = ref view.GetComponentRO<ResolvedStyle>(entity);
            Affiliation = style.Affiliation;
            DamageLevel = style.DamageLevel;
        }
    }

    /// <summary>
    /// Clears the current selection.  Sets <see cref="HasSelection"/> to <c>false</c>
    /// and <see cref="InspectedEntity"/> to <see cref="Entity.Null"/>.
    /// </summary>
    public void Clear()
    {
        HasSelection    = false;
        InspectedEntity = Entity.Null;
    }
}
