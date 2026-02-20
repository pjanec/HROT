using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CycloneDDS.Runtime;
using CycloneDDS.Runtime.Interop;
using FDP.Toolkit.DER;
using System.Collections.Concurrent;
using System.Linq;

namespace FDP.Toolkit.DER.Examples
{
    /// <summary>
    /// Example of a generic "Ingress" component that dynamically reads ANY
    /// number of DDS descriptors and routes them to a local DerRepo.
    /// Demonstrates an elegant approach to scaling to 50+ descriptors.
    /// </summary>
    public class EntityMasterIngressExample
    {
        private readonly IDerRepo _repo;
        private readonly DdsParticipant _participant;
        private readonly CancellationTokenSource _cts;
        private Task? _ingressTask;

        // A list of generic handlers that poll and route DDS topics into the Repo
        private readonly List<IIngressHandler> _handlers = new();

        public EntityMasterIngressExample()
        {
            _repo = new DerRepo();
            _participant = new DdsParticipant(0);
            _cts = new CancellationTokenSource();

            // 1) Core Handler: EntityMaster (Handles Entity Creation/Deletion lifecycle)
            _handlers.Add(new MasterIngressHandler<LocalEntityMaster>(
                _participant, _repo, "LocalEntityMaster",
                data => data.EntityId,
                data => data.TkbType));

            // 2) Standard Single-Part Descriptor
            _handlers.Add(new DescriptorIngressHandler<LocalGeoSpatial>(
                _participant, _repo, "LocalGeoSpatial", 
                data => data.EntityId));

            // 3) Standard Multi-Part Descriptor
            _handlers.Add(new DescriptorIngressHandler<LocalMapEntitySymbol>(
                _participant, _repo, "LocalMapEntitySymbol", 
                data => data.EntityId, 
                data => data.MapGroupId));

            // ... Adding 50 more descriptors is simply 50 more isolated registrations.
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
            try { _ingressTask?.Wait(2000); } catch { }
        }

        private void IngressLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                foreach (var handler in _handlers)
                {
                    try
                    {
                        handler.Poll();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error in handler: {ex.Message}");
                    }
                }
                
                // Throttle polling across all registered topics
                Thread.Sleep(5);
            }
        }

        public void PrintRepoStatus()
        {
            var all = _repo.GetAllEntities().ToList();
            Console.WriteLine($"Repo contains {all.Count} entities.");
        }
    }
}
