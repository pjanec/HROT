using System.Collections.Generic;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using StructEdit.Core;
using StructEdit.Json;

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

            var doc = BuildEditDocument();
            string json = EditDocumentJsonSerializer.Serialize(doc);

            _publisher.Publish(new GizmoUiState { GizmoInstanceId = 0, EditDocumentJson = json });
            _registry.ClearDirty();
        }

        private EditDocument BuildEditDocument()
        {
            var leafNodes = new List<EditNode>();
            int nodeId = 1;
            foreach (var (key, active, _) in _registry.EnumerateAll())
            {
                EditNodeKind kind;
                IValueBinding binding;
                System.Type clrType;
                switch (active.Type)
                {
                    case SettingType.Bool:
                        kind = EditNodeKind.Boolean;
                        binding = new SnapshotValueBinding<bool>(active.BoolValue);
                        clrType = typeof(bool);
                        break;
                    case SettingType.Int32:
                        kind = EditNodeKind.Scalar;
                        binding = new SnapshotValueBinding<int>(active.IntValue);
                        clrType = typeof(int);
                        break;
                    case SettingType.Float32:
                        kind = EditNodeKind.Scalar;
                        binding = new SnapshotValueBinding<float>(active.FloatValue);
                        clrType = typeof(float);
                        break;
                    default:
                        continue; // skip unknown types
                }

                leafNodes.Add(new EditNode(
                    id:       new EditNodeId(nodeId++),
                    name:     key,
                    jsonPath: key,       // Use the setting key as both name and path
                    kind:     kind,
                    clrType:  clrType,
                    binding:  binding));
            }

            var root = new EditNode(
                id:       new EditNodeId(0),
                name:     "$",
                jsonPath: "$",
                kind:     EditNodeKind.SelectionRoot,
                clrType:  typeof(object),
                children: leafNodes);

            return new EditDocument(root, typeof(GizmoSettingValue), EditScope.WholeComponent);
        }

        private sealed class SnapshotValueBinding<T> : IValueBinding
        {
            private readonly T _value;
            public SnapshotValueBinding(T value) => _value = value;
            public System.Type ValueType => typeof(T);
            public object? GetBoxed() => _value;
            public void SetBoxed(object? value) { /* read-only snapshot, no-op */ }
            public bool TryGetSpan(out System.Span<byte> bytes) { bytes = default; return false; }
        }
    }
}
