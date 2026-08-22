namespace Content.Shared.Internals;

public enum ToggleMode
{
    Toggle,
    On,
    Off,
}

public sealed partial class ToggleInternalsAlertEvent : EntityEventArgs
{
    public bool Handled;
}
