// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Polymorph.Systems;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Systems;
using Content.Shared.Popups;
using Content.Shared.Speech.EntitySystems;
using Content.Shared.Throwing;
using Content.Shared._Misfits.Actions;

namespace Content.Server._Misfits.Genetics.Abilities;

public sealed class ShootOrganActionSystem : EntitySystem
{
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private PolymorphSystem _polymorph = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStutteringSystem _stutter = default!;
    [Dependency] private ThrowingSystem _throwing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ShootOrganActionComponent, ShootOrganActionEvent>(OnAction);
    }

    private void OnAction(Entity<ShootOrganActionComponent> ent, ref ShootOrganActionEvent args)
    {
        args.Handled = true;
        EntityUid? organ = null;
        foreach (var candidate in _body.GetBodyOrgans(args.Performer))
        {
            var slotId = candidate.Component.SlotId;
            if (string.Equals(slotId, ent.Comp.Organ, StringComparison.OrdinalIgnoreCase))
            {
                organ = candidate.Id;
                break;
            }
        }

        EntityUid projectile;
        if (organ is not null && _body.RemoveOrgan(organ.Value) &&
            _polymorph.PolymorphEntity(organ.Value, ent.Comp.Polymorph) is {} polymorphed)
        {
            projectile = polymorphed;
        }
        else if (ent.Comp.Fallback is {} fallback)
        {
            // most bodies in this fork have no tongue organ slot, so grow a fresh spike
            // instead of failing with "you don't have a tongue".
            projectile = Spawn(fallback, Transform(args.Performer).Coordinates);
        }
        else
        {
            _popup.PopupEntity(Loc.GetString("MutationTongueSpike-popup-no-organ", ("organ", ent.Comp.Organ)), args.Performer, args.Performer);
            return;
        }

        var projectileComp = EnsureComp<ActionProjectileComponent>(projectile);
        projectileComp.Container = args.Action.Comp.Container;
        Dirty(projectile, projectileComp);
        _throwing.TryThrow(projectile, args.Target, user: args.Performer, playSound: false);

        // the tongue has to regrow: can't speak right until it's back
        if (ent.Comp.RegrowTime > TimeSpan.Zero)
        {
            _stutter.DoStutter(args.Performer, ent.Comp.RegrowTime, refresh: true);
            _popup.PopupEntity(Loc.GetString("MutationTongueSpike-popup-regrowing"), args.Performer, args.Performer);
        }
    }
}
