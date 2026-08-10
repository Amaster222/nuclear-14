// Origin: ColonialMarinesUniverse (AU-14) — Multi Z system
// Ported to misfits-14 _MultiZ/
// #Cythisiax Ported — Multi-Z PVS expansion for adjacent levels

using System.Collections.Generic;
using System.Linq;
using Content.Server._Misfits.GameStates;
using Content.Shared._MultiZ;
using Content.Shared._MultiZ.Core.Components;
using Content.Shared._MultiZ.Core.EntitySystems;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Server._MultiZ.Core;

/// <summary>
/// Expands a player's PVS to include the current Z-level and its immediate neighbors.
/// This keeps the rendered adjacent level populated with entities rather than just map art.
/// </summary>
public sealed partial class MZPvsSystem : MZSharedSystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private MisfitsPvsSystem _pvs = default!;

    private readonly HashSet<ICommonSession> _trackedSessions = new();
    private readonly Dictionary<ICommonSession, HashSet<EntityUid>> _activeOverrides = new();

    private float _probeAccumulator;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<PlayerDetachedEvent>(OnPlayerDetached);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var probeHz = _cfg.GetCVar(MZCVars.ProbeUpdateHz);
        if (probeHz <= 0f || _trackedSessions.Count == 0)
            return;

        _probeAccumulator += frameTime;
        var probeInterval = 1f / probeHz;
        if (_probeAccumulator < probeInterval)
            return;

        _probeAccumulator = 0f;
        RefreshTrackedSessions();
    }

    private void OnPlayerAttached(PlayerAttachedEvent ev)
    {
        _trackedSessions.Add(ev.Player);
        RefreshSession(ev.Player);
    }

    private void OnPlayerDetached(PlayerDetachedEvent ev)
    {
        _trackedSessions.Remove(ev.Player);
        ClearSession(ev.Player);
    }

    private void RefreshTrackedSessions()
    {
        foreach (var session in _trackedSessions.ToArray())
        {
            if (session.Status == SessionStatus.Disconnected)
            {
                ClearSession(session);
                _trackedSessions.Remove(session);
                continue;
            }

            RefreshSession(session);
        }
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

        var desired = new HashSet<EntityUid> { mapUid };

        if (TryMapUp((mapUid, zMap), out var aboveMap))
            desired.Add(aboveMap.Value.Owner);

        if (TryMapDown((mapUid, zMap), out var belowMap))
            desired.Add(belowMap.Value.Owner);

        ApplySessionOverrides(session, desired);
    }

    private void ApplySessionOverrides(ICommonSession session, HashSet<EntityUid> desired)
    {
        if (!_activeOverrides.TryGetValue(session, out var current))
        {
            current = new HashSet<EntityUid>();
            _activeOverrides[session] = current;
        }

        foreach (var uid in current.ToArray())
        {
            if (desired.Contains(uid))
                continue;

            _pvs.RemoveSessionOverride(uid, session);
            current.Remove(uid);
        }

        foreach (var uid in desired)
        {
            if (!current.Add(uid))
                continue;

            _pvs.AddSessionOverride(uid, session);
        }
    }

    private void ClearSession(ICommonSession session)
    {
        if (!_activeOverrides.TryGetValue(session, out var current))
            return;

        foreach (var uid in current)
        {
            _pvs.RemoveSessionOverride(uid, session);
        }

        current.Clear();
        _activeOverrides.Remove(session);
    }
}
