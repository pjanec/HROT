namespace Fdp.Toolkit.Blueprints;

/// <summary>Blackboard memory tier selection for Blueprint state storage.</summary>
public enum BlackboardTier
{
    // ⛔ The payload figures are NOT repeated here — they moved with B3②'s MaxSlots re-pick and a
    //   comment is the one copy no rail can check. 📄 BlueprintTierLadder carries the numbers.
    // ⛔⛔ These ordinals reach compiled artefacts (§17.1 N2 measured THREE enums spelling this
    //   ladder, one of them `: byte`). ⇒ a new tier is APPENDED, never inserted.
    B1024  = 0,
    B4096  = 1,
    B16384 = 2,
}
