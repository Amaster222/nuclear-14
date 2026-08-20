using Content.Goobstation.Common.Body;

namespace Content.Client._Shitmed.Body;

/// <summary>
/// Client compatibility implementation for Goob's body-cavity hook.
/// Mirrors the server-side stub: Misfits does not yet provide the chest-burst
/// action layer, so cavity changes remain side-effect free on the client.
/// </summary>
// #Cythisiax Added - Client-side implementation so SharedBodySystem's
// CommonInsideBodyPartSystem dependency resolves during client DI graph build.
public sealed class InsideBodyPartSystem : CommonInsideBodyPartSystem
{
    public override void InsertedIntoPart(EntityUid item, EntityUid part)
    {
    }

    public override void RemovedFromPart(EntityUid item)
    {
    }
}
