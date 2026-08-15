using Robust.Shared.Map;

/// <summary>
/// Based from <see cref="PredictedProjectileClientComponent"> in the same file which was ripped from RCM.
/// Comp that listens for the client physics events and calls <see cref="PhysicsSystem.UpdateIsPredicted"/>UpdateIsPredicted on CompInit
///
/// <see cref="PhysicsSystem.PhysicsUpdateBeforeSolveEvent"/>
/// <see cref="PhysicsSystem.PhysicsUpdateAfterSolveEvent"/>
///
/// allow physic updates without being ent deleted or reset by prediction (so extends prediction basically)
/// Of course can use alongside other comps so take advantage of this with your own implementations
/// DONT! mark as predicted or use anything that will mark it else itll just be deleted (ie... SpawnPredicted)
///
/// Ideally should be used for clientside that dont need be to perfectly sync(ie dynamic visuals)
/// Can still send visuals to other clients(networked events ect...) even from the server
/// tho I suggest not having the server waste time computing/handling anything
/// and just have it network stuff since that'll be missing the point
///
/// </summary>
namespace Content.Client._Misfits.Weapons.Ranged.Prediction;
//TODO put in corect file
[RegisterComponent]
public sealed partial class PredictedClientPhysicsComponent : Component
{
    [DataField]
    public EntityCoordinates Coords = EntityCoordinates.Invalid;
}
