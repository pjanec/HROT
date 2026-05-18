namespace GizmoMap.Network
{
    // Stateless transport adapter that reads GizmoInteractionBatch from DDS.
    // No ECS dependencies.
    public sealed class DdsGizmoInteractionSubscriber
    {
        private readonly IDdsReader<GizmoInteractionBatch> _reader;

        public DdsGizmoInteractionSubscriber(IDdsReader<GizmoInteractionBatch> reader)
        {
            _reader = reader ?? throw new System.ArgumentNullException(nameof(reader));
        }

        // Reads one pending GizmoInteractionBatch sample, or returns null if none available.
        public GizmoInteractionBatch? PollAndRead()
        {
            if (!_reader.TryRead(out var batch))
                return null;
            return batch;
        }
    }
}
