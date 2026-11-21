using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.MuscleCharacter.Animation.Nodes;

namespace Hrot.MuscleCharacter.Animation.Validation
{
    /// <summary>
    /// Validation result severity level.
    /// </summary>
    public enum ValidationSeverity
    {
        /// <summary>Non-blocking warning.</summary>
        Warning = 0,

        /// <summary>Blocking error.</summary>
        Error = 1,
    }

    /// <summary>
    /// One validation violation (DD-4 §6).
    /// </summary>
    public sealed class ValidationMessage
    {
        /// <summary>Severity of the violation.</summary>
        public required ValidationSeverity Severity { get; init; }

        /// <summary>Validator rule ID (ANIM001, ANIM002, etc.).</summary>
        public required string RuleId { get; init; }

        /// <summary>Human-readable error/warning message.</summary>
        public required string Message { get; init; }

        /// <summary>Optional path or context (e.g., montage name) for easier debugging.</summary>
        public string? Context { get; init; }
    }

    /// <summary>
    /// Animation descriptor validators for TKB load-time and compiler-time checks (DD-4 §6).
    /// Includes DTO-level validation (ANIM006, ANIM007) that runs at TKB load time.
    /// </summary>
    public static class AnimationValidators
    {
        /// <summary>
        /// Validate a character animation descriptor DTO (DD-4 §6).
        /// DTO-level validators (ANIM006, ANIM007) run at TKB load and check the descriptor itself.
        /// Compiler-level validators (ANIM001–005) run when Blueprints reference animation nodes.
        /// </summary>
        /// <param name="dto">Animation descriptor to validate.</param>
        /// <returns>List of validation messages (empty if valid).</returns>
        public static IReadOnlyList<ValidationMessage> ValidateDto(CharacterAnimationDefDto dto)
        {
            if (dto == null)
                throw new ArgumentNullException(nameof(dto));

            var messages = new List<ValidationMessage>();

            // ANIM006: Stance transition montages must exist
            ValidateStanceTransitions(dto, messages);

            // ANIM007: Notify markers referenced in montages must exist
            ValidateNotifyMarkers(dto, messages);

            return messages;
        }

        /// <summary>
        /// ANIM006 — Stance transition declared in StanceTransitions that references
        /// a non-existent transition montage name (DD-4 §6).
        /// </summary>
        private static void ValidateStanceTransitions(CharacterAnimationDefDto dto, List<ValidationMessage> messages)
        {
            if (dto.StanceTransitions == null || dto.StanceTransitions.Count == 0)
                return;

            var montageNameSet = new HashSet<string>(dto.Montages.Select(m => m.Name));

            foreach (var trans in dto.StanceTransitions)
            {
                if (!montageNameSet.Contains(trans.TransitionMontageName))
                {
                    messages.Add(new ValidationMessage
                    {
                        Severity = ValidationSeverity.Error,
                        RuleId = "ANIM006",
                        Message = $"Stance transition from {trans.From} to {trans.To} references non-existent montage '{trans.TransitionMontageName}'",
                        Context = $"Transition {trans.From}→{trans.To}",
                    });
                }
            }
        }

        /// <summary>
        /// ANIM007 — Notify marker referenced in MontageDefDto.Notifies that doesn't exist
        /// in CharacterAnimationDefDto.NotifyMarkers (DD-4 §6).
        /// </summary>
        private static void ValidateNotifyMarkers(CharacterAnimationDefDto dto, List<ValidationMessage> messages)
        {
            if (dto.Montages == null || dto.Montages.Count == 0)
                return;

            var markerNameSet = new HashSet<string>(dto.NotifyMarkers.Select(m => m.Name));

            foreach (var montage in dto.Montages)
            {
                if (montage.Notifies == null || montage.Notifies.Count == 0)
                    continue;

                foreach (var notify in montage.Notifies)
                {
                    if (!markerNameSet.Contains(notify.MarkerName))
                    {
                        messages.Add(new ValidationMessage
                        {
                            Severity = ValidationSeverity.Error,
                            RuleId = "ANIM007",
                            Message = $"Montage '{montage.Name}' references non-existent marker '{notify.MarkerName}'",
                            Context = $"{montage.Name}:{notify.MarkerName}",
                        });
                    }
                }
            }
        }

        /// <summary>
        /// Compiler-level validator ANIM001 helper:
        /// Check if a montage exists in the character class.
        /// Used by Blueprint compiler when validating PlayMontageNode references.
        /// </summary>
        /// <param name="dto">Animation descriptor.</param>
        /// <param name="montageName">Montage name to check.</param>
        /// <returns>True if montage exists; false otherwise.</returns>
        public static bool MontageExists(CharacterAnimationDefDto dto, string montageName)
        {
            if (dto == null || string.IsNullOrEmpty(montageName))
                return false;

            return dto.Montages.Any(m => m.Name == montageName);
        }

        /// <summary>
        /// Compiler-level validator ANIM002 helper:
        /// Check if a stance is in the supported stances list.
        /// Used by Blueprint compiler when validating SetStanceNode references.
        /// </summary>
        /// <param name="dto">Animation descriptor.</param>
        /// <param name="stance">Stance to check.</param>
        /// <returns>True if stance is supported; false otherwise.</returns>
        public static bool StanceIsSupported(CharacterAnimationDefDto dto, byte stance)
        {
            if (dto == null || dto.SupportedStances == null)
                return false;

            return dto.SupportedStances.Contains((StanceId)stance);
        }

        /// <summary>
        /// Compiler-level validator ANIM003 helper:
        /// Check if aim/look-at is supported by this character class.
        /// Used by Blueprint compiler when validating LookAtNode usage.
        /// </summary>
        /// <param name="dto">Animation descriptor.</param>
        /// <returns>True if AimConfig is declared; false otherwise.</returns>
        public static bool SupportsAim(CharacterAnimationDefDto dto)
        {
            return dto != null && dto.AimConfig != null;
        }
    }

    /// <summary>
    /// Minimal Blueprint graph IR used for compile-time static analysis (DD-5 §10).
    /// Represents a single graph scope as a flat sequence of node types.
    /// Cross-graph reasoning is intentionally out of scope (per DD-5 §10).
    /// </summary>
    public sealed class BlueprintGraphIr
    {
        /// <summary>Node types present in this graph scope (in execution order).</summary>
        public IReadOnlyList<Type> Nodes { get; }

        public BlueprintGraphIr(IReadOnlyList<Type> nodes)
        {
            Nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
        }

        public BlueprintGraphIr(params Type[] nodes) : this((IReadOnlyList<Type>)nodes) { }

        /// <summary>Returns true if the graph contains at least one node of the given type.</summary>
        public bool HasNode<T>() => Nodes.Any(n => n == typeof(T));
    }

    /// <summary>
    /// Blueprint graph validators for DD-5 §10 rules ANIM008–ANIM011.
    /// These run at compile time, checking Blueprint graphs for structural issues.
    /// </summary>
    public static class BlueprintAnimationValidators
    {
        /// <summary>
        /// ANIM008 — EnqueueMontageNode used without a preceding PlayMontageChainNode
        /// in the same graph scope (DD-5 §10). Warning: cross-graph chain starts are
        /// legitimate so this is advisory only.
        /// </summary>
        public static IReadOnlyList<ValidationMessage> ValidateAnim008(BlueprintGraphIr graph)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));

            var messages = new List<ValidationMessage>();

            bool hasEnqueue = graph.HasNode<EnqueueMontageNode>();
            bool hasPlayChain = graph.HasNode<PlayMontageChainNode>();

            if (hasEnqueue && !hasPlayChain)
            {
                messages.Add(new ValidationMessage
                {
                    Severity = ValidationSeverity.Warning,
                    RuleId = "ANIM008",
                    Message = "Enqueue Montage executed without a preceding Play Montage Chain in this graph. " +
                              "The enqueue will silently no-op at runtime if no queue is active.",
                });
            }

            return messages;
        }

        /// <summary>
        /// ANIM009 — ReleaseLookNode used without a preceding LookAtPointNode or LookAtEntityNode
        /// in the same graph scope (DD-5 §10). Warning: cross-graph acquire is legitimate.
        /// </summary>
        public static IReadOnlyList<ValidationMessage> ValidateAnim009(BlueprintGraphIr graph)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));

            var messages = new List<ValidationMessage>();

            bool hasRelease = graph.HasNode<ReleaseLookNode>();
            bool hasAcquire = graph.HasNode<LookAtPointNode>() || graph.HasNode<LookAtEntityNode>();

            if (hasRelease && !hasAcquire)
            {
                messages.Add(new ValidationMessage
                {
                    Severity = ValidationSeverity.Warning,
                    RuleId = "ANIM009",
                    Message = "Release Look executed without a preceding Look At in this graph. " +
                              "The release will succeed harmlessly at runtime if no aim is active.",
                });
            }

            return messages;
        }

        /// <summary>
        /// ANIM010 — Codegen self-check: queue mutation code must use the Span-cast
        /// (Pattern A: MemoryMarshal.Cast) or pointer-cast (Pattern B) safe pattern (DD-5 §10).
        /// This is an internal compiler error; it fires on codegen output, not designer graphs.
        /// </summary>
        /// <param name="patternKind">
        /// The pattern identifier detected in generated code.
        /// "SpanCast" (Pattern A) and "PointerCast" (Pattern B) are safe; anything else is flagged.
        /// </param>
        public static IReadOnlyList<ValidationMessage> ValidateAnim010(string patternKind)
        {
            if (patternKind == null) throw new ArgumentNullException(nameof(patternKind));

            var messages = new List<ValidationMessage>();

            // Only the two approved safe patterns are allowed (DD-5 §9.4).
            if (patternKind != "SpanCast" && patternKind != "PointerCast")
            {
                messages.Add(new ValidationMessage
                {
                    Severity = ValidationSeverity.Error,
                    RuleId = "ANIM010",
                    Message = $"Compiler bug — fix codegen template. " +
                              $"Queue mutation used unrecognised pattern '{patternKind}' instead of SpanCast or PointerCast.",
                    Context = patternKind,
                });
            }

            return messages;
        }

        /// <summary>
        /// ANIM011 — Animation primitive used in an inappropriate context.
        /// Fires when an animation node is authored in a Blueprint whose entity class
        /// does not carry the required animation component (DD-5 §10).
        /// <para>
        /// Pass <paramref name="entityAnimDef"/> as non-null if the entity class has
        /// <c>AnimationChannel</c> (i.e., it is a humanoid character). Pass null if the
        /// entity class does not participate in the animation pipeline.
        /// </para>
        /// </summary>
        public static IReadOnlyList<ValidationMessage> ValidateAnim011(
            BlueprintGraphIr graph,
            string entityClassName,
            CharacterAnimationDefDto? entityAnimDef)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            if (entityClassName == null) throw new ArgumentNullException(nameof(entityClassName));

            var messages = new List<ValidationMessage>();

            // If the entity class has an animation descriptor it participates in the animation pipeline.
            if (entityAnimDef != null)
                return messages; // All animation primitives are valid on this class.

            // Entity class does NOT have animation config — flag any animation primitives used.
            foreach (var nodeType in graph.Nodes)
            {
                if (IsAnimationPrimitive(nodeType))
                {
                    messages.Add(new ValidationMessage
                    {
                        Severity = ValidationSeverity.Error,
                        RuleId = "ANIM011",
                        Message = $"Animation primitive '{nodeType.Name}' used in a context where its " +
                                  $"target component is not present on the entity class '{entityClassName}'.",
                        Context = nodeType.Name,
                    });
                }
            }

            return messages;
        }

        private static readonly HashSet<Type> AnimationPrimitiveTypes = new()
        {
            typeof(PlayMontageNode),
            typeof(StopMontageNode),
            typeof(PlayMontageChainNode),
            typeof(EnqueueMontageNode),
            typeof(ClearMontageQueueNode),
            typeof(SetStanceNode),
            typeof(LookAtPointNode),
            typeof(LookAtEntityNode),
            typeof(ReleaseLookNode),
            typeof(GetMontageQueueProgressNode),
            typeof(GetCurrentStanceNode),
        };

        private static bool IsAnimationPrimitive(Type nodeType)
            => AnimationPrimitiveTypes.Contains(nodeType);
    }
}
