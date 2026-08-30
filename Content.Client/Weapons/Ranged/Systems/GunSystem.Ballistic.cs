using Content.Client._Misfits.Movement;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Client.GameStates;
using Robust.Client.Timing;

namespace Content.Client.Weapons.Ranged.Systems;

public sealed partial class GunSystem
{

    protected override void InitializeBallistic()
    {
        base.InitializeBallistic();
        SubscribeLocalEvent<BallisticAmmoProviderComponent, UpdateAmmoCounterEvent>(OnBallisticAmmoCount);
        SubscribeLocalEvent<GunEjectEvent>(OnEject);
    }
    private void OnEject(GunEjectEvent args)
    {
        var giverUid = GetEntity(args.NetEnt);
        var xform = (giverUid, Transform(giverUid));
        var ent = Spawn(args.Proto);
        FlagPredicted(ent);

        PlaceNextToRot((ent, Transform(ent)), xform);
        EjectCartRNG(ent, args.Sequence, args.Seed);
    }
    /// <summary>
    /// updates client ui on ammo change
    /// </summary>
    private void OnBallisticAmmoCount(EntityUid uid, BallisticAmmoProviderComponent component, UpdateAmmoCounterEvent args)
    {
        if (args.Control is DefaultStatusControl control)
        {
            control.Update(component.AmmoCount, component.Capacity);
        }
    }

}
