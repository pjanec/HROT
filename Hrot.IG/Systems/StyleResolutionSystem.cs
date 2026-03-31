using System;
using System.Globalization;
using Hrot.NED.Descriptors;
using Hrot.IG.Components;
using Hrot.Map.Definitions.Tkb;
using FDP.Kernel.Logging;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using ModuleHost.Core.Abstractions;

namespace Hrot.IG.Systems;

/// <summary>
/// Simulation-phase system that evaluates a 3-layer style merge for every entity
/// carrying <see cref="NetworkIdentity"/> and <see cref="SimTransform"/>, writing the
/// result into <see cref="ResolvedStyle"/>.
///
/// Layer priority (highest overwrites lower):
/// <list type="number">
///   <item>
///     <b>Layer 1 — TKB default:</b> reads <see cref="VisualData"/> applied to the entity
///     at spawn time; provides base texture, colour, and label values.
///   </item>
///   <item>
///     <b>Layer 2 — Network override:</b> reads <see cref="IgSymbolOverride"/> (class
///     component populated by the <c>MapEntitySymbol</c> DDS translator); overrides
///     affiliation tint, texture, label, and trail flag.
///   </item>
///   <item>
///     <b>Layer 3 — User config:</b> applies <see cref="MapUserConfig"/> operator settings
///     (force-hostile, hide-labels); highest priority, cannot be suppressed by network data.
///   </item>
/// </list>
///
/// Damage integration is handled by ingress systems that populate
/// <see cref="ResolvedStyle.DamageLevel"/>; when absent it remains at
/// <see cref="ResolvedStyleConstants.DamageMin"/>.
///
/// Registered in <see cref="SystemPhase.PostSimulation"/>. Must run after network ingress
/// so that freshly-received <see cref="IgSymbolOverride"/> data is visible in the view.
/// </summary>
[UpdateInPhase(SystemPhase.PostSimulation)]
public class StyleResolutionSystem : IEcsModuleSystem
{
    private readonly MapUserConfig _userConfig;

    public StyleResolutionSystem(MapUserConfig userConfig)
        => _userConfig = userConfig ?? throw new ArgumentNullException(nameof(userConfig));

    /// <inheritdoc/>
    public void Execute(ISimulationView view, float deltaTime)
    {
        var repo = view as EntityRepository;
        var cmd = repo == null ? view.GetCommandBuffer() : null;

        var query = view.Query()
            .With<NetworkIdentity>()
            .With<SimTransform>()
            .WithLifecycle(EntityLifecycle.All)
            .Build();

        foreach (var entity in query)
        {
            var style = BuildStyle(view, entity);

            if (!view.HasComponent<ResolvedStyle>(entity))
            {
                FdpLog<StyleResolutionSystem>.Debug(
                    "[TRACE-IG] Style: Resolved Entity={0} Texture={1}", entity.Index, style.GetTextureName());
            }

            if (repo != null)
                repo.SetComponent(entity, style);
            else
                cmd!.SetComponent(entity, style);
        }
    }

    // ── Core resolution logic ─────────────────────────────────────────────────

    private ResolvedStyle BuildStyle(ISimulationView view, Entity entity)
    {
        // ── Layer 1: TKB defaults (VisualData applied at spawn) ──────────────
        string textureName = string.Empty;
        byte   tintR       = ResolvedStyleConstants.UnknownTintR;
        byte   tintG       = ResolvedStyleConstants.UnknownTintG;
        byte   tintB       = ResolvedStyleConstants.UnknownTintB;
        byte   tintA       = ResolvedStyleConstants.UnknownTintA;
        string labelText   = string.Empty;
        var    affiliation = ForceId.Unknown;

        if (view.HasComponent<VisualData>(entity))
        {
            ref readonly var visual = ref view.GetComponentRO<VisualData>(entity);
            textureName = visual.SymbolCode;

            var colorHex = (string)visual.ColorHex;
            if (!string.IsNullOrEmpty(colorHex))
                ParseColorHex(colorHex, out tintR, out tintG, out tintB, out tintA);
            // Derive affiliation from TKB colour when no network override exists.
        }

        // ── Layer 1.5: IgEntityData (from Hrot.NED.Descriptors.EntityInfo DDS / spawn descriptor) ──
        // Provides force affiliation and human-readable name when no IgSymbolOverride is present.
        if ( view.HasComponent<Components.EntityInfo>( entity ) )
        {
            ref readonly var entityData = ref view.GetComponentRO<Components.EntityInfo>( entity );
            if ( entityData.ForceId != ForceId.Unknown)
            {
                affiliation = entityData.ForceId;
				ApplyAffiliationColor( affiliation, out tintR, out tintG, out tintB, out tintA);
            }
            if (!entityData.Name.IsEmpty)
                labelText = entityData.Name;
        }

        // ── Layer 2: Network override (IgSymbolOverride) ──────────────────────
        bool showTrail = false;
        if (view.HasManagedComponent<IgSymbolOverride>(entity))
        {
            var symbol        = view.GetManagedComponentRO<IgSymbolOverride>(entity);
            var resolvedAffil = ResolveAffiliation(symbol.StyleSetId);

            if (resolvedAffil.HasValue)
            {
                affiliation = resolvedAffil.Value;
                ApplyAffiliationColor(affiliation, out tintR, out tintG, out tintB, out tintA);
            }

            if (!string.IsNullOrEmpty(symbol.TextureOverride))
                textureName = symbol.TextureOverride;
            if (!string.IsNullOrEmpty(symbol.LabelOverride))
                labelText = symbol.LabelOverride;

            showTrail = symbol.ShowHistory;
        }

        // ── Layer 2.5: NetworkIdentity label fallback ─────────────────────────
        // Show the entity's network ID when no human-readable label was set, so every
        // entity always has a readable identifier on the map.  The DebugPanel "Hide Labels"
        // toggle (Layer 3) can suppress this.
        if (string.IsNullOrEmpty(labelText) && view.HasComponent<NetworkIdentity>(entity))
        {
            ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);
            labelText = netId.Value.ToString(CultureInfo.InvariantCulture);
        }

        // ── Layer 3: User config overrides ────────────────────────────────────
        if (_userConfig.ForceHostile)
        {
            affiliation = ForceId.Hostile;
            tintR       = ResolvedStyleConstants.HostileTintR;
            tintG       = ResolvedStyleConstants.HostileTintG;
            tintB       = ResolvedStyleConstants.HostileTintB;
            tintA       = ResolvedStyleConstants.HostileTintA;
        }
        if (_userConfig.HideLabels)
            labelText = string.Empty;

        float damage = ResolvedStyleConstants.DamageMin;

        // ── Assemble output ───────────────────────────────────────────────────
        var style = ResolvedStyle.CreateDefault();
        style.TintR       = tintR;
        style.TintG       = tintG;
        style.TintB       = tintB;
        style.TintA       = tintA;
        style.Affiliation = affiliation;
        style.DamageLevel = damage;
        style.ShowTrail   = showTrail;
        style.ShowSensors = false;
        style.SetTextureName(textureName);
        style.SetLabelText(labelText);

        return style;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Maps a <see cref="IgSymbolOverride.StyleSetId"/> token to a <see cref="ForceId"/>.
    /// Comparison is case-insensitive and allocation-free (ordinal string equality).
    /// Returns <c>null</c> when the token is unrecognised — TKB default is preserved.
    /// </summary>
    private static ForceId? ResolveAffiliation(string? styleSetId)
    {
        if (string.IsNullOrEmpty(styleSetId))
            return null;

        if (styleSetId.Equals(IgSymbolOverride.StyleSetHostile, StringComparison.OrdinalIgnoreCase))
            return ForceId.Hostile;
        if (styleSetId.Equals(IgSymbolOverride.StyleSetFriend, StringComparison.OrdinalIgnoreCase))
            return ForceId.Friend;
        if (styleSetId.Equals(IgSymbolOverride.StyleSetNeutral, StringComparison.OrdinalIgnoreCase))
            return ForceId.Neutral;
        if (styleSetId.Equals(IgSymbolOverride.StyleSetUnknown, StringComparison.OrdinalIgnoreCase))
            return ForceId.Unknown;

        return null;
    }

    /// <summary>
    /// Writes spec-defined RGBA bytes for the given <paramref name="affiliation"/>.
    /// All values come from <see cref="ResolvedStyleConstants"/> named constants (§CODE-STANDARDS §1).
    /// </summary>
    private static void ApplyAffiliationColor(
        ForceId affiliation,
        out byte r, out byte g, out byte b, out byte a)
    {
        switch (affiliation)
        {
            case ForceId.Friend:
                r = ResolvedStyleConstants.FriendTintR;
                g = ResolvedStyleConstants.FriendTintG;
                b = ResolvedStyleConstants.FriendTintB;
                a = ResolvedStyleConstants.FriendTintA;
                break;
            case ForceId.Hostile:
                r = ResolvedStyleConstants.HostileTintR;
                g = ResolvedStyleConstants.HostileTintG;
                b = ResolvedStyleConstants.HostileTintB;
                a = ResolvedStyleConstants.HostileTintA;
                break;
            case ForceId.Neutral:
                r = ResolvedStyleConstants.NeutralTintR;
                g = ResolvedStyleConstants.NeutralTintG;
                b = ResolvedStyleConstants.NeutralTintB;
                a = ResolvedStyleConstants.NeutralTintA;
                break;
            default:
                r = ResolvedStyleConstants.UnknownTintR;
                g = ResolvedStyleConstants.UnknownTintG;
                b = ResolvedStyleConstants.UnknownTintB;
                a = ResolvedStyleConstants.UnknownTintA;
                break;
        }
    }

    /// <summary>
    /// Parses a hex color string (<c>#RRGGBB</c> or <c>#RRGGBBAA</c>) into RGBA channels.
    /// Falls back to the Unknown (white) tint on any parse failure so rendering stays safe.
    /// </summary>
    private static void ParseColorHex(
        string? hex,
        out byte r, out byte g, out byte b, out byte a)
    {
        r = ResolvedStyleConstants.UnknownTintR;
        g = ResolvedStyleConstants.UnknownTintG;
        b = ResolvedStyleConstants.UnknownTintB;
        a = ResolvedStyleConstants.UnknownTintA;

        if (string.IsNullOrEmpty(hex) || hex[0] != '#')
            return;

        var span = hex.AsSpan(1);

        if (span.Length == 6)
        {
            if (!byte.TryParse(span[0..2], NumberStyles.HexNumber, null, out r)) r = ResolvedStyleConstants.UnknownTintR;
            if (!byte.TryParse(span[2..4], NumberStyles.HexNumber, null, out g)) g = ResolvedStyleConstants.UnknownTintG;
            if (!byte.TryParse(span[4..6], NumberStyles.HexNumber, null, out b)) b = ResolvedStyleConstants.UnknownTintB;
            a = ResolvedStyleConstants.UnknownTintA;
        }
        else if (span.Length == 8)
        {
            if (!byte.TryParse(span[0..2], NumberStyles.HexNumber, null, out r)) r = ResolvedStyleConstants.UnknownTintR;
            if (!byte.TryParse(span[2..4], NumberStyles.HexNumber, null, out g)) g = ResolvedStyleConstants.UnknownTintG;
            if (!byte.TryParse(span[4..6], NumberStyles.HexNumber, null, out b)) b = ResolvedStyleConstants.UnknownTintB;
            if (!byte.TryParse(span[6..8], NumberStyles.HexNumber, null, out a)) a = ResolvedStyleConstants.UnknownTintA;
        }
    }
}
