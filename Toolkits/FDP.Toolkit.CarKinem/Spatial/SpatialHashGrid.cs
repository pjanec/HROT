using System;
using System.Numerics;
using Fdp.Kernel;
using Fdp.Kernel.Collections;

namespace CarKinem.Spatial
{
    /// <summary>
    /// 2D spatial hash grid for fast neighbor queries.
    /// Hardcoded cell size: 5.0 meters.
    /// <para>
    /// <b>Incremental update support (BATCH-09):</b>
    /// <see cref="Remove"/> splices an entity out of its cell's linked-list chain and
    /// recycles the freed slot via an internal free-list.  <see cref="Add"/> preferentially
    /// pops a free-list slot before allocating a new one by incrementing
    /// <see cref="EntityCount"/>.  This makes per-entity relocations O(chain_length) for
    /// removal (typically O(1) in sparse cells) without requiring a full
    /// <see cref="Clear"/> + re-insert on every movement.
    /// </para>
    /// </summary>
    public struct SpatialHashGrid : IDisposable
    {
        public NativeArray<int> GridHead;        // Cell -> first entity slot index
        public NativeArray<int> GridNext;        // Entity slot index -> next slot
        public NativeArray<Entity> GridValues;   // Entity slot index -> Entity (full handle with generation)
        public NativeArray<Vector2> Positions;   // Entity slot index -> position

        /// <summary>
        /// Stack of recycled slot indices returned by <see cref="Remove"/>.
        /// <see cref="Add"/> pops from here before allocating a fresh slot via
        /// <see cref="EntityCount"/>++.  Capacity == maxEntities so it never overflows.
        /// </summary>
        public NativeArray<int> FreeList;

        /// <summary>Number of valid entries currently stored in <see cref="FreeList"/>.</summary>
        public int FreeListCount;
        
        public float CellSize;
        public int Width;
        public int Height;
        public int EntityCount;

        /// <summary>
        /// World-space origin of the grid (bottom-left corner).
        /// Allows the grid to cover negative world coordinates.
        /// </summary>
        public float OriginX;
        public float OriginY;
        
        /// <summary>
        /// Create grid with specified dimensions.
        /// </summary>
        public static SpatialHashGrid Create(int width, int height, float cellSize,
            int maxEntities, Allocator allocator, float originX = 0f, float originY = 0f)
        {
            return new SpatialHashGrid
            {
                GridHead = new NativeArray<int>(width * height, allocator),
                GridNext = new NativeArray<int>(maxEntities, allocator),
                GridValues = new NativeArray<Entity>(maxEntities, allocator),
                Positions = new NativeArray<Vector2>(maxEntities, allocator),
                FreeList = new NativeArray<int>(maxEntities, allocator),
                FreeListCount = 0,
                CellSize = cellSize,
                Width = width,
                Height = height,
                EntityCount = 0,
                OriginX = originX,
                OriginY = originY,
            };
        }
        
        /// <summary>
        /// Clear grid (reset all heads to -1) and reset the free-list.
        /// After a full clear all slot state is gone, so the free-list must also be reset
        /// to prevent stale indices from being reused on the next <see cref="Add"/>.
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < GridHead.Length; i++)
                GridHead[i] = -1;
            
            EntityCount = 0;
            FreeListCount = 0;
        }
        
        /// <summary>
        /// Add entity to grid. Stores the full <see cref="Entity"/> handle (Index + Generation)
        /// to preserve generational safety — never stores a raw index alone.
        /// <para>
        /// Preferentially reuses a recycled slot from the internal free-list (populated by
        /// <see cref="Remove"/>) before allocating a new slot by incrementing
        /// <see cref="EntityCount"/>.
        /// </para>
        /// </summary>
        public void Add(Entity entity, Vector2 position)
        {
            int cellX = (int)((position.X - OriginX) / CellSize);
            int cellY = (int)((position.Y - OriginY) / CellSize);

            if (cellX < 0 || cellX >= Width || cellY < 0 || cellY >= Height)
                return; // Out of bounds
            
            int cellIdx = cellY * Width + cellX;

            // Prefer a recycled slot; fall back to the high-water-mark counter.
            int entityIdx;
            if (FreeListCount > 0)
                entityIdx = FreeList[--FreeListCount];
            else
            {
                entityIdx = EntityCount++;
                if (entityIdx >= GridValues.Length)
                    return; // Exceeded max entities capacity
            }
            
            Positions[entityIdx] = position;
            GridValues[entityIdx] = entity;
            GridNext[entityIdx] = GridHead[cellIdx];
            GridHead[cellIdx] = entityIdx;
        }

        /// <summary>
        /// Removes <paramref name="entity"/> from the cell corresponding to
        /// <paramref name="previousPosition"/> and recycles its slot into the
        /// internal free-list so that <see cref="Add"/> can reuse it.
        /// <para>
        /// Complexity: O(k) where k is the number of entities in the same cell.
        /// For a well-tuned spatial hash k ≈ 1–3, so this is effectively O(1).
        /// </para>
        /// </summary>
        /// <returns>
        /// <c>true</c> if the entity was found and removed; <c>false</c> if the position
        /// maps out of bounds or the entity is not present in that cell.
        /// </returns>
        public bool Remove(Entity entity, Vector2 previousPosition)
        {
            int cellX = (int)((previousPosition.X - OriginX) / CellSize);
            int cellY = (int)((previousPosition.Y - OriginY) / CellSize);

            if (cellX < 0 || cellX >= Width || cellY < 0 || cellY >= Height)
                return false;

            int cellIdx = cellY * Width + cellX;

            int prev = -1;
            int curr = GridHead[cellIdx];
            while (curr >= 0)
            {
                if (GridValues[curr] == entity)
                {
                    // Splice out of linked list.
                    if (prev < 0)
                        GridHead[cellIdx] = GridNext[curr];
                    else
                        GridNext[prev] = GridNext[curr];

                    // Sentinel — mark the removed slot's next pointer as -1 so it
                    // is never mistakenly followed if a stale reference exists.
                    GridNext[curr] = -1;

                    // Recycle the slot for later use by Add().
                    if (FreeListCount < FreeList.Length)
                        FreeList[FreeListCount++] = curr;

                    return true;
                }
                prev = curr;
                curr = GridNext[curr];
            }
            return false; // Entity not in this cell.
        }
        
        /// <summary>
        /// Query neighbors within radius.
        /// Writes results to output array, returns count.
        /// Each result carries the full <see cref="Entity"/> handle (Index + Generation).
        /// </summary>
        public int QueryNeighbors(Vector2 position, float radius, 
            Span<(Entity entity, Vector2 pos)> output)
        {
            int count = 0;
            float radiusSq = radius * radius;
            
            // Get search bounds in grid space (subtract world origin to convert to cell space)
            int minCellX = (int)((position.X - radius - OriginX) / CellSize);
            int maxCellX = (int)((position.X + radius - OriginX) / CellSize);
            int minCellY = (int)((position.Y - radius - OriginY) / CellSize);
            int maxCellY = (int)((position.Y + radius - OriginY) / CellSize);
            
            // Clamp to grid bounds
            minCellX = Math.Max(0, minCellX);
            maxCellX = Math.Min(Width - 1, maxCellX);
            minCellY = Math.Max(0, minCellY);
            maxCellY = Math.Min(Height - 1, maxCellY);
            
            // Iterate cells
            for (int cy = minCellY; cy <= maxCellY; cy++)
            {
                for (int cx = minCellX; cx <= maxCellX; cx++)
                {
                    int cellIdx = cy * Width + cx;
                    int head = GridHead[cellIdx];
                    
                    // Iterate linked list
                    while (head >= 0)
                    {
                        Vector2 neighborPos = Positions[head];
                        float distSq = Vector2.DistanceSquared(position, neighborPos);
                        
                        if (distSq <= radiusSq)
                        {
                            if (count < output.Length)
                            {
                                output[count] = (GridValues[head], neighborPos); // GridValues[head] is Entity
                                count++;
                            }
                        }
                        
                        head = GridNext[head];
                    }
                }
            }
            
            return count;
        }
        
        public void Dispose()
        {
            if (GridHead.IsCreated) GridHead.Dispose();
            if (GridNext.IsCreated) GridNext.Dispose();
            if (GridValues.IsCreated) GridValues.Dispose();
            if (Positions.IsCreated) Positions.Dispose();
            if (FreeList.IsCreated) FreeList.Dispose();
        }
    }
}
