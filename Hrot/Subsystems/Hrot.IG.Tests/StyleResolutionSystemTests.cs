using System.Runtime.CompilerServices;
using Hrot.NED.Descriptors;
using Hrot.IG.Components;
using Hrot.IG.Systems;
using Hrot.Map.Definitions.Tkb;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.IG.Tests;

/// <summary>
/// Unit tests covering IG.2.1 (<see cref="ResolvedStyle"/> component) and
/// IG.2.2 (<see cref="StyleResolutionSystem"/> 3-layer merge).
///
/// Entity setup is performed by calling <see cref="EntityRepository"/> helpers
/// directly (no DDS participant required).  The command buffer is played back
/// after each <c>Execute</c> call so that <see cref="ResolvedStyle"/> values
/// are immediately readable from the repository.
/// </summary>
public class StyleResolutionSystemTests
{
    // ── Test constants (§CODE-STANDARDS §1) ───────────────────────────────────

    private const string TestSymbolCode    = "SFGPUCIZ-------";
    private const string TestTextureOvr    = "override_tex";
    private const string TestLabel         = "Alpha-1";
    private const string TestColorHexBlue  = "#0064FF";   // Friend blue #RRGGBB
    private const string TestColorHexRed   = "#FF0000";   // Hostile red #RRGGBB

    // ── World factory ─────────────────────────────────────────────────────────

    private static EntityRepository CreateRepo()
    {
        var repo = new EntityRepository();

        // Unmanaged components
        repo.RegisterComponent<NetworkIdentity>();
        repo.RegisterComponent<SimTransform>();
        repo.RegisterComponent<ResolvedStyle>();

        // Managed class components
        repo.RegisterComponent<VisualData>();
        repo.RegisterManagedComponent<IgSymbolOverride>();

        return repo;
    }

    /// <summary>
    /// Creates an entity that has <see cref="NetworkIdentity"/> and <see cref="SimTransform"/>
    /// — the minimum required for <see cref="StyleResolutionSystem"/> to process it.
    /// </summary>
    private static Entity CreateBaseEntity(EntityRepository repo)
    {
        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new NetworkIdentity(1));
        repo.AddComponent(entity, new SimTransform());
        return entity;
    }

    /// <summary>
    /// Runs the system and plays back the command buffer so that written
    /// <see cref="ResolvedStyle"/> values are immediately visible.
    /// </summary>
    private static void RunSystem(EntityRepository repo, StyleResolutionSystem system)
    {
        system.Execute(repo, 0f);
        var cb = (EntityCommandBuffer)((ISimulationView)repo).GetCommandBuffer();
        cb.Playback(repo);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // IG.2.1 — ResolvedStyle component structural contracts
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The struct must fit inside a CPU cache line.
    /// References the named constant per §CODE-STANDARDS §1.
    /// </summary>
    [Fact]
    public void ResolvedStyle_StructSize_IsBelowCacheLimit()
    {
        Assert.True(
            Unsafe.SizeOf<ResolvedStyle>() < ResolvedStyleConstants.MaxStyleBytes,
            $"ResolvedStyle is {Unsafe.SizeOf<ResolvedStyle>()} bytes; must be < {ResolvedStyleConstants.MaxStyleBytes}");
    }

    /// <summary>
    /// <see cref="ResolvedStyle.CreateDefault"/> must produce a white tint so that an
    /// entity with no configuration still renders visibly without a tint shift.
    /// </summary>
    [Fact]
    public void ResolvedStyle_Default_HasWhiteUnknownTint()
    {
        var s = ResolvedStyle.CreateDefault();

        Assert.Equal(ResolvedStyleConstants.UnknownTintR, s.TintR);
        Assert.Equal(ResolvedStyleConstants.UnknownTintG, s.TintG);
        Assert.Equal(ResolvedStyleConstants.UnknownTintB, s.TintB);
        Assert.Equal(ResolvedStyleConstants.UnknownTintA, s.TintA);
    }

    [Fact]
    public void ResolvedStyle_Default_HasUnknownAffiliation()
    {
        var s = ResolvedStyle.CreateDefault();
        Assert.Equal(ForceId.Unknown, s.Affiliation);
    }

    [Fact]
    public void ResolvedStyle_Default_HasZeroDamage()
    {
        var s = ResolvedStyle.CreateDefault();
        Assert.Equal(ResolvedStyleConstants.DamageMin, s.DamageLevel);
    }

    [Fact]
    public void ResolvedStyle_Default_AllFlagsOff()
    {
        var s = ResolvedStyle.CreateDefault();
        Assert.False(s.ShowTrail);
        Assert.False(s.ShowSensors);
    }

    /// <summary>
    /// Round-trip through fixed UTF-8 buffer must preserve the texture-name string.
    /// </summary>
    [Fact]
    public void ResolvedStyle_SetGetTextureName_RoundTrips()
    {
        var s = ResolvedStyle.CreateDefault();
        s.SetTextureName(TestSymbolCode);
        Assert.Equal(TestSymbolCode, s.GetTextureName());
    }

    /// <summary>
    /// Round-trip through fixed UTF-8 buffer must preserve the label-text string.
    /// </summary>
    [Fact]
    public void ResolvedStyle_SetGetLabelText_RoundTrips()
    {
        var s = ResolvedStyle.CreateDefault();
        s.SetLabelText(TestLabel);
        Assert.Equal(TestLabel, s.GetLabelText());
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // IG.2.2 — StyleResolutionSystem — Layer 1 (TKB / VisualData)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// When <c>VisualData</c> is present the system must copy <c>SymbolCode</c>
    /// into <see cref="ResolvedStyle.GetTextureName()"/>.
    /// </summary>
    [Fact]
    public void StyleResolutionSystem_TkbLayer_SetsTextureFromVisualData()
    {
        var repo   = CreateRepo();
        var system = new StyleResolutionSystem(new MapUserConfig());

        var entity = CreateBaseEntity(repo);
        repo.AddComponent(entity, new VisualData { SymbolCode = TestSymbolCode });

        RunSystem(repo, system);

        var style = repo.GetComponent<ResolvedStyle>(entity);
        Assert.Equal(TestSymbolCode, style.GetTextureName());
    }

    /// <summary>
    /// When <c>VisualData.ColorHex</c> is the friend-blue hex code the system
    /// must decode and store the correct RGBA tint channels.
    /// </summary>
    [Fact]
    public void StyleResolutionSystem_TkbLayer_ParsesColorHexIntoTint()
    {
        var repo   = CreateRepo();
        var system = new StyleResolutionSystem(new MapUserConfig());

        var entity = CreateBaseEntity(repo);
        repo.AddComponent(entity, new VisualData { ColorHex = TestColorHexBlue });

        RunSystem(repo, system);

        var style = repo.GetComponent<ResolvedStyle>(entity);
        // #0064FF → R=0, G=100, B=255 per hex decode
        Assert.Equal(0,   style.TintR);
        Assert.Equal(100, style.TintG);
        Assert.Equal(255, style.TintB);
        Assert.Equal(ResolvedStyleConstants.UnknownTintA, style.TintA); // no alpha in #RRGGBB → defaults to 255
    }

    /// <summary>
    /// When <c>VisualData</c> is absent the system must still write a
    /// <see cref="ResolvedStyle"/> with the default white tint (no exception, no skip).
    /// </summary>
    [Fact]
    public void StyleResolutionSystem_MissingVisualData_WritesDefaultStyle()
    {
        var repo   = CreateRepo();
        var system = new StyleResolutionSystem(new MapUserConfig());

        var entity = CreateBaseEntity(repo);
        // Do NOT set VisualData

        RunSystem(repo, system);

        Assert.True(repo.HasComponent<ResolvedStyle>(entity),
            "ResolvedStyle must be written even when VisualData is absent");

        var style = repo.GetComponent<ResolvedStyle>(entity);
        Assert.Equal(ResolvedStyleConstants.UnknownTintR, style.TintR);
        Assert.Equal(ForceId.Unknown,                     style.Affiliation);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // IG.2.2 — Layer 2 (Network / IgSymbolOverride)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <c>StyleSetId = "hostile"</c> must override the tint to the hostile red colour
    /// defined in <see cref="ResolvedStyleConstants"/>.
    /// </summary>
    [Fact]
    public void StyleResolutionSystem_NetworkOverride_HostileStyleSetId_SetsTintRed()
    {
        var repo   = CreateRepo();
        var system = new StyleResolutionSystem(new MapUserConfig());

        var entity = CreateBaseEntity(repo);
        repo.SetManagedComponent(entity, new IgSymbolOverride
        {
            StyleSetId = IgSymbolOverride.StyleSetHostile,
        });

        RunSystem(repo, system);

        var style = repo.GetComponent<ResolvedStyle>(entity);
        Assert.Equal(ResolvedStyleConstants.HostileTintR, style.TintR);
        Assert.Equal(ResolvedStyleConstants.HostileTintG, style.TintG);
        Assert.Equal(ResolvedStyleConstants.HostileTintB, style.TintB);
        Assert.Equal(ForceId.Hostile,                     style.Affiliation);
    }

    /// <summary>
    /// <c>StyleSetId = "friendly"</c> must override the tint to the friend-blue colour.
    /// </summary>
    [Fact]
    public void StyleResolutionSystem_NetworkOverride_FriendlyStyleSetId_SetsTintBlue()
    {
        var repo   = CreateRepo();
        var system = new StyleResolutionSystem(new MapUserConfig());

        var entity = CreateBaseEntity(repo);
        repo.SetManagedComponent(entity, new IgSymbolOverride
        {
            StyleSetId = IgSymbolOverride.StyleSetFriend,
        });

        RunSystem(repo, system);

        var style = repo.GetComponent<ResolvedStyle>(entity);
        Assert.Equal(ResolvedStyleConstants.FriendTintR, style.TintR);
        Assert.Equal(ResolvedStyleConstants.FriendTintG, style.TintG);
        Assert.Equal(ResolvedStyleConstants.FriendTintB, style.TintB);
        Assert.Equal(ForceId.Friend,                     style.Affiliation);
    }

    /// <summary>
    /// When <c>IgSymbolOverride.TextureOverride</c> is set it must replace the
    /// TKB-derived texture name.
    /// </summary>
    [Fact]
    public void StyleResolutionSystem_NetworkOverride_TextureOverride_ReplacesTexture()
    {
        var repo   = CreateRepo();
        var system = new StyleResolutionSystem(new MapUserConfig());

        var entity = CreateBaseEntity(repo);
        repo.AddComponent(entity, new VisualData { SymbolCode = TestSymbolCode });
        repo.SetManagedComponent(entity, new IgSymbolOverride { TextureOverride = TestTextureOvr });

        RunSystem(repo, system);

        var style = repo.GetComponent<ResolvedStyle>(entity);
        Assert.Equal(TestTextureOvr, style.GetTextureName());
    }

    /// <summary>
    /// When <c>IgSymbolOverride.LabelOverride</c> is set it must appear in
    /// <see cref="ResolvedStyle.GetLabelText()"/>.
    /// </summary>
    [Fact]
    public void StyleResolutionSystem_NetworkOverride_LabelOverride_SetsLabel()
    {
        var repo   = CreateRepo();
        var system = new StyleResolutionSystem(new MapUserConfig());

        var entity = CreateBaseEntity(repo);
        repo.SetManagedComponent(entity, new IgSymbolOverride { LabelOverride = TestLabel });

        RunSystem(repo, system);

        var style = repo.GetComponent<ResolvedStyle>(entity);
        Assert.Equal(TestLabel, style.GetLabelText());
    }

    /// <summary>
    /// <c>IgSymbolOverride.ShowHistory = true</c> must set <see cref="ResolvedStyle.ShowTrail"/>.
    /// </summary>
    [Fact]
    public void StyleResolutionSystem_NetworkOverride_ShowHistory_SetsShowTrail()
    {
        var repo   = CreateRepo();
        var system = new StyleResolutionSystem(new MapUserConfig());

        var entity = CreateBaseEntity(repo);
        repo.SetManagedComponent(entity, new IgSymbolOverride { ShowHistory = true });

        RunSystem(repo, system);

        var style = repo.GetComponent<ResolvedStyle>(entity);
        Assert.True(style.ShowTrail);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // IG.2.2 — Layer 3 (User config / MapUserConfig) — highest priority
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <c>MapUserConfig.ForceHostile = true</c> must override even a friendly
    /// network override with the hostile red tint.
    /// </summary>
    [Fact]
    public void StyleResolutionSystem_UserConfig_ForceHostile_OverridesNetworkFriend()
    {
        var repo      = CreateRepo();
        var userConfig = new MapUserConfig { ForceHostile = true };
        var system    = new StyleResolutionSystem(userConfig);

        var entity = CreateBaseEntity(repo);
        // Layer 2 says friendly — Layer 3 must win
        repo.SetManagedComponent(entity, new IgSymbolOverride
        {
            StyleSetId = IgSymbolOverride.StyleSetFriend,
        });

        RunSystem(repo, system);

        var style = repo.GetComponent<ResolvedStyle>(entity);
        Assert.Equal(ResolvedStyleConstants.HostileTintR, style.TintR);
        Assert.Equal(ForceId.Hostile,                     style.Affiliation);
    }

    /// <summary>
    /// <c>MapUserConfig.HideLabels = true</c> must clear the label even when
    /// a network override has set one.
    /// </summary>
    [Fact]
    public void StyleResolutionSystem_UserConfig_HideLabels_ClearsLabel()
    {
        var repo      = CreateRepo();
        var userConfig = new MapUserConfig { HideLabels = true };
        var system    = new StyleResolutionSystem(userConfig);

        var entity = CreateBaseEntity(repo);
        repo.SetManagedComponent(entity, new IgSymbolOverride { LabelOverride = TestLabel });

        RunSystem(repo, system);

        var style = repo.GetComponent<ResolvedStyle>(entity);
        Assert.Equal(string.Empty, style.GetLabelText());
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // IG.2.2 — Damage integration
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// When no damage ingress is present the damage level must remain at
    /// <see cref="ResolvedStyleConstants.DamageMin"/> (healthy state).
    /// </summary>
    [Fact]
    public void StyleResolutionSystem_DefaultDamage_IsZero()
    {
        var repo   = CreateRepo();
        var system = new StyleResolutionSystem(new MapUserConfig());

        var entity = CreateBaseEntity(repo);

        RunSystem(repo, system);

        var style = repo.GetComponent<ResolvedStyle>(entity);
        Assert.Equal(ResolvedStyleConstants.DamageMin, style.DamageLevel);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // IG.2.2 — Update path (component already exists)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Running the system twice must overwrite the first result with fresh values.
    /// Verifies that <c>cmd.SetComponent</c> (update path) works alongside the initial
    /// <c>cmd.AddComponent</c> path.
    /// </summary>
    [Fact]
    public void StyleResolutionSystem_SecondExecution_OverwritesPreviousResult()
    {
        var repo      = CreateRepo();
        var userConfig = new MapUserConfig();
        var system    = new StyleResolutionSystem(userConfig);

        var entity = CreateBaseEntity(repo);
        repo.SetManagedComponent(entity, new IgSymbolOverride
        {
            StyleSetId = IgSymbolOverride.StyleSetFriend,
        });

        RunSystem(repo, system);   // First run — friend blue

        // Swap to hostile for second run
        repo.SetManagedComponent(entity, new IgSymbolOverride
        {
            StyleSetId = IgSymbolOverride.StyleSetHostile,
        });

        RunSystem(repo, system);   // Second run — hostile red must overwrite

        var style = repo.GetComponent<ResolvedStyle>(entity);
        Assert.Equal(ResolvedStyleConstants.HostileTintR, style.TintR);
        Assert.Equal(ForceId.Hostile,                     style.Affiliation);
    }
}
