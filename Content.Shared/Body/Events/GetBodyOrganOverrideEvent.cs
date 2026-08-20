using Content.Shared.Body.Organ;

namespace Content.Shared.Body.Events;

/// <summary>
/// Allows body implementations to supply a single authoritative organ for a
/// query before the normal body tree is inspected.
/// </summary>
[ByRefEvent]
public record struct GetBodyOrganOverrideEvent<T>() where T : IComponent
{
    public Entity<T, OrganComponent>? Organ;
}
