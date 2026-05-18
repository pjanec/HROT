using System;
using System.Diagnostics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Messages;
using Fdp.Toolkit.Replication.Services;

namespace Fdp.Toolkit.Replication.Systems
{
    public class IdAllocationMonitorSystem : IEcsModuleSystem, IDisposable
    {
        private BlockIdManager? _manager;
        private string _clientId = string.Empty;
        private EntityRepository? _repo;

        // Default to a random client ID if not configured.
        private string GetClientId()
        {
            // For now, stable random per process
            return "Node_" + Process.GetCurrentProcess().Id;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            var repo = (EntityRepository)view;

            // Lazy init on first Execute (replaces OnCreate)
            if (_clientId == string.Empty)
            {
                _clientId = GetClientId();
                _repo = repo;

                // Try to resolve manager immediately (if available)
                if (repo.HasSingletonManaged<BlockIdManager>())
                {
                    _manager = repo.GetSingletonManaged<BlockIdManager>();
                }
            }
            else
            {
                _repo = repo;
            }

            // 1. Maintain connection to Manager
            if (_manager == null && repo.HasSingletonManaged<BlockIdManager>())
            {
                _manager = repo.GetSingletonManaged<BlockIdManager>();
                if (_manager != null)
                {
                    _manager.OnLowWaterMark += HandleLowWaterMark;
                }
            }
            
            // 2. Consume Network Responses
            if (repo.Bus.HasManagedEvent<IdBlockResponse>())
            {
                var responses = view.ReadManagedEvents<IdBlockResponse>();
                foreach (var resp in responses)
                {
                    if (resp.ClientId == _clientId)
                    {
                        if (_manager != null)
                        {
                            _manager.AddBlock(resp.StartId, resp.Count);
                        }
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_manager != null)
            {
                _manager.OnLowWaterMark -= HandleLowWaterMark;
            }
        }

        private void HandleLowWaterMark()
        {
            // Publish Request
            var req = new IdBlockRequest 
            { 
                ClientId = _clientId, 
                RequestSize = 100 
            };
            
            _repo?.Bus.PublishManaged(req);
        }
    }
}
