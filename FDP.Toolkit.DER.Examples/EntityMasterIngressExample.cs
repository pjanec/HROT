using System;
using System.Threading;
using System.Threading.Tasks;
using Bagira.BDC.SSTD;
using CycloneDDS.Runtime;
using CycloneDDS.Runtime.Interop;
using FDP.Toolkit.DER;

namespace FDP.Toolkit.DER.Examples
{
    /// <summary>
    /// Example of a simplified "Ingress" component that reads DDS EntityMaster
    /// samples and updates a local DerRepo.
    /// This demonstrates the intended usage pattern for bridging DDS data to the application.
    /// </summary>
    public class EntityMasterIngressExample
    {
        private readonly IDerRepo _repo;
        private readonly DdsParticipant _participant;
        private readonly DdsReader<EntityMaster> _reader;
        private readonly CancellationTokenSource _cts;
        private Task _ingressTask;

        // We need to map DDS InstanceHandles to our entity IDs because
        // when a sample is disposed (NotAlive), we might not get the data key depending on binding.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<long, int> _handleToIdMap 
            = new System.Collections.Concurrent.ConcurrentDictionary<long, int>();

        public EntityMasterIngressExample()
        {
            // 1. Create the repository
            _repo = new DerRepo();

            // 2. Initialize DDS
            _participant = new DdsParticipant(0);

            // 3. Create a reader for EntityMaster
            // No need for explicit topic creation if using simple constructor, but checking existing code.
            _reader = new DdsReader<EntityMaster>(_participant, "EntityMaster");

            _cts = new CancellationTokenSource();
        }


        public void Start()
        {
            Console.WriteLine("Starting Ingress...");
            _ingressTask = Task.Run(() => IngressLoop(_cts.Token));
        }

        public void Stop()
        {
            Console.WriteLine("Stopping Ingress...");
            _cts.Cancel();
            try
            {
                _ingressTask.Wait(2000);
            }
            catch (AggregateException) { }
        }

        private void IngressLoop(CancellationToken token)
        {
            // We use a waitset or polling. The binding has a simple blocking Take if we want,
            // or we can just poll with a sleep.
            // Let's poll for simplicity in this example.

            while (!token.IsCancellationRequested)
            {
                try
                {
                    // Take available samples
                    using (var loan = _reader.Take(10))
                    {
                        if (loan.Length > 0)
                        {
                            foreach (var sample in loan)
                            {
                                ProcessSample(sample);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in ingress loop: {ex.Message}");
                }

                // Sleep a bit to avoid busy waiting
                Thread.Sleep(5);
            }
        }

        private void ProcessSample(DdsSample<EntityMaster> sample)
        {
            // EntityMaster uses int EntityId as key.
            long handle = sample.Info.InstanceHandle;

            if (sample.IsValid)
            {
                // ALIVE
                var data = sample.Data;
                int entityId = data.EntityId;
                
                // Update our handle map
                _handleToIdMap[handle] = entityId;

                // Create or update entity
                var existing = _repo.GetEntity(entityId);
                if (existing == null)
                {
                    _repo.CreateEntity(entityId, data.TkbType);
                    Console.WriteLine($"[NEW] Entity {entityId} (Type: {data.TkbType})");
                }
                else
                {
                    // Update logic (e.g. check for changes)
                    // For now just log
                    // Console.WriteLine($"[UPD] Entity {entityId}");
                }
            }
            else
            {
                // NOT ALIVE (Disposed or NoWriters)
                if (sample.Info.InstanceState == DdsInstanceState.NotAliveDisposed)
                {
                    if (_handleToIdMap.TryRemove(handle, out int entityId))
                    {
                        _repo.DeleteEntity(entityId);
                        Console.WriteLine($"[DEL] Entity {entityId}");
                    }
                }
            }
        }

        public void PrintRepoStatus()
        {
            var all = _repo.GetAllEntities().ToList();
            Console.WriteLine($"Repo contains {all.Count} entities.");
        }
    }
}
