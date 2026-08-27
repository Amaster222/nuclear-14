using System.Runtime.CompilerServices;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Timing;
using static Content.Shared.Weapons.Ranged.Systems.SharedGunSystem;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;


namespace Content.Shared.Weapons.Ranged.Systems;
/// <summary>
/// Things with <see cref="BallisticAmmoProviderComponent"/> should be for anything that
/// needs gun logic related to containing and ejecting ammo (loading mags, cycling ect..)
/// Mostly includes ammo boxes, but also goes for bows, specific guns, ect...
///
/// Main implementation and events are listed for that listed here
/// showing the general flow and logic of what it does.
/// Main work is done in <see cref="TakeAmmoEvent"/> which actually updates comps, spawns ammo, ect...
/// Guess you can view everything here as a wrapper for TakeAmmoEvent lol
///
/// Things are generally ordered by order of event calls.
/// ie...(example stuff not real but generally shows how events chain into eachother)
///     InteractBefore  -->  Interact    -->  InteractAfter
///             |               |                  |
///     OnPreGunInteract    OnGunInteract --> OnGunAfterInteract
///
/// </summary>
public abstract partial class SharedGunSystem
{
    [Dependency] private SharedDoAfterSystem _doAfter = default!;

    private const int DEFAULT_AMMO = -1;
    //private static System.Random RNG;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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


        // comp handlers
        InitCompGen();

    }

    /// pressing z with in hand item
    private void OnBallisticUse(EntityUid giverUid, BallisticAmmoProviderComponent comp, UseInHandEvent args)
    {
        if (args.Handled || !comp.Cycleable)
            return;

        ManualCycle(giverUid, comp, args.User);
        args.Handled = true;
    }
    /// <summary>
    /// Cycling specific to ballisticAmmoProviders
    /// Manual in that it is player triggered
    /// </summary>
    private void ManualCycle(EntityUid giverUid, BallisticAmmoProviderComponent comp, EntityUid user, GunComponent? gunComp = null)
    {
        // TODO MISFIT: make firerate thing tied to cycling event when i feel like it. seperation of responibilities
        // Reset shotting for cycling
        if (Resolve(giverUid, ref gunComp, false) &&
            gunComp is { FireRateModified: > 0f })
        {
            gunComp.NextFire = Timing.CurTime + TimeSpan.FromSeconds(1 / gunComp.FireRateModified);
        }

        Audio.PlayPredicted(comp.SoundRack, giverUid, user);
        _popup.PopupPredicted(
        Loc.GetString(comp.AmmoCount == 0 ?
        "gun-ballistic-cycled-empty" : "gun-ballistic-cycled")
        , giverUid, user);

        Cycle(giverUid, comp, user);
    }

    /// <summary>
    /// Usually first event triggered when clicking on ent with something
    /// Just check if used ent is speedloader or bullet via whitelist and comp
    /// Only marks as handled if reciever is full
    /// </summary>
    /// <param name="recieverUid">UID of clicked on ent. Also stored as args.Target</param>
    /// <param name="recieverComp">known comp that listened for event</param>
    /// <param name="args">event args with info like user, and ent used to click on target</param>
    private void OnBallisticInteractUsing(EntityUid recieverUid, BallisticAmmoProviderComponent recieverComp, InteractUsingEvent args)
    {
        if (args.Handled || _whitelistSystem.IsWhitelistFailOrNull(recieverComp.Whitelist, args.Used))
            return;
        // TODO: rework
        if (!Timing.IsFirstTimePredicted)
        {
            int slots = CanInstantFill(args.User) ? recieverComp.AmmoCount : 1;
            TryAmmoInsert(slots, args.Used, recieverComp, recieverUid, args.User);
            return;
        }
        args.Handled = true;
        // reciever is full so doesnt matter what used ent is for useAfter event
        // special interactions should rely on BeforeUseInHandEvent event
        if (!(recieverComp.Capacity - recieverComp.AmmoCount is int emptySlots and > 0))
        {
            _popup.PopupPredicted(Loc.GetString("gun-ballistic-transfer-target-full", ("entity", MetaData(recieverUid).EntityName)), recieverUid, args.User);
            return;
        }

        // ent has whitelist but doesnt pass method to instantly insert more than 1 ammo
        if (!CanInstantFill(args.User)) emptySlots = 1;

        if (!TryAmmoInsert(emptySlots, args.Used, recieverComp, recieverUid, args.User))
        {
            _popup.PopupPredicted(Loc.GetString("gun-general-empty", ("entName", args.Used)), args.Used, args.User);
            return;
        }
        Audio.PlayPredicted(recieverComp.SoundInsert, recieverUid, args.User);
    }

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
    // TODO MISFIT: throw exeception on init if doesnt have whitelist or Tags to avoid null checks later on
    private void OnBallisticAfterInteract(EntityUid giverUID, BallisticAmmoProviderComponent giverComp, AfterInteractEvent args)
    {

        if (args.Handled || !giverComp.MayTransfer || Deleted(args.Target) ||
            !TryComp<BallisticAmmoProviderComponent>(args.Target, out var recieverComp) ||
            PopupCancelsWhitelist(recieverComp, args.Target.Value,
                         giverComp, giverUID, args.User,
                         giverComp.Whitelist?.Tags, recieverComp.Whitelist?.Tags))
        {
            return;
        }

        args.Handled = _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, giverComp.FillDelay, new AmmoFillDoAfterEvent(), used: giverUID, target: args.Target, eventTarget: giverUID)
        {
            BreakOnMove = false,
            BreakOnDamage = false,
            NeedHand = true,
            BlockDuplicate = true,
            RequireCanInteract = true,
            BreakOnDropItem = true
        });
    }


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

        args.Repeat = !args.Cancelled && !Deleted(args.Target) && TryComp<BallisticAmmoProviderComponent>(args.Target.Value, out var recieverComp) &&
                      !PopupCancels(recieverComp, args.Target.Value, giverComp, giverUID, args.User) &&
                      TryAmmoInsert(Math.Min(5, Math.Max(0, recieverComp.Capacity - recieverComp.AmmoCount)), giverUID, recieverComp, args.Target.Value, args.User);

        Audio.PlayPredicted(giverComp.SoundInsert, giverUID, args.User);
    }

    /// <summary>
    /// Verbs or available "commands"/"actions" on the drop down menu when you right click the item
    /// </summary>
    private void OnBallisticVerb(EntityUid uid, BallisticAmmoProviderComponent comp, GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null || !comp.Cycleable)
            return;
        args.Verbs.Add(new Verb()
        {
            Text = Loc.GetString("gun-ballistic-cycle"),
            Disabled = comp.AmmoCount == 0,
            Act = () => ManualCycle(uid, comp, args.User),
        });
    }

    ///  UI info on examine
    private void OnBallisticExamine(EntityUid uid, BallisticAmmoProviderComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("gun-magazine-examine", ("color", AmmoExamineColor), ("count", component.AmmoCount)));
    }

    /// <summary>
    /// listening Comp supplies args with ammo data
    /// </summary>
    private void OnBallisticAmmoCount(EntityUid uid, BallisticAmmoProviderComponent comp, ref GetAmmoCountEvent args)
    {
        args.Count = comp.AmmoCount;
        args.Capacity = comp.Capacity;
    }
    /// <summary>
    /// Updates and initializes appearence data on server side
    /// </summary>
    /// <remarks>
    /// uids with BallisticComp also have MagazineVisualsComp(code in client GunSystem)
    /// is it sprite logic where current ammoCount/ammoMax is proportional to a level. Each level corresponds to a sprite
    /// So if u wanna do sprite stuff in yaml or here keep that in mind. Simpler to let the system do its thang
    /// <see cref="GunSystem.MagazineVisuals.cs"/>
    ///</remarks>
    public void UpdateBallisticAppearance(EntityUid uid, BallisticAmmoProviderComponent comp)
    {
        if (!TryComp<AppearanceComponent>(uid, out var appearance))
            return;

        Appearance.SetData(uid, AmmoVisuals.AmmoCount, comp.AmmoCount, appearance);
        Appearance.SetData(uid, AmmoVisuals.AmmoMax, comp.Capacity, appearance);
    }

    /// <summary>
    /// Attempt to insert ammo into recieverUID with known BallisticComp
    /// </summary>
    /// <param name="ammoAmount">Ammo we TRY to take from giverUID. Though not guaranteed(ie. not enough ammo, or other mechanic ect)</param>
    /// <param name="giverUID">UID who we take ammo from(should have comps that listen to event)</param>
    /// <param name="recieverComp">Comp that recieves taken ammo and is updated</param>
    /// <param name="recieverUid">UID with comp that recieves taken ammo and is updated</param>
    /// <param name="user"> user that caused event(ie. player interacting with ammo box)</param>
    private bool TryAmmoInsert(int ammoAmount,
                            EntityUid giverUID,
                            BallisticAmmoProviderComponent recieverComp, EntityUid recieverUid,
                            EntityUid user)
    {
        var ammo = DoTakeAmmo(ammoAmount, giverUID, user);
        if (ammo.Count == 0) return false;
        DoAmmoInsert(ammo, recieverComp, recieverUid, user);
        return true;
    }
    /// <summary>
    /// How Ballistic comps handle takeAmmo event.
    /// Any already spawned ammo is removed first, then we spawn ammo if needed,
    /// decreasing amount of unspawned ammo
    /// </summary>
    /// <remarks>
    /// Side effects: 1. giverComp.UnspawnedCount is decreased by ammo that had to be spawned
    ///               2. already spawned ammo is removed from container(gun, ammobox ect)
    ///               3. spawned ammo is dropped in closest valid parent of giverUID
    /// <remarks/>
    private void OnBallisticTakeAmmo(EntityUid giverUID, BallisticAmmoProviderComponent giverComp, TakeAmmoEvent args)
    {
        // Transfrom data we apply to all spawned ammo
        if (!Timing.IsFirstTimePredicted)
        {
            foreach (var ammo in giverComp.ClientPredictedAmmoVisual)
            {
                var uid = Spawn(ammo);
                FlagPredicted(uid);
                args.Ammo.Add((uid, EnsureShootable(uid)));
            }
            return;
        }
        giverComp.ClientPredictedAmmoVisual.Clear();

        int ammoToSpawn = Math.Max(args.Shots - giverComp.SpawnedCountPredict, 0);
        ammoToSpawn = Math.Min(giverComp.AmmoCount, ammoToSpawn);
        // branchless baby!!!!!!!!!!!!!!
        // We only go by the "real" count(containers networked seperate)
        // when otherwise would result in an index error by going over
        // so we atleast always go equal or less than container count
        int miniMin = Math.Min(giverComp.SpawnedCountPredict, giverComp.Container.Count);
        int ammoToRemove = Math.Min(miniMin, args.Shots - ammoToSpawn);
        int toRemCount = Math.Max(ammoToRemove, 0);
        ammoToRemove = toRemCount;

        var alreadySpawnedAmmo = giverComp.Container.ContainedEntities;
        int index = miniMin - 1;

        while (toRemCount > 0)
        {
            DebugTools.Assert(DebugCheckNullAmmo(alreadySpawnedAmmo, index));
            var uid = alreadySpawnedAmmo[index];
            giverComp.ClientPredictedAmmoVisual.Add(MetaData(uid)?.EntityPrototype?.ID);
            var ammo = (uid, EnsureShootable(uid));
            args.Ammo.Add(ammo);
            Containers.Remove(uid, giverComp.Container);
            //FlagPredicted(uid);

            index--;
            toRemCount--;
        }

        for (int i = 0; i < ammoToSpawn; i++)
        {
            var uid = Spawn(giverComp.Proto);
            FlagPredicted(uid);
            giverComp.ClientPredictedAmmoVisual.Add(giverComp.Proto);

            var spawnedAmmo = (uid, EnsureShootable(uid));
            args.Ammo.Add(spawnedAmmo);
        }

        // update stuff hereeee. REALLY WANNA MAKE SURE THESE GET UPDATED CORRECTLY!!!!!!!!!
        giverComp.UnspawnedCount -= ammoToSpawn;
        giverComp.SpawnedCountPredict -= ammoToRemove;
        giverComp.IndexPredict = giverComp.IndexPredict + ammoToSpawn + ammoToRemove;

        DebugTools.Assert(DebugAmmoProviderChange(giverComp));
        NetworkCompState(giverUID, args.User, giverComp);
        UpdateBallisticAppearance(giverUID, giverComp);
        UpdateAmmoCount(giverUID);
    }
    public void NetworkCompState(EntityUid uid, EntityUid? user, BallisticAmmoProviderComponent comp)
    {
        var ev = new AmmoProviderDirtyEvent(uid, user, comp.IndexPredict, comp.UnspawnedCount, comp.SpawnedCountPredict, Timing.CurTick.Value);
        RaiseLocalEvent(ref ev);

        if (_netManager.IsServer && Math.Abs(Timing.CurTick.Value - comp.LastModifiedTick.Value) > 20)
        {
            DebugTools.Assert(DebugAmmoProviderClientDirty(uid));
            Dirty(uid, comp);
        }
    }
}
[ByRefEvent]
public record struct AmmoProviderDirtyEvent(EntityUid Gun, EntityUid? User, int AmmoIndex,
                                            int AmmoUnspawned, int AmmoSpawned, uint Tick);


public sealed partial class OnCompHandling(IComponentState? cur, IComponentState? next, BallisticAmmoState? stateToApply)
{
    public IComponentState? Cur = cur;
    public IComponentState? Next = next;
    public BallisticAmmoState? StateToApply = stateToApply;
}
// BallisticAmmoState? StateToApply
/// <summary>
/// DoAfter event for filling one ammo provider from another.
/// </summary>
/// <remarks> only used by ballistics for now, since it is only ammo provider that uses a do after(i think???) <remarks/>
[Serializable, NetSerializable]
public sealed partial class AmmoFillDoAfterEvent : SimpleDoAfterEvent
{
}
