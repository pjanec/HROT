using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.Behavior.Params;
using Fdp.Toolkit.Replication;
using Hrot.Map.Definitions.Behavior;
using Hrot.Presentation.Behavior;
using Hrot.UI.Common.Panels;
using StructEdit.Core;
using Xunit;

namespace Hrot.Presentation.Tests.Behavior
{
    /// <summary>
    /// ⭐⭐ <c>CE-1023</c> (📄 <c>docs/DESIGN_Sensors_And_Doctrine.md</c> §7.6b) — THE ONE PARAMS FORM on the mission panel: a pick lands
    /// in the task's JSON through the task's scoped path, the form never rewrites a draft it only looked at, and every declared
    /// behaviour's params type draws. ⭐ The two pick rails are re-homed from <c>BehaviorUiCompilerTests</c> (CE-2023 ③, the
    /// compiled form deleted by CE-1023): the claim — a pick on a STRUCT contract reaches the JSON, not a copy — is the same.
    /// </summary>
    public sealed class BehaviorParamsFormTests
    {
        private sealed class PickingContext : IMapPickContext
        {
            public long Entity;
            public PickableGeoPoint? Location;
            public readonly List<string> AskedPaths = new();
            public bool IsPickPendingFor(string path) => false;
            public bool TryConsumeEntityPick(string path, out EntityRef picked)
            { AskedPaths.Add(path); picked = new EntityRef(Entity); return Entity != 0; }
            public bool TryConsumeLocationPick(string path, out PickableGeoPoint location)
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

        /// <summary>The mission panel's form for task 0 with <paramref name="behaviour"/> and <paramref name="json"/>.</summary>
        private static BehaviorParamsEditor FormOf(string behaviour, string json)
            => new MissionPanel().ParamsFormFor(Guid.NewGuid(), behaviour, json)!;

        [Fact]
        public void CE1023_AnEntityPick_LandsInTheTasksJson_AtTheTasksScopedPath()
        {
            Assert.True(typeof(FireAtTargetParamsJsonDto).IsValueType);
            var form = FormOf(FireAtTargetParamsJsonDto.BehaviorId, "{\"targetNetworkId\":42,\"maxRounds\":5,\"cooldownSeconds\":1.0}");
            var ctx  = new PickingContext { Entity = 99 };

            DrawOneFrame(form.Session!, new ScopedMapPickContext(ctx, MissionPanel.PickScope(0)));
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
            var form = FormOf(MoveToLocationParamsJsonDto.BehaviorId, "{\"targetLat\":1,\"targetLon\":2,\"speed\":5}");

            DrawOneFrame(form.Session!, new ScopedMapPickContext(new PickingContext { Location = new PickableGeoPoint(50.5, 14.25) },
                                                                MissionPanel.PickScope(0)));
            string? json = form.Edited();

            Assert.NotNull(json);
            Assert.Contains("\"targetLat\":50.5", json);
            Assert.Contains("\"targetLon\":14.25", json);
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
                var form = new MissionPanel().ParamsFormFor(Guid.NewGuid(), d.Name, "{}");
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
    }
}
