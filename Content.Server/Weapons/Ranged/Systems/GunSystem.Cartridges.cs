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


    /// <summary>
    ///  Server does basic checks before networking cart event to clients
    ///  so they know to do the visual
    /// </summary>
    /// <param name="baseCoord"> where the event/ejected casing originate from</param>
    /// <param name="baseAngle">usually angle 'shooter' was facing</param>
    /// <param name="cartProto">prototype of the shot cartridge(the bullet shot)</param>
    /// <param name="player">client the event originated from.
    ///                      Important for filtering clients who sent the event
    ///                      so they dont get it twice and positioning visual correctly</param>
    public override void EjectSpentCart(MapCoordinates baseCoord, Angle baseAngle, string? cartProto, ICommonSession? player = null)
    {

        if (cartProto is null || !ProtoMan.HasIndex(cartProto))
            return;

        NetUserId? shooterID = player?.UserId;
        Filter filter = Filter.Empty().AddPlayersByPvs(baseCoord);
        if (shooterID is not null) { filter.RemovePlayer(_net.GetSessionById(shooterID.Value)); }
        RaiseNetworkEvent(new SpentCartEvent(baseCoord, baseAngle, cartProto, shooterID), filter);
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
        if (!ProtoManager.TryIndex<EntityPrototype>(proto, out var entityProto))
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
