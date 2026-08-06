using Content.Client.Interactable.Components;

namespace Content.Client.Weapons.Ranged.Systems;

public sealed partial class GunSystem
{

    /// strips client only comps of spent cart
    /// <see cref="SharedGunSystem.Cartridges"/>
    public override void StripCartComps(EntityUid uid)
    {
        RemComp<InteractionOutlineComponent>(uid);
    }

}
