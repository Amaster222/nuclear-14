using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Shared.Containers;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs;
using Content.Shared.Tag;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Whitelist;
using JetBrains.Annotations;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared.Weapons.Ranged.Systems;

public abstract partial class SharedGunSystem
{
    [Dependency] private SharedDoAfterSystem _doAfter = default!;

    protected virtual void InitializeBallistic()
    {
        SubscribeLocalEvent<BallisticAmmoProviderComponent, ComponentInit>(OnBallisticInit);
        SubscribeLocalEvent<BallisticAmmoProviderComponent, MapInitEvent>(OnBallisticMapInit);
        SubscribeLocalEvent<BallisticAmmoProviderComponent, TakeAmmoEvent>(OnBallisticTakeAmmo);
        SubscribeLocalEvent<BallisticAmmoProviderComponent, GetAmmoCountEvent>(OnBallisticAmmoCount);

        SubscribeLocalEvent<BallisticAmmoProviderComponent, ExaminedEvent>(OnBallisticExamine);
        SubscribeLocalEvent<BallisticAmmoProviderComponent, GetVerbsEvent<Verb>>(OnBallisticVerb);
        SubscribeLocalEvent<BallisticAmmoProviderComponent, InteractUsingEvent>(OnBallisticInteractUsing);
        SubscribeLocalEvent<BallisticAmmoProviderComponent, AfterInteractEvent>(OnBallisticAfterInteract);
        SubscribeLocalEvent<BallisticAmmoProviderComponent, AmmoFillDoAfterEvent>(OnBallisticAmmoFillDoAfter);
        SubscribeLocalEvent<BallisticAmmoProviderComponent, UseInHandEvent>(OnBallisticUse);
    }
    // pressing z
    private void OnBallisticUse(EntityUid uid, BallisticAmmoProviderComponent component, UseInHandEvent args)
    {
        if (args.Handled || !component.Cycleable)
            return;

        ManualCycle(uid, component, TransformSystem.GetMapCoordinates(uid), args.User);
        args.Handled = true;
    }
    private void ManualCycle(EntityUid uid, BallisticAmmoProviderComponent component, MapCoordinates coordinates, EntityUid user, GunComponent? gunComp = null)
    {
        // TODO MISFIT: make firerate thing tied to cycling event when i feel like it
        // Reset shotting for cycling

        if (Resolve(uid, ref gunComp, false) &&
            gunComp is { FireRateModified: > 0f })
        {
            gunComp.NextFire = Timing.CurTime + TimeSpan.FromSeconds(1 / gunComp.FireRateModified);
        }

        Audio.PlayPredicted(component.SoundRack, uid, user);

        _popup.PopupPredicted(
        Loc.GetString(GetBallisticShots(component) == 0 ?
        "gun-ballistic-cycled-empty" : "gun-ballistic-cycled")
        , uid, user);

        Cycle(uid, component, coordinates, user);

        Dirty(uid, component);
        UpdateBallisticAppearance(uid, component);
        UpdateAmmoCount(uid);
    }

    private void OnBallisticInit(EntityUid uid, BallisticAmmoProviderComponent component, ComponentInit args)
    {
        component.Container = Containers.EnsureContainer<Container>(uid, "ballistic-ammo");
        // TODO: This is called twice though we need to support loading appearance data (and we need to call it on MapInit
        // to ensure it's correct).

        //# Misfit: ^^^ I dunno what they mean yet it seems to already "support" this

        UpdateBallisticAppearance(uid, component);
    }
    private void OnBallisticMapInit(EntityUid uid, BallisticAmmoProviderComponent component, MapInitEvent args)
    {
        // TODO this should be part of the prototype, not set on map init.
        // Alternatively, just track spawned count, instead of unspawned count.

        //# Misfit: I agree ^^^ refactor soon. Not the spawned count thing tho
        if (component.Proto != null)
        {
            component.UnspawnedCount = Math.Max(0, component.Capacity - component.Container.ContainedEntities.Count);
            UpdateBallisticAppearance(uid, component);
            Dirty(uid, component);
        }
    }

    protected static int GetBallisticShots(BallisticAmmoProviderComponent component) => component.Container.Count + component.UnspawnedCount;
    private bool CanInstantFill(EntityUid giver) => HasComp<SpeedLoaderComponent>(giver);
    private bool PopupCancels(BallisticAmmoProviderComponent targetComp, EntityUid targetUid, List<ProtoId<TagPrototype>> targetTags,
                                BallisticAmmoProviderComponent giverComp, EntityUid giverUid, List<ProtoId<TagPrototype>> giverTags,
                                EntityUid user)
    {

        if (GetBallisticShots(targetComp) == targetComp.Capacity)
        {
            _popup.PopupPredicted(
                Loc.GetString("gun-ballistic-transfer-target-full",
                    ("entity", targetComp)),
                targetUid,
                user);
            return true;
        }

        if (GetBallisticShots(giverComp) == 0)
        {
            _popup.PopupPredicted(
                Loc.GetString("gun-ballistic-transfer-empty",
                    ("entity", giverUid)),
                giverUid,
                user);
            return true;
        }

        if (!targetTags.Any(giverTags.Contains))
        {
            _popup.PopupPredicted(
                        Loc.GetString("gun-ballistic-transfer-invalid",
                            ("ammoEntity", giverUid),
                            ("targetEntity", targetUid)),
                        giverUid,
                        user);
            return true;
        }
        return false;
    }
    // FIRST THING CALLED.
    private void OnBallisticInteractUsing(EntityUid reciverUID, BallisticAmmoProviderComponent reciverComp, InteractUsingEvent args)
    {
        if (args.Handled || _whitelistSystem.IsWhitelistFailOrNull(reciverComp.Whitelist, reciverUID))
            return;
        if (!(reciverComp.Capacity - GetBallisticShots(reciverComp) is int emptySlots and > 0))
        {
            args.Handled = true;
            _popup.PopupPredicted(Loc.GetString("gun-ballistic-transfer-target-full", ("entity", reciverUID)), reciverUID, args.User);
            return;
        }

        if (!CanInstantFill(args.User)) emptySlots = 1;
        StartAmmoSwap(emptySlots, args.Used, reciverComp, args.Target, args.User);

        args.Handled = true;
        Audio.PlayPredicted(reciverComp.SoundInsert, reciverUID, args.User);

        UpdateAmmoCount(reciverUID);
        UpdateBallisticAppearance(reciverUID, reciverComp);
        Dirty(reciverUID, reciverComp);
    }
    // for stuff that is not instant and so uses a do after
    // so just does checks and calls do after which does the work
    private void OnBallisticAfterInteract(EntityUid uid, BallisticAmmoProviderComponent giverComp, AfterInteractEvent args)
    {
        if (args.Handled || !giverComp.MayTransfer ||
            !HasComp<BallisticAmmoProviderComponent>(args.Target) ||
            !giverComp.MayTransfer)
        {
            return;
        }
        args.Handled = true;
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, giverComp.FillDelay, new AmmoFillDoAfterEvent(), used: uid, target: args.Target, eventTarget: uid)
        {
            BreakOnMove = true,
            BreakOnDamage = false,
            NeedHand = true
        });
    }
    // when the do after is done, we do checks,
    // raise TakeAmmoEvent to remove bullet from BallisticAmmoProviderComponent of giving/feeder container
    // raise InteractUsingEvent to give bullet to BallisticAmmoProviderComponent of recieving/feeding container
    private void OnBallisticAmmoFillDoAfter(EntityUid giverUID, BallisticAmmoProviderComponent giverComp, AmmoFillDoAfterEvent args)
    {
        if (args.Target is not EntityUid targetUid || Deleted(targetUid) ||
            !TryComp<BallisticAmmoProviderComponent>(args.Target, out var targetComp) ||
            targetComp.Whitelist?.Tags is null || giverComp.Whitelist?.Tags is null)
            return;

        if (PopupCancels(targetComp, targetUid, targetComp.Whitelist.Tags, giverComp, giverUID, giverComp.Whitelist.Tags, args.User))
            return;

        StartAmmoSwap(1, giverUID, targetComp, targetUid, args.User);

        args.Repeat = GetBallisticShots(targetComp) < targetComp.Capacity // target has room for more ammo
                   && GetBallisticShots(giverComp) > 0;                   // giver still has ammo left
    }
    private void StartAmmoSwap(int ammoAmount,
                            EntityUid giverUID,
                            BallisticAmmoProviderComponent targetComp, EntityUid targetUid,
                            EntityUid user)
    {

        var ammo = DoTakeAmmoEvent(ammoAmount, giverUID, user);
        if (ammo.Count == 0) _popup.PopupPredicted(Loc.GetString("gun-general-empty"), giverUID, user);
        DoAmmoSwap(ammo, targetComp);

        Audio.PlayPredicted(targetComp.SoundInsert, giverUID, user);
        Dirty(targetUid, targetComp);
        UpdateBallisticAppearance(targetUid, targetComp);
        UpdateAmmoCount(targetUid);
    }

    private void OnBallisticTakeAmmo(EntityUid giverUID, BallisticAmmoProviderComponent giverComp, TakeAmmoEvent args)
    {

        int ammoToSpawn = Math.Max(0, args.Shots - giverComp.Container.Count);
        ammoToSpawn = Math.Min(GetBallisticShots(giverComp), ammoToSpawn);
        int ammoToRemove = Math.Min(giverComp.Container.Count, args.Shots - ammoToSpawn);

        var ammo = giverComp.Container.ContainedEntities.Take(ammoToRemove);
        giverComp.UnspawnedCount -= ammoToSpawn;

        foreach (var shot in ammo)
        {
            args.Ammo.Add((shot, EnsureShootable(shot)));
        }

        for (int i = 0; i < ammoToSpawn; i++)
        {
            var spawnedAmmo = PredictedSpawnAtPosition(giverComp.Proto, args.Coordinates);
            args.Ammo.Add((spawnedAmmo, EnsureShootable(spawnedAmmo)));
        }

        Dirty(giverUID, giverComp);
        UpdateBallisticAppearance(giverUID, giverComp);
        UpdateAmmoCount(giverUID);
    }

    private void DoAmmoSwap(List<(EntityUid? Entity, IShootable Shootable)> ammo, BallisticAmmoProviderComponent reciever)
    {
        foreach (var (shotUID, _) in ammo)
        {
            Containers.Insert(shotUID!.Value, reciever.Container);
        }
    }

    private void OnBallisticVerb(EntityUid uid, BallisticAmmoProviderComponent component, GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null || !component.Cycleable)
            return;
        args.Verbs.Add(new Verb()
        {
            Text = Loc.GetString("gun-ballistic-cycle"),
            Disabled = GetBallisticShots(component) == 0,
            Act = () => ManualCycle(uid, component, TransformSystem.GetMapCoordinates(uid), args.User),
        });
    }

    private void OnBallisticExamine(EntityUid uid, BallisticAmmoProviderComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("gun-magazine-examine", ("color", AmmoExamineColor), ("count", GetBallisticShots(component))));
    }

    protected void Cycle(EntityUid uid, BallisticAmmoProviderComponent comp, MapCoordinates coordinates, EntityUid user)
    {
        bool needToRemove = comp.Container.Count > 0;
        var ammo = DoTakeAmmoEvent(1, uid, user);

        bool empty = ammo.Count == 0;

        // IT'S NOT FUCKING NULL IT'S NOT FUCKING NULL
        EntityUid cycledAmmo = !empty ? ammo[0].Item1!.Value : default;
        if (needToRemove) Containers.Remove(cycledAmmo, comp.Container);

        var cycledEvent = new GunCycledEvent();
        RaiseLocalEvent(uid, ref cycledEvent);
    }

    private void OnBallisticAmmoCount(EntityUid uid, BallisticAmmoProviderComponent component, ref GetAmmoCountEvent args)
    {
        args.Count = GetBallisticShots(component);
        args.Capacity = component.Capacity;
    }

    public void UpdateBallisticAppearance(EntityUid uid, BallisticAmmoProviderComponent component)
    {
        // if (!Timing.IsFirstTimePredicted || !TryComp<AppearanceComponent>(uid, out var appearance))
        //    return;
        if (!TryComp<AppearanceComponent>(uid, out var appearance))
            return;

        Appearance.SetData(uid, AmmoVisuals.AmmoCount, GetBallisticShots(component), appearance);
        Appearance.SetData(uid, AmmoVisuals.AmmoMax, component.Capacity, appearance);
    }

    /// <summary>
    /// Gets rid of THREE WHOLE LINES OF BOILER PLATE!!!!
    /// </summary>
    /// <param name="ammoAmount"></param>
    /// <param name="giverUID"></param>
    /// <param name="user"></param>
    /// <returns></returns>
    public List<(EntityUid?, IShootable)> DoTakeAmmoEvent(int ammoAmount, EntityUid giverUID, EntityUid? user)
    {
        List<(EntityUid? Entity, IShootable Shootable)> ammo = new(ammoAmount);
        var evTakeAmmo = new TakeAmmoEvent(ammoAmount, ammo, Transform(giverUID).Coordinates, user);
        RaiseLocalEvent(giverUID, evTakeAmmo);
        return ammo;
    }

}

/// <summary>
/// DoAfter event for filling one ballistic ammo provider from another.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class AmmoFillDoAfterEvent : SimpleDoAfterEvent
{
}
