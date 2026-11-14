using System;

namespace Hrot.Editor.AiShared;

/// <summary>
/// ⭐ <c>CE-428</c>/<c>CE-434</c> — a behaviour's bound blueprint RESOLVER asset and the block shape it was derived for.
/// ⭐ <c>CE-503</c>: host-neutral (it was <c>BTreeResolverRef</c> in the BTree editor) — a BTree and an HSM bind a resolver
/// with the SAME record, as their DTOs share <c>BehaviorResolverRefDto</c>. 📄 <c>Architect_Question_76</c> §12.21 / §12.27g.
/// </summary>
public sealed record BehaviorResolverRef(Guid AssetId, string Name, uint ShapeHash);
