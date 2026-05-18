using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System;
using System.Linq;

namespace Fbt.Serialization
{
    public static class BinaryTreeSerializer
    {
        private static readonly byte[] MagicBytes = { (byte)'F', (byte)'B', (byte)'T', 0 };
        private const int CurrentVersion = 1;
        
        public static void Save(BehaviorTreeBlob blob, string filePath)
        {
            using var fs = File.Create(filePath);
            Save(blob, fs);
        }

        public static void Save(BehaviorTreeBlob blob, Stream stream)
        {
             using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            
            // Header
            writer.Write(MagicBytes);
            writer.Write(CurrentVersion);
            writer.Write(blob.StructureHash);
            writer.Write(blob.ParamHash);
            
            // We should arguably also save TreeName here, even if not strictly in strict binary layout given in tasks
            // task 4.1 in design explicitly lists TreeName
            writer.Write(blob.TreeName ?? "");

            // Nodes
            writer.Write(blob.Nodes.Length);
            foreach (var node in blob.Nodes)
            {
                writer.Write((byte)node.Type);
                writer.Write(node.ChildCount);
                writer.Write(node.SubtreeOffset);
                writer.Write(node.PayloadIndex);
            }
            
            // Method names
            writer.Write(blob.MethodNames?.Length ?? 0);
            if (blob.MethodNames != null)
                foreach (var name in blob.MethodNames)
                    writer.Write(name);
            
            // Float params
            writer.Write(blob.FloatParams?.Length ?? 0);
            if (blob.FloatParams != null)
                foreach (var f in blob.FloatParams)
                    writer.Write(f);
            
            // Int params
            writer.Write(blob.IntParams?.Length ?? 0);
            if (blob.IntParams != null)
                foreach (var i in blob.IntParams)
                    writer.Write(i);
        }
        
        public static BehaviorTreeBlob Load(string filePath)
        {
            using var fs = File.OpenRead(filePath);
            return Load(fs);
        }

        public static BehaviorTreeBlob Load(Stream stream)
        {
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            
            // Validate header
            var magic = reader.ReadBytes(4);
            if (!magic.SequenceEqual(MagicBytes))
                throw new InvalidDataException("Invalid magic bytes");
            
            var version = reader.ReadInt32();
            if (version != CurrentVersion)
                throw new InvalidDataException($"Unsupported version: {version}");
            
            var blob = new BehaviorTreeBlob
            {
                StructureHash = reader.ReadInt32(),
                ParamHash = reader.ReadInt32(),
                TreeName = reader.ReadString()
            };
            blob.Version = version;
            
            // Read nodes
            int nodeCount = reader.ReadInt32();
            blob.Nodes = new NodeDefinition[nodeCount];
            for (int i = 0; i < nodeCount; i++)
            {
                blob.Nodes[i] = new NodeDefinition
                {
                    Type = (NodeType)reader.ReadByte(),
                    ChildCount = reader.ReadByte(),
                    SubtreeOffset = reader.ReadUInt16(),
                    PayloadIndex = reader.ReadInt32()
                };
            }
            
            // Read method names
            int methodCount = reader.ReadInt32();
            blob.MethodNames = new string[methodCount];
            for (int i = 0; i < methodCount; i++)
                blob.MethodNames[i] = reader.ReadString();
            
            // Read float params
            int floatCount = reader.ReadInt32();
            blob.FloatParams = new float[floatCount];
            for (int i = 0; i < floatCount; i++)
                blob.FloatParams[i] = reader.ReadSingle();
            
            // Read int params
            int intCount = reader.ReadInt32();
            blob.IntParams = new int[intCount];
            for (int i = 0; i < intCount; i++)
                blob.IntParams[i] = reader.ReadInt32();
            
            return blob;
        }
    }
}
