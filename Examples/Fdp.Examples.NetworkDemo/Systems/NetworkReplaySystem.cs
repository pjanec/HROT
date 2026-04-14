using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Kernel.Logging; // Added
using Fdp.ModuleHost_Core.Abstractions;
using Fdp.ModuleHost_Core.Network; 
using Fdp.Network.Cyclone.Abstractions;

namespace Fdp.Examples.NetworkDemo.Systems
{
    public class NetworkReplaySystem : IEcsModuleSystem
    {
        private readonly Dictionary<long, INetworkReplayTarget> _replayTargets;
        private readonly HashSet<long> _nonReplayOrdinals;
        private readonly BinaryReader _fileReader; 
        private byte[] _scratchBuffer = new byte[65536];

        public NetworkReplaySystem(IEnumerable<Fdp.Interfaces.IDescriptorTranslator> translators, string filePath)
        {
            _replayTargets = new Dictionary<long, INetworkReplayTarget>();
            _nonReplayOrdinals = new HashSet<long>();

            foreach (var t in translators)
            {
                if (t is INetworkReplayTarget rt)
                {
                    _replayTargets[t.DescriptorOrdinal] = rt;
                }
                else
                {
                    _nonReplayOrdinals.Add(t.DescriptorOrdinal);
                }
            }
                                  
            _fileReader = new BinaryReader(File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read));
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (_fileReader.BaseStream.Position >= _fileReader.BaseStream.Length)
                return;

            var cmd = view.GetCommandBuffer();

            try 
            {
                // 1. Read Frame Header from File
                // Format: [Count] [Ordinal] [Length] [Bytes] ...
                // Note: The replay format must match what was recorded. 
                // Assuming the format described in instruction:
                int messageCount = _fileReader.ReadInt32();

                for (int i = 0; i < messageCount; i++)
                {
                    long ordinal = _fileReader.ReadInt64();
                    int length = _fileReader.ReadInt32();
                    
                    if (_replayTargets.TryGetValue(ordinal, out var target))
                    {
                        if (length > _scratchBuffer.Length)
                        {
                            Array.Resize(ref _scratchBuffer, Math.Max(length, _scratchBuffer.Length * 2));
                        }

                        int bytesRead = 0;
                        while (bytesRead < length)
                        {
                            int n = _fileReader.Read(_scratchBuffer, bytesRead, length - bytesRead);
                            if (n == 0) break;
                            bytesRead += n;
                        }

                        var dataSpan = new ReadOnlySpan<byte>(_scratchBuffer, 0, length);
                        target.InjectReplayData(dataSpan, cmd, view);
                    }
                    else
                    {
                        // Skip bytes
                        _fileReader.BaseStream.Seek(length, SeekOrigin.Current);

                        if (_nonReplayOrdinals.Contains(ordinal))
                        {
                            FdpLog<NetworkReplaySystem>.Warn(
                                "Skipping replay data for translator ordinal {0} (Does not support INetworkReplayTarget)",
                                ordinal);
                        }
                        else
                        {
                            FdpLog<NetworkReplaySystem>.Warn(
                                "Skipping replay data for unknown ordinal {0}",
                                ordinal);
                        }
                    }
                }
            }
            catch (EndOfStreamException)
            {
                // Handled by length check usually, but safe guard
            }
        }
        
        // Should implement IDisposable to close stream, but IEcsModuleSystem doesn't enforce it.
    }
}
