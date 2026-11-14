using System;
using System.Globalization;
using Hrot.NED.Descriptors;
using FdpEntityInfo = Fdp.Core.EntityInfo;
using Hrot.Map.Common.Components;
using Fdp.Toolkit.Combat.Components;
using Hrot.Map.Definitions.Tkb;
using Fdp.Core.Logging;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.World;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Hrot.Player.Systems;

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
///     <b>Layer 2 — Network override:</b> reads <see cref="MapSymbolOverride"/> (class
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
/// so that freshly-received <see cref="MapSymbolOverride"/> data is visible in the view.
/// </summary>
[UpdateInPhase(SystemPhase.PostSimulation)]
[TickMode(TickPolicy.EveryFrame)]
public class StyleResolutionSystem : IEcsModuleSystem
{
    private readonly MapUserConfig _userConfig;
    private readonly NodeId _localNodeId;

    // ⭐ CE-1480 G3 (docs/DESIGN_Change_Gated_Systems.md) — the ONE ChangeGate, with THIS system's inputs: what BuildStyle reads per entity
    //   (VisualData, EntityInfo, MapSymbolOverride, Health, NetworkIdentity — plus ResolvedStyle itself, so a foreign write is still overwritten
    //   as before) and the one global input, the operator's two flags (an external version: compare-with-last-seen). A pass is skipped when
    //   none moved; a pass that runs writes a style only when it differs from the one the entity holds (no dirty ResolvedStyle chunk for the
    //   recorder / the view copy). ⚠ SimTransform is a query requirement, not an input — the style does not read it.
    private readonly ChangeGate _gate;
    private EntityQuery? _query;
    private ISimulationView? _queryView;

    /// <summary>The gate (its counters) — for rails and diagnostics.</summary>
    public ChangeGate Gate => _gate;

    /// <summary>How many entity walks actually ran (the gate skipped the rest) — for rails and diagnostics.</summary>
    public int Passes { get; private set; }

    public StyleResolutionSystem(MapUserConfig userConfig, int localNodeId = 0)
    {
        _userConfig = userConfig ?? throw new ArgumentNullException(nameof(userConfig));
        _localNodeId = (Fdp.Core.NodeId)(localNodeId);
        _gate = new ChangeGate()
            .Watch(typeof(NetworkIdentity), typeof(VisualData), typeof(FdpEntityInfo), typeof(MapSymbolOverride), typeof(Health), typeof(ResolvedStyle))
            .WatchExternal(ConfigVersion);
    }

    /// <summary>The operator flags as one number: any flip moves it (flip-and-back too — it is compared with the last value SEEN).</summary>
    private long ConfigVersion() => (_userConfig.ForceHostile ? 1L : 0L) | (_userConfig.HideLabels ? 2L : 0L);

    /// <inheritdoc/>
    public void Execute(ISimulationView view, float deltaTime)
    {
        var repo = view as EntityRepository;

        // Unchanged ⇒ nothing BuildStyle reads moved ⇒ every style is as it was. Delta is a full pass: most inputs are OPTIONAL components of the
        // query, which a delta over its required tables would not see (ChangeResult.Entities).
        if (_gate.Next(view).Kind == ChangeKind.Unchanged) return;
        Passes++;

        var cmd = repo == null ? view.GetCommandBuffer() : null;

        if (_query == null || !ReferenceEquals(_queryView, view))
        {
            _query = view.Query()
                .With<NetworkIdentity>()
                .With<SimTransform>()
                .WithLifecycle(EntityLifecycle.All)
                .Build();
            _queryView = view;
        }
        var query = _query;

        foreach (var entity in query)
        {
            var style = BuildStyle(view, entity);

            if (!view.HasComponent<ResolvedStyle>(entity))
            {
                FdpLog<StyleResolutionSystem>.Debug(
                    "[Node-{0}] Style: Resolved Entity={1} Texture={2}", _localNodeId, entity.Index, style.GetTextureName());
            }
            else if (SameStyle(in view.GetComponentRO<ResolvedStyle>(entity), in style))
                continue;   // ⭐ write only on change

            if (repo != null)
                repo.SetComponent(entity, style);
            else
                cmd!.SetComponent(entity, style);
        }
    }

    /// <summary>Byte equality of two styles (a packed unmanaged struct: texture and label buffers, tint, flags, damage).</summary>
    private static bool SameStyle(in ResolvedStyle a, in ResolvedStyle b)
        => MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in a), 1))
            .SequenceEqual(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in b), 1)));

    // ── Core resolution logic ─────────────────────────────────────────────────

    private ResolvedStyle BuildStyle(ISimulationView view, Entity entity)
    {
        // ⭐ CE-4432 (R-2084) — the texture name and the label are built in STACK buffers (the fixed strings decode into them, CopyTo), never as
        //   strings: a string per entity per changed frame was ~5 MB/s of garbage while a 1 000-man replay played (measured on Windows).
        Span<char> texture = stackalloc char[ResolvedStyleConstants.TextureNameMaxBytes];
        Span<char> label   = stackalloc char[ResolvedStyleConstants.LabelTextMaxBytes];
        int    textureLen  = 0, labelLen = 0;

        // ── Layer 1: TKB defaults (VisualData applied at spawn) ──────────────
        byte   tintR       = ResolvedStyleConstants.UnknownTintR;
        byte   tintG       = ResolvedStyleConstants.UnknownTintG;
        byte   tintB       = ResolvedStyleConstants.UnknownTintB;
        byte   tintA       = ResolvedStyleConstants.UnknownTintA;
        var    affiliation = ForceId.Neutral;

        if (view.HasComponent<VisualData>(entity))
        {
            ref readonly var visual = ref view.GetComponentRO<VisualData>(entity);
            textureLen = visual.SymbolCode.CopyTo(texture);

            Span<char> colorHex = stackalloc char[FixedString32.MaxLength];
            int colorLen = visual.ColorHex.CopyTo(colorHex);
            if (colorLen > 0)
                ParseColorHex(colorHex[..colorLen], out tintR, out tintG, out tintB, out tintA);
            // Derive affiliation from TKB colour when no network override exists.
        }

        // ── Layer 1.5: IgEntityData (from Hrot.NED.Descriptors.EntityInfo DDS / spawn descriptor) ──
        // Provides force affiliation and human-readable name when no IgSymbolOverride is present.
        if ( view.HasComponent<FdpEntityInfo>( entity ) )
        {
            ref readonly var entityData = ref view.GetComponentRO<FdpEntityInfo>( entity );
            affiliation = entityData.ForceId;
			ApplyAffiliationColor( affiliation, out tintR, out tintG, out tintB, out tintA);
            if (!entityData.Name.IsEmpty)
                labelLen = entityData.Name.CopyTo(label);
        }

        // ── Layer 2: Network override (IgSymbolOverride) ──────────────────────
        bool showTrail = false;
        if (view.HasManagedComponent<MapSymbolOverride>(entity))
        {
            var symbol        = view.GetManagedComponentRO<MapSymbolOverride>(entity);
            var resolvedAffil = ResolveAffiliation(symbol.StyleSetId);

            if (resolvedAffil.HasValue)
            {
                affiliation = resolvedAffil.Value;
                ApplyAffiliationColor(affiliation, out tintR, out tintG, out tintB, out tintA);
            }

            if (!string.IsNullOrEmpty(symbol.TextureOverride))
                textureLen = Copy(symbol.TextureOverride, texture);
            if (!string.IsNullOrEmpty(symbol.LabelOverride))
                labelLen = Copy(symbol.LabelOverride, label);

            showTrail = symbol.ShowHistory;
        }

        // ── Layer 2.5: NetworkIdentity label fallback ─────────────────────────
        // Show the entity's network ID when no human-readable label was set, so every
        // entity always has a readable identifier on the map.  The DebugPanel "Hide Labels"
        // toggle (Layer 3) can suppress this.
        if (labelLen == 0 && view.HasComponent<NetworkIdentity>(entity))
        {
            ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);
            netId.Value.TryFormat(label, out labelLen, default, CultureInfo.InvariantCulture);
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
            labelLen = 0;

        // ⭐ DamageLevel stays a 0..100 percentage (ResolvedStyle's documented range), but it is now
        //   DERIVED from the authority's Health rather than read from a precomputed wire field.
        //   🔒 User ruling, 2026-09-05: "no precalculated percentages".
        // ⚠ Max <= 0 leaves the style at DamageMin — an entity with no meaningful maximum renders as
        //   undamaged rather than as a division by zero.
        float damage = ResolvedStyleConstants.DamageMin;
        if (view.HasComponent<Health>(entity))
        {
            ref readonly var health = ref view.GetComponentRO<Health>(entity);
            if (health.Max > 0f)
            {
                damage = (1f - health.Current / health.Max) * 100f;
                if (damage < ResolvedStyleConstants.DamageMin) damage = ResolvedStyleConstants.DamageMin;
                if (damage > ResolvedStyleConstants.DamageMax) damage = ResolvedStyleConstants.DamageMax;
            }
        }

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
        style.SetTextureName(texture[..textureLen]);
        style.SetLabelText(label[..labelLen]);

        return style;
    }

    /// <summary>A string into a stack buffer, cut to it (the style's own UTF-8 write cuts again at its byte capacity).</summary>
    private static int Copy(string text, Span<char> into)
    {
        int n = Math.Min(text.Length, into.Length);
        text.AsSpan(0, n).CopyTo(into);
        return n;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Maps a <see cref="MapSymbolOverride.StyleSetId"/> token to a <see cref="ForceId"/>.
    /// Comparison is case-insensitive and allocation-free (ordinal string equality).
    /// Returns <c>null</c> when the token is unrecognised — TKB default is preserved.
    /// </summary>
    private static ForceId? ResolveAffiliation(string? styleSetId)
    {
        if (string.IsNullOrEmpty(styleSetId))
            return null;

        if (styleSetId.Equals(MapSymbolOverride.StyleSetHostile, StringComparison.OrdinalIgnoreCase))
            return ForceId.Hostile;
        if (styleSetId.Equals(MapSymbolOverride.StyleSetFriend, StringComparison.OrdinalIgnoreCase))
            return ForceId.Friend;
        if (styleSetId.Equals(MapSymbolOverride.StyleSetNeutral, StringComparison.OrdinalIgnoreCase))
            return ForceId.Neutral;
        if (styleSetId.Equals(MapSymbolOverride.StyleSetUnknown, StringComparison.OrdinalIgnoreCase))
            return ForceId.Neutral;

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
        ReadOnlySpan<char> hex,
        out byte r, out byte g, out byte b, out byte a)
    {
        r = ResolvedStyleConstants.UnknownTintR;
        g = ResolvedStyleConstants.UnknownTintG;
        b = ResolvedStyleConstants.UnknownTintB;
        a = ResolvedStyleConstants.UnknownTintA;

        if (hex.IsEmpty || hex[0] != '#')
            return;

        var span = hex[1..];

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
