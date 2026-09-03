using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._Misfits.MaterialExtractor;

[RegisterComponent]
public sealed partial class MaterialExtractorComponent : Component
{
    // Balance fields. Keep gameplay tuning on the entity prototype, not in the system.
    [DataField] public Dictionary<string, int> OutputWeights = new()
    {
        ["N14IronOre1"] = 20,
        ["N14CopperOre1"] = 18,
        ["N14LeadOre1"] = 14,
        ["SulfurOre1"] = 12,
        ["N14Sand1"] = 12,
        ["Salt1"] = 10,
        ["N14ZincOre1"] = 6,
        ["N14BauxiteOre1"] = 5,
        ["FertilizerOre1"] = 3,
    };
    [DataField] public int PlayerActivationRadius = 30;
    [DataField] public int PulseIntervalSeconds = 2;
    [DataField] public int FirstWaveMinSeconds = 30;
    [DataField] public int FirstWaveMaxSeconds = 30;
    [DataField] public int WaveMinSeconds = 30;
    [DataField] public int WaveMaxSeconds = 30;
    [DataField] public int WaveWarningSeconds;
    [DataField] public float PoorDepositChance = 0.25f;
    [DataField] public float RichDepositChance = 0.15f;
    [DataField] public float PoorYieldMultiplier = 0.7f;
    [DataField] public float RichYieldMultiplier = 1.4f;

    public TimeSpan NextPulse;
    public TimeSpan NextWave;
    public TimeSpan DamagePauseUntil;
    public bool BeaconOn;
    public bool WarningSent;
    public bool WasRunning;
    public int WaveCount;
    public readonly HashSet<EntityUid> ActiveAttackers = [];
    public float YieldMultiplier = 1f;
    public string DepositQuality = "FAIR";
}
