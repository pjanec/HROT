using System;
using System.Collections.Concurrent;
using CycloneDDS.Runtime;
using FDP.Kernel.Logging;

namespace FDP.Toolkit.DER
{
    /// <summary>
    /// Base abstraction for bridging any DDS Topic into the DER Repo.
    /// </summary>
    public interface IIngressHandler
    {
        void Poll();
    }

    /// <summary>
    /// Generic Handler for EntityMaster topics (or equivalent), 
    /// as it uniquely controls the lifecycle (Create/Delete of entities in DerRepo).
    /// </summary>
    public class MasterIngressHandler<T> : IIngressHandler where T : new()
    {
        private readonly DdsReader<T> _reader;
        private readonly IDerRepo _repo;
        private readonly Func<T, int> _getEntityId;
        private readonly Func<T, long> _getTkbType;
        private readonly ConcurrentDictionary<long, int> _handleMap = new();
        private readonly long _localNodeId;

        public MasterIngressHandler(
            DdsParticipant participant, 
            IDerRepo repo, 
            string topicName, 
            Func<T, int> getEntityId,
            Func<T, long> getTkbType,
            long localNodeId = 0)
        {
            _reader = new DdsReader<T>(participant);
            _repo = repo;
            _getEntityId = getEntityId;
            _getTkbType = getTkbType;
            _localNodeId = localNodeId;
        }

        public void Poll()
        {
            using var loan = _reader.Take(10);
            foreach (var sample in loan)
            {
                long handle = sample.Info.InstanceHandle;

                // NOTE: dispose notifications can arrive with IsValid == true when the dispose
                // message carries key data. Must check instance state BEFORE IsValid so that
                // entity teardown is never silently swallowed by the alive-update branch.
                // Also covers NotAliveNoWriters (remote node crash / disconnect).
                if (sample.Info.InstanceState != DdsInstanceState.Alive)
                {
                    if (_handleMap.TryRemove(handle, out int id))
                    {
                        _repo.DeleteEntity(id);
                    }
                    continue;
                }

                if (sample.IsValid)
                {
                    int id = _getEntityId(sample.Data);
                    FdpLog<MasterIngressHandler<T>>.Debug(
                        "[Node-{0}] DER: Received EntityMaster for NetID {1}. Storing in Repo.", _localNodeId, id);
                    _handleMap[handle] = id;

                    var entity = _repo.GetEntity(id) ?? _repo.CreateEntity(id, _getTkbType(sample.Data));
                    entity.SetDescriptor(sample.Data);
                }
            }
        }
    }

    /// <summary>
    /// Generic Handler for all other Descriptors.
    /// Attaches data safely, resolving the structural key and multi-part id through fast lambdas.
    /// </summary>
    public class DescriptorIngressHandler<T> : IIngressHandler where T : new()
    {
        private readonly DdsReader<T> _reader;
        private readonly IDerRepo _repo;
        private readonly Func<T, int> _getEntityId;
        private readonly Func<T, int> _getPartId;

        public DescriptorIngressHandler(
            DdsParticipant participant, 
            IDerRepo repo, 
            string topicName, 
            Func<T, int> getEntityId, 
            Func<T, int>? getPartId = null)
        {
            _reader = new DdsReader<T>(participant);
            _repo = repo;
            _getEntityId = getEntityId;
            _getPartId = getPartId ?? (_ => 0); // Defaults to PartId 0
        }

        public void Poll()
        {
            using var loan = _reader.Take(10);
            foreach (var sample in loan)
            {
                if (sample.IsValid)
                {
                    int entityId = _getEntityId(sample.Data);
                    var entity = _repo.GetEntity(entityId);
                    
                    // Note: In strict designs, descriptors without an active Master might be dropped 
                    // or queued. Here we assume Master lifecycle arrives first.
                    if (entity != null)
                    {
                        int partId = _getPartId(sample.Data);
                        entity.SetDescriptor(sample.Data, partId);
                    }
                }
            }
        }
    }
}
