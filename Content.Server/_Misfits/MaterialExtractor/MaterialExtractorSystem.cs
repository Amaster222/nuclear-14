using System.Numerics;
using Content.Server.Storage.EntitySystems;
using Content.Server.Chat.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.NPC.Systems;
using Content.Shared.Power.Generator;
using Content.Shared.Storage;
using Content.Shared.Chat;
using Robust.Shared.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.MaterialExtractor;

/// <summary>Runs the low-frequency seismic pulse and deposits raw materials in the extractor hopper.</summary>
public sealed partial class MaterialExtractorSystem : EntitySystem
{
    private static readonly SoundPathSpecifier ThumpSound = new("/Audio/Effects/Footsteps/largethud.ogg");

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private StorageSystem _storage = default!;
    [Dependency] private SharedPointLightSystem _lights = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private NPCSystem _npc = default!;
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private ChatSystem _chat = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<MaterialExtractorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<MaterialExtractorComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<MaterialExtractorComponent, ExaminedEvent>(OnExamined);
    }

    private void OnDamageChanged(Entity<MaterialExtractorComponent> ent, ref DamageChangedEvent args)
    {
        ent.Comp.DamagePauseUntil = _timing.CurTime + TimeSpan.FromSeconds(30);
        SetBeacon(ent.Owner, ent.Comp, true);
    }

    private void OnExamined(Entity<MaterialExtractorComponent> ent, ref ExaminedEvent args)
    {
        if (args.IsInDetailsRange)
            args.PushMarkup(Loc.GetString("material-extractor-examine", ("quality", ent.Comp.DepositQuality.ToLowerInvariant())));
    }

    private void OnMapInit(Entity<MaterialExtractorComponent> ent, ref MapInitEvent args)
    {
        var qualityRoll = _random.NextFloat();
        if (qualityRoll < ent.Comp.PoorDepositChance)
        {
            (ent.Comp.DepositQuality, ent.Comp.YieldMultiplier) = ("POOR", ent.Comp.PoorYieldMultiplier);
        }
        else if (qualityRoll > 1f - ent.Comp.RichDepositChance)
        {
            (ent.Comp.DepositQuality, ent.Comp.YieldMultiplier) = ("RICH", ent.Comp.RichYieldMultiplier);
        }
        else
        {
            (ent.Comp.DepositQuality, ent.Comp.YieldMultiplier) = ("FAIR", 1f);
        }
        _lights.SetEnabled(ent.Owner, false);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<MaterialExtractorComponent, StorageComponent>();
        while (query.MoveNext(out var uid, out var extractor, out var storage))
        {
            if (!HasNearbyPlayer((uid, extractor)))
            {
                SetBeacon(uid, extractor, false);
                continue;
            }

            // This is a self-contained welding-fuel generator. Its normal generator
            // start/stop state is the extractor's operating switch.
            if (!TryComp<FuelGeneratorComponent>(uid, out var generator) || !generator.On)
            {
                extractor.WasRunning = false;
                SetBeacon(uid, extractor, false);
                continue;
            }

            if (!extractor.WasRunning)
            {
                extractor.WasRunning = true;
                extractor.WarningSent = false;
                extractor.NextPulse = _timing.CurTime;
                extractor.NextWave = _timing.CurTime + TimeSpan.FromSeconds(_random.Next(extractor.FirstWaveMinSeconds, extractor.FirstWaveMaxSeconds + 1));
            }

            extractor.ActiveAttackers.RemoveWhere(attacker => Deleted(attacker));

            if (_timing.CurTime < extractor.DamagePauseUntil)
            {
                SetBeacon(uid, extractor, true);
                continue;
            }

            if (!extractor.WarningSent && _timing.CurTime >= extractor.NextWave - TimeSpan.FromSeconds(extractor.WaveWarningSeconds))
            {
                extractor.WarningSent = true;
                SetBeacon(uid, extractor, true);
                _audio.PlayPvs(ThumpSound, uid,
                    AudioParams.Default.WithVolume(-3f).WithMaxDistance(30f));
            }

            if (_timing.CurTime >= extractor.NextWave)
                StartWave(uid, extractor);

            if (_timing.CurTime >= extractor.NextPulse)
            {
                SetBeacon(uid, extractor, !extractor.BeaconOn);
                _audio.PlayPvs(ThumpSound, uid,
                    AudioParams.Default.WithVolume(-7f).WithMaxDistance(22f));
                ProduceOutput(uid, extractor, storage);
                extractor.NextPulse = _timing.CurTime + PulseDelay(extractor);
            }
        }
    }

    private void ProduceOutput(EntityUid extractorUid, MaterialExtractorComponent extractor, StorageComponent storage)
    {
        var output = Spawn(SelectOutput(extractor), Transform(extractorUid).Coordinates);
        if (_storage.Insert(extractorUid, output, out _, storageComp: storage, playSound: false))
            return;

        Del(output);
        SetBeacon(extractorUid, extractor, true);
    }

    private static TimeSpan PulseDelay(MaterialExtractorComponent extractor)
        => TimeSpan.FromSeconds(extractor.PulseIntervalSeconds / extractor.YieldMultiplier);

    private string SelectOutput(MaterialExtractorComponent extractor)
    {
        var totalWeight = 0;
        foreach (var weight in extractor.OutputWeights.Values)
            totalWeight += weight;

        if (totalWeight <= 0)
            throw new InvalidOperationException("Material extractor output weights must have a positive total.");

        var roll = _random.Next(totalWeight);
        string? fallback = null;
        foreach (var (prototype, weight) in extractor.OutputWeights)
        {
            fallback = prototype;
            roll -= weight;
            if (roll < 0)
                return prototype;
        }

        return fallback!;
    }

    private bool HasNearbyPlayer(Entity<MaterialExtractorComponent> extractor)
    {
        var origin = Transform(extractor);
        var originPosition = _transform.GetWorldPosition(extractor);
        var query = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (query.MoveNext(out var playerUid, out _, out var player))
        {
            if (player.MapID == origin.MapID && Vector2.DistanceSquared(_transform.GetWorldPosition(playerUid), originPosition) <= extractor.Comp.PlayerActivationRadius * extractor.Comp.PlayerActivationRadius)
                return true;
        }

        return false;
    }

    private void StartWave(EntityUid extractorUid, MaterialExtractorComponent extractor)
    {
        _chat.TrySendInGameICMessage(extractorUid,
            Loc.GetString("material-extractor-rumble"),
            InGameICChatType.Emote,
            ChatTransmitRange.Normal,
            ignoreActionBlocker: true);

        // Small, escalating packs keep this a defendable world objective rather than an unattended farm.
        var count = Math.Min(2 + extractor.WaveCount, 5);
        var prototype = extractor.WaveCount switch
        {
            < 2 => "N14MobMoleratWave",
            < 4 => "N14MobGeckoWave",
            _ => "N14MobNightstalkerWave",
        };

        var origin = Transform(extractorUid).Coordinates;
        for (var i = 0; i < count; i++)
        {
            var angle = _random.NextFloat() * MathF.Tau;
            var distance = _random.NextFloat(10f, 14f);
            var attacker = Spawn(prototype, origin.Offset(new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance));
            extractor.ActiveAttackers.Add(attacker);

            if (TryComp<HTNComponent>(attacker, out var htn))
            {
                _npc.SetBlackboard(attacker, NPCBlackboard.CurrentOrderedTarget, extractorUid, htn);
                _htn.Replan(htn);
            }
        }

        extractor.WaveCount++;
        extractor.WarningSent = false;
        extractor.NextWave = _timing.CurTime + TimeSpan.FromSeconds(_random.Next(extractor.WaveMinSeconds, extractor.WaveMaxSeconds + 1));
        SetBeacon(extractorUid, extractor, true);
    }

    private void SetBeacon(EntityUid uid, MaterialExtractorComponent extractor, bool enabled)
    {
        extractor.BeaconOn = enabled;
        _lights.SetEnabled(uid, enabled);
    }
}
