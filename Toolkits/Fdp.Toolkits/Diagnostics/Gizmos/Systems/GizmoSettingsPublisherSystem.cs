using System.Text.Json;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Systems
{
    // Watches the GizmoSettingsRegistry for changes and publishes a GizmoUiState
    // DDS message whenever settings are modified, so remote UIs stay in sync.
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public sealed class GizmoSettingsPublisherSystem : IEcsModuleSystem
    {
        private readonly GizmoSettingsRegistry _registry;
        private readonly IGizmoUiStatePublisher? _publisher; // null = local-only, system is no-op
        private bool _firstFrame = true;

        public GizmoSettingsPublisherSystem(GizmoSettingsRegistry registry, IGizmoUiStatePublisher? publisher = null)
        {
            _registry = registry;
            _publisher = publisher;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (_publisher == null) return;

            bool hasEvent = false;
            foreach (var _ in view.ReadEvents<GizmoSettingChangedEvent>())
            {
                hasEvent = true;
                break;
            }

            if (!_firstFrame && !_registry.IsDirty && !hasEvent) return;

            _firstFrame = false;

            // Build JSON of all settings (key -> active value).
            using var ms = new System.IO.MemoryStream();
            using (var writer = new Utf8JsonWriter(ms))
            {
                writer.WriteStartObject();
                foreach (var (key, active, _) in _registry.EnumerateAll())
                {
                    writer.WritePropertyName(key);
                    WriteSettingValue(writer, active);
                }
                writer.WriteEndObject();
            }
            string json = System.Text.Encoding.UTF8.GetString(ms.ToArray());

            _publisher.Publish(new GizmoUiState { GizmoInstanceId = 0, EditDocumentJson = json });
            _registry.ClearDirty();
        }

        private static void WriteSettingValue(Utf8JsonWriter w, GizmoSettingValue v)
        {
            switch (v.Type)
            {
                case SettingType.Bool:    w.WriteBooleanValue(v.BoolValue);  break;
                case SettingType.Int32:   w.WriteNumberValue(v.IntValue);    break;
                case SettingType.Float32: w.WriteNumberValue(v.FloatValue);  break;
                default:                  w.WriteNullValue();                break;
            }
        }
    }
}
