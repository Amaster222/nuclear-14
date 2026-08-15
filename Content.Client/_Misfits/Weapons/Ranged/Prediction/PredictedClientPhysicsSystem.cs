using Robust.Client.GameObjects;
using Robust.Client.Physics;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;


/// <summary>
/// Based from <see cref="GunPredictionSystem"> which was ripped from RCM
/// Comp for it is <see cref="PredictedClientPhysicsComponent">
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
///
/// </summary>
namespace Content.Client._Misfits.Weapons.Ranged.Prediction;

public sealed partial class PredictedClientPhysicsSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private PhysicsSystem _physics = default!;
    [Dependency] private TransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        //SubscribeLocalEvent<PredictedClientPhysicsComponent, PhysicsUpdateBeforeSolveEvent>(OnBeforeSolve);
        //SubscribeLocalEvent<PredictedClientPhysicsComponent, PhysicsUpdateAfterSolveEvent>(OnAfterSolve);
        SubscribeLocalEvent<PredictedClientPhysicsComponent, UpdateIsPredictedEvent>(OnUpdatePredicted);
        SubscribeLocalEvent<PredictedClientPhysicsComponent, ComponentInit>(OnCompInit);
        UpdatesBefore.Add(typeof(TransformSystem));
    }

    /// kill a bird with a stone
    public void OnCompInit(EntityUid ent, PredictedClientPhysicsComponent comp, ComponentInit args)
    {
        _physics.UpdateIsPredicted(ent); ///
    }
    public void OnUpdatePredicted(Entity<PredictedClientPhysicsComponent> ent, ref UpdateIsPredictedEvent args)
    {
        args.IsPredicted = true;
    }

    public void OnBeforeSolve(ref PhysicsUpdateBeforeSolveEvent args)
    {
        var query = EntityQueryEnumerator<PredictedClientPhysicsComponent>();
        while (query.MoveNext(out var uid, out var predicted))
        {
            predicted.Coords = Transform(uid).Coordinates;
        }

    }

    public void OnAfterSolve(ref PhysicsUpdateAfterSolveEvent args)
    {
        var query = EntityQueryEnumerator<PredictedClientPhysicsComponent>();
        while (query.MoveNext(out var uid, out var predicted))
        {
            if (!_timing.IsFirstTimePredicted)
                continue;

            if (predicted.Coords != EntityCoordinates.Invalid)
                _transform.SetCoordinates(uid, predicted.Coords);

            predicted.Coords = EntityCoordinates.Invalid;
        }

    }


    /*
        public void OnCompInit(EntityUid ent, PredictedClientPhysicsComponent comp, ComponentInit args)
        {

        }
        public void OnBeforeSolve(Entity<PredictedClientPhysicsComponent> ent, ref PhysicsUpdateBeforeSolveEvent args)
        {

        }

        public void OnAfterSolve(Entity<PredictedClientPhysicsComponent> ent, ref PhysicsUpdateAfterSolveEvent args)
        {

        }
        */
}
