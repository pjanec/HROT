using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.MuscleCharacter.Animation.Descriptors;

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

            return dto.SupportedStances.Contains((Components.StanceId)stance);
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
}
