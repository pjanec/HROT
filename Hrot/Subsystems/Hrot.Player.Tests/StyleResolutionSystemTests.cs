using System.Runtime.CompilerServices;
using Hrot.NED.Descriptors;
using Hrot.Map.Common.Components;
using Hrot.Player.Systems;
using Hrot.Map.Definitions.Tkb;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Player.Tests;

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
        repo.RegisterManagedComponent<MapSymbolOverride>();

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
    public void ResolvedStyle_Default_HasNeutralAffiliation()
    {
        var s = ResolvedStyle.CreateDefault();
        Assert.Equal(ForceId.Neutral, s.Affiliation);
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
    /// <summary>⭐ CE-4432 (R-2084) — a pass that re-resolves every entity (an input changed) costs 0 B: the texture name, the label (a network id)
    /// and the colour are read into stack buffers, never strings. 🔴 Red-proof: <c>labelText = netId.Value.ToString(…)</c> back ⇒ red.</summary>
    [Fact]
    public void StyleResolutionSystem_AResolvingPass_Allocates0B()
    {
        var repo   = CreateRepo();
        var system = new StyleResolutionSystem(new MapUserConfig());
        var units = new Entity[50];
        for (int i = 0; i < units.Length; i++)
        {
            units[i] = repo.CreateEntity();
            repo.AddComponent(units[i], new NetworkIdentity(1000 + i));
            repo.AddComponent(units[i], new SimTransform());
            repo.AddComponent(units[i], new VisualData { SymbolCode = TestSymbolCode, ColorHex = TestColorHexBlue });
        }
        for (int k = 0; k < 3; k++) { repo.SetComponent(units[0], new VisualData { SymbolCode = TestSymbolCode, ColorHex = TestColorHexBlue }); system.Execute(repo, 0f); }   // warm
        Assert.Equal("1007", repo.GetComponent<ResolvedStyle>(units[7]).GetLabelText());   // ⛔ anti-vacuity: the id label was resolved
        int passes = system.Passes;
        long a0 = System.GC.GetAllocatedBytesForCurrentThread();
        for (int k = 0; k < 10; k++)
        {
            repo.SetComponent(units[0], new VisualData { SymbolCode = TestSymbolCode, ColorHex = TestColorHexBlue });   // an input moved ⇒ a full pass
            system.Execute(repo, 0f);
        }
        long allocated = System.GC.GetAllocatedBytesForCurrentThread() - a0;
        Assert.Equal(passes + 10, system.Passes);
        Assert.Equal(0, allocated);
    }

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
        Assert.Equal(ForceId.Neutral,                     style.Affiliation);
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
        repo.SetManagedComponent(entity, new MapSymbolOverride
        {
            StyleSetId = MapSymbolOverride.StyleSetHostile,
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
        repo.SetManagedComponent(entity, new MapSymbolOverride
        {
            StyleSetId = MapSymbolOverride.StyleSetFriend,
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
        repo.SetManagedComponent(entity, new MapSymbolOverride { TextureOverride = TestTextureOvr });

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
        repo.SetManagedComponent(entity, new MapSymbolOverride { LabelOverride = TestLabel });

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
        repo.SetManagedComponent(entity, new MapSymbolOverride { ShowHistory = true });

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
        repo.SetManagedComponent(entity, new MapSymbolOverride
        {
            StyleSetId = MapSymbolOverride.StyleSetFriend,
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
        repo.SetManagedComponent(entity, new MapSymbolOverride { LabelOverride = TestLabel });

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
        repo.SetManagedComponent(entity, new MapSymbolOverride
        {
            StyleSetId = MapSymbolOverride.StyleSetFriend,
        });

        RunSystem(repo, system);   // First run — friend blue

        // Swap to hostile for second run
        repo.SetManagedComponent(entity, new MapSymbolOverride
        {
            StyleSetId = MapSymbolOverride.StyleSetHostile,
        });

        RunSystem(repo, system);   // Second run — hostile red must overwrite

        var style = repo.GetComponent<ResolvedStyle>(entity);
        Assert.Equal(ResolvedStyleConstants.HostileTintR, style.TintR);
        Assert.Equal(ForceId.Hostile,                     style.Affiliation);
    }

    /// <summary>⭐ <c>CE-1349</c> — a label longer than the fixed buffer is TRUNCATED at a whole character, never thrown on. 🔒 User,
    /// 2026-10-25: placing an "AH-64 Apache (attack helicopter)" crashed the editor here. ✅ Red-proof: <c>Encoding.UTF8.GetBytes</c> back ⇒ throws.</summary>
    [Fact]
    public void CE1349_ALongLabel_IsTruncated_NotThrownOn()
    {
        var style = new ResolvedStyle();
        style.SetLabelText("AH-64 Apache (attack helicopter)");
        string got = style.GetLabelText();
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(got) <= ResolvedStyleConstants.LabelTextMaxBytes - 1);
        Assert.StartsWith("AH-64 Apache (attack", got);
        style.SetLabelText(new string('é', 40));   // two bytes each: never half a character
        Assert.Equal(new string('é', (ResolvedStyleConstants.LabelTextMaxBytes - 1) / 2), style.GetLabelText());
        style.SetLabelText("short");
        Assert.Equal("short", style.GetLabelText());
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // CE-1480 G3 — the pass is gated (docs/DESIGN_Change_Gated_Systems.md §4 G3)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>One kernel frame: the clock ticks, then the system runs (the playback is a no-op on a repository view — the system writes directly).</summary>
    private static void Frame(EntityRepository repo, StyleResolutionSystem system) { repo.Tick(); system.Execute(repo, 0f); }

    /// <summary>
    /// ⭐⭐ <c>CE-1480 G3</c> — (1) an unchanged world is not walked and no <see cref="ResolvedStyle"/> chunk is stamped; (2) a pass that DOES run
    /// (forced by an unrelated entity appearing) writes nothing when no style differs; (3) a Health / VisualData / network-override change restyles
    /// THAT entity on the next frame; (4) the operator flags (an external version) restyle everything and back; (5) a seek (clock backwards) is a full pass.
    /// </summary>
    [Fact]
    public void CE1480_G3_AnUnchangedWorld_IsNotWalked_AndEveryInputIsFollowedOnTheNextFrame()
    {
        var repo = CreateRepo();
        repo.RegisterComponent<Health>();
        var cfg = new MapUserConfig();
        var system = new StyleResolutionSystem(cfg);
        var ents = new System.Collections.Generic.List<Entity>();
        for (int i = 0; i < 30; i++)
        {
            var e = CreateBaseEntity(repo);
            repo.SetComponent(e, new NetworkIdentity(100 + i));
            repo.AddComponent(e, new VisualData { SymbolCode = TestSymbolCode });
            ents.Add(e);
        }
        for (int i = 0; i < 6; i++) Frame(repo, system);
        Assert.Equal(ForceId.Neutral, repo.GetComponentRO<ResolvedStyle>(ents[0]).Affiliation);

        // (1) nothing changed ⇒ the walk does not run; no ResolvedStyle chunk is stamped
        uint v = repo.GlobalVersion; int passes = system.Passes;
        for (int i = 0; i < 5; i++) Frame(repo, system);
        Assert.Equal(passes, system.Passes);
        Assert.False(repo.HasComponentChanged(typeof(ResolvedStyle), v));

        // (2) a full pass that finds every style as it was writes nothing (an unrelated entity: a structural change ⇒ Full)
        repo.CreateEntity();
        Frame(repo, system);
        Assert.Equal(passes + 1, system.Passes);
        Assert.False(repo.HasComponentChanged(typeof(ResolvedStyle), v));

        // (3a) Health: added (structural), then a VALUE change ⇒ that entity's damage follows the next frame
        repo.AddComponent(ents[5], new Health { Current = 10f, Max = 10f });
        for (int i = 0; i < 4; i++) Frame(repo, system);
        Assert.Equal(ResolvedStyleConstants.DamageMin, repo.GetComponentRO<ResolvedStyle>(ents[5]).DamageLevel);
        repo.SetComponent(ents[5], new Health { Current = 5f, Max = 10f });
        Frame(repo, system);
        Assert.Equal(50f, repo.GetComponentRO<ResolvedStyle>(ents[5]).DamageLevel);
        for (int i = 0; i < 4; i++) Frame(repo, system);

        // (3b) the network override: set (structural), then changed in place of the old one (SetManagedComponent)
        repo.SetManagedComponent(ents[7], new MapSymbolOverride { LabelOverride = "A" });
        for (int i = 0; i < 4; i++) Frame(repo, system);
        repo.SetManagedComponent(ents[7], new MapSymbolOverride { LabelOverride = "B" });
        Frame(repo, system);
        Assert.Equal("B", repo.GetComponentRO<ResolvedStyle>(ents[7]).GetLabelText());

        // (3c) VisualData value change
        for (int i = 0; i < 4; i++) Frame(repo, system);
        repo.SetComponent(ents[9], new VisualData { SymbolCode = "SHGPU----------" });
        Frame(repo, system);
        Assert.Equal("SHGPU----------", repo.GetComponentRO<ResolvedStyle>(ents[9]).GetTextureName());
        for (int i = 0; i < 4; i++) Frame(repo, system);

        // (4) the operator flags: an external version ⇒ everything restyled, and back again
        cfg.ForceHostile = true;
        Frame(repo, system);
        Assert.All(ents, e => Assert.Equal(ForceId.Hostile, repo.GetComponentRO<ResolvedStyle>(e).Affiliation));
        cfg.ForceHostile = false;
        Frame(repo, system);
        Assert.All(ents, e => Assert.Equal(ForceId.Neutral, repo.GetComponentRO<ResolvedStyle>(e).Affiliation));
        for (int i = 0; i < 4; i++) Frame(repo, system);

        // (5) a seek: the clock goes back ⇒ a full pass
        int fulls = system.Gate.Fulls;
        repo.ResetGlobalVersion(2);
        system.Execute(repo, 0f);
        Assert.Equal(fulls + 1, system.Gate.Fulls);
    }
}
