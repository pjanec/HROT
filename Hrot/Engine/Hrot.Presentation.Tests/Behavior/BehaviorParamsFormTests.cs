using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.Replication;
using Hrot.Map.Definitions.Behavior;
using Hrot.Presentation.Behavior;
using Hrot.UI.Common.Panels;
using StructEdit.Core;
using Xunit;

namespace Hrot.Presentation.Tests.Behavior
{
    /// <summary>
    /// ⭐⭐ <c>CE-1023</c> (📄 <c>docs/DESIGN_Sensors_And_Doctrine.md</c> §7.6b) — THE ONE PARAMS FORM on a task (the Orders tab since P-7 7d; the retired mission panel before): a pick lands
    /// in the task's JSON through the task's scoped path, the form never rewrites a draft it only looked at, and every declared
    /// behaviour's params type draws. ⭐ The two pick rails are re-homed from <c>BehaviorUiCompilerTests</c> (CE-2023 ③, the
    /// compiled form deleted by CE-1023): the claim — a pick on a STRUCT contract reaches the JSON, not a copy — is the same.
    /// </summary>
    [Xunit.Collection(Hrot.Presentation.Tests.ImGuiTestCollection.Name)]   // ⭐ one global ImGui context
    public sealed class BehaviorParamsFormTests
    {
        private sealed class PickingContext : IMapPickContext
        {
            public int Entity;
            public PointLink? Location;
            public readonly List<string> AskedPaths = new();
            public bool IsPickPendingFor(string path) => false;
            public bool TryConsumeEntityPick(string path, out EntityRef picked)
            { AskedPaths.Add(path); picked = new EntityRef(Entity); return Entity != 0; }
            public bool TryConsumeLocationPick(string path, out PointLink location)
            { AskedPaths.Add(path); location = Location ?? default; return Location.HasValue; }
            public void RequestEntityPick(string path, string[]? filterPresets) { }
            public void RequestLocationPick(string path) { }
        }

        private static void DrawOneFrame(IEditSession session, IMapPickContext? picks)
        {
            var ctx = ImGuiNET.ImGui.CreateContext();
            ImGuiNET.ImGui.SetCurrentContext(ctx);
            var io = ImGuiNET.ImGui.GetIO();
            io.DisplaySize = new System.Numerics.Vector2(1024, 768);
            io.DeltaTime   = 1.0f / 60.0f;
            io.Fonts.AddFontDefault();
            io.Fonts.Build();
            try
            {
                ImGuiNET.ImGui.NewFrame();
                ImGuiNET.ImGui.Begin("CE1023");
                BehaviorParamsForm.DrawFields(session, picks);
                ImGuiNET.ImGui.End();
                ImGuiNET.ImGui.Render();
            }
            finally { ImGuiNET.ImGui.DestroyContext(ctx); }
        }

        /// <summary>⭐ P-7 7d — the pick scope of a task's form (what the retired <c>Task0</c> answered): a field <c>X</c> picks at <c>$.tasks[0].X</c>.</summary>
        private const string Task0 = "$.tasks[0]";

        /// <summary>⭐ P-7 7d — a task's params form, built the way the Orders tab builds it (one editor over the shared edit service, the host's lookup — here the declared contracts).
        /// Null when the behaviour has no params type.</summary>
        private static BehaviorParamsEditor? TryFormOf(string behaviour, string json)
        {
            var type = BehaviorParamsForm.ParamsTypeOf(behaviour, null);
            if (type == null) return null;
            var form = new BehaviorParamsEditor(Fdp.Presentation.Editing.ComponentEditServices.CreateBuilder().Build());
            form.Sync(type, json);
            return form;
        }

        private static BehaviorParamsEditor FormOf(string behaviour, string json) => TryFormOf(behaviour, json)!;

        [Fact]
        public void CE1023_AnEntityPick_LandsInTheTasksJson_AtTheTasksScopedPath()
        {
            Assert.True(typeof(FireAtTargetParamsJsonDto).IsValueType);
            var form = FormOf(FireAtTargetParamsJsonDto.BehaviorId, "{\"targetNetworkId\":42,\"maxRounds\":5,\"cooldownSeconds\":1.0}");
            var ctx  = new PickingContext { Entity = 99 };

            DrawOneFrame(form.Session!, new ScopedMapPickContext(ctx, Task0));
            string? json = form.Edited();

            Assert.NotNull(json);
            Assert.Contains("\"targetNetworkId\":99", json);
            Assert.Contains("\"maxRounds\":5", json);
            Assert.Contains("$.tasks[0].TargetNetworkId", ctx.AskedPaths);   // ⭐ the task's scope — the path the broker keys on
        }

        [Fact]
        public void CE1023_ALocationPick_LandsInTheTasksJson()
        {
            Assert.True(typeof(MoveToLocationParamsJsonDto).IsValueType);
            var form = FormOf(MoveToLocationParamsJsonDto.BehaviorId, "{\"target\":[1,2,0],\"speed\":5}");

            DrawOneFrame(form.Session!, new ScopedMapPickContext(new PickingContext { Location = new PointLink(new Fdp.Core.WorldPoint(50.5f, 14.25f, 0f)) },
                                                                Task0));
            string? json = form.Edited();

            Assert.NotNull(json);
            Assert.Contains("\"target\":[50.5, 14.25, 0]", json);
            Assert.Contains("\"speed\":5", json);
        }

        /// <summary>⭐ F4 — opening (and drawing with no edit) never rewrites the draft; an OUTSIDE change reopens; the form's own
        /// commit does not.</summary>
        [Fact]
        public void CE1023_TheForm_NeverRewritesADraftItOnlyLookedAt_AndFollowsAnOutsideChange()
        {
            var form = FormOf(FireAtTargetParamsJsonDto.BehaviorId, "{\"targetNetworkId\":42}");
            var first = form.Session;
            DrawOneFrame(form.Session!, picks: null);
            Assert.Null(form.Edited());

            form.Sync(typeof(FireAtTargetParamsJsonDto), "{\"targetNetworkId\":42}");
            Assert.Same(first, form.Session);                                           // same JSON: kept (an edit in progress survives)

            form.Sync(typeof(FireAtTargetParamsJsonDto), "{\"targetNetworkId\":7}");   // a snapshot sync from outside
            Assert.NotSame(first, form.Session);
            Assert.Contains("\"targetNetworkId\":7", BehaviorParamsForm.CommitToJson(form.Session!, typeof(FireAtTargetParamsJsonDto)));

            var second = form.Session;
            DrawOneFrame(second!, new PickingContext { Entity = 11 });
            string? own = form.Edited();
            Assert.Contains("\"targetNetworkId\":11", own);
            form.Sync(typeof(FireAtTargetParamsJsonDto), own);                          // the panel writes the edit back to the draft
            Assert.Same(second, form.Session);                                          // its own commit: not reopened
        }

        /// <summary>
        /// ⭐ R-137 guard — every declared behaviour's params type opens and draws in THE form (the deleted compiled form drew only
        /// float/double/int/long/bool/EntityRef/PickableGeoPoint properties; a type it could not draw fell to raw JSON).
        /// </summary>
        [Fact]
        public void CE1023_EveryDeclaredBehavioursParams_OpenAndDraw()
        {
            var behaviours = ParamsDtoRegistry.Default.Behaviors;
            Assert.NotEmpty(behaviours);
            foreach (var d in behaviours)
            {
                var form = TryFormOf(d.Name, "{}");
                Assert.True(form?.Session != null, $"{d.Name}: no form for {d.ParamsType.Name}");
                DrawOneFrame(form!.Session!, new PickingContext());
                Assert.Null(form.Edited());
            }
        }

        [Fact]
        public void CE1023_TheScopedContext_RewritesAFormPath_IntoItsScope()
        {
            var ctx = new PickingContext();
            var scoped = new ScopedMapPickContext(ctx, "$.tasks[3]");
            Assert.Equal("$.tasks[3].Vehicle", scoped.Path("$.Vehicle"));
            Assert.Equal("$.tasks[3]", scoped.Path("$"));
            scoped.TryConsumeEntityPick("$.Vehicle", out _);
            Assert.Equal("$.tasks[3].Vehicle", ctx.AskedPaths.Single());
        }


        // ── CE-1061 U5b (§13.10c E1/E2): an asset's params defaults ─────────────────────────────

        public struct DefaultsRailDto
        {
            public float Radius = 1.5f;
            public int Rounds = 4;
            public DefaultsRailDto() { }
        }

        private static void SetField(IEditSession session, string name, object value)
        {
            var root = session.Document.Root;
            var fields = root.Kind == EditNodeKind.SelectionRoot ? root.Children.First() : root;
            fields.Children.Single(c => c.Name == name).Binding!.SetBoxed(value);
        }

        /// <summary>⭐ <c>CE-1061</c> U5b (acceptance 4) — the form opens on the EFFECTIVE value (the DTO's initialisers, the asset's
        /// overrides on top); editing one field writes ONLY that key; Reset removes it; the form's own commit does not reopen the session,
        /// an outside change (an undo) does.</summary>
        [Fact]
        public void CE1061_EditingADefault_WritesOnlyThatKey_AndResetRemovesIt()
        {
            var model = new ParamsDefaultsModel(ComponentEditServices.CreateBuilder().Build());
            model.Sync(typeof(DefaultsRailDto), "{\"radius\":2.5}");
            var opened = (DefaultsRailDto)model.Session!.Commit();
            Assert.Equal((2.5f, 4), (opened.Radius, opened.Rounds));          // override on top of the initialiser
            Assert.True(model.IsOverridden("Radius"));
            Assert.False(model.IsOverridden("Rounds"));
            Assert.False(model.TryEdited(out _, out _));                       // nothing edited yet

            SetField(model.Session, "Rounds", 7);
            Assert.True(model.TryEdited(out var json, out var field));
            Assert.Equal("Rounds", field);
            var written = System.Text.Json.Nodes.JsonNode.Parse(json!)!.AsObject();
            Assert.Equal(new[] { "radius", "Rounds" }, written.Select(kv => kv.Key));   // the old override + ONLY the edited key
            Assert.Equal(7, (int)written["Rounds"]!);

            var session = model.Session;
            model.Sync(typeof(DefaultsRailDto), json);                         // the asset now holds the form's own commit
            Assert.Same(session, model.Session);                               // not reopened (a drag keeps its widget)

            Assert.Equal("{\"Rounds\":7}", model.Without("Radius"));          // Reset Radius: its key goes, the DTO default returns
            Assert.Null(new ParamsDefaultsModel(ComponentEditServices.CreateBuilder().Build()).Without("Radius"));

            model.Sync(typeof(DefaultsRailDto), "{\"radius\":2.5}");          // an outside change (an undo)
            Assert.NotSame(session, model.Session);
            Assert.Equal(4, ((DefaultsRailDto)model.Session!.Commit()).Rounds);
        }

        /// <summary>⭐ CE-3384 — an override is a DIFFERENCE: a value written equal to the DTO's own default drops the key (what the table's ↺ writes), and the model offers the struct table its defaults.</summary>
        [Fact]
        public void CE3384_AValueEqualToTheDtoDefault_IsNoOverride_AndTheModelOffersTheTableItsDefaults()
        {
            var model = new ParamsDefaultsModel(ComponentEditServices.CreateBuilder().Build());
            model.Sync(typeof(DefaultsRailDto), "{\"radius\":2.5}");
            Assert.NotNull(model.Defaults);
            var radius = (model.Session!.Document.Root.Kind == EditNodeKind.SelectionRoot ? model.Session.Document.Root.Children.First() : model.Session.Document.Root).Children.Single(c => c.Name == "Radius");
            Assert.True(model.Defaults!.TryGetDefault(radius, out var d)); Assert.Equal(1.5f, d);
            SetField(model.Session, "Radius", 1.5f);                           // ↺: the default written back
            Assert.True(model.TryEdited(out var json, out _));
            Assert.Null(json);                                                  // no override is left
            Assert.False(model.IsOverridden("Radius"));
        }
}
}
