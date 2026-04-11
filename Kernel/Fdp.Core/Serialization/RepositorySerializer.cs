using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using MessagePack;

namespace Fdp.Kernel.Serialization
{
    /// <summary>
    /// Handles serialization of the EntityRepository to/from streams and files.
    /// Stage 20: Serialization
    /// </summary>
    /// <summary>
    /// Handles serialization of the EntityRepository using MessagePack.
    /// Stage 20: Serialization (Refined)
    /// </summary>
    public static class RepositorySerializer
    {
        private const int FORMAT_VERSION = 2; // Bumped version for MessagePack format

        /// <summary>
        /// Saves the repository state to a file.
        /// </summary>
        public static void SaveToFile(EntityRepository repo, string filePath)
        {
            if (repo == null) throw new ArgumentNullException(nameof(repo));
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentNullException(nameof(filePath));
            
            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            SaveToStream(repo, fs);
        }

        /// <summary>
        /// Loads the repository state from a file.
        /// Warning: Overwrites current state.
        /// </summary>
        public static void LoadFromFile(EntityRepository repo, string filePath)
        {
            if (repo == null) throw new ArgumentNullException(nameof(repo));
            if (!File.Exists(filePath)) throw new FileNotFoundException("Save file not found", filePath);
            
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            LoadFromStream(repo, fs);
        }

        /// <summary>
        /// Saves repository data to a stream using MessagePack.
        /// </summary>
        public static void SaveToStream(EntityRepository repo, Stream stream)
        {
            var root = new SaveFileRoot
            {
                FileVersion = FORMAT_VERSION,
                Entities = new List<EntitySaveData>(),
                ComponentBlobs = new Dictionary<string, byte[]>()
            };

            var options = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);

            // 1. Save Entities
            var index = repo.GetEntityIndex();
            // Important: We must save up to MaxIssuedIndex to preserve valid indices
            for(int i=0; i <= repo.MaxEntityIndex; i++)
            {
                ref var header = ref index.GetHeader(i);
                if(header.IsActive) 
                {
                    root.Entities.Add(new EntitySaveData 
                    { 
                        Id = i, 
                        Generation = (int)header.Generation, 
                        IsActive = true,
                        // Persist the DisType Value
                        DisType = header.DisType.Value
                    });
                }
            }
            
            // 2. Save Components
            // Iterate known registered types
            foreach(var type in ComponentTypeRegistry.GetAllTypes())
            {
                int typeId = ComponentTypeRegistry.GetId(type);
                if (!ComponentTypeRegistry.IsSaveable(typeId)) continue;

                if(repo.TryGetTable(type, out var table))
                {
                     // Use the Type Name as the persistent key
                     // Use AssemblyQualifiedName to be safe, or FullName if preferred for loose coupling
                     // User said "Component Type Name (string)" and "skip types it doesn't know". 
                     // FullName is brittle if assembly name changes but types are same namespace. 
                     // AssemblyQualifiedName is safest for strict typing.
                     // User snippet used `FullName`. I will use `FullName` BUT fallback logic in Load handles resolution.
                     // Let's use `FullName` to be "friendly".
                     root.ComponentBlobs[type.FullName!] = table.Serialize(repo, options);
                }
            }

            MessagePackSerializer.Serialize(stream, root, options);
        }

        /// <summary>
        /// Loads repository data from a stream using MessagePack.
        /// </summary>
        public static void LoadFromStream(EntityRepository repo, Stream stream)
        {
            var options = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);
            var root = MessagePackSerializer.Deserialize<SaveFileRoot>(stream, options);

            // 0. Clear existing state
            repo.Clear();

            // 1. Restore Entities (Headers)
            if (root.Entities != null)
            {
                foreach(var e in root.Entities)
                {
                    // We pass 'default' for mask because components will update it as they load
                    // We pass the persisted DisType (casted back to struct)
                    var disType = new DISEntityType { Value = e.DisType };
                    repo.RestoreEntity(e.Id, e.IsActive, e.Generation, default, disType);
                }
            }
            
            // Rebuild free list (finds gaps in restored entities)
            repo.RebuildFreeList();

            // 2. Restore Components
            if (root.ComponentBlobs != null)
            {
                foreach(var kvp in root.ComponentBlobs)
                {
                    string typeName = kvp.Key;
                    byte[] blob = kvp.Value;

                // Robust Type Resolution
                Type? type = Type.GetType(typeName);
                if (type == null)
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        type = asm.GetType(typeName);
                        if (type != null) break;
                    }
                }
                
                // Backward Compat: If type was deleted/renamed, type is null. We just SKIP it.
                if (type != null)
                {
                    // Check DataPolicy compliance (IsSaveable)
                    // We must register the type first to know its policy (unless we check attribute manually)
                    // But we can registers it, check policy, and if not saveable, just don't deserialize data?
                    // Actually, if it's in the file, we can probably load it. 
                    // But adhering to policy is safer.
                    
                    // Ensurance logic:
                    if (!repo.TryGetTable(type, out var table))
                    {
                         // Register it to create the table
                         // RegisterComponent<T>(DataPolicy? policyOverride = null)
                         var method = typeof(EntityRepository).GetMethod(nameof(EntityRepository.RegisterComponent))!.MakeGenericMethod(type);
                         method.Invoke(repo, new object?[] { null });
                         
                         repo.TryGetTable(type, out table);
                    }
                    
                    // Verify Saveable status
                    int typeId = ComponentTypeRegistry.GetId(type);
                    if (typeId >= 0 && !ComponentTypeRegistry.IsSaveable(typeId))
                    {
                        // Skip loading this component because it is now marked as NoSave
                        continue;
                    }
                    
                    if (table != null)
                    {
                        table.Deserialize(repo, blob, options);
                    }
                }
            }
            }
        }
    }

    [MessagePackObject]
    public class SaveFileRoot
    {
        [Key(0)] public int FileVersion;
        [Key(1)] public List<EntitySaveData>? Entities; 
        [Key(2)] public Dictionary<string, byte[]>? ComponentBlobs;
    }

    [MessagePackObject]
    public struct EntitySaveData
    {
        [Key(0)] public int Id;
        [Key(1)] public int Generation;
        [Key(2)] public bool IsActive; 
        [Key(3)] public ulong DisType; // Serialized as raw ulong logic value 
    }
}
