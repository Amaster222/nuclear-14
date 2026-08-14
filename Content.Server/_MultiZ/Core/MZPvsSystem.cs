// Origin: ColonialMarinesUniverse (AU-14) — Multi Z system
// Ported to misfits-14 _MultiZ/
// #Cythisiax Ported — Multi-Z PVS expansion for adjacent levels

using System.Collections.Generic;
using System.Linq;
using Content.Shared._MultiZ;
using Content.Shared._MultiZ.Core.Components;
using Content.Shared._MultiZ.Core.EntitySystems;
using Content.Shared.GameTicking;
using Robust.Server.GameObjects;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server._MultiZ.Core;

/// <summary>
/// Expands a player's PVS to include the current Z-level and its immediate neighbors.
/// This keeps the rendered adjacent level populated with entities rather than just map art.
/// </summary>
public sealed partial class MZPvsSystem : MZSharedSystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ViewSubscriberSystem _viewSubscriber = default!;

    private readonly HashSet<ICommonSession> _trackedSessions = new();
    private readonly Dictionary<ICommonSession, EntityUid> _lowerViewRelays = new();
    private readonly Queue<ICommonSession> _refreshQueue = new();

    private float _refreshBudget;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<PlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var probeHz = _cfg.GetCVar(MZCVars.ProbeUpdateHz);
        if (probeHz <= 0f)
        {
            ClearAllRelays();
            _refreshBudget = 0f;
            return;
        }

        if (_trackedSessions.Count == 0)
        {
            _refreshBudget = 0f;
            return;
        }

        _refreshBudget += frameTime * probeHz * _trackedSessions.Count;
        var refreshCount = Math.Min((int) _refreshBudget, _refreshQueue.Count);
        if (refreshCount == 0)
            return;

        _refreshBudget -= refreshCount;
        for (var i = 0; i < refreshCount; i++)
        {
            var session = _refreshQueue.Dequeue();
            if (!_trackedSessions.Contains(session))
                continue;

            if (session.Status == SessionStatus.Disconnected)
            {
                ClearSession(session);
                _trackedSessions.Remove(session);
                RemoveFromRefreshQueue(session);
                continue;
            }

            RefreshSession(session);
            _refreshQueue.Enqueue(session);
        }
    }

    private void OnPlayerAttached(PlayerAttachedEvent ev)
    {
        if (_trackedSessions.Add(ev.Player))
            _refreshQueue.Enqueue(ev.Player);

        if (_cfg.GetCVar(MZCVars.ProbeUpdateHz) > 0f)
            RefreshSession(ev.Player);
    }

    private void OnPlayerDetached(PlayerDetachedEvent ev)
    {
        _trackedSessions.Remove(ev.Player);
        RemoveFromRefreshQueue(ev.Player);
        ClearSession(ev.Player);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        ClearAllRelays();
        _trackedSessions.Clear();
        _refreshQueue.Clear();
        _refreshBudget = 0f;
    }

    private void RefreshSession(ICommonSession session)
    {
        if (session.AttachedEntity is not { Valid: true } attached ||
            !TryComp(attached, out TransformComponent? xform) ||
            xform.MapUid is not { } mapUid ||
            !TryComp<MZMapComponent>(mapUid, out var zMap))
        {
            ClearSession(session);
            return;
        }

        if (HasRenderableGrids(mapUid) ||
            !TryMapDown((mapUid, zMap), out var belowMap) ||
            !TryComp<MapComponent>(belowMap.Value.Owner, out var belowMapComp))
        {
            ClearSession(session);
            return;
        }

        var playerPos = _transform.GetMapCoordinates(xform).Position;
        var relayCoords = new MapCoordinates(playerPos, belowMapComp.MapId);

        EnsureLowerViewRelay(session, relayCoords);
    }

    private void EnsureLowerViewRelay(ICommonSession session, MapCoordinates coordinates)
    {
        if (_lowerViewRelays.TryGetValue(session, out var relay) &&
            !TerminatingOrDeleted(relay))
        {
            _transform.SetMapCoordinates(relay, coordinates);
            return;
        }

        relay = Spawn(null, coordinates);
        _lowerViewRelays[session] = relay;
        _viewSubscriber.AddViewSubscriber(relay, session);
    }

    private void ClearSession(ICommonSession session)
    {
        if (!_lowerViewRelays.Remove(session, out var relay))
            return;

        _viewSubscriber.RemoveViewSubscriber(relay, session);

        if (!TerminatingOrDeleted(relay))
            QueueDel(relay);
    }

    private void ClearAllRelays()
    {
        foreach (var session in _lowerViewRelays.Keys.ToArray())
            ClearSession(session);
    }

    private void RemoveFromRefreshQueue(ICommonSession session)
    {
        var count = _refreshQueue.Count;
        for (var i = 0; i < count; i++)
        {
            var queued = _refreshQueue.Dequeue();
            if (queued != session)
                _refreshQueue.Enqueue(queued);
        }
    }

    private bool HasRenderableGrids(EntityUid mapUid)
    {
        var query = EntityQueryEnumerator<TransformComponent, MapGridComponent>();
        while (query.MoveNext(out _, out var xform, out _))
        {
            if (xform.MapUid == mapUid)
                return true;
        }

        return false;
    }
}
