using System.Collections.Generic;
using System.Text.Json;
using CycloneDDS.Schema;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Xunit;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Tests
{
    // ========================================================================
    // Helper: capturing publisher for GizmoUiState
    // ========================================================================

    internal sealed class CapturingPublisher : IGizmoUiStatePublisher
    {
        public readonly List<GizmoUiState> Published = new();
        public void Publish(GizmoUiState state) => Published.Add(state);
    }

    // ========================================================================
    // SC-GZ016: DebugPrimitivesBatch DDS topic
    // ========================================================================

    public class DebugPrimitivesBatchTopicTests
    {
        [Fact]
        public void SC_GZ016_1_DebugPrimitivesBatch_HasCorrectTopicName()
        {
            var attr = (DdsTopicAttribute?)System.Attribute.GetCustomAttribute(
                typeof(DebugPrimitivesBatch), typeof(DdsTopicAttribute));
            Assert.NotNull(attr);
            Assert.Equal("DebugPrimitivesBatch", attr!.TopicName);
        }
    }

    // ========================================================================
    // SC-GZ017: GizmoUiState DDS topic and GizmoSettingsPublisherSystem
    // ========================================================================

    public class GizmoUiStateTopicTests
    {
        [Fact]
        public void SC_GZ017_1_GizmoUiState_HasCorrectTopicName()
        {
            var attr = (DdsTopicAttribute?)System.Attribute.GetCustomAttribute(
                typeof(GizmoUiState), typeof(DdsTopicAttribute));
            Assert.NotNull(attr);
            Assert.Equal("GizmoUiState", attr!.TopicName);
        }

        [Fact]
        public void SC_GZ017_2_System_PublishesOnFirstDirtyFrame()
        {
            // Arrange
            var reg = new GizmoSettingsRegistry();
            reg.RegisterSetting("TestSetting", GizmoSettingValue.From(false));
            var hash = GizmoSettingsRegistry.ComputeHash("TestSetting");
            reg.Write(hash, GizmoSettingValue.From(true));

            var publisher = new CapturingPublisher();
            var sys = new GizmoSettingsPublisherSystem(reg, publisher);

            using var repo = new EntityRepository();
            repo.RegisterEvent<GizmoSettingChangedEvent>();

            // Act
            sys.Execute(repo, 0f);

            // Assert
            Assert.Single(publisher.Published);
            Assert.Equal(0u, publisher.Published[0].GizmoInstanceId);
            Assert.NotEmpty(publisher.Published[0].EditDocumentJson);
        }

        [Fact]
        public void SC_GZ017_3_System_SkipsPublishOnCleanSecondFrame()
        {
            // Arrange: register and run once to exhaust the firstFrame trigger
            var reg = new GizmoSettingsRegistry();
            reg.RegisterSetting("K", GizmoSettingValue.From(1));

            var publisher = new CapturingPublisher();
            var sys = new GizmoSettingsPublisherSystem(reg, publisher);

            using var repo = new EntityRepository();
            repo.RegisterEvent<GizmoSettingChangedEvent>();

            // Warm-up: first frame always publishes
            sys.Execute(repo, 0f);
            Assert.Single(publisher.Published);
            publisher.Published.Clear();

            // Act: second frame with no changes
            sys.Execute(repo, 0f);

            // Assert: no publication on clean frame
            Assert.Empty(publisher.Published);
        }

        [Fact]
        public void SC_GZ017_4_GizmoUiState_FieldsRoundTrip()
        {
            var state = new GizmoUiState
            {
                GizmoInstanceId = 42,
                EditDocumentJson = "{}",
            };

            Assert.Equal(42u, state.GizmoInstanceId);
            Assert.Equal("{}", state.EditDocumentJson);
        }
    }
}
