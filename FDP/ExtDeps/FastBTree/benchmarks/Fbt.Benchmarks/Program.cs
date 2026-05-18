using BenchmarkDotNet.Running;

namespace Fbt.Benchmarks
{
    class Program
    {
        static void Main(string[] args)
        {
            var summary1 = BenchmarkRunner.Run<InterpreterBenchmarks>();
            var summary2 = BenchmarkRunner.Run<SerializationBenchmarks>();
        }
    }
}
