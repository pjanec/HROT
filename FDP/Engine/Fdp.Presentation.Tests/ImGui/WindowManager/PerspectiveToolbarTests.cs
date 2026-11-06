using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using NodeEditor.Core.Interfaces;
using Xunit;

using WM = Fdp.Presentation.WindowManager.WindowManager;

namespace Fdp.Presentation.Tests.WindowManager;

/// <summary>
/// Tests for <see cref="PerspectiveToolbarSection"/> radio-group model and
/// selection logic (§8.1, MTB-P3-T4).
/// Uses a fake <see cref="IIconProvider"/> + registered <c>TestWindow</c>s
/// to exercise <see cref="PerspectiveToolbarSection.BuildRadioModel"/> and
/// <see cref="PerspectiveToolbarSection.OnSelect"/> headlessly (no ImGui context).
/// </summary>
[Collection("ImGui Sequential")]
public class PerspectiveToolbarTests : IDisposable
{
    private readonly IconAtlas _atlas = new(new IntPtr(1), 256f, 256f, 16f);

    public void Dispose() => _atlas.Dispose();

    private WM CreateManager() => new(_atlas);

    // ── Fake IIconProvider ─────────────────────────────────────────────────

    /// <summary>
    /// Fake icon provider that resolves only keys added via <see cref="Add"/>.
    /// All other keys return <c>false</c> from <see cref="IIconProvider.TryGet"/>.
    /// </summary>
    private sealed class FakeIconProvider : IIconProvider
    {
        private readonly Dictionary<string, IconHandle> _icons = new();

        public void Add(string key, IconHandle handle) => _icons[key] = handle;

        public bool TryGet(string key, out IconHandle handle)
        {
            if (key != null && _icons.TryGetValue(key, out handle))
                return true;
            handle = default;
            return false;
        }
    }

    /// <summary>
    /// Minimal concrete window that exposes <see cref="ManagedWindow.IconKey"/>.
    /// </summary>
    private sealed class TestPerspWindow : ManagedWindow
    {
        public TestPerspWindow(string id, string perspective, string? iconKey = null)
            : base(id, id, perspective, WindowScope.PerspectiveBound)
        {
            IconKey = iconKey;
        }

        protected override void DrawClientArea() { }
    }

    /// <summary>
    /// Creates a <see cref="PerspectiveToolbarSection"/> for testing.
    /// The toolbar manager is optional for headless tests (only needed for Render).
    /// </summary>
    private static PerspectiveToolbarSection CreateSection(
        WM wm,
        IIconProvider iconProvider,
        MainToolbarManager? toolbar = null)
    {
        toolbar ??= new MainToolbarManager();
        return new PerspectiveToolbarSection(wm, iconProvider, toolbar, sortOrder: 10);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // BuildRadioModel — ExactlyOneToggled_EqualsCurrentPerspective
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ExactlyOneToggled_EqualsCurrentPerspective()
    {
        var wm = CreateManager();
        wm.RegisterWindow(new TestPerspWindow("w1", "Default"));
        wm.RegisterWindow(new TestPerspWindow("w2", "Player"));
        wm.RegisterWindow(new TestPerspWindow("w3", "ExCon"));
        wm.SwitchPerspective("Player");

        var fakeIcons = new FakeIconProvider();
        var section = CreateSection(wm, fakeIcons);

        var model = section.BuildRadioModel();

        // Exactly one entry is toggled.
        Assert.Single(model.Where(e => e.IsToggled));

        // It equals CurrentPerspective.
        var toggled = model.Single(e => e.IsToggled);
        Assert.Equal("Player", toggled.Perspective);
        Assert.Equal(wm.CurrentPerspective, toggled.Perspective);

        // The others are not toggled.
        foreach (var entry in model.Where(e => !e.IsToggled))
        {
            Assert.NotEqual(wm.CurrentPerspective, entry.Perspective);
        }
    }

    [Fact]
    public void ExactlyOneToggled_AfterSwitchingPerspective()
    {
        var wm = CreateManager();
        wm.RegisterWindow(new TestPerspWindow("w1", "Default"));
        wm.RegisterWindow(new TestPerspWindow("w2", "Player"));
        var fakeIcons = new FakeIconProvider();
        var section = CreateSection(wm, fakeIcons);

        // Initially: Default is toggled.
        var model1 = section.BuildRadioModel();
        Assert.True(model1.Single(e => e.Perspective == "Default").IsToggled);
        Assert.False(model1.Single(e => e.Perspective == "Player").IsToggled);

        // Switch to IG.
        wm.SwitchPerspective("Player");
        var model2 = section.BuildRadioModel();
        Assert.False(model2.Single(e => e.Perspective == "Default").IsToggled);
        Assert.True(model2.Single(e => e.Perspective == "Player").IsToggled);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // OnSelect — ClickNonActive_SwitchesPerspective
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ClickNonActive_SwitchesPerspective()
    {
        var wm = CreateManager();
        wm.RegisterWindow(new TestPerspWindow("w1", "Default"));
        wm.RegisterWindow(new TestPerspWindow("w2", "Player"));
        wm.SwitchPerspective("Default");
        Assert.Equal("Default", wm.CurrentPerspective);

        var fakeIcons = new FakeIconProvider();
        var section = CreateSection(wm, fakeIcons);

        // Select a non-active perspective.
        section.OnSelect("Player");

        Assert.Equal("Player", wm.CurrentPerspective);
    }

    [Fact]
    public void ClickActive_IsNoOp_StaysOnSamePerspective()
    {
        var wm = CreateManager();
        wm.RegisterWindow(new TestPerspWindow("w1", "Default"));
        wm.RegisterWindow(new TestPerspWindow("w2", "Player"));
        wm.SwitchPerspective("Player");

        var fakeIcons = new FakeIconProvider();
        var section = CreateSection(wm, fakeIcons);

        // Select the already-active perspective.
        section.OnSelect("Player");

        // Should still be "IG" (no-op).
        Assert.Equal("Player", wm.CurrentPerspective);
    }

    [Fact]
    public void OnSelect_FiresPerspectiveChangedEvent()
    {
        var wm = CreateManager();
        wm.RegisterWindow(new TestPerspWindow("w1", "Default"));
        wm.RegisterWindow(new TestPerspWindow("w2", "Player"));
        wm.SwitchPerspective("Default");

        string? old = null, @new = null;
        wm.OnPerspectiveChanged += (o, n) => { old = o; @new = n; };

        var fakeIcons = new FakeIconProvider();
        var section = CreateSection(wm, fakeIcons);
        section.OnSelect("Player");

        Assert.Equal("Default", old);
        Assert.Equal("Player", @new);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // IconKey resolution — MissingIconKey_FallsBackToTextButton
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void MissingIconKey_FallsBackToTextButton()
    {
        var wm = CreateManager();
        // IG has no IconKey set.
        wm.RegisterWindow(new TestPerspWindow("w1", "Player", iconKey: null));
        // Default has a resolvable IconKey.
        wm.RegisterWindow(new TestPerspWindow("w2", "Default", iconKey: "perspective/default"));

        var fakeIcons = new FakeIconProvider();
        fakeIcons.Add("perspective/default", new IconHandle(new IntPtr(42), 64, 64));

        var section = CreateSection(wm, fakeIcons);

        var model = section.BuildRadioModel();

        // Default: hasIcon == true (key is resolvable).
        var defaultEntry = model.Single(e => e.Perspective == "Default");
        Assert.True(defaultEntry.HasIcon);

        // IG: hasIcon == false (no IconKey set → text fallback).
        var playerEntry = model.Single(e => e.Perspective == "Player");
        Assert.False(playerEntry.HasIcon);
    }

    [Fact]
    public void UnresolvableIconKey_FallsBackToTextButton()
    {
        var wm = CreateManager();
        // IG has an IconKey, but the provider doesn't know about it.
        wm.RegisterWindow(new TestPerspWindow("w1", "Player", iconKey: "player/unknown"));
        // Default has a resolvable IconKey.
        wm.RegisterWindow(new TestPerspWindow("w2", "Default", iconKey: "perspective/default"));

        var fakeIcons = new FakeIconProvider();
        fakeIcons.Add("perspective/default", new IconHandle(new IntPtr(42), 64, 64));
        // Note: "ig/unknown" is NOT added to the fake provider.

        var section = CreateSection(wm, fakeIcons);

        var model = section.BuildRadioModel();

        // Default: key resolves → hasIcon == true.
        Assert.True(model.Single(e => e.Perspective == "Default").HasIcon);

        // IG: key exists but doesn't resolve → hasIcon == false.
        Assert.False(model.Single(e => e.Perspective == "Player").HasIcon);
    }

    [Fact]
    public void FirstNonNullIconKey_UsedForPerspective()
    {
        var wm = CreateManager();
        // Register two windows in the same perspective; only the first one has IconKey.
        wm.RegisterWindow(new TestPerspWindow("w1", "Player", iconKey: null));
        wm.RegisterWindow(new TestPerspWindow("w2", "Player", iconKey: "player/icon"));

        var fakeIcons = new FakeIconProvider();
        fakeIcons.Add("player/icon", new IconHandle(new IntPtr(99), 64, 64));

        var section = CreateSection(wm, fakeIcons);

        var model = section.BuildRadioModel();
        var playerEntry = model.Single(e => e.Perspective == "Player");

        // It should pick up the first non-null IconKey (from w2) and resolve it.
        Assert.True(playerEntry.HasIcon);
    }

    [Fact]
    public void GetPerspectiveIconKey_ReturnsFirstNonNull()
    {
        var wm = CreateManager();
        wm.RegisterWindow(new TestPerspWindow("w1", "Player", iconKey: null));
        wm.RegisterWindow(new TestPerspWindow("w2", "Player", iconKey: "player/second"));

        // The first non-null in order of insertion.
        // Note: _windows is a Dictionary, so iteration order is insertion order.
        var key = wm.GetPerspectiveIconKey("Player");

        // Should be the first non-null, which is w2's key.
        // But since both w1 (null) and w2 ("ig/second") are in the dictionary,
        // FirstOrDefault should skip w1's null and return w2's.
        Assert.Equal("player/second", key);
    }
}
