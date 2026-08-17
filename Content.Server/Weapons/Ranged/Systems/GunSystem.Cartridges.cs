using Content.Shared.Damage;
using Content.Shared.Damage.Events;
using Content.Shared.Examine;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Server.Player;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server.Weapons.Ranged.Systems;

public sealed partial class GunSystem
{

    [Dependency] private IPlayerManager _net = default!;
    protected override void InitializeCartridge()
    {
        base.InitializeCartridge();
        SubscribeLocalEvent<CartridgeAmmoComponent, ExaminedEvent>(OnCartridgeExamine);
        SubscribeLocalEvent<CartridgeAmmoComponent, DamageExamineEvent>(OnCartridgeDamageExamine);

    }

    // server to clients
    public override void EjectSpentCart(MapCoordinates coord, Angle angle, string? cartProto, ICommonSession? player = null)
    {

        if (cartProto is null || !ProtoMan.HasIndex(cartProto))
            return;

        NetUserId? shooterID = player?.UserId;
        Filter filter = Filter.Empty().AddPlayersByPvs(coord);
        if (shooterID is not null) { filter.RemovePlayer(_net.GetSessionById(shooterID.Value)); }
        RaiseNetworkEvent(new SpentCartEvent(coord, angle, cartProto, shooterID), filter);
    }
    private void OnCartridgeDamageExamine(EntityUid uid, CartridgeAmmoComponent component, ref DamageExamineEvent args)
    {
        var damageSpec = GetProjectileDamage(component.Prototype);

        if (damageSpec == null)
            return;

        _damageExamine.AddDamageExamine(args.Message, damageSpec, Loc.GetString("damage-projectile"));
    }

    private DamageSpecifier? GetProjectileDamage(string proto)
    {
        if (!ProtoManager.TryIndex<Robust.Shared.Prototypes.EntityPrototype>(proto, out var entityProto))
            return null;

        if (entityProto.Components
            .TryGetValue(_factory.GetComponentName(typeof(ProjectileComponent)), out var projectile))
        {
            var p = (ProjectileComponent) projectile.Component;

            if (!p.Damage.Empty)
            {
                return p.Damage;
            }
        }

        return null;
    }

    private void OnCartridgeExamine(EntityUid uid, CartridgeAmmoComponent component, ExaminedEvent args)
    {
        if (component.Spent)
        {
            args.PushMarkup(Loc.GetString("gun-cartridge-spent"));
        }
        else
        {
            args.PushMarkup(Loc.GetString("gun-cartridge-unspent"));
        }
    }
}
