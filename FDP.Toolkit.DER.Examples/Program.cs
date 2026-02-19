using System;
using System.Threading.Tasks;
using Bagira.BDC.SSTD;
using CycloneDDS.Runtime;

namespace FDP.Toolkit.DER.Examples
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("========================================");
            Console.WriteLine("   FDP.Toolkit.DER Example Application");
            Console.WriteLine("========================================");

            // 1. Initialize the Ingress component (Reader)
            var ingress = new EntityMasterIngressExample();
            ingress.Start();

            // 2. Start a background task to simulate DDS traffic (Writer)
            var trafficTask = Task.Run(async () => await SimulateTraffic());

            Console.WriteLine("Press ENTER to stop...");
            Console.ReadLine();

            // 3. Shutdown
            ingress.Stop();
            
            // Wait for traffic to finish if we wanted, but here we just exit.
            // Ideally should signal cancellation to traffic task too.
        }

        static async Task SimulateTraffic()
        {
            Console.WriteLine("Starting Traffic Simulator...");
            
            // Use domain 0 to match ingress
            using var participant = new DdsParticipant(0);
            using var writer = new DdsWriter<EntityMaster>(participant);

            var random = new Random();
            int baseId = 1000;

            // Scenario: Create 5 entities, update them, then delete 2.
            for (int i = 0; i < 5; i++)
            {
                var entity = new EntityMaster
                {
                    EntityId = baseId + i,
                    TkbType = 1,
                    DisType = 1,
                    Flags = 0
                };
                writer.Write(entity);
                Console.WriteLine($"[TRAFFIC] Wrote Entity {entity.EntityId}");
                await Task.Delay(500);
            }

            // Update one entity
            {
                var entity = new EntityMaster
                {
                    EntityId = baseId + 2,
                    TkbType = 2, // Changed type
                    DisType = 1,
                    Flags = 1
                };
                writer.Write(entity);
                Console.WriteLine($"[TRAFFIC] Updated Entity {entity.EntityId}");
                await Task.Delay(1000);
            }

            // Delete two entities
            {
                 var entity1 = new EntityMaster { EntityId = baseId + 0 };
                 writer.DisposeInstance(entity1);
                 Console.WriteLine($"[TRAFFIC] Disposed Entity {entity1.EntityId}");

                 await Task.Delay(500);

                 var entity2 = new EntityMaster { EntityId = baseId + 4 };
                 writer.DisposeInstance(entity2);
                 Console.WriteLine($"[TRAFFIC] Disposed Entity {entity2.EntityId}");
            }
            
            Console.WriteLine("Traffic Simulation Finished Cycle.");
        }
    }
}
