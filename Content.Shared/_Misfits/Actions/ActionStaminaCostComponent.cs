namespace Content.Shared._Misfits.Actions;

/// <summary>
/// A component added to Actions to restrict their use based on stamina.
/// </summary>
[RegisterComponent]
public sealed partial class ActionStaminaCostComponent : Component
{
    [DataField]
    public float Stamina;
}
