1. Can we replace component registration with csharp class attribute defining the unique component id
    - same as event bus events are handled?
    - Would that work for both managed and umanaged components?
2. Unify event bus event unique id attribute usage
    - now unmanaged uses attrib and managed uses type name.
    - Both should use unique numeric id 
3. Mouse pan in showcase
4. Generic debugging environment base for any project
    - 2d map win pan & zoom, showing entities (how to make this generic? some entity type adapters needed)
    - generic entity inspector
    - generic event inspector
    - generic simulation system performance monitor
    - rec/plb with frame stepping...
5. Unify API for managed/unmanaged stuff wherever possible (same as done for entity repo)
    - event bus publishing and consuming events
    - 
6. Pass simulation event bus reference to entity typo so it is accessible from there
    - no need to pass to eventy system...
7. Review the zero-alloc policy on the hot path everywhere, especially the managed component/event rec/plpb seems not to care much.
8. 