


using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Client.GameObjects;
using Robust.Client.Input;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;

namespace Content.IntegrationTests.Tests._Misfits.GunSystem;

[TestFixture]
public sealed partial class BallisticAmmoProviderNetworkTests : BallisticAmmoProviderSetUp
{

    [Test]
    public async Task BallisticAmmoProviderClientNet()
    {

        EntityUid ammoBoxOne = default;
        NetEntity ammoBoxOneNet = default;

        EntityUid ammoBoxEmpty = default;
        NetEntity ammoBoxEmptyNet = default;

        await server.WaitPost(() =>
        {
            var coords = testMap.GridCoords;
            ammoBoxOne = sEntMan.SpawnEntity(BasicAmmoUseCaseProto, coords);
            ammoBoxOneNet = sEntMan.GetNetEntity(ammoBoxOne);

            ammoBoxEmpty = sEntMan.SpawnEntity(BasicAmmoUseCaseEmptyProto, coords);
            ammoBoxEmptyNet = sEntMan.GetNetEntity(ammoBoxEmpty);

        });

        await pair.SyncTicks();
        await pair.RunTicksSync(25);
        EntityUid ammoClientOne = default;
        EntityUid ammoClientEmpty = default;


        BallisticAmmoProviderComponent ammoCompOne = default;
        BallisticAmmoProviderComponent ammoCompEmpty = default;

        await client.WaitPost(() =>
        {
            ammoClientOne = cEntMan.GetEntity(ammoBoxOneNet);
            ammoClientEmpty = cEntMan.GetEntity(ammoBoxEmptyNet);

            ammoCompOne = cEntMan.GetComponent<BallisticAmmoProviderComponent>(ammoClientOne);
            ammoCompEmpty = cEntMan.GetComponent<BallisticAmmoProviderComponent>(ammoClientEmpty);


        });

        await server.WaitPost(() =>
               {
                   var playerEnt = sEntMan.GetEntity(player);
                   var hand = sEntMan.GetComponent<HandsComponent>(playerEnt);
                   Assert.That(server.System<SharedHandsSystem>().TryPickup(playerEnt, ammoBoxOne, hand.ActiveHand, false, false, hand));
               });


        await pair.SyncTicks();
        await pair.RunTicksSync(10);

        await Interact(EngineKeyFunctions.Use, BoundKeyState.Down, testMap.GridCoords, ammoClientEmpty);
        //await pair.RunTicksSync(2);
        await Interact(EngineKeyFunctions.Use, BoundKeyState.Up, testMap.GridCoords, ammoClientEmpty);
        await pair.RunTicksSync(2);
        await pair.SyncTicks();
        var ammoCompOneS = sEntMan.GetComponent<BallisticAmmoProviderComponent>(ammoBoxOne);
        var ammoCompEmptyS = sEntMan.GetComponent<BallisticAmmoProviderComponent>(ammoBoxEmpty);

        await pair.SyncTicks();
        await pair.RunTicksSync(sTime.TickRate * 8);
        await pair.SyncTicks();
        Assert.Multiple(() =>
                        {
                            Assert.That(ammoCompEmpty.AmmoCount == 32);
                            Assert.That(ammoCompEmpty.UnspawnedCount == 0);
                            Assert.That(ammoCompEmpty.SpawnedCountPredict == 32);

                            Assert.That(ammoCompOne.AmmoCount == 0);
                            Assert.That(ammoCompOne.UnspawnedCount == 0);
                            Assert.That(ammoCompOne.SpawnedCountPredict == 0);
                        });

        Assert.Multiple(() =>
        {
            Assert.That(ammoCompEmptyS.AmmoCount == 32);
            Assert.That(ammoCompEmptyS.UnspawnedCount == 0);
            Assert.That(ammoCompEmptyS.SpawnedCountPredict == 32);

            Assert.That(ammoCompOneS.AmmoCount == 0);
            Assert.That(ammoCompOneS.UnspawnedCount == 0);
            Assert.That(ammoCompOneS.SpawnedCountPredict == 0);
        });
    }

}
