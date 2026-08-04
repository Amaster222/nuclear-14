using System.Linq;
using Content.Shared.Tag;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Containers;
using Robust.Shared.Map;




namespace Content.Shared.Weapons.Ranged.Systems;
/// <summary>
/// More detailed implementation for event handlers of <see cref="BallisticAmmoProviderComponent"/>
/// Some methods were made to also be reusable with other comps(pending further refactor)
/// </summary>
public abstract partial class SharedGunSystem
{

    /// <summary>
    /// Point of no return when we decide we can takeammo from giver and give to target
    /// without worrying about edge cases.
    /// This shouldn't be interrupted!!!!
    /// </summary>
    /// <param name="ammoAmount">Ammo we TRY to take from giverUID. Though not guaranteed(ie. not enough ammo, or other mechanic ect)</param>
    /// <param name="giverUID">UID who we take ammo from(should have comps that listen to event)</param>
    /// <param name="recieverComp">Comp that recieves taken ammo and is updated</param>
    /// <param name="recieverUid">UID with comp that recieves taken ammo and is updated</param>
    /// <param name="user"> user that caused event(ie. player interacting with ammo box)</param>
    private void StartAmmoSwap(int ammoAmount,
                            EntityUid giverUID,
                            BallisticAmmoProviderComponent recieverComp, EntityUid recieverUid,
                            EntityUid user)
    {

        var ammo = GetAmmo(ammoAmount, giverUID, user);
        if (ammo.Count == 0) _popup.PopupPredicted(Loc.GetString("gun-general-empty"), giverUID, user);
        DoAmmoSwap(ammo, recieverComp);

        Audio.PlayPredicted(recieverComp.SoundInsert, giverUID, user);
        Dirty(recieverUid, recieverComp);
        UpdateBallisticAppearance(recieverUid, recieverComp);
        UpdateAmmoCount(recieverUid);
    }

    /// <summary>
    /// Take some or none amount of ammo from giverUID returning a list of that ammo
    /// How this is done is up to comps of giverUID that listen to <see cref="TakeAmmoEvent"/>
    /// THIS REMOVES AMMO ENTITIES FROM THE GIVING CONTAINER!!!!
    /// SO DONT CANCEL W/O EXPECTING SIDE EFFECTS!!!!
    /// </summary>
    /// <param name="ammoAmount">Ammo we TRY to take from giverUID. Though not guaranteed(ie. not enough ammo, or other mechanic ect)</param>
    /// <param name="giverUID">UID who we take ammo from(should have comps that listen to event)</param>
    /// <param name="user"> user that caused event (ie. player interacting with ammo box)</param>
    /// <returns>list of tuples with UID and Ishootable of spawned ammo</returns>
    /// <remarks>
    /// Gets rid of 3 lines of boiler plate, but also makes it clear that we just
    /// get and use the returned ammo, no handling from here needed
    /// <remarks/>
    public List<(EntityUid?, IShootable)> GetAmmo(int ammoAmount, EntityUid giverUID, EntityUid user)
    {
        List<(EntityUid? Entity, IShootable Shootable)> ammo = new(ammoAmount);
        var evTakeAmmo = new TakeAmmoEvent(ammoAmount, ammo, Transform(giverUID).Coordinates, user);
        RaiseLocalEvent(giverUID, evTakeAmmo);
        return ammo;
    }

    /// <summary>
    /// Same as above method, but doesnt return ammo. Used for things
    /// where we only want to "remove" ammo from container and not get/take it
    /// So doesnt return ammo
    /// THIS STILL REMOVES AMMO ENTITIES FROM THE GIVING CONTAINER!!!!
    /// SO DONT CANCEL WITHOUT EXPECTING SIDE EFFECTS!!!!
    /// </summary>
    public void DoTakeAmmo(int ammoAmount, EntityUid giverUID, EntityUid? user = null, bool rng = false)
    {
        List<(EntityUid? Entity, IShootable Shootable)> ammo = new(ammoAmount);
        RaiseLocalEvent(giverUID, new TakeAmmoEvent(ammoAmount, ammo, Transform(giverUID).Coordinates, user, rng));
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
    /// Cycling specific to ballisticAmmoProvider.
    /// Manual in that it is player triggered
    /// </summary>
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
        Loc.GetString(component.AmmoCount == 0 ?
        "gun-ballistic-cycled-empty" : "gun-ballistic-cycled")
        , uid, user);

        Cycle(uid, component, coordinates, user);

        Dirty(uid, component);
        UpdateBallisticAppearance(uid, component);
        UpdateAmmoCount(uid);
    }
    /// <summary>
    /// Method where taken ammo is specifically tied to  a gun being cycled(GunCycledEvent)
    /// </summary>
    /// <remarks>GunCycledEvent seems unused for now<remarks/>
    protected void Cycle(EntityUid uid, BallisticAmmoProviderComponent comp, MapCoordinates coordinates, EntityUid user)
    {
        DoTakeAmmo(1, uid, user, true);
        var cycledEvent = new GunCycledEvent();
        RaiseLocalEvent(uid, ref cycledEvent);
    }


    /// <summary>
    /// Corrects comp values from bad yaml to prevent errors
    /// unspawned = capacity - containedEnts if prototype isnt null else 0
    /// Containers cant go over capacity else they get cleared
    /// </summary>
    private void EnsureCorrect(EntityUid uid, BallisticAmmoProviderComponent comp)
    {
        // this isnt only "make container if null". Each comp with a container needs its owning entity
        // to also have a containerManagerComp which handles stuff like initializing containers
        // so this ensures container and containerManagerComp for ent
        // I dont know why this couldn't be done earlier like during serialization seems like an inefficency
        comp.Container = Containers.EnsureContainer<Container>(uid, "ballistic-ammo");

        if (comp.Proto is null && comp.UnspawnedCount > 0)
        {
            comp.UnspawnedCount = 0;
        }
        else if (comp.UnspawnedCount == DEFAULT_AMMO)
        {
            comp.UnspawnedCount = Math.Clamp(Math.Min(comp.Capacity, comp.Capacity - comp.Container.ContainedEntities.Count), 0, comp.Capacity);
        }

        if (comp.Container.ContainedEntities.Count > comp.Capacity)
        {
            Containers.CleanContainer(comp.Container);
        }
    }
    /// <summary>
    /// Should this valid ballisticAmmoProvider use a do after?
    /// </summary>
    private bool CanInstantFill(EntityUid giver) => HasComp<SpeedLoaderComponent>(giver);

    /// <summary>
    /// Big method of ingame popups that can happen on afterinteraction
    /// </summary>
    private bool PopupCancels(BallisticAmmoProviderComponent recieverComp, EntityUid recieverUid, List<ProtoId<TagPrototype>> recieverTags,
                                BallisticAmmoProviderComponent giverComp, EntityUid giverUid, List<ProtoId<TagPrototype>> giverTags,
                                EntityUid user)
    {

        if (recieverComp.AmmoCount == recieverComp.Capacity)
        {
            _popup.PopupPredicted(
                Loc.GetString("gun-ballistic-transfer-target-full",
                    ("entity", MetaData(recieverUid).EntityName)),
                recieverUid,
                user);
            return true;
        }

        if (giverComp.AmmoCount == 0)
        {
            _popup.PopupPredicted(
                Loc.GetString("gun-ballistic-transfer-empty",
                    ("entity", MetaData(giverUid).EntityName)),
                giverUid,
                user);
            return true;
        }

        if (!recieverTags.Any(giverTags.Contains))
        {
            _popup.PopupPredicted(
                        Loc.GetString("gun-ballistic-transfer-invalid",
                            ("ammoEntity", MetaData(giverUid).EntityName),
                            ("targetEntity", MetaData(recieverUid).EntityName)),
                        giverUid,
                        user);
            return true;
        }
        return false;
    }

}
