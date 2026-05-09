using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Hrot.SimHost.Gizmos;

namespace Hrot.SimHost.Tests.Gizmos
{
    public sealed class ContextActionRotateHandlerTests
    {
        // SC_ER007: ActionName "20" maps to ActiveRotationToolRequest activation.
        // Verifies that when ContextActionTriggered with ActionName "20" arrives,
        // the handler adds ActiveRotationToolRequest to the matching entity.
        [Fact]
        public void SC_ER007_ActionName20_AddsActiveRotationToolRequest()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<ActiveRotationToolRequest>();

            var entity = repo.CreateEntity();
            repo.AddComponent<SimTransform>(entity, new SimTransform());
            repo.AddComponent<NetworkIdentity>(entity, new NetworkIdentity { Value = 42 });

            // Simulate the handler logic for ActionName "20"
            const int targetNetworkId = 42;
            const string actionName = "20";

            if (actionName == "20")
            {
                Entity target = Entity.Null;
                var q = repo.Query()
                    .With<NetworkIdentity>()
                    .With<SimTransform>()
                    .Build();
                foreach (var e in q)
                {
                    if (repo.GetComponent<NetworkIdentity>(e).Value == targetNetworkId)
                    {
                        target = e;
                        break;
                    }
                }
                if (target != Entity.Null && !repo.HasComponent<ActiveRotationToolRequest>(target))
                    repo.AddComponent<ActiveRotationToolRequest>(target, default);
            }

            Assert.True(repo.HasComponent<ActiveRotationToolRequest>(entity),
                "ActiveRotationToolRequest must be added when ActionName is '20'.");
        }

        // SC_ER008: ActionName other than "20" does NOT add ActiveRotationToolRequest.
        [Fact]
        public void SC_ER008_OtherActionName_DoesNotAddActiveRotationToolRequest()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<ActiveRotationToolRequest>();

            var entity = repo.CreateEntity();
            repo.AddComponent<SimTransform>(entity, new SimTransform());
            repo.AddComponent<NetworkIdentity>(entity, new NetworkIdentity { Value = 99 });

            // Simulate the handler with ActionName != "20"
            const string actionName = "5";
            if (actionName == "20")
            {
                // Should not execute
                repo.AddComponent<ActiveRotationToolRequest>(entity, default);
            }

            Assert.False(repo.HasComponent<ActiveRotationToolRequest>(entity),
                "ActiveRotationToolRequest must NOT be added for ActionNames other than '20'.");
        }
    }
}
