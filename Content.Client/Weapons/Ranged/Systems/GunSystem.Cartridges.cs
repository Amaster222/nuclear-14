using System.Runtime.CompilerServices;
using Content.Client.Weapons.Ranged.Components;
using Content.Shared.Audio;
using Content.Shared.CCVar;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using static Robust.Client.Graphics.RSI;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;
using static Content.Client.Weapons.Ranged.Systems.GunSystem.CartridgeSettings;

namespace Content.Client.Weapons.Ranged.Systems;

public sealed partial class GunSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] IRobustRandom _rng = default!;
    private ISawmill _logCart = default!;
    public CartridgeSettings CartridgeVisualsSetting;
    private const string Proto_Physics = "ClientCartridgePhysics";
    private const string Proto_Static = "ClientCartridgeStatic";
    public enum CartridgeSettings
    {
        CART_VISUAL_OFF = 1,
        OLD_SCHOOL = 2,
        PHYSICS_ON = 3
    }
    private void InitializeSpentAmmo()
    {
        SubscribeLocalEvent<SpentAmmoVisualsComponent, AppearanceChangeEvent>(OnSpentAmmoAppearance);
        SubscribeNetworkEvent<SpentCartEvent>(RecieveSpentCartEvent);
        _logCart = _logMan.GetSawmill("client.gun.cartridge");
        _rng.SetSeed(666); // satan rng
        Subs.CVar(_config, CCVars.SpentCartridgeVisual, OnCartSetting, true);
        CartridgeVisualsSetting = (CartridgeSettings) _config.GetCVar(CCVars.SpentCartridgeVisual);
    }

    private void OnCartSetting(int value)
    {
        CartridgeVisualsSetting = (CartridgeSettings) value;
    }
    private void OnSpentAmmoAppearance(EntityUid uid, SpentAmmoVisualsComponent component, ref AppearanceChangeEvent args)
    {
        var sprite = args.Sprite;
        if (sprite == null) return;

        if (!args.AppearanceData.TryGetValue(AmmoVisuals.Spent, out var varSpent))
        {
            return;
        }

        var spent = (bool) varSpent;
        string state;

        if (spent)
            state = component.Suffix ? $"{component.State}-spent" : "spent";
        else
            state = component.State;

        sprite.LayerSetState(AmmoVisualLayers.Base, state);
        if (sprite.LayerExists(AmmoVisualLayers.Tip))
        {
            sprite.RemoveLayer(AmmoVisualLayers.Tip);
        }
    }

    /// <summary>
    /// Client recieves outside event to spawn cart visual
    /// based from other clients(RequestShootEvent)
    /// or server(SpentCartEvent rasied in shared code)
    /// </summary>
    private void RecieveSpentCartEvent(SpentCartEvent ev)
    {
        EjectSpentCart(ev.Coords, ev.Angle, ev.Proto);
    }
    // client only. Dont need to network this since attemptShoot from client already runs this on server
    // server version networks this to other clients on the server
    public override void EjectSpentCart(MapCoordinates coord, Angle angle, string? cartProto, ICommonSession? dontNeedToUseHere = null)
    {
        // client effect called by shared code, so turn off prediction
        if (!_timing.IsFirstTimePredicted || CartridgeVisualsSetting == CART_VISUAL_OFF)
        {
            return;
        }
        SpawnClientCart(coord, angle, cartProto, _player.LocalUser);
    }

    private static Vector2i _cSPRITE_SIZE = new(32, 32);
    private const string RSI_FAIL = "/Textures/Objects/Weapons/Guns/Ammunition/Casings/ammo_casing.rsi";
    private static ResPath _cRSI_FAIL = new(RSI_FAIL);
    private static RSI _cRSI = new(_cSPRITE_SIZE, _cRSI_FAIL);
    private static StateId _constSpentID = new("base-spent");
    private static StateId _constBaseID = new("base");
    /// <summary>
    /// Main method for spawning a client side spent cartridge visual.
    /// Use prototype to spawn an unit copy of the cartridge to get its RSI
    /// and check if its spent cart sprite has the states we need
    /// </summary>
    /// <param name="baseCoord">should be coordinates where spent cartridge came from</param>
    /// <param name="curAngle">Usually the angle the 'shooter' was facing</param>
    /// <param name="cartProto">cartridge prototype we spawn the casing from</param>
    /// <param name="source">original source of networked spentCartEvent. null if server</param>
    /// <returns>entUID of spent cartridge</returns>
    private EntityUid SpawnClientCart(MapCoordinates baseCoord, Angle curAngle, string? cartProto, NetUserId? source = null)
    {
        if (!(_entMan.CreateEntityUninitialized(cartProto) is EntityUid dummyCart)
            || Comp<SpriteComponent>(dummyCart).BaseRSI is not RSI rsi)
        {
            _logCart.Warning($"Supplied cartridge prototype null or invalid protoId: {cartProto}");
            return SpawnCartPhysics(baseCoord, curAngle, _constSpentID, _cRSI);
        }

        // This is prolly dumb when I just need some data that's prolly already cache'd somewhere(or read the proto)
        // but I dunno yet how to efficently look that up. so we just look at a spawend copy
        var stateId = rsi.TryGetState(_constSpentID, out var _) ? _constSpentID : // check for spent-base, else base else null
                      rsi.TryGetState(_constBaseID, out var _) ? _constBaseID : null;
        // TODO: remove this when refactoring all ammo cart protos to follow da rulez
        if (stateId == null)
        {
            _logCart.Error($"cartridge prototype null rsi or doesnt use correct texture State: {cartProto}");
            return SpawnCartPhysics(baseCoord, curAngle, _constSpentID, _cRSI);
        }

        var spentCartVisual = CartridgeVisualsSetting == OLD_SCHOOL ? SpawnCartOldSchool(baseCoord, stateId, rsi) :
                                                             SpawnCartPhysics(baseCoord, curAngle, stateId, rsi);

        DoEjectSound(dummyCart, source, spentCartVisual);
        Del(dummyCart);
        return spentCartVisual;
    }

    private static AudioParams _sAUDIO_PARAM = AudioParams.Default.WithVariation(SharedContentAudioSystem.DefaultVariation);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DoEjectSound(EntityUid ent, NetUserId? source, EntityUid spentCartVisual)
    {
        var sound = Comp<CartridgeAmmoComponent>(ent).EjectSound;
        var sender = (source is null || !_player.TryGetSessionById(source.Value, out var session)) ? null : session;
        Audio.PlayLocal(sound, spentCartVisual, sender?.AttachedEntity, _sAUDIO_PARAM);
    }

    private const float MaxArc = 2.85f;
    private const float MinArc = 1.5f;
    private const float DistMax = .7f;
    private const float DistMin = .25f;
    private const float LandAngleMax = 6.3f; // little over 2*Pi
    private const float SpinMax = 1f;
    /// <summary>
    /// method where we achully spawn the cart. Prototype already has comp to
    /// make the visual work on the client without the server, so we just spawn it
    /// and apply a PULSE(what TryThrow does basically) using some rng
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private EntityUid SpawnCartPhysics(MapCoordinates basePos, Angle baseAngle, StateId state, RSI rsi)
    {
        var cartVisual = Spawn(Proto_Physics, basePos, rotation: _rng.NextAngle(LandAngleMax));
        _sprite.LayerSetRsi(cartVisual, 0, rsi, state);
        var angleRng = _rng.NextAngle(MinArc, MaxArc) + baseAngle;

        _physics.ApplyLinearImpulse(cartVisual, _rng.NextFloat(DistMin, DistMax) * angleRng.ToVec());
        _physics.ApplyAngularImpulse(cartVisual, _rng.NextFloat(SpinMax));
        return cartVisual;
    }
    /// <summary>
    /// Alt version of above where we just spawn static cartridges to save on preformance
    /// this spawns cartridges in a radius rather than throwing them at an angle
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private EntityUid SpawnCartOldSchool(MapCoordinates basePos, StateId state, RSI rsi, int seed = 666)
    {
        var (posEjectRNG, angleEjectRNG) = GetRandVectAngle(seed, _timing.CurTime.Nanoseconds);
        var cartVisual = Spawn(Proto_Static, basePos.Offset(posEjectRNG), rotation: angleEjectRNG);
        _sprite.LayerSetRsi(cartVisual, 0, rsi, state);
        return cartVisual;
    }


}
