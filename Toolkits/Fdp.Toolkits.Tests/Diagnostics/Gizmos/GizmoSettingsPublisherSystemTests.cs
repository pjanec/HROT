using System.Text.Json;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Xunit;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Tests
{
    // ========================================================================
    // SC-GZ034: GizmoSettingsPublisherSystem emits StructEdit schema JSON
    // ========================================================================

    public class GizmoSettingsPublisherSystemTests
    {
        // SC-GZ034-1: Published EditDocumentJson has "structedit_version" key.
        [Fact]
        public void SC_GZ034_1_PublishedJson_HasStructEditVersionKey()
        {
            var reg = new GizmoSettingsRegistry();
            reg.RegisterSetting("HealthBar.Active", GizmoSettingValue.From(true));
            var publisher = new CapturingPublisher();
            var sys = new GizmoSettingsPublisherSystem(reg, publisher);
            using var repo = new EntityRepository();
            repo.RegisterEvent<GizmoSettingChangedEvent>();

            sys.Execute(repo, 0f);

            Assert.Single(publisher.Published);
            var json = publisher.Published[0].EditDocumentJson;
            var jdoc = JsonDocument.Parse(json);
            Assert.True(jdoc.RootElement.TryGetProperty("structedit_version", out _));
        }

        // SC-GZ034-2: Bool setting "HealthBar.Active"=true appears as Boolean node with value true.
        [Fact]
        public void SC_GZ034_2_BoolSetting_AppearsAsBooleanNode()
        {
            var reg = new GizmoSettingsRegistry();
            reg.RegisterSetting("HealthBar.Active", GizmoSettingValue.From(true));
            var publisher = new CapturingPublisher();
            var sys = new GizmoSettingsPublisherSystem(reg, publisher);
            using var repo = new EntityRepository();
            repo.RegisterEvent<GizmoSettingChangedEvent>();

            sys.Execute(repo, 0f);

            var json = publisher.Published[0].EditDocumentJson;
            var jdoc = JsonDocument.Parse(json);
            var nodes = jdoc.RootElement.GetProperty("nodes");
            bool found = false;
            foreach (var node in nodes.EnumerateArray())
            {
                if (node.GetProperty("path").GetString() == "HealthBar.Active")
                {
                    Assert.Equal("Boolean", node.GetProperty("kind").GetString());
                    Assert.True(node.GetProperty("value").GetBoolean());
                    found = true;
                    break;
                }
            }
            Assert.True(found, "Expected HealthBar.Active node in nodes array");
        }

        // SC-GZ034-3: Float32 setting "HealthBar.BarHeight"=3.5f appears as Scalar node with value 3.5.
        [Fact]
        public void SC_GZ034_3_FloatSetting_AppearsAsScalarNode()
        {
            var reg = new GizmoSettingsRegistry();
            reg.RegisterSetting("HealthBar.BarHeight", GizmoSettingValue.From(3.5f));
            var publisher = new CapturingPublisher();
            var sys = new GizmoSettingsPublisherSystem(reg, publisher);
            using var repo = new EntityRepository();
            repo.RegisterEvent<GizmoSettingChangedEvent>();

            sys.Execute(repo, 0f);

            var json = publisher.Published[0].EditDocumentJson;
            var jdoc = JsonDocument.Parse(json);
            var nodes = jdoc.RootElement.GetProperty("nodes");
            bool found = false;
            foreach (var node in nodes.EnumerateArray())
            {
                if (node.GetProperty("path").GetString() == "HealthBar.BarHeight")
                {
                    Assert.Equal("Scalar", node.GetProperty("kind").GetString());
                    Assert.Equal(3.5f, (float)node.GetProperty("value").GetDouble(), precision: 4);
                    found = true;
                    break;
                }
            }
            Assert.True(found, "Expected HealthBar.BarHeight node in nodes array");
        }
    }
}
