using BenchmarkDotNet.Attributes;
using Fbt.Serialization;
using System.IO;

namespace Fbt.Benchmarks
{
    [MemoryDiagnoser]
    [ShortRunJob]
    public class SerializationBenchmarks
    {
        private string _simpleJson = string.Empty;
        private string _complexJson = string.Empty;
        private BehaviorTreeBlob _blob = null!;
        private string _tempPath = string.Empty;
        
        [GlobalSetup]
        public void Setup()
        {
            _simpleJson = @"{
                ""TreeName"": ""Simple"",
                ""Root"": {
                    ""Type"": ""Sequence"",
                    ""Children"": [
                        { ""Type"": ""Action"", ""Action"": ""DoNothing"" },
                        { ""Type"": ""Action"", ""Action"": ""DoNothing"" }
                    ]
                }
            }";
            
            _complexJson = @"{
                ""TreeName"": ""Complex"",
                ""Root"": {
                    ""Type"": ""Selector"",
                    ""Children"": [
                        {
                            ""Type"": ""Sequence"",
                            ""Children"": [
                                { ""Type"": ""Condition"", ""Action"": ""AlwaysFalse"" },
                                { ""Type"": ""Action"", ""Action"": ""DoNothing"" }
                            ]
                        },
                        {
                            ""Type"": ""Sequence"",
                            ""Children"": [
                                { ""Type"": ""Action"", ""Action"": ""DoNothing"" },
                                { ""Type"": ""Action"", ""Action"": ""DoNothing"" },
                                { ""Type"": ""Action"", ""Action"": ""DoNothing"" },
                                {
                                    ""Type"": ""Parallel"",
                                    ""Policy"": 0,
                                    ""Children"": [
                                         { ""Type"": ""Action"", ""Action"": ""DoNothing"" },
                                         { ""Type"": ""Action"", ""Action"": ""DoNothing"" }
                                    ]
                                }
                            ]
                        }
                    ]
                }
            }";
            
            _blob = TreeCompiler.CompileFromJson(_complexJson);
            _tempPath = Path.GetTempFileName();
            
            // Ensure file exists for LoadBinary
            BinaryTreeSerializer.Save(_blob, _tempPath);
        }
        
        [Benchmark]
        public BehaviorTreeBlob CompileSimpleTree()
        {
            return TreeCompiler.CompileFromJson(_simpleJson);
        }
        
        [Benchmark]
        public BehaviorTreeBlob CompileComplexTree()
        {
            return TreeCompiler.CompileFromJson(_complexJson);
        }
        
        [Benchmark]
        public void SaveBinary()
        {
            BinaryTreeSerializer.Save(_blob, _tempPath);
        }
        
        [Benchmark]
        public BehaviorTreeBlob LoadBinary()
        {
            return BinaryTreeSerializer.Load(_tempPath);
        }
        
        [GlobalCleanup]
        public void Cleanup()
        {
            if (File.Exists(_tempPath))
                File.Delete(_tempPath);
        }
    }
}
