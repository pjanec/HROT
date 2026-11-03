using System;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Testing;
using Fdp.Toolkit.Scenario;
using Hrot.CGF.Orchestration;
using Hrot.Common.Macros;
using Hrot.Map.Common;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ CE-4244 (MA-10; docs/DESIGN_Macro_Authoring_Editor.md §10) — a macro PREVIEW is the ONE expander's children as ghosts: nothing created, the failure as text, the anchor of an <c>InsertMacro</c> step resolved the way the step reads it.
    /// </summary>
    public sealed class MacroPreviewTests
    {
        static MacroPreviewTests()
        {
            using var repo = new EntityRepository();
            HrotSharedComponentRegistry.RegisterAll(repo);
            Hrot.CGF.CgfComponentRegistry.RegisterAll(repo);
        }

        private static MacroExpander Expander()
        {
            var serializer = Hrot.SimHost.Serializers.HrotScenarioSerializerFactory.Build(new Fdp.Toolkit.Behavior.BehaviorRegistry(), null, UnknownComponentPolicy.WarnAndSkip);
            return new MacroExpander(new FileMacroLibrary(TestRepo.Root), new StagingEntityExtractor(), serializer);
        }

        private static SimTransform At(float x, float y) => new() { Position = new Vector3(x, y, 0), Rotation = Quaternion.Identity };

        [Fact]
        public void TheGhosts_AreTheExpandersChildren_RelativeToTheAnchor_AndNothingIsPublished()
        {
            var ex = Expander();
            var r = MacroPreview.Of(ex, "test/fire-team", "{}", At(100, 50));
            Assert.True(r.Ok, r.Failure);
            Assert.NotEmpty(r.Ghosts);
            var real = ex.Expand(new MacroInstance { Macro = "test/fire-team", ArgsJson = "{}" }, 1, Guid.NewGuid(), (NodeId)3, At(100, 50), null, new Hrot.Core.Network.SequentialIdAllocator());
            Assert.Equal(real.Children.Count, r.Ghosts.Count + r.Hidden);            // every child is a ghost or counted as hidden (a rule / marker)
            var other = MacroPreview.Of(ex, "test/fire-team", "{}", At(0, 0));
            Assert.Equal(other.Ghosts.Count, r.Ghosts.Count);
            foreach (var (a, b) in other.Ghosts.Zip(r.Ghosts)) Assert.True(Vector3.Distance(a.Offset, b.Offset) < 0.01f, $"{a.Name}: {a.Offset} vs {b.Offset}");   // offsets: one expansion serves a moving cursor
        }

        [Fact]
        public void AFailure_IsTheExpandersOwnWords_NothingIsDrawn()
        {
            var r = MacroPreview.Of(Expander(), "no/such/macro", "{}", At(0, 0));
            Assert.False(r.Ok);
            Assert.Contains("no/such/macro", r.Failure);
            Assert.Empty(r.Ghosts);
            Assert.Equal("this host cannot expand macros", MacroPreview.Of(null, "test/fire-team", "{}", At(0, 0)).Failure);
        }

        [Fact]
        public void AnInsertMacroStep_ResolvesItsAnchor_AFixedPoint_OrAKey_OrSaysItDoesNot()
        {
            var ex = Expander();
            using var world = new EntityRepository();
            HrotSharedComponentRegistry.RegisterAll(world);
            var e = world.CreateEntity();
            world.AddComponent(e, new SimTransform { Position = new Vector3(300, 40, 0), Rotation = Quaternion.Identity });
            world.AddComponent(e, new RefKey("QrfFrom"));
            world.AddComponent(e, new EntityGuid(Guid.NewGuid()));
            world.AddComponent(e, new Fdp.Toolkit.Replication.Components.NetworkIdentity(77));
            var map = new Fdp.Toolkit.Replication.Services.NetworkEntityMap(); map.Register(77, e); world.SetSingletonManaged(map);
            var fixedAt = MacroPreview.OfStep(ex, world, new JsonObject { ["macro"] = "test/fire-team", ["at"] = new JsonArray(10, 20, 0), ["heading"] = 90 });
            Assert.True(fixedAt.Result.Ok, fixedAt.Result.Failure);
            Assert.Equal(new Vector3(10, 20, 0), fixedAt.Anchor);
            Assert.Equal(90f, fixedAt.HeadingDeg);

            var byKey = MacroPreview.OfStep(ex, world, new JsonObject { ["macro"] = "test/fire-team", ["at"] = "@QrfFrom" });
            Assert.True(byKey.Result.Ok, byKey.Result.Failure);
            Assert.Equal(new Vector3(300, 40, 0), byKey.Anchor);

            var none = MacroPreview.OfStep(ex, world, new JsonObject { ["macro"] = "test/fire-team", ["at"] = "@Nowhere" });
            Assert.False(none.Result.Ok);
            Assert.Contains("Nowhere", none.Result.Failure);
            Assert.False(MacroPreview.OfStep(ex, world, new JsonObject { ["macro"] = "test/fire-team" }).Result.Ok);   // no place at all
        }
    }
}
