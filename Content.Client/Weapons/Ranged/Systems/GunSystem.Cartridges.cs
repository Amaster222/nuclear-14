using System.Runtime.CompilerServices;
using Content.Client.Weapons.Ranged.Components;
using Content.Shared.Audio;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
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

namespace Content.Client.Weapons.Ranged.Systems;

public sealed partial class GunSystem
{
    // note, if changing field order IResourceCache MUST be declared before IPrototypeManager
    [Dependency] private IResourceCache _resourceCache = default!;
    [Dependency] private IPrototypeManager _protoMan = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] IRobustRandom _rng = default!;
    private ISawmill _logCart = default!;


    private void InitializeSpentAmmo()
    {
        SubscribeLocalEvent<SpentAmmoVisualsComponent, AppearanceChangeEvent>(OnSpentAmmoAppearance);
        SubscribeNetworkEvent<SpentCartEvent>(RecieveSpentCartEvent);
        _logCart = _logMan.GetSawmill("client.gun.cartridge");
        //Subs.CVar(_config, CCVars.TileFrictionModifier, value => _frictionModifier = value, true);
        _rng.SetSeed(666); // satan rng
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


    // client only. Dont need to network this since attemptShoot from client already runs this on server
    // server version networks this to other clients on the server
    public override void EjectSpentCart(MapCoordinates coord, Angle angle, string? cartProto, ICommonSession? dontNeedToUseHere = null)
    {
        // client effect called by shared code, so turn off prediction
        if (!_timing.IsFirstTimePredicted) { return; }
        SpawnClientCart(coord, angle, cartProto, _player.LocalUser);
    }

    /// <summary>
    /// Client recieves outside event to spawn cart visual
    /// based from other clients(RequestShootEvent)
    /// or server(SpentCartEvent rasied in shared code)
    /// </summary>
    private void RecieveSpentCartEvent(SpentCartEvent ev)
    {
        SpawnClientCart(ev.Coords, ev.Angle, ev.Proto);
    }

    private static Vector2i _cSPRITE_SIZE = new(32, 32);
    private const string RSI_FAIL = "/Textures/Objects/Weapons/Guns/Ammunition/Casings/ammo_casing.rsi";
    private static ResPath _cRSI_FAIL = new(RSI_FAIL);
    private static RSI _cRSI = new(_cSPRITE_SIZE, _cRSI_FAIL);
    private static StateId _constSpentID = new("base-spent");
    private static StateId _constBaseID = new("base");
    private EntityUid SpawnClientCart(MapCoordinates coord, Angle angle, string? cartProto, NetUserId? source = null)
    {
        if (cartProto is null || !_protoMan.HasIndex(cartProto) ||
            !(_entMan.CreateEntityUninitialized(cartProto) is EntityUid dummyCart)
            || Comp<SpriteComponent>(dummyCart).BaseRSI is not RSI rsi)
        {
            _logCart.Warning($"Supplied cartridge prototype null or invalid protoId: {cartProto}");
            return SpawnCartPhysics(coord, angle, _constSpentID, _cRSI);
        }

        // This is prolly dumb when I just need some data that's prolly already cache'd somewhere
        // but I dunno yet how to efficently look that up. so like enjoy the syntax sugar i guesssss
        var stateId = rsi.TryGetState(_constSpentID, out var _) ? _constSpentID :
                      rsi.TryGetState(_constBaseID, out var _) ? _constBaseID : null;
        if (stateId == null)
        {
            _logCart.Error($"cartridge prototype null rsi or doesnt use correct texture State: {cartProto}");
            return SpawnCartPhysics(coord, angle, _constSpentID, _cRSI);
        }

        var spentCartVisual = SpawnCartPhysics(coord, angle, stateId, rsi);
        // EnsureComp<PredictedProjectileClientComponent>(spentCartVisual);
        //_physics.UpdateIsPredicted(spentCartVisual);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private EntityUid SpawnCartPhysics(MapCoordinates basePos, Angle baseAngle, StateId state, RSI rsi)
    {
        var cartVisual = Spawn("ClientCartridge", basePos, rotation: _rng.NextAngle(LandAngleMax));
        _sprite.LayerSetRsi(cartVisual, 0, rsi, state);
        var angleRng = _rng.NextAngle(MinArc, MaxArc) + baseAngle;

        _physics.ApplyLinearImpulse(cartVisual, _rng.NextFloat(DistMin, DistMax) * angleRng.ToVec());
        _physics.ApplyAngularImpulse(cartVisual, _rng.NextFloat(SpinMax));

        return cartVisual;
    }

}
