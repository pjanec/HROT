using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Fonts;
using Fdp.Presentation.Utils;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Tests.Map3D;
using ImGuiNET;
using NodeEditor.Core.Widgets;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Panels;

/// <summary>
/// ⭐ U-5 (docs/UX/DESIGN_UI_Conventions.md §4 C3, Q-C5) — every production collapsing header is a <see cref="SectionHeader"/>.
/// (①) a SOURCE rail: no production <c>.cs</c> calls <c>ImGui.CollapsingHeader(</c> except <c>SectionHeader</c> itself and the named stand-alone demos / example apps
/// (they cannot reference NodeEditor.Core, or are not the product); (②) a FRAME rail on a real surface (the raw component editor, <see cref="ComponentReflector"/>): its header is
/// slate with the coloured component icon, and the one header that keeps its own colour — authority — is the green <see cref="HeaderColors"/> override, not a global change.
/// 🔴 Red-proof (compiles): <c>ComponentReflector</c> calling <c>ImGui.CollapsingHeader</c> again (blue ImGui default) ⇒ no slate rows ⇒ red.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class SectionHeaderMigrationRail
{
    /// <summary>Stand-alone demos and example apps — the only files still allowed a raw <c>CollapsingHeader</c> (U-5: they do not reference NodeEditor.Core / are not the product).</summary>
    private static readonly string[] Allowed =
    {
        "FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Widgets/SectionHeader.cs",
        "FDP/ExtDeps/NodeEdit/src/NodeEditor.Demo/",
        "FDP/ExtDeps/FastHSM/demos/",
        "FDP/ExtDeps/FastBTree/demos/",
        "FDP/Examples/",
    };

    [Fact]
    public void NoProductionHeaderIsARawCollapsingHeader_ExceptTheNamedDemos()
    {
        string root = Fdp.Testing.TestRepo.Root;
        var bad = new List<string>();
        int viaSection = 0;
        foreach (var file in Fdp.Testing.RepoSources.CsFiles(root))
        {
            string rel = file.Substring(root.Length).TrimStart('/', '\\').Replace('\\', '/');
            string text = System.IO.File.ReadAllText(file);
            viaSection += text.Split("SectionHeader.Draw(").Length - 1;
            if (!text.Contains(".CollapsingHeader(", StringComparison.Ordinal)) continue;
            if (Allowed.Any(a => rel.StartsWith(a, StringComparison.Ordinal))) continue;
            if (rel.EndsWith("Tests.cs", StringComparison.Ordinal) || rel.Contains(".Tests/", StringComparison.Ordinal)) continue;
            bad.Add(rel);
        }
        Assert.True(bad.Count == 0, "raw CollapsingHeader outside SectionHeader (route it, Q-C5): " + string.Join(", ", bad));
        Assert.True(viaSection >= 35, $"the 35 production headers draw through SectionHeader.Draw (found {viaSection})");
    }

    [ComponentId(247)]
    private struct RailComponent { public int Value; }

    private sealed class Session : IInspectableSession
    {
        public bool Authority;
        public bool IsReadOnly => false;
        public int EntityCount => 1;
        public IEnumerable<Entity> GetEntities() => Array.Empty<Entity>();
        public bool IsAlive(Entity e) => true;
        public IEnumerable<Type> GetAllComponentTypes() => new[] { typeof(RailComponent) };
        public bool HasComponent(Entity e, Type t) => t == typeof(RailComponent);
        public object? GetComponent(Entity e, Type t) => t == typeof(RailComponent) ? new RailComponent { Value = 3 } : null;
        public void SetComponent(Entity e, Type t, object v) { }
        public bool HasAuthority(Entity e, Type t) => Authority;
    }

    private static bool Near(Color c, Vector4 v, int tol) =>
        Math.Abs(c.R - v.X * 255f) <= tol && Math.Abs(c.G - v.Y * 255f) <= tol && Math.Abs(c.B - v.Z * 255f) <= tol;

    [SkippableFact]
    public void TheRawEditorHeader_IsSlateWithItsIcon_AndAuthorityIsTheGreenOverride()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(520, 160);
        new EditorFontService().Initialize(1f, 1f);
        using var repo = new EntityRepository();
        var entity = repo.CreateEntity();
        var reflector = new ComponentReflector();
        var session = new Session();

        void Draw()
        {
            ImGui.SetNextWindowPos(new Vector2(10, 10)); ImGui.SetNextWindowSize(new Vector2(500, 140));
            ImGui.Begin("##hdr", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings);
            reflector.DrawComponents(session, entity);
            ImGui.End();
        }

        // rows of x=300 (clear of icon and text) whose pixel is within tol of a colour
        int Rows(Image shot, Func<Color, bool> what)
        {
            int n = 0;
            for (int y = 10; y < 80; y++) if (what(Raylib.GetImageColor(shot, 300, y))) n++;
            return n;
        }

        session.Authority = false;
        for (int i = 0; i < 6; i++) frame.Step(Draw);
        string? shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS");
        if (shots is { Length: > 0 }) frame.Screenshot(System.IO.Path.Combine(shots, "raw-editor-header.png"));
        var plain = Raylib.LoadImageFromScreen();
        int slateRows, iconPx;
        try
        {
            slateRows = Rows(plain, c => Near(c, SectionHeader.Slate, 6));
            iconPx = 0;
            var comp = Icons.Get("tab/components").Color;
            for (int x = 14; x < 70; x++) for (int y = 10; y < 40; y++) if (Near(Raylib.GetImageColor(plain, x, y), comp, 28)) iconPx++;
        }
        finally { Raylib.UnloadImage(plain); }
        Assert.True(slateRows >= 10, $"the header's background is slate (rows found: {slateRows})");
        Assert.True(iconPx >= 8, $"the header carries its coloured icon (pixels found: {iconPx})");

        session.Authority = true;
        for (int i = 0; i < 6; i++) frame.Step(Draw);
        if (shots is { Length: > 0 }) frame.Screenshot(System.IO.Path.Combine(shots, "raw-editor-header-authority.png"));
        var auth = Raylib.LoadImageFromScreen();
        try
        {
            var green = new Vector4(0.15f, 0.45f, 0.15f, 0.8f);
            int greenRows = Rows(auth, c => c.G > c.R + 25 && c.G > c.B + 25);
            Assert.True(greenRows >= 10, $"authority keeps its green (rows found: {greenRows})");
            Assert.Equal(0, Rows(auth, c => Near(c, SectionHeader.Slate, 6)));
            _ = green;
        }
        finally { Raylib.UnloadImage(auth); }
    }
}
