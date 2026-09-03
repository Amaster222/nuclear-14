using Content.Shared._Misfits.Medical.Cryogenics;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Misfits.Medical.Cryogenics;

[UsedImplicitly]
public sealed class MisfitsCryoPodBoundUserInterface : BoundUserInterface
{
    private MisfitsCryoPodWindow? _window;

    public MisfitsCryoPodBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<MisfitsCryoPodWindow>();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not MisfitsCryoPodBoundUserInterfaceState cryoState)
            return;

        _window?.UpdateState(cryoState);
    }
}
