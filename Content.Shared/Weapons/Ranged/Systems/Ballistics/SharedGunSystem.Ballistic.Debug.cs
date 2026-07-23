using Content.Shared.Weapons.Ranged.Components;

namespace Content.Shared.Weapons.Ranged.Systems;

public abstract partial class SharedGunSystem
{
    /// TODO: debug tool to get every single prototype that doesnt follow rules
    /// <summary>
    /// debug just for knowing which prototypes in yaml cause issues for future reference
    /// </summary>
    private void DebugInfo(EntityUid uid, BallisticAmmoProviderComponent comp)
    {
        if (comp.UnspawnedCount > 0 && comp.Proto is null)
            Log.Error($"Ballistic Comp has ammo but no ammo prototype... uid:{uid} Proto:{Prototype(uid)} ");

        if (comp.UnspawnedCount > comp.Capacity)
            Log.Error($"Ballistic Comp of Proto: {Prototype(uid)} owner UID:{uid} unspawnedCount > capacity: {comp.UnspawnedCount} > {comp.Capacity}");

        if (comp.AmmoCount > comp.Capacity)
            Log.Error($"Ballistic Comp of Proto: {Prototype(uid)} owner UID:{uid} GetBallisticShots(component) > capacity: {comp.AmmoCount} > {comp.Capacity}");

        if (comp.Container.ContainedEntities.Count > comp.Capacity)
            Log.Error($"Ballistic Comp of Proto: {Prototype(uid)} owner UID:{uid} Container.ContainedEntities.Count > capacity: {comp.Container.ContainedEntities.Count} > {comp.Capacity}");
    }
}
