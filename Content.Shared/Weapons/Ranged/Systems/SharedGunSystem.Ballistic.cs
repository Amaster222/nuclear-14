using System.Diagnostics;
using System.Linq;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Tag;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;


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
    // pressing z on in hand item
    private void OnBallisticUse(EntityUid uid, BallisticAmmoProviderComponent component, UseInHandEvent args)
    {
        if (args.Handled || !component.Cycleable)
            return;

        ManualCycle(uid, component, TransformSystem.GetMapCoordinates(uid), args.User);
        args.Handled = true;
    }
    private void ManualCycle(EntityUid uid, BallisticAmmoProviderComponent component, MapCoordinates coordinates, EntityUid user, GunComponent? gunComp = null)
    {
        // TODO MISFIT: make firerate thing tied to cycling event when i feel like it. seperation of responibilities
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
    /// <summary>
    /// Sets appearance data on init
    /// </summary>
    /// <remarks>
    /// visualizer handled by system described in <see cref="UpdateBallisticAppearance"/>
    /// So shouldnt have something like genericvisualizer in yaml
    /// </remarks>
    private void OnBallisticInit(EntityUid uid, BallisticAmmoProviderComponent component, ComponentInit args)
    {
        // component.Container = Containers.EnsureContainer<Container>(uid, "ballistic-ammo");
        UpdateBallisticAppearance(uid, component);
    }
    /// <summary>
    /// Sets appearance data on init
    /// </summary>
    /// <remarks>
    /// visualizer handled by system described in <see cref="UpdateBallisticAppearance"/>
    /// So shouldnt have something like genericvisualizer in yaml
    /// </remarks>
    private void OnBallisticMapInit(EntityUid uid, BallisticAmmoProviderComponent component, MapInitEvent args)
    {

        // TODO this should be part of the prototype, not set on map init.
        // Alternatively, just track spawned count, instead of unspawned count.

        //# Misfit: -1 is default value
#if !RELEASE
        if (component.UnspawnedCount > 0 && component.Proto is null)
            Log.Error($"Ballistic Comp has ammo but no prototype uid:{uid} Proto:{Prototype(uid)} ");

        if (component.UnspawnedCount > component.Capacity)
            Log.Error($"Ballistic Comp of Proto: {Prototype(uid)} owner UID:{uid} has too high unspawnedCount: {component.UnspawnedCount} with cap:{component.Capacity}");
#endif
        if (component.UnspawnedCount == -1)
            component.UnspawnedCount = Math.Min(component.Capacity, component.Capacity - component.Container.ContainedEntities.Count);
#if !RELEASE
        if (component.UnspawnedCount < 0)
            Log.Error($"Ballistic Comp of Proto: {Prototype(uid)} owner UID:{uid} unspawnedCount is below 0: {component.UnspawnedCount}");
#endif
        //Compon
        Math.Clamp(component.UnspawnedCount, 0, component.Capacity);

        UpdateBallisticAppearance(uid, component);
        Dirty(uid, component);

    }

    protected static int GetBallisticShots(BallisticAmmoProviderComponent component) => component.Container.Count + component.UnspawnedCount;
    private bool CanInstantFill(EntityUid giver) => HasComp<SpeedLoaderComponent>(giver);
    // Big method of popups that cancel doafter
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
    /// <summary>
    /// When we click on a entity with BallisticAmmoProviderComponent, with some other ent in hand,
    /// this is how it's handled
    /// </summary>
    /// <param name="reciverUID"></param>
    /// <param name="reciverComp"></param>
    /// <param name="args"></param>
    private void OnBallisticInteractUsing(EntityUid reciverUID, BallisticAmmoProviderComponent reciverComp, InteractUsingEvent args)
    {
        if (args.Handled || _whitelistSystem.IsWhitelistFailOrNull(reciverComp.Whitelist, args.Used))
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
    /// <summary>
    /// Check if target we interacted with(clicked) has a valid BallisticAmmoProviderComponent
    /// which triggers interaction specific to them and other ent with BallisticAmmoProviderComponent via AmmoFillDoAfterEvent
    /// </summary>
    /// <param name="giverUID">UID in hand that can give ammo to target</param>
    /// <param name="giverComp">comp of giverUID </param>
    /// <param name="args">event args with info like target we touched or user</param>
    /// <remarks>
    /// Other spechiul interactions with other comps could be put here for BallisticAmmoProviderComponent
    /// <remarks/>
    // TODO MISFIT: see why args.Target is nullable here. It shouldn't be null at this point
    private void OnBallisticAfterInteract(EntityUid giverUID, BallisticAmmoProviderComponent giverComp, AfterInteractEvent args)
    {   // tell compiler within scope Target isnt null
        if (args.Handled || !giverComp.MayTransfer || args.Target is not EntityUid targetUid ||
            !TryComp<BallisticAmmoProviderComponent>(args.Target, out var targetComp) ||
            //TODO MISFIT: throw exeception on init if doesnt have tag
            targetComp.Whitelist?.Tags is null || giverComp.Whitelist?.Tags is null ||
            PopupCancels(targetComp, targetUid, targetComp.Whitelist.Tags, giverComp,
            giverUID, giverComp.Whitelist.Tags, args.User))
        {
            return;
        }
        args.Handled = true;
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, giverComp.FillDelay, new AmmoFillDoAfterEvent(), used: giverUID, target: args.Target, eventTarget: giverUID)
        {
            BreakOnMove = true,
            BreakOnDamage = false,
            NeedHand = true
        });
    }

    // raise TakeAmmoEvent to remove bullet from BallisticAmmoProviderComponent of giving/feeder container
    // raise InteractUsingEvent to give bullet to BallisticAmmoProviderComponent of recieving/feeding container
    /// <summary>
    /// handler for <see cref="AmmoFillDoAfterEvent"/>. What Ballistic system does when do after is complete
    /// Ammo is taken 1 by 1(wait for repeated do after to be done) until giver runs out of ammo or target is full
    /// Target ideally has already been checked and verified as UID with Ballistic comp
    /// but we still check it again since alot could have happened between that time
    /// </summary>
    /// <param name="giverUID">UID who we take ammo from</param>
    /// <param name="giverComp">Comp who listens to takeammo event and who we take ammo from </param>
    /// <remarks>
    /// Target ideally has already been checked and verified as existing entity with Ballistic comp
    /// but we still check it again since alot could have happened between that time
    /// <remarks/>
    private void OnBallisticAmmoFillDoAfter(EntityUid giverUID, BallisticAmmoProviderComponent giverComp, AmmoFillDoAfterEvent args)
    {
        // check target isnt deleted/null. Deleted already checks for null
        // but I do it here to tell compiler within scope of method that it isnt(I dont like the use of nullables)
        if (args.Target is not EntityUid targetUid || Deleted(targetUid) ||
            !TryComp<BallisticAmmoProviderComponent>(args.Target, out var targetComp))
            return;

        StartAmmoSwap(1, giverUID, targetComp, targetUid, args.User);

        args.Repeat = GetBallisticShots(targetComp) < targetComp.Capacity // target has room for more ammo
                   && GetBallisticShots(giverComp) > 0;                   // giver still has ammo left
    }
    /// <summary>
    /// Point of no return when we decide we can takeammo from giver and give to target
    /// without worrying about edge cases. This shouldn't be interrupted!!
    /// </summary>
    /// <param name="ammoAmount">Ammo we TRY to take from giverUID. Though not guaranteed(ie. not enough ammo, or other mechanic ect)</param>
    /// <param name="giverUID">UID who we take ammo from(should have comps that listen to event)</param>
    /// <param name="recieverComp">Comp that recieves taken ammo and is updated</param>
    /// <param name="recieverUid">UID with comp that recieves taken ammo and is updated</param>
    /// <param name="user"> optional user that caused event in most cases(ie. player interacting with ammo box)</param>
    private void StartAmmoSwap(int ammoAmount,
                            EntityUid giverUID,
                            BallisticAmmoProviderComponent recieverComp, EntityUid recieverUid,
                            EntityUid? user = null)
    {

        var ammo = DoTakeAmmoEvent(ammoAmount, giverUID, user);
        if (ammo.Count == 0) _popup.PopupPredicted(Loc.GetString("gun-general-empty"), giverUID, user);
        DoAmmoSwap(ammo, recieverComp);

        Audio.PlayPredicted(recieverComp.SoundInsert, giverUID, user);
        Dirty(recieverUid, recieverComp);
        UpdateBallisticAppearance(recieverUid, recieverComp);
        UpdateAmmoCount(recieverUid);
    }


    /// <summary>
    /// Location in ballistics system code where we actually put the ammo into the comp(reciever)
    /// </summary>
    /// <remarks>
    /// Seperated into its own method for clarity and is probably a likely point of failure
    /// <remarks/>
    private void DoAmmoSwap(List<(EntityUid? Entity, IShootable Shootable)> ammo, BallisticAmmoProviderComponent reciever)
    {
        foreach (var (shotUID, _) in ammo)
        {
            Containers.Insert(shotUID!.Value, reciever.Container);
        }
    }
    /// <summary>
    /// Verbs or available "commands"/"actions" on the drop down menu when you right click the item
    /// </summary>
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


    ///  UI info on examine
    private void OnBallisticExamine(EntityUid uid, BallisticAmmoProviderComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("gun-magazine-examine", ("color", AmmoExamineColor), ("count", GetBallisticShots(component))));
    }

    /// <summary>
    /// How Ballistics does cycling. We call take ammo, but since nothing is taking it we just remove it from
    /// ballistics container that it WAS parented to(so has same coords as parent)
    /// Creates and raises GunCycledEvent
    /// </summary>
    /// <param name="uid">Gun or thing we are cycling</param>
    /// <param name="comp">comp of uid</param>
    /// <param name="user">optional user that is doing cycling. Maybe spooky shit can cycle stuff remotely in the future</param>
    /// <remarks>
    ///
    /// <remarks/>
    protected void Cycle(EntityUid uid, BallisticAmmoProviderComponent comp, MapCoordinates coordinates, EntityUid? user = null)
    {
        // recently spawned or non-existent ammo doesnt need to be removed from container
        bool needToRemove = comp.Container.Count > 0;
        var ammo = DoTakeAmmoEvent(1, uid, user);
        // care if it's empty and not if it's null. Keep it simple/cheap
        bool empty = ammo.Count == 0;
        EntityUid cycledAmmo = !empty ? ammo[0].Item1!.Value : default;

        if (needToRemove) Containers.Remove(cycledAmmo, comp.Container);

        // unused I think
        var cycledEvent = new GunCycledEvent();
        RaiseLocalEvent(uid, ref cycledEvent);
    }

    /// <summary>
    /// listening Comp supplies args with ammo data
    /// </summary>
    private void OnBallisticAmmoCount(EntityUid uid, BallisticAmmoProviderComponent component, ref GetAmmoCountEvent args)
    {
        args.Count = GetBallisticShots(component);
        args.Capacity = component.Capacity;
    }


    /// <summary>
    /// Updates and initializes appearence data on server side
    /// </summary>
    /// <remarks>
    /// alot of uids with BallisticComp also have MagazineVisualsComp(only exposed to client GunSystem)
    /// Which uses custom logic to update appearence depending on ammo capacity and current ammo amount
    /// basically as ammo gets lower/higher a level adjusts corresponding to a sprite.
    /// So we dont need to or should define appearance data in yaml to let the system do its thing
    /// <see cref="GunSystem.MagazineVisuals.cs"/>
    ///</remarks>
    public void UpdateBallisticAppearance(EntityUid uid, BallisticAmmoProviderComponent component)
    {
        if (!TryComp<AppearanceComponent>(uid, out var appearance))
            return;

        Appearance.SetData(uid, AmmoVisuals.AmmoCount, GetBallisticShots(component), appearance);
        Appearance.SetData(uid, AmmoVisuals.AmmoMax, component.Capacity, appearance);
    }

    /// <summary>
    /// Take some or none amount of ammo from giverUID returning a list of that ammo
    /// How this is done is up to comps of giverUID that listen to <see cref="TakeAmmoEvent"/>
    /// </summary>
    /// <param name="ammoAmount">Ammo we TRY to take from giverUID. Though not guaranteed(ie. not enough ammo, or other mechanic ect)</param>
    /// <param name="giverUID">UID who we take ammo from(should have comps that listen to event)</param>
    /// <param name="user">optional user that caused event in most cases(ie. player interacting with ammo box)</param>
    /// <returns>list of tuples with UID and Ishootable of spawned ammo</returns>
    ///
    /// <remarks>
    /// Gets rid of 3 lines of boiler plate, but also makes it clear that we just call TakeAmmo and take whatever it gives for ammo
    /// ideally calling this we should assume that giverUID has SOME comp that listens to TakeAmmo
    /// The "How" is handled by that comp's system method for TakeAmmo
    /// Outside callers should just get their ammo and just care about handling it for their own systems not the giverUID's systems
    /// event also can SPAWN ammo, so obvious cancel conditions that CAN BE checked SHOULD be checked before calling this method
    /// this is to save on performance but also to avoid side effects and complications from calling this event needlessly
    /// <remarks/>
    public List<(EntityUid?, IShootable)> DoTakeAmmoEvent(int ammoAmount, EntityUid giverUID, EntityUid? user = null)
    {
        List<(EntityUid? Entity, IShootable Shootable)> ammo = new(ammoAmount);
        var evTakeAmmo = new TakeAmmoEvent(ammoAmount, ammo, Transform(giverUID).Coordinates, user);
        RaiseLocalEvent(giverUID, evTakeAmmo);
        return ammo;
    }
    /// <summary>
    /// How Ballistic comps handle takeAmmo event.
    /// Any already spawned ammo is taken first. Spawn ammo only if we have to
    /// Ideally called when it is CERTAIN that ammo needs to be taken
    /// </summary>
    /// <remarks>
    /// Side effects: giverComp.UnspawnedCount is decreased by ammo that had to be spawned
    // TODO: see later if this below should be left up to event handler or calling method depending on how other gunsystems are done
    ///               Assumed caller will insert or at least remove all ammo from giverComp.Container
    /// <remarks/>
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

}

/// <summary>
/// DoAfter event for filling one ammo provider from another.
/// </summary>
/// <remarks> only used by ballistics for now, since it is only ammo provider that uses a do after(i think???) <remarks/>
[Serializable, NetSerializable]
public sealed partial class AmmoFillDoAfterEvent : SimpleDoAfterEvent
{
}
