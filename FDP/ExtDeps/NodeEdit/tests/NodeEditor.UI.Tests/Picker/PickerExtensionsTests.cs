using FluentAssertions;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Picker;
using Xunit;

namespace NodeEditor.UI.Tests.Picker;

/// <summary>
/// ⭐ CE-4168 (AQ108 D26 P1–P5) — the five opt-in extensions of the ONE generic picker, driven headless through <see cref="PickerWindow"/>'s
/// confirm (no ImGui frame): side pane (P1) · interactive pane state handed back (P2) · footer actions (P3) · remembered layout toggle (P4) ·
/// shared lazy image cache (P5). Red-proofs (each compiles, measured): <c>PickerAction.Resolve</c> taking <c>actions[^1]</c> instead of the default ⇒
/// the default-action rails fail · <c>PickerImageCache.TryGet</c> without the <c>_map</c> insert ⇒ the load-once / LRU / budget / failure rails fail.
/// </summary>
public sealed class PickerExtensionsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pickerview-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static PickerEntry E(string id, string? category = null, string? image = null)
        => new(id, id, "desc " + id, category, null, null, id, ImageKey: image);

    private static PickerRequest Req(Func<PickerRequest, PickerRequest>? tweak = null, string ctx = "ctx.ext")
    {
        var r = new PickerRequest
        {
            ContextKey = ctx,
            Title = "t",
            ItemsProvider = () => new[] { E("a", "X"), E("b", "X"), E("c", "Y") },
        };
        return tweak is null ? r : tweak(r);
    }

    private static PickerResult? Open(PickerWindow w, PickerRequest req, string confirmId, string? actionId = null)
    {
        PickerResult? got = null;
        w.Open(req, r => got = r);
        w.ConfirmEntry(confirmId, actionId);
        return got;
    }

    // ── off by default (every existing picker unchanged) ─────────────────────

    [Fact]
    public void A_classic_request_has_no_side_pane_no_action_no_pane_state_and_ignores_the_view_store()
    {
        var w = new PickerWindow();
        w.View.SetLayout("ctx.ext", PickerLayout.Grid);            // a stale choice must NOT leak into a picker that did not opt in
        w.Open(Req(r => new PickerRequest { ContextKey = r.ContextKey, Title = "t", Layout = PickerLayout.Tree, ItemsProvider = r.ItemsProvider }), _ => { });

        w.SidePaneOn.Should().BeFalse();
        w.CurrentLayout.Should().Be(PickerLayout.Tree);

        var res = Open(w, Req(), "a");
        res!.ActionId.Should().BeNull();
        res.PaneState.Should().BeNull();
        res.First!.Id.Should().Be("a");
        w.SetLayout(PickerLayout.Grid).Should().BeFalse("no LayoutChoices ⇒ no toggle");
    }

    // ── P1 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Side_pane_is_on_for_ShowSidePane_or_a_DrawPane_and_the_width_is_clamped()
    {
        var w = new PickerWindow();
        w.Open(Req(r => new PickerRequest { ContextKey = "c", Title = "t", ItemsProvider = r.ItemsProvider, ShowSidePane = true, SidePaneWidth = 333f }), _ => { });
        w.SidePaneOn.Should().BeTrue();
        w.PaneWidth.Should().Be(333f);

        w.Open(Req(r => new PickerRequest { ContextKey = "c2", Title = "t", ItemsProvider = r.ItemsProvider, DrawPane = (_, _) => { } }), _ => { });
        w.SidePaneOn.Should().BeTrue();

        PickerWindow.ClampPaneWidth(10f, 800f, 6f).Should().Be(PickerWindow.MinPaneWidth);
        PickerWindow.ClampPaneWidth(790f, 800f, 6f).Should().Be((800f - 6f) * 0.7f);
        PickerWindow.ClampPaneWidth(300f, 800f, 6f).Should().Be(300f);
    }

    [Fact]
    public void Side_pane_width_is_remembered_per_context_in_a_new_window()
    {
        var store = new PickerViewStore();
        var w1 = new PickerWindow { View = store };
        w1.Open(Req(r => new PickerRequest { ContextKey = "c", Title = "t", ItemsProvider = r.ItemsProvider, ShowSidePane = true }), _ => { });
        store.SetPaneWidth("c", 420f);

        var w2 = new PickerWindow { View = store };
        w2.Open(Req(r => new PickerRequest { ContextKey = "c", Title = "t", ItemsProvider = r.ItemsProvider, ShowSidePane = true }), _ => { });
        w2.PaneWidth.Should().Be(420f);
        w2.Open(Req(r => new PickerRequest { ContextKey = "other", Title = "t", ItemsProvider = r.ItemsProvider, ShowSidePane = true }), _ => { });
        w2.PaneWidth.Should().Be(280f, "another context keeps the request's own default");
    }

    // ── P2 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Pane_state_is_captured_at_confirm_for_the_chosen_entry_and_handed_back()
    {
        var hostState = new { Variant = "random" };
        PickerEntry? seen = null;
        var res = Open(new PickerWindow(), Req(r => new PickerRequest
        {
            ContextKey = "c", Title = "t", ItemsProvider = r.ItemsProvider, ShowSidePane = true,
            CapturePaneState = e => { seen = e; return hostState; },
        }), "b");

        res!.PaneState.Should().BeSameAs(hostState);
        seen!.Id.Should().Be("b");
        res.Cancelled.Should().BeFalse();
    }

    [Fact]
    public void PickerResult_keeps_its_one_argument_constructor_and_cancelled_carries_no_pane_state()
    {
        var res = new PickerResult(new[] { E("a") });
        res.First!.Id.Should().Be("a");
        res.ActionId.Should().BeNull();
        res.PaneState.Should().BeNull();

        var w = new PickerWindow();
        PickerResult? got = null;
        w.Open(Req(r => new PickerRequest { ContextKey = "c", Title = "t", ItemsProvider = r.ItemsProvider, CapturePaneState = _ => "x" }), r => got = r);
        w.Open(Req(), _ => { });   // a new Open cancels the previous session
        got!.Cancelled.Should().BeTrue();
        got.PaneState.Should().BeNull();
    }

    // ── P3 ────────────────────────────────────────────────────────────────────

    private static PickerRequest WithActions(params PickerAction[] actions)
        => Req(r => new PickerRequest { ContextKey = "c", Title = "t", ItemsProvider = r.ItemsProvider, Actions = actions });

    [Fact]
    public void A_clicked_action_returns_its_id_and_the_default_action_is_taken_on_a_plain_confirm()
    {
        var actions = new[]
        {
            new PickerAction("open", "Open", IsDefault: true),
            new PickerAction("golive", "Open and go live"),
        };

        Open(new PickerWindow(), WithActions(actions), "a", "golive")!.ActionId.Should().Be("golive");
        Open(new PickerWindow(), WithActions(actions), "a")!.ActionId.Should().Be("open", "Enter / double-click take the default action");
    }

    [Fact]
    public void With_no_action_flagged_default_the_first_is_the_default()
    {
        var res = Open(new PickerWindow(), WithActions(new PickerAction("one", "One"), new PickerAction("two", "Two")), "a");
        res!.ActionId.Should().Be("one");
    }

    [Fact]
    public void A_disabled_action_never_confirms_and_sees_the_entry_it_applies_to()
    {
        PickerEntry? asked = null;
        var disabled = new PickerAction("place", "Place", IsDefault: true, DisabledReason: e => { asked = e; return e?.Id == "a" ? "not a" : null; });

        var w = new PickerWindow();
        PickerResult? got = null;
        w.Open(WithActions(disabled), r => got = r);

        w.ConfirmEntry("a").Should().BeFalse("the disabled default action keeps the picker open");
        got.Should().BeNull("no result — not even an empty one — reached the host");
        asked!.Id.Should().Be("a");

        w.ConfirmEntry("b").Should().BeTrue();
        got!.ActionId.Should().Be("place");
        got.First!.Id.Should().Be("b");
    }

    [Fact]
    public void An_unknown_action_id_never_confirms()
    {
        var w = new PickerWindow();
        PickerResult? got = null;
        w.Open(WithActions(new PickerAction("open", "Open", true)), r => got = r);
        w.ConfirmEntry("a", "nope").Should().BeFalse();
        got.Should().BeNull();
    }

    // ── P4 ────────────────────────────────────────────────────────────────────

    private static PickerRequest Toggle(string ctx, PickerLayout first = PickerLayout.Tree)
        => Req(r => new PickerRequest
        {
            ContextKey = ctx, Title = "t", ItemsProvider = r.ItemsProvider, Layout = first,
            LayoutChoices = new[] { PickerLayout.Tree, PickerLayout.Grid },
        });

    [Fact]
    public void The_layout_toggle_is_remembered_per_context_across_a_new_window_and_a_restart()
    {
        var path = Path.Combine(_dir, "picker-view.json");
        var w1 = new PickerWindow { View = PickerViewStore.Load(path) };
        w1.Open(Toggle("scen"), _ => { });
        w1.CurrentLayout.Should().Be(PickerLayout.Tree);
        w1.SetLayout(PickerLayout.Grid).Should().BeTrue();

        // new window, same store ⇒ opens in the remembered layout; another context is untouched
        var w2 = new PickerWindow { View = w1.View };
        w2.Open(Toggle("scen"), _ => { });
        w2.CurrentLayout.Should().Be(PickerLayout.Grid);
        w2.Open(Toggle("ent"), _ => { });
        w2.CurrentLayout.Should().Be(PickerLayout.Tree);

        // restart: a store reloaded from the file
        var w3 = new PickerWindow { View = PickerViewStore.Load(path) };
        w3.Open(Toggle("scen"), _ => { });
        w3.CurrentLayout.Should().Be(PickerLayout.Grid);
    }

    [Fact]
    public void A_remembered_layout_outside_the_choices_is_ignored_and_a_foreign_layout_is_refused()
    {
        var w = new PickerWindow();
        w.View.SetLayout("scen", PickerLayout.Wide);
        w.Open(Toggle("scen"), _ => { });
        w.CurrentLayout.Should().Be(PickerLayout.Tree);
        w.SetLayout(PickerLayout.Wide).Should().BeFalse();
        w.CurrentLayout.Should().Be(PickerLayout.Tree);
    }

    [Fact]
    public void Toggling_keeps_the_chosen_entry_and_still_confirms_it()
    {
        var w = new PickerWindow();
        PickerResult? got = null;
        w.Open(Toggle("scen"), r => got = r);
        w.ConfirmEntry("c").Should().BeTrue();   // sanity: confirm works in Tree
        w.Open(Toggle("scen"), r => got = r);
        w.State.ApplyInitialSelection("b");
        w.SetLayout(PickerLayout.Grid);
        w.State.CurrentEntry()!.Id.Should().Be("b");
        w.State.FocusLeafOnDraw.Should().Be(w.State.Filtered.FindIndex(r => r.Entry.Id == "b"));
    }

    // ── P5 ────────────────────────────────────────────────────────────────────

    private sealed class Loader
    {
        public readonly List<string> Loaded = [];
        public readonly List<IntPtr> Unloaded = [];
        public readonly HashSet<string> Missing = [];
        private int _next = 1;
        public (IntPtr, int, int)? Load(string key)
        {
            Loaded.Add(key);
            return Missing.Contains(key) ? null : ((IntPtr)_next++, 200, 100);
        }
    }

    [Fact]
    public void The_image_cache_loads_lazily_and_once_per_key()
    {
        var l = new Loader();
        var cache = new PickerImageCache(l.Load, l.Unloaded.Add);
        l.Loaded.Should().BeEmpty("nothing loads until asked");

        var first = cache.Get("a.png");
        first.Should().NotBeNull();
        cache.Get("a.png").Should().Be(first);
        cache.TryGet("a.png", out var img).Should().BeTrue();
        (img.Width, img.Height).Should().Be((200, 100));
        l.Loaded.Should().Equal("a.png");
    }

    [Fact]
    public void The_image_cache_evicts_the_least_recently_used_and_unloads_it()
    {
        var l = new Loader();
        var cache = new PickerImageCache(l.Load, l.Unloaded.Add, capacity: 2, maxLoadsPerFrame: 99);
        var a = cache.Get("a")!.Value;
        var b = cache.Get("b")!.Value;
        cache.Get("a");                    // a is now more recent than b
        cache.Get("c");                    // evicts b
        cache.Count.Should().Be(2);
        l.Unloaded.Should().Equal(b);

        cache.Get("a").Should().Be(a, "a survived");
        cache.Get("b");                    // reload (evicts c, the oldest now)
        l.Loaded.Should().Equal("a", "b", "c", "b");
        l.Unloaded.Should().HaveCount(2);
    }

    [Fact]
    public void The_image_cache_loads_at_most_N_per_frame_and_serves_the_rest_next_frame()
    {
        var l = new Loader();
        var cache = new PickerImageCache(l.Load, null, capacity: 64, maxLoadsPerFrame: 2);

        cache.Get("k1").Should().NotBeNull();
        cache.Get("k2").Should().NotBeNull();
        cache.Get("k3").Should().BeNull("over this frame's budget");
        cache.Get("k4").Should().BeNull();
        l.Loaded.Should().Equal("k1", "k2");
        cache.Get("k1").Should().NotBeNull("a cached image is free — it costs no load budget");

        cache.BeginFrame();
        cache.Get("k3").Should().NotBeNull();
        cache.Get("k4").Should().NotBeNull();
        l.Loaded.Should().Equal("k1", "k2", "k3", "k4");
    }

    [Fact]
    public void A_failed_load_is_remembered_and_not_retried_until_invalidated()
    {
        var l = new Loader();
        l.Missing.Add("gone.png");
        var cache = new PickerImageCache(l.Load, l.Unloaded.Add, maxLoadsPerFrame: 99);

        cache.Get("gone.png").Should().BeNull();
        cache.Get("gone.png").Should().BeNull();
        l.Loaded.Should().Equal("gone.png");

        l.Missing.Clear();
        cache.Invalidate("gone.png");
        cache.Get("gone.png").Should().NotBeNull();
        l.Loaded.Should().Equal("gone.png", "gone.png");
    }

    [Fact]
    public void A_throwing_loader_is_a_failed_load_not_a_crash()
    {
        var cache = new PickerImageCache(_ => throw new InvalidOperationException("gpu"));
        cache.Get("x").Should().BeNull();
    }

    [Fact]
    public void FitToWidth_keeps_aspect_never_enlarges_and_honours_a_height_cap()
    {
        PickerImageCache.FitToWidth(400, 200, 200f).Should().Be(new System.Numerics.Vector2(200f, 100f));
        PickerImageCache.FitToWidth(100, 50, 500f).Should().Be(new System.Numerics.Vector2(100f, 50f));
        PickerImageCache.FitToWidth(400, 400, 200f, maxH: 100f).Should().Be(new System.Numerics.Vector2(100f, 100f));
        PickerImageCache.FitToWidth(0, 0, 200f).Should().Be(System.Numerics.Vector2.Zero);
    }
}
