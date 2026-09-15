using System.Collections.Generic;
using Hrot.IG.Components;
using Hrot.Core.Network;
using Hrot.IG.UI;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning.Events;

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

    private static SimTransform? ExtractTransform(EntityCreationRequest cmd)
    {
        foreach (var obj in cmd.InitialComponents)
            if (obj is SimTransform t) return t;
        return null;
    }

    private static IgSymbolOverride? ExtractSymbolOverride(EntityCreationRequest cmd)
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
        var requests = new ScenarioEntityCreationRequestSource();
        var state = new MiniExConPanelState { TkbType = TkbTank };

        EntityCreationRequest? captured = null;
        state.OnRequestEnqueued += req => captured = req;

        state.Submit(requests);

        Assert.NotNull(captured);
        Assert.Equal(TkbTank, captured!.TkbType);
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
        var requests = new ScenarioEntityCreationRequestSource();
        var state = new MiniExConPanelState { TkbType = tkbType };

        EntityCreationRequest? captured = null;
        state.OnRequestEnqueued += req => captured = req;

        state.Submit(requests);

        Assert.Equal(tkbType, captured!.TkbType);
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
        var requests = new ScenarioEntityCreationRequestSource();
        var state = new MiniExConPanelState { TkbType = TkbTank };

        EntityCreationRequest? captured = null;
        state.OnRequestEnqueued += req => captured = req;

        state.Submit(requests);

        // ⭐ RE-HOMED CLAIM (host (f), 2026-09-02). This asserted that the panel names the LOCAL node as
        // owner. 📐 Measured: that was never observable — SpawnEntityCommandEgressTranslator wrote
        // Owner = default onto every wire sample regardless of cmd.OwnerNodeId, so every IG creation has
        // always been UNTARGETED and serviced by the default processor. The retarget makes that explicit
        // rather than changing it. ⛔ Honouring LocalNodeId would make IG own (and under R-140 stop
        // persisting) the operator's entity — a product decision, deliberately not taken here.
        // 📄 IgEntityCreationRequests remarks.
        Assert.Equal(0, captured!.OwnerAppInstanceId);
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
        var requests = new ScenarioEntityCreationRequestSource();
        var state = new MiniExConPanelState
        {
            TkbType     = TkbTank,
            Affiliation = ForceId.Hostile,
        };

        EntityCreationRequest? captured = null;
        state.OnRequestEnqueued += req => captured = req;

        state.Submit(requests);

        var symbolOverride = ExtractSymbolOverride(captured!);
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
        var requests = new ScenarioEntityCreationRequestSource();
        var state = new MiniExConPanelState
        {
            TkbType     = TkbTank,
            Affiliation = ForceId.Friend,
        };

        EntityCreationRequest? captured = null;
        state.OnRequestEnqueued += req => captured = req;

        state.Submit(requests);

        var symbolOverride = ExtractSymbolOverride(captured!);
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
        var requests = new ScenarioEntityCreationRequestSource();
        var state = new MiniExConPanelState
        {
            TkbType     = TkbTank,
            Affiliation = ForceId.Neutral,
        };

        EntityCreationRequest? captured = null;
        state.OnRequestEnqueued += req => captured = req;

        state.Submit(requests);

        var symbolOverride = ExtractSymbolOverride(captured!);
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
        var requests = new ScenarioEntityCreationRequestSource();
        var state = new MiniExConPanelState
        {
            TkbType   = TkbTank,
            PositionX = SpawnX,
            PositionY = SpawnY,
        };

        EntityCreationRequest? captured = null;
        state.OnRequestEnqueued += req => captured = req;

        state.Submit(requests);

        var transform = ExtractTransform(captured!);
        Assert.NotNull(transform);
        Assert.Equal(SpawnX, transform!.Value.Position.X);
    }

    /// <summary>
    /// The SimTransform in InitialComponents must reflect the form's PositionY value.
    /// </summary>
    [Fact]
    public void Submit_SpawnPosition_TransformYMatchesFormPositionY()
    {
        var requests = new ScenarioEntityCreationRequestSource();
        var state = new MiniExConPanelState
        {
            TkbType   = TkbTank,
            PositionX = SpawnX,
            PositionY = SpawnY,
        };

        EntityCreationRequest? captured = null;
        state.OnRequestEnqueued += req => captured = req;

        state.Submit(requests);

        var transform = ExtractTransform(captured!);
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
        var requests = new ScenarioEntityCreationRequestSource();
        var state = new MiniExConPanelState { TkbType = TkbTank };

        var ids = new List<System.Guid>();
        state.OnRequestEnqueued += cmd => ids.Add(cmd.RequestId);

        state.Submit(requests);
        state.Submit(requests);

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
        var requests = new ScenarioEntityCreationRequestSource();
        var state = new MiniExConPanelState { TkbType = TkbTank };

        EntityCreationRequest? captured = null;
        state.OnRequestEnqueued += req => captured = req;

        state.Submit(requests);

        int transformCount      = 0;
        int symbolOverrideCount = 0;
        foreach (var obj in captured!.InitialComponents)
        {
            if (obj is SimTransform)     transformCount++;
            if (obj is IgSymbolOverride) symbolOverrideCount++;
        }

        Assert.Equal(1, transformCount);
        Assert.Equal(1, symbolOverrideCount);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // OnRequestEnqueued event (PACK2-U003)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Submit must raise the <see cref="MiniExConPanelState.OnRequestEnqueued"/> event
    /// synchronously with the published command (PACK2-U003 / C.2).
    /// </summary>
    [Fact]
    public void Submit_FiresOnRequestEnqueuedEvent()
    {
        var state = new MiniExConPanelState { TkbType = 301L };
        var requests = new ScenarioEntityCreationRequestSource();

        EntityCreationRequest? captured = null;
        state.OnRequestEnqueued += req => captured = req;

        state.Submit(requests);

        Assert.NotNull(captured);
        Assert.Equal(301L, captured!.TkbType);
    }
}
