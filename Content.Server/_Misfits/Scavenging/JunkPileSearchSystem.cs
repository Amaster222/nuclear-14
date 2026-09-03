using Content.Server._Misfits.SpecialStats;
using Content.Shared._Misfits.Scavenging;
using Content.Shared.DoAfter;
using Content.Shared.Popups;
using Content.Shared.Random.Helpers;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Content.Shared.Verbs;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.Scavenging;

/// <summary>
/// Gives mapped junk piles a deliberate, shared search interaction and a cooldown that begins on a successful search.
/// </summary>
public sealed class JunkPileSearchSystem : EntitySystem
{
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SpecialLuckSystem _luck = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<JunkPileSearchableComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<JunkPileSearchableComponent, JunkPileSearchDoAfterEvent>(OnSearchComplete);
    }

    private void OnGetVerbs(Entity<JunkPileSearchableComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null)
            return;

        var user = args.User;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("junk-pile-search-verb"),
            Act = () => StartSearch(ent, user),
            Priority = 1,
        });
    }

    private void StartSearch(Entity<JunkPileSearchableComponent> ent, EntityUid user)
    {
        if (ent.Comp.CooldownEnd > _timing.CurTime)
        {
            var remaining = (int) Math.Ceiling((ent.Comp.CooldownEnd - _timing.CurTime).TotalMinutes);
            _popup.PopupEntity(Loc.GetString("junk-pile-search-cooldown", ("minutes", remaining)), ent, user, PopupType.SmallCaution);
            return;
        }

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, ent.Comp.SearchDuration,
            new JunkPileSearchDoAfterEvent(), ent, ent)
        {
            BlockDuplicate = true,
            BreakOnDamage = true,
            BreakOnMove = true,
            DistanceThreshold = 2f,
        });
    }

    private void OnSearchComplete(Entity<JunkPileSearchableComponent> ent, ref JunkPileSearchDoAfterEvent args)
    {
        if (args.Cancelled || ent.Comp.CooldownEnd > _timing.CurTime)
            return;

        if (!TryComp<StorageFillComponent>(ent, out var fill) || fill.Contents.Count == 0)
            return;

        ent.Comp.CooldownEnd = _timing.CurTime + TimeSpan.FromSeconds(ent.Comp.CooldownSeconds);
        var coordinates = Transform(ent).Coordinates;
        foreach (var prototype in EntitySpawnCollection.GetSpawns(fill.Contents, _random))
        {
            Spawn(prototype, coordinates);
        }

        if (TryComp<LuckJunkBonusComponent>(ent, out var luck))
            _luck.TryGrantJunkBonus((ent, luck), args.User);

        _popup.PopupEntity(Loc.GetString("junk-pile-search-complete"), ent, args.User);
    }
}
