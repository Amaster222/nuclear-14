// #Misfits Add - Verify N14 chemical syringes delay reagent transfer until injection completes.
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Misfits;

[TestFixture]
public sealed class N14SyringeInjectionDelayTests
{
    [TestCase(true, 1f)]
    [TestCase(false, 2f)]
    public async Task InjectionWaitsForConfiguredDelay(bool selfInject, float delaySeconds)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var hands = entities.System<SharedHandsSystem>();
        var solutions = entities.System<SharedSolutionContainerSystem>();

        EntityUid syringe = default;

        await server.WaitAssertion(() =>
        {
            var user = entities.SpawnEntity("MobHuman", map.GridCoords);
            var target = selfInject ? user : entities.SpawnEntity("MobHuman", map.GridCoords);
            syringe = entities.SpawnEntity("N14Stimpak", map.GridCoords);
            Assert.That(hands.TryPickupAnyHand(user, syringe, checkActionBlocker: false), Is.True);

            if (selfInject)
            {
                entities.EventBus.RaiseLocalEvent(syringe, new UseInHandEvent(user));
            }
            else
            {
                entities.EventBus.RaiseLocalEvent(syringe,
                    new AfterInteractEvent(user, syringe, target, map.GridCoords, true));
            }

            Assert.That(solutions.TryGetSolution(syringe, "pen", out _, out var solution), Is.True);
            Assert.That(solution.Volume, Is.EqualTo(FixedPoint2.New(25)));
        });

        await pair.RunSeconds(delaySeconds - 0.25f);

        await server.WaitAssertion(() =>
        {
            Assert.That(solutions.TryGetSolution(syringe, "pen", out _, out var solution), Is.True);
            Assert.That(solution.Volume, Is.EqualTo(FixedPoint2.New(25)));
        });

        await pair.RunSeconds(0.5f);

        await server.WaitAssertion(() =>
        {
            Assert.That(solutions.TryGetSolution(syringe, "pen", out _, out var solution), Is.True);
            Assert.That(solution.Volume, Is.EqualTo(FixedPoint2.Zero));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SyringeCannotQueueInjectionsAgainstMultipleTargets()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var hands = entities.System<SharedHandsSystem>();
        var solutions = entities.System<SharedSolutionContainerSystem>();

        EntityUid user = default;
        EntityUid inhaler = default;

        await server.WaitAssertion(() =>
        {
            user = entities.SpawnEntity("MobHuman", map.GridCoords);
            var firstTarget = entities.SpawnEntity("MobHuman", map.GridCoords);
            inhaler = entities.SpawnEntity("N14RadAwayInhaler", map.GridCoords);
            Assert.That(hands.TryPickupAnyHand(user, inhaler, checkActionBlocker: false), Is.True);

            entities.EventBus.RaiseLocalEvent(inhaler,
                new AfterInteractEvent(user, inhaler, firstTarget, map.GridCoords, true));
        });

        await pair.RunSeconds(0.75f);

        await server.WaitAssertion(() =>
        {
            var secondTarget = entities.SpawnEntity("MobHuman", map.GridCoords);
            entities.EventBus.RaiseLocalEvent(inhaler,
                new AfterInteractEvent(user, inhaler, secondTarget, map.GridCoords, true));
        });

        await pair.RunSeconds(2.25f);

        await server.WaitAssertion(() =>
        {
            Assert.That(solutions.TryGetSolution(inhaler, "pen", out _, out var solution), Is.True);
            Assert.That(solution.Volume, Is.EqualTo(FixedPoint2.New(20)));
        });

        await pair.CleanReturnAsync();
    }
}
