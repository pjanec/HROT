using System.Collections.Generic;
using Hrot.IG.Components;
using Hrot.IG.UI;
using Fdp.Kernel;
using FDP.Toolkit.NetworkSpawning.Events;

namespace Hrot.IG.Tests;

/// <summary>
/// Unit tests for Task IG.5.3: <see cref="MiniExConPanelState"/>.
///
/// Validates that <see cref="MiniExConPanelState.Submit"/> accurately maps
/// variable form data into <see cref="SpawnEntityCommand"/> instances published
/// onto the event bus:
/// <list type="bullet">
///   <item>The published command contains the correct TKB type.</item>
///   <item>The published command targets the local node.</item>
///   <item>The affiliation is embedded in an <see cref="IgSymbolOverride"/>
///         inside <see cref="SpawnEntityCommand.InitialComponents"/>.</item>
///   <item>The spawn position is reflected in the <see cref="SimTransform"/>
///         inside <see cref="SpawnEntityCommand.InitialComponents"/>.</item>
///   <item>Multiple successive Submit calls each produce a distinct command
///         with unique <see cref="SpawnEntityCommand.RequestId"/>.</item>
/// </list>
///
/// No DDS or Raylib window context required.
/// </summary>
public class MiniExConPanelStateTests
{
    // ── Test constants (§CODE-STANDARDS §1) ───────────────────────────────────

    private const long   TkbTank         = 201L;
    private const long   TkbHelicopter   = 202L;
    private const float  SpawnX          = 1500f;
    private const float  SpawnY          = 2500f;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static FdpEventBus CreateBus() => new FdpEventBus();

    private static SimTransform? ExtractTransform(SpawnEntityCommand cmd)
    {
        foreach (var obj in cmd.InitialComponents)
            if (obj is SimTransform t) return t;
        return null;
    }

    private static IgSymbolOverride? ExtractSymbolOverride(SpawnEntityCommand cmd)
    {
        foreach (var obj in cmd.InitialComponents)
            if (obj is IgSymbolOverride o) return o;
        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TKB type mapping
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Submit must publish a command whose TkbType equals the form's TkbType.</summary>
    [Fact]
    public void Submit_PublishesCommandWithCorrectTkbType()
    {
        var bus   = CreateBus();
        var state = new MiniExConPanelState { TkbType = TkbTank };

        SpawnEntityCommand? captured = null;
        state.OnCommandPublished += cmd => captured = cmd;

        state.Submit(bus);

        Assert.NotNull(captured);
        Assert.Equal(TkbTank, captured!.Value.TkbType);
    }

    /// <summary>
    /// Different TKB types submitted in sequence must each produce a command with
    /// the matching TKB type.
    /// </summary>
    [Theory]
    [InlineData(TkbTank)]
    [InlineData(TkbHelicopter)]
    [InlineData(999L)]
    public void Submit_VariousTkbTypes_CommandReflectsFormValue(long tkbType)
    {
        var bus   = CreateBus();
        var state = new MiniExConPanelState { TkbType = tkbType };

        SpawnEntityCommand? captured = null;
        state.OnCommandPublished += cmd => captured = cmd;

        state.Submit(bus);

        Assert.Equal(tkbType, captured!.Value.TkbType);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Owner node
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// OwnerNodeId must equal <see cref="IgNetworkConstants.LocalNodeId"/> so the SimHost
    /// assigns ownership to the IG application.
    /// </summary>
    [Fact]
    public void Submit_CommandHasLocalNodeId()
    {
        var bus   = CreateBus();
        var state = new MiniExConPanelState { TkbType = TkbTank };

        SpawnEntityCommand? captured = null;
        state.OnCommandPublished += cmd => captured = cmd;

        state.Submit(bus);

        Assert.Equal(IgNetworkConstants.LocalNodeId, captured!.Value.OwnerNodeId);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Affiliation mapping
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// When Affiliation is <see cref="ForceId.Hostile"/>, the <see cref="IgSymbolOverride"/>
    /// in InitialComponents must carry the hostile StyleSetId.
    /// </summary>
    [Fact]
    public void Submit_HostileAffiliation_SymbolOverrideIsHostile()
    {
        var bus   = CreateBus();
        var state = new MiniExConPanelState
        {
            TkbType     = TkbTank,
            Affiliation = ForceId.Hostile,
        };

        SpawnEntityCommand? captured = null;
        state.OnCommandPublished += cmd => captured = cmd;

        state.Submit(bus);

        var symbolOverride = ExtractSymbolOverride(captured!.Value);
        Assert.NotNull(symbolOverride);
        Assert.Equal(IgSymbolOverride.StyleSetHostile, symbolOverride!.StyleSetId);
    }

    /// <summary>
    /// When Affiliation is <see cref="ForceId.Friend"/>, the <see cref="IgSymbolOverride"/>
    /// in InitialComponents must carry the friendly StyleSetId.
    /// </summary>
    [Fact]
    public void Submit_FriendAffiliation_SymbolOverrideIsFriend()
    {
        var bus   = CreateBus();
        var state = new MiniExConPanelState
        {
            TkbType     = TkbTank,
            Affiliation = ForceId.Friend,
        };

        SpawnEntityCommand? captured = null;
        state.OnCommandPublished += cmd => captured = cmd;

        state.Submit(bus);

        var symbolOverride = ExtractSymbolOverride(captured!.Value);
        Assert.NotNull(symbolOverride);
        Assert.Equal(IgSymbolOverride.StyleSetFriend, symbolOverride!.StyleSetId);
    }

    /// <summary>
    /// When Affiliation is <see cref="ForceId.Neutral"/>, the <see cref="IgSymbolOverride"/>
    /// in InitialComponents must carry the neutral StyleSetId.
    /// </summary>
    [Fact]
    public void Submit_NeutralAffiliation_SymbolOverrideIsNeutral()
    {
        var bus   = CreateBus();
        var state = new MiniExConPanelState
        {
            TkbType     = TkbTank,
            Affiliation = ForceId.Neutral,
        };

        SpawnEntityCommand? captured = null;
        state.OnCommandPublished += cmd => captured = cmd;

        state.Submit(bus);

        var symbolOverride = ExtractSymbolOverride(captured!.Value);
        Assert.NotNull(symbolOverride);
        Assert.Equal(IgSymbolOverride.StyleSetNeutral, symbolOverride!.StyleSetId);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Position mapping
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The SimTransform in InitialComponents must reflect the form's PositionX value.
    /// </summary>
    [Fact]
    public void Submit_SpawnPosition_TransformXMatchesFormPositionX()
    {
        var bus   = CreateBus();
        var state = new MiniExConPanelState
        {
            TkbType   = TkbTank,
            PositionX = SpawnX,
            PositionY = SpawnY,
        };

        SpawnEntityCommand? captured = null;
        state.OnCommandPublished += cmd => captured = cmd;

        state.Submit(bus);

        var transform = ExtractTransform(captured!.Value);
        Assert.NotNull(transform);
        Assert.Equal(SpawnX, transform!.Value.Position.X);
    }

    /// <summary>
    /// The SimTransform in InitialComponents must reflect the form's PositionY value.
    /// </summary>
    [Fact]
    public void Submit_SpawnPosition_TransformYMatchesFormPositionY()
    {
        var bus   = CreateBus();
        var state = new MiniExConPanelState
        {
            TkbType   = TkbTank,
            PositionX = SpawnX,
            PositionY = SpawnY,
        };

        SpawnEntityCommand? captured = null;
        state.OnCommandPublished += cmd => captured = cmd;

        state.Submit(bus);

        var transform = ExtractTransform(captured!.Value);
        Assert.NotNull(transform);
        Assert.Equal(SpawnY, transform!.Value.Position.Y);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Request ID uniqueness
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Two successive Submit calls must produce commands with distinct RequestIds.
    /// </summary>
    [Fact]
    public void Submit_TwiceSameState_ProducesDistinctRequestIds()
    {
        var bus   = CreateBus();
        var state = new MiniExConPanelState { TkbType = TkbTank };

        var ids = new List<System.Guid>();
        state.OnCommandPublished += cmd => ids.Add(cmd.RequestId);

        state.Submit(bus);
        state.Submit(bus);

        Assert.Equal(2, ids.Count);
        Assert.NotEqual(ids[0], ids[1]);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // InitialComponents structure
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// InitialComponents must contain exactly one SimTransform and one IgSymbolOverride.
    /// </summary>
    [Fact]
    public void Submit_InitialComponents_ContainsTransformAndSymbolOverride()
    {
        var bus   = CreateBus();
        var state = new MiniExConPanelState { TkbType = TkbTank };

        SpawnEntityCommand? captured = null;
        state.OnCommandPublished += cmd => captured = cmd;

        state.Submit(bus);

        int transformCount      = 0;
        int symbolOverrideCount = 0;
        foreach (var obj in captured!.Value.InitialComponents)
        {
            if (obj is SimTransform)     transformCount++;
            if (obj is IgSymbolOverride) symbolOverrideCount++;
        }

        Assert.Equal(1, transformCount);
        Assert.Equal(1, symbolOverrideCount);
    }
}
