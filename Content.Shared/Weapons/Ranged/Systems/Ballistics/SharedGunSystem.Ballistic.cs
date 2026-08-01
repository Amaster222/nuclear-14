using System.Linq;
using Content.Shared.Coordinates;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Random.Helpers;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Random;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;
using Robust.Shared.Toolshed.Commands.Math;


namespace Content.Shared.Weapons.Ranged.Systems;
/// <summary>
/// This file has all of the event handlers for <see cref="BallisticAmmoProviderComponent"/>
/// showing the general flow and logic of what it does. The main work is done in TakeAmmoEvent
/// since everything circulates on when/how ammo is taken out/put in a container defined by the comp
/// </summary>
public abstract partial class SharedGunSystem
{
    [Dependency] private SharedDoAfterSystem _doAfter = default!;

    private const int DEFAULT_AMMO = -1;
    //private static System.Random RNG;
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

        ManualCycle(uid, component, _xform.GetMapCoordinates(uid), args.User);
        args.Handled = true;
    }

    /// <summary>
    /// Corrects bad yaml and sets appearance data on Map init
    /// </summary>
    /// <remarks>
    /// visualizer handled by system described in <see cref="UpdateBallisticAppearance"/>
    /// So shouldnt have something like genericvisualizer in yaml
    /// </remarks>
    private void OnBallisticInit(EntityUid uid, BallisticAmmoProviderComponent comp, ComponentInit args)
    {
#if !RELEASE
        DebugInfo(uid, comp);
#endif
        EnsureCorrect(uid, comp);
        UpdateBallisticAppearance(uid, comp);
        Dirty(uid, comp);
    }
    /// <summary>
    /// Corrects bad yaml and sets appearance data on Map init
    /// </summary>
    /// <remarks>
    /// visualizer handled by system described in <see cref="UpdateBallisticAppearance"/>
    /// So shouldnt have something like genericvisualizer in yaml
    /// </remarks>
    private void OnBallisticMapInit(EntityUid uid, BallisticAmmoProviderComponent comp, MapInitEvent args)
    {
        // TODO Misfit:
#if !RELEASE
        DebugInfo(uid, comp);
#endif
        EnsureCorrect(uid, comp);
        UpdateBallisticAppearance(uid, comp);
        Dirty(uid, comp);
    }



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
        if (!(reciverComp.Capacity - reciverComp.AmmoCount is int emptySlots and > 0))
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
    // TODO MISFIT: throw exeception on init if doesnt have whitelist or Tags to avoid null checks
    private void OnBallisticAfterInteract(EntityUid giverUID, BallisticAmmoProviderComponent giverComp, AfterInteractEvent args)
    {
        // TODO MISFIT: maybe as a code exercise see how to make this more readable
        if (args.Handled || !giverComp.MayTransfer || Deleted(args.Target) ||

            !TryComp<BallisticAmmoProviderComponent>(args.Target, out var targetComp) ||

            targetComp.Whitelist?.Tags is null || giverComp.Whitelist?.Tags is null ||

            PopupCancels(targetComp, args.Target.Value, targetComp.Whitelist.Tags,
                         giverComp, giverUID, giverComp.Whitelist.Tags,
                                                            args.User))
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
        // Deleted already checks for null
        // but I do it here to tell compiler within scope that it isnt null
        // to stop repetitive null checking
        if (Deleted(args.Target) ||
            !TryComp<BallisticAmmoProviderComponent>(args.Target.Value, out var targetComp))
            return;

        StartAmmoSwap(1, giverUID, targetComp, args.Target.Value, args.User);

        args.Repeat = targetComp.AmmoCount < targetComp.Capacity // target has room for more ammo
                   && giverComp.AmmoCount > 0;                   // giver still has ammo left
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
            Disabled = component.AmmoCount == 0,
            Act = () => ManualCycle(uid, component, _xform.GetMapCoordinates(uid), args.User),
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
    /// How Ballistics does cycling. We call take ammo, but since nothing is taking it we just remove it from
    /// ballistics container that it WAS parented to(so has same coords as parent)
    /// Creates and raises GunCycledEvent
    /// </summary>
    /// <param name="uid">Gun or thing we are cycling</param>
    /// <param name="comp">comp of uid</param>
    /// <param name="user">user who triggered the event</param>
    /// <remarks>
    ///
    /// <remarks/>

    /// <summary>
    /// listening Comp supplies args with ammo data
    /// </summary>
    private void OnBallisticAmmoCount(EntityUid uid, BallisticAmmoProviderComponent component, ref GetAmmoCountEvent args)
    {
        args.Count = component.AmmoCount;
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
    public void UpdateBallisticAppearance(EntityUid uid, BallisticAmmoProviderComponent comp)
    {
        if (!TryComp<AppearanceComponent>(uid, out var appearance))
            return;

        Appearance.SetData(uid, AmmoVisuals.AmmoCount, comp.AmmoCount, appearance);
        Appearance.SetData(uid, AmmoVisuals.AmmoMax, comp.Capacity, appearance);
    }

    /// <summary>
    /// How Ballistic comps handle takeAmmo event.
    /// Any already spawned ammo is removed first, then we spawn ammo,
    /// decreasing amount of unspawned ammo
    /// </summary>
    /// <remarks>
    /// Side effects: giverComp.UnspawnedCount is decreased by ammo that had to be spawned and
    ///               already spawned ammo is removed from container
    /// <remarks/>
    private void OnBallisticTakeAmmo(EntityUid giverUID, BallisticAmmoProviderComponent giverComp, TakeAmmoEvent args)
    {
        //var rng = new System.Random();

        int ammoToSpawn = Math.Max(0, args.Shots - giverComp.Container.Count);
        ammoToSpawn = Math.Min(giverComp.AmmoCount, ammoToSpawn);
        int ammoToRemove = Math.Min(giverComp.Container.Count, args.Shots - ammoToSpawn);

        var ammo = giverComp.Container.ContainedEntities.Take(ammoToRemove);
        giverComp.UnspawnedCount -= ammoToSpawn;

        foreach (var shot in ammo)
        {
            args.Ammo.Add((shot, EnsureShootable(shot)));
            Containers.Remove(shot, giverComp.Container);
        }


        for (int i = 0; i < ammoToSpawn; i++)
        {
            var spawnedAmmo = PredictedSpawnAtPosition(giverComp.Proto, args.Coordinates);
            args.Ammo.Add((spawnedAmmo, EnsureShootable(spawnedAmmo)));

            RandomVector(args.Rng, GetNetEntity(giverUID), spawnedAmmo, giverComp.AmmoCount, Transform(spawnedAmmo).Coordinates.Position);
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
