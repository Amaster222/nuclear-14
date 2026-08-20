using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared.Internals;

[Serializable, NetSerializable]
public sealed partial class InternalsDoAfterEvent : SimpleDoAfterEvent
{
    public ToggleMode ToggleMode = ToggleMode.Toggle;

    public InternalsDoAfterEvent()
    {
    }

    public InternalsDoAfterEvent(ToggleMode mode)
    {
        ToggleMode = mode;
    }
}
