using Content.Shared._Misfits.Special;
using Content.Shared.Construction;
using Content.Shared.Construction.Conditions;
using JetBrains.Annotations;
using Robust.Shared.Map;

namespace Content.Shared._N14.Construction.Conditions;

[UsedImplicitly]
[DataDefinition]
public sealed partial class SpecialStatCondition : IConstructionCondition
{
    [DataField]
    public SpecialStat Stat = SpecialStat.Intelligence;

    [DataField]
    public int Minimum = 1;

    public bool Condition(EntityUid user, EntityCoordinates location, Direction direction)
    {
        var entManager = IoCManager.Resolve<IEntityManager>();
        var special = entManager.System<SharedSpecialSystem>();
        return special.GetEffective(user, Stat) >= Minimum;
    }

    public ConstructionGuideEntry? GenerateGuideEntry()
    {
        return new ConstructionGuideEntry
        {
            Localization = "construction-step-condition-special-stat",
            Arguments = [("stat", Stat.ToString()), ("minimum", Minimum)],
        };
    }
}
