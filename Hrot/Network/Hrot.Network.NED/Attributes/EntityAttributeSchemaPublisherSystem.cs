using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using Fdp.Toolkit.Replication.Patching;
using Hrot.NED.Messages;

namespace Hrot.Network.NED.Attributes
{
    /// <summary>
    /// Publishes the SimHost entity attribute schema as a JSON document over the
    /// <see cref="EntityAttributeSchema"/> DDS topic once at startup.
    ///
    /// Only the default-processor node publishes, preventing broadcast storms in
    /// multi-node SimHost clusters. Subsequent <see cref="Execute"/> calls after the
    /// first successful publish are no-ops gated by the <c>_published</c> flag.
    /// </summary>
    [UpdateInPhase(SystemPhase.BeforeSync)]
    public sealed class EntityAttributeSchemaPublisherSystem : IEcsModuleSystem
    {
        private readonly int                                    _nodeId;
        private readonly JsonAttributeCompiler?                 _compiler;
        private readonly IDdsWriter<EntityAttributeSchema>?     _writer;
        private readonly bool                                   _isDefaultProcessor;
        private bool                                            _published;

        public EntityAttributeSchemaPublisherSystem(
            int nodeId,
            JsonAttributeCompiler? compiler,
            IDdsWriter<EntityAttributeSchema>? writer,
            bool isDefaultProcessor)
        {
            _nodeId             = nodeId;
            _compiler           = compiler;
            _writer             = writer;
            _isDefaultProcessor = isDefaultProcessor;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            // Only the default processor publishes; prevents broadcast storm in multi-node clusters.
            if (!_isDefaultProcessor || _published || _compiler == null || _writer == null)
                return;

            string schemaJson = _compiler.ExportSchema();
            _writer.Write(new EntityAttributeSchema { NodeId = _nodeId, SchemaJson = schemaJson });
            _published = true;
        }
    }
}
