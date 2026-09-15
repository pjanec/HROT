using Hrot.Blueprints.Editor.Debug;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Documents;
using NodeEditor.Core.Action;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-059</c> — the <c>debug.*</c> toolbar group, and the wire without which it would be
/// permanently disabled.</b> 📄 The user's <c>--mode all</c> check, <c>2026-08-27</c>: *"Editor has also
/// lots of toolbar buttons for debuggiing, none shown."*
///
/// <para>📐 <b>Measured:</b> <see cref="AiDebugCommands.Register"/> had exactly ONE caller repo-wide, so
/// the six commands did not exist on CGF. ⛔ And registering them alone was NOT the fix: every one gates
/// <c>IsEnabled</c> on <see cref="IDebugSessionRegistry.ActiveSession"/>, which nothing on that host ever
/// set — so the group would have been present and dead, which ruling 49 rates WORSE than absent.</para>
///
/// <para>⭐⭐ So this file rails BOTH halves: the group is registered, AND the active-document mirror
/// actually puts a session in the registry. ⚠ The second is the half a *"the ids are registered"* rail
/// would have missed — the same weaker/stronger split that let <c>CE-049</c>'s equality rail pass over an
/// empty picker.</para>
/// </summary>
public sealed class TheAiDebugGroupExistsOnBothHostsTests
{
    private static readonly string[] ExpectedIds =
    {
        AiDebugCommands.ContinueId, AiDebugCommands.StepOverId, AiDebugCommands.StepIntoId,
        AiDebugCommands.StepOutId,  AiDebugCommands.PauseId,
    };

    // ══ ① THE GROUP ═════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐ The five common ids register against a bare registry — the shape both hosts now call.
    /// </summary>
    [Fact]
    public void TheCommonDebugCommandsRegister()
    {
        var seen = new List<string>();
        AiDebugCommands.Register(
            (d, _) => seen.Add(d.Id),
            new DebugSessionRegistry());

        foreach (var id in ExpectedIds) Assert.Contains(id, seen);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The composition guard: a host that composes a shell toolbar composes the AI-debug group.</b>
    /// 📐 Before <c>CE-059</c> only <c>EditorSubsystem</c> called it, so CGF's toolbar had no debug
    /// section at all. ⚠ A source scan is necessary — this is composition, invisible to the call graph —
    /// and it is anchored on <c>RegisterCommonCore</c>, the call that means *"this host has a shell
    /// toolbar"*.
    /// </summary>
    [Theory]
    [InlineData("Hrot.CGF",    "CgfSubsystem.cs")]
    [InlineData("Hrot.Editor", "EditorSubsystem.cs")]
    public void AHostWithAShellToolbarRegistersTheAiDebugGroup(string project, string file)
    {
        var text = HostSource.Read(project, file);
        if (!text.Contains("RegisterCommonCore(", StringComparison.Ordinal)) return;

        Assert.Contains("AiDebugCommands.Register(", text);
        // ⭐ …and the mirror, or the group is dead on arrival.
        Assert.Contains("ActiveDebugSessionMirror.Wire(", text);
    }

    /// <summary>
    /// ⭐⭐ <b>A host that registers the group also CONSTRUCTS a session for it.</b> 📐 This is the claim
    /// that turns CE-059 from *"greyed-out parity"* into a capability: the editor builds a
    /// <c>BlueprintDebugSession</c> and so, now, does CGF.
    /// </summary>
    [Theory]
    [InlineData("Hrot.CGF",    "CgfSubsystem.cs")]
    [InlineData("Hrot.Editor", "EditorSubsystem.cs")]
    public void AHostWithTheAiDebugGroupConstructsABlueprintDebugSession(string project, string file)
    {
        var text = HostSource.Read(project, file);
        if (!text.Contains("AiDebugCommands.Register(", StringComparison.Ordinal)) return;

        Assert.Contains("new Hrot.Blueprints.Core.Debug.BlueprintDebugSession(", text);
    }

    // ══ ② THE MIRROR ════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐ The pure policy: a Blueprint document yields the blueprint session.
    /// </summary>
    [Fact]
    public void ABlueprintDocumentResolvesTheBlueprintSession()
    {
        var session = new FakeSession();
        var docs    = DocsWith(AssetKind.Blueprint);

        Assert.Same(session, ActiveDebugSessionMirror.Resolve(docs, () => session));
    }

    /// <summary>
    /// ⭐ …and a BTree document resolves <c>null</c>, deliberately (those sessions are not attached on
    /// either host). ⚠ Railed so a later "fix" that returns one has to argue with this.
    /// </summary>
    [Fact]
    public void ANonBlueprintDocumentResolvesNoSession()
    {
        var session = new FakeSession();
        Assert.Null(ActiveDebugSessionMirror.Resolve(DocsWith(AssetKind.BTree), () => session));
        Assert.Null(ActiveDebugSessionMirror.Resolve(null,                      () => session));
    }

    /// <summary>
    /// ⭐⭐⭐ <b><see cref="ActiveDebugSessionMirror.Wire"/> pushes the session immediately</b>, not only on
    /// the next <c>ActiveChanged</c>. ⚠ That "run once" is the difference between a toolbar that enables
    /// on boot with a document already open and one that stays grey until the user switches documents.
    /// </summary>
    [Fact]
    public void WiringPushesTheSessionAtOnce()
    {
        var registry = new DebugSessionRegistry();
        var session  = new FakeSession();

        Assert.Null(registry.ActiveSession);
        ActiveDebugSessionMirror.Wire(DocsWith(AssetKind.Blueprint), registry, () => session);
        Assert.Same(session, registry.ActiveSession);
    }

    /// <summary>
    /// ⛔⛔ <b>The inverse — the state CGF shipped in:</b> the group registered with NOTHING mirrored gives
    /// a registry with no session, which is what every <c>IsEnabled</c> reads. ⭐ This is the rail that
    /// makes "registered" and "usable" two separate claims.
    /// </summary>
    [Fact]
    public void WithoutTheMirrorTheRegistryStaysEmpty()
    {
        var registry = new DebugSessionRegistry();
        AiDebugCommands.Register((_, _) => { }, registry);

        Assert.Null(registry.ActiveSession);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static AiDocumentManager DocsWith(AssetKind kind)
    {
        var docs = new AiDocumentManager((Action<string>)(_ => { }));
        docs.Open(new RailAsset(kind));
        return docs;
    }

    private sealed class RailAsset : IEditableAsset
    {
        public RailAsset(AssetKind kind) => Kind = kind;
        public Guid      AssetId        { get; } = Guid.NewGuid();
        public string    Name           => "rail-doc";
        public AssetKind Kind           { get; }
        public string    SourceFilePath => string.Empty;
        public bool      IsDirty        => false;
        public bool      IsEditorOwned  => true;
        public event Action? Changed;
    }

    /// <summary>⭐ Over the SHARED base, not a hand-rolled interface implementation — ⛔ a fake that
    /// restates 15 members drifts from the contract the moment the contract grows.</summary>
    private sealed class FakeSession : AiDebugSessionBase
    {
        protected override void OnContinueImpl() { }
        protected override void OnPauseImpl()    { }
        protected override void OnStepOverImpl() { }
        protected override void OnStepIntoImpl() { }
        protected override void OnStepOutImpl()  { }
    }
}
