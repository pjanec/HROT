using System.Collections.Generic;
using Fdp.Kernel;
using Fdp.Examples.Showcase.Components;

namespace Fdp.Examples.Showcase.Systems
{
    public class SpatialMap
    {
        private readonly Dictionary<int, List<Entity>> _buckets = new();
        private const int CellSize = 10; // Bucket size (10x10 units)

        public void Clear()
        {
            foreach (var list in _buckets.Values) list.Clear();
        }

        public void Add(Entity entity, Position pos)
        {
            int key = GetKey(pos.X, pos.Y);
            if (!_buckets.TryGetValue(key, out var list))
            {
                list = new List<Entity>(32); // Pre-allocate
                _buckets[key] = list;
            }
            list.Add(entity);
        }

        public void Query(Position pos, float radius, List<Entity> results)
        {
            results.Clear();
            int minX = (int)(pos.X - radius) / CellSize;
            int maxX = (int)(pos.X + radius) / CellSize;
            int minY = (int)(pos.Y - radius) / CellSize;
            int maxY = (int)(pos.Y + radius) / CellSize;

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    int key = (x * 73856093) ^ (y * 19349663); // Simple spatial hash
                    if (_buckets.TryGetValue(key, out var list))
                    {
                        results.AddRange(list);
                    }
                }
            }
        }

        private int GetKey(float x, float y)
        {
            int cellX = (int)x / CellSize;
            int cellY = (int)y / CellSize;
            return (cellX * 73856093) ^ (cellY * 19349663);
        }
    }
}
