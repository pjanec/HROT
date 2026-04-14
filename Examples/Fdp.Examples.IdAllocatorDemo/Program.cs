using System;
using System.Threading;
using System.Threading.Tasks;
using CycloneDDS.Runtime;
using Fdp.ModuleHost.Network.Cyclone.Services;

namespace Fdp.Examples.IdAllocatorDemo
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("========================================");
            Console.WriteLine("  FDP IdAllocator Server                ");
            Console.WriteLine("========================================");

            // Using standard domain ID (0) or configurable
            uint domainId = 0;
            if (args.Length > 0 && uint.TryParse(args[0], out uint d))
            {
                domainId = d;
            }
            
            Console.WriteLine($"Starting DDS on Domain {domainId}...");
            
            try
            {
                using var participant = new DdsParticipant(domainId);
                using var server = new DdsIdAllocatorServer(participant);
                
                Console.WriteLine("Server running. Press Ctrl+C to exit.");
                
                var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (s, e) =>
                {
                    e.Cancel = true;
                    cts.Cancel();
                };

                while (!cts.Token.IsCancellationRequested)
                {
                    server.ProcessRequests();
                    await Task.Delay(10);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
            
            Console.WriteLine("Server stopped.");
        }
    }
}
