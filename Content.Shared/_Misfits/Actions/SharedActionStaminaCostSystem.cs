using Content.Shared.Actions;
using Content.Shared.Actions.Events;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Popups;


namespace Content.Shared._Misfits.Actions;

/// <summary>
/// A system for governing stamina action costs
///
/// Borrowed more or less entirely from the Crystal Edge MIT-license archive.
///
/// https://github.com/crystallpunk-14/crystall-punk-14/blob/5b6108377e40235c768be3ac6ffadb37a085f441/Content.Shared/_CP14/Actions/CP14ActionSystem.Attempt.cs
/// https://github.com/crystallpunk-14/crystall-punk-14/blob/5b6108377e40235c768be3ac6ffadb37a085f441/Content.Shared/_CP14/Actions/CP14ActionSystem.Performed.cs
/// </summary>
public sealed partial class SharedActionStaminaCostSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private StaminaSystem _stamina = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ActionStaminaCostComponent, ActionAttemptEvent>(OnActionAttempt);
    }

    private void OnActionAttempt(Entity<ActionStaminaCostComponent> ent, ref ActionAttemptEvent args)
    {
        if (!TryComp<StaminaComponent>(args.User, out var staminaComp))
            return;

        // if (!staminaComp.Critical)
        //     return;

        if ((ent.Comp.Stamina + staminaComp.StaminaDamage) < staminaComp.CritThreshold)
        {
            _stamina.TakeStaminaDamage(args.User, ent.Comp.Stamina, visual: false);
            return;
        }
        _popup.PopupClient(Loc.GetString("misfits-action-stamina-insufficient"), args.User, args.User);
        args.Cancelled = true;
    }
}
