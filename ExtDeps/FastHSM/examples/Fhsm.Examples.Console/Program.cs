using System;
using System.IO;
using System.Numerics;

namespace Fhsm.Examples.Console
{
    class Program
    {
        static void Main(string[] args)
        {
            System.Console.WriteLine("FastHSM Examples\n");
            
            TrafficLightExample.Run();
            
            //System.Console.WriteLine("\nPress any key to exit...");
            //System.Console.ReadKey();
        }
    }
}
