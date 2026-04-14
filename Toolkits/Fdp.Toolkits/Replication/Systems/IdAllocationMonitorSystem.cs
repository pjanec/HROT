using System;
using System.Diagnostics;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Messages;
using FDP.Toolkit.Replication.Services;

namespace FDP.Toolkit.Replication.Systems
{
    public class IdAllocationMonitorSystem : ComponentSystem
    {
        private BlockIdManager? _manager;
        private string _clientId = string.Empty;

        // Default to a random client ID if not configured.
        private string GetClientId()
        {
            // For now, stable random per process
            return "Node_" + Process.GetCurrentProcess().Id;
        }

        protected override void OnCreate()
        {
            _clientId = GetClientId();
            
            // Try to resolve manager immediately (if available)
            if (World.HasSingletonManaged<BlockIdManager>())
            {
                _manager = World.GetSingletonManaged<BlockIdManager>();
            }
        }

        protected override void OnUpdate()
        {
            // 1. Maintain connection to Manager
            if (_manager == null && World.HasSingletonManaged<BlockIdManager>())
            {
                _manager = World.GetSingletonManaged<BlockIdManager>();
                if (_manager != null)
                {
                    _manager.OnLowWaterMark += HandleLowWaterMark;
                }
            }
            
            // 2. Consume Network Responses
            if (World.Bus.HasManagedEvent<IdBlockResponse>())
            {
                var responses = World.Bus.ConsumeManaged<IdBlockResponse>();
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

        protected override void OnDestroy()
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
            
            World.Bus.PublishManaged(req);
        }
    }
}
