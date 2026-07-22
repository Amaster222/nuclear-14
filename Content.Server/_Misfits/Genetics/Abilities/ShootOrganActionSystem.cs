// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Polymorph.Systems;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Systems;
using Content.Shared.Popups;
using Content.Shared.Throwing;
using Content.Shared._Misfits.Actions;

namespace Content.Server._Misfits.Genetics.Abilities;

public sealed class ShootOrganActionSystem : EntitySystem
{
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private PolymorphSystem _polymorph = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
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

        if (organ is null || !_body.RemoveOrgan(organ.Value))
        {
            _popup.PopupEntity(Loc.GetString("MutationTongueSpike-popup-no-organ", ("organ", ent.Comp.Organ)), args.Performer, args.Performer);
            return;
        }

        if (_polymorph.PolymorphEntity(organ.Value, ent.Comp.Polymorph) is not {} projectile)
            return;

        var projectileComp = EnsureComp<ActionProjectileComponent>(projectile);
        projectileComp.Container = args.Action.Comp.Container;
        Dirty(projectile, projectileComp);
        _throwing.TryThrow(projectile, args.Target, user: args.Performer, playSound: false);
    }
}
