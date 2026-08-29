// # #Cythisiax Added - Marks the sentient deathclaw as able to smash through structures that have no
// Damageable component (e.g. "indestructible" walls), which normal damage can never touch.
using Robust.Shared.GameObjects;

namespace Content.Server._Misfits.Deathclaw;

[RegisterComponent]
public sealed partial class StructureBreakerComponent : Component;
