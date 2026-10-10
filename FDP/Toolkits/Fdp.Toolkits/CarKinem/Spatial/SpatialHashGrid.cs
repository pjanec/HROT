using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.Collections;

namespace CarKinem.Spatial
{
    /// <summary>
    /// 2D spatial hash grid for fast neighbor queries.
    /// Cell size and origin are set at <see cref="Create"/> and moved by <see cref="Rebase"/>.
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
        
        /// <summary>
        /// ⭐ CE-3018 — the grid's geometry <c>[cellSize, originX, originY]</c> in SHARED native memory, exactly like the
        /// cell arrays: every value copy of this struct (a system's field, the <c>SpatialGridData</c> singleton, a
        /// background snapshot) sees a <see cref="Rebase"/> at once. Before, these were plain fields, so a copy kept the
        /// geometry it was made with and a grid could not follow a terrain (docs/DESIGN_Terrain_World.md §4.4).
        /// </summary>
        public NativeArray<float> Geometry;

        /// <summary>Cell edge length in metres.</summary>
        public float CellSize => Geometry.IsCreated ? Geometry[0] : 0f;

        public int Width;
        public int Height;
        public int EntityCount;

        /// <summary>
        /// World-space origin of the grid (bottom-left corner).
        /// Allows the grid to cover negative world coordinates.
        /// </summary>
        public float OriginX => Geometry.IsCreated ? Geometry[1] : 0f;
        public float OriginY => Geometry.IsCreated ? Geometry[2] : 0f;
        
        /// <summary>
        /// Create grid with specified dimensions.
        /// </summary>
        public static SpatialHashGrid Create(int width, int height, float cellSize,
            int maxEntities, Allocator allocator, float originX = 0f, float originY = 0f)
        {
            var geometry = new NativeArray<float>(3, allocator);
            geometry[0] = cellSize;
            geometry[1] = originX;
            geometry[2] = originY;
            var grid = new SpatialHashGrid
            {
                Geometry = geometry,
                GridHead = new NativeArray<int>(width * height, allocator),
                GridNext = new NativeArray<int>(maxEntities, allocator),
                GridValues = new NativeArray<Entity>(maxEntities, allocator),
                Positions = new NativeArray<Vector2>(maxEntities, allocator),
                FreeList = new NativeArray<int>(maxEntities, allocator),
                FreeListCount = 0,
                Width = width,
                Height = height,
                EntityCount = 0,
            };

            // ⭐⭐⭐ CE-3152 — ESTABLISH THE EMPTY-CELL INVARIANT HERE. 🔴 This one missing line was the producer behind
            //   CE-3146's hang, measured live on 2026-10-09: "cell 0 … head=0, GridNext[head]=0 (SELF-CYCLE)".
            //   📐 In this structure "-1" means "empty", but NativeArray DELIBERATELY zero-fills its block
            //   (NativeArray.cs:39-41 — "Marshal.AllocHGlobal does not zero-initialize. Clear the block …"), so a grid
            //   straight out of Create() has EVERY cell claiming slot 0 as its head, and GridNext[0] is 0 too ⇒ any
            //   walk of cell 0 follows 0 → 0 → 0 forever.
            //   ⛔ It was NOT a double-free / double hand-out of a slot: Clear() and Rebase() were the only writers of
            //   the -1 sentinel, so the grid was only well formed AFTER the first Clear(). It bites whenever anything
            //   reads the grid before the builder's first FullRebuild — on the editor host, scenario load does exactly
            //   that. ⭐ Contrast RoadNetworkBuilder.cs:203-204, which has always initialised its heads explicitly.
            grid.Clear();
            return grid;
        }
        
        /// <summary>
        /// ⭐ CE-3018 — move the grid to a new origin and cell size, keeping its cell COUNT and capacity, and empty it.
        /// ⛔ Nothing is reallocated, so no other holder of this memory is left reading freed memory — the reason this is
        /// a rebase and not a resize (docs/DESIGN_Terrain_World.md §4.4). The caller re-inserts; a copy whose own
        /// <see cref="EntityCount"/> it tracks (the perception builder) must also full-rebuild.
        /// </summary>
        public void Rebase(float originX, float originY, float cellSize)
        {
            if (cellSize <= 0f) throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "cell size must be positive");
            Geometry[0] = cellSize;
            Geometry[1] = originX;
            Geometry[2] = originY;
            Clear();
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
            float CellSize = this.CellSize, OriginX = this.OriginX, OriginY = this.OriginY;
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
            float CellSize = this.CellSize, OriginX = this.OriginX, OriginY = this.OriginY;
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
            float CellSize = this.CellSize, OriginX = this.OriginX, OriginY = this.OriginY;   // one read of the shared geometry
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
            if (Geometry.IsCreated) Geometry.Dispose();
        }
    }
}
