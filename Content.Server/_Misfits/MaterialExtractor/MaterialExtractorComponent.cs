using Robust.Shared.GameObjects;

namespace Content.Server._Misfits.MaterialExtractor;

[RegisterComponent]
public sealed partial class MaterialExtractorComponent : Component
{
    public TimeSpan NextPulse;
    public TimeSpan NextOutput;
    public TimeSpan NextWave;
    public TimeSpan DamagePauseUntil;
    public bool BeaconOn;
    public bool WarningSent;
    public bool OutputBlocked;
    public int WaveCount;
    public readonly HashSet<EntityUid> ActiveAttackers = [];
    public float YieldMultiplier = 1f;
    public string DepositQuality = "FAIR";
}
