using Robust.Shared.Configuration;

namespace Content.Shared._Goobstation.CCVar;

[CVarDefs]
public sealed partial class GoobCVars
{
    /// <summary>
    ///     Multiplies bloodloss damage and recovery in the Goob Shitmed bloodstream model.
    /// </summary>
    public static readonly CVarDef<float> BleedMultiplier =
        CVarDef.Create("medical.bloodloss_multiplier", 4f, CVar.SERVER);

    #region Mechs

    /// <summary>
    ///     Whether or not players can use mech guns outside of mechs.
    /// </summary>
    public static readonly CVarDef<bool> MechGunOutsideMech =
        CVarDef.Create("mech.gun_outside_mech", true, CVar.SERVER | CVar.REPLICATED);

    #endregion
}
