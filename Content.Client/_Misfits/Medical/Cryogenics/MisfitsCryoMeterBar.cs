using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._Misfits.Medical.Cryogenics;

public sealed class MisfitsCryoMeterBar : ProgressBar
{
    private static readonly Color InBandColor = new(0.35f, 0.85f, 0.45f);
    private static readonly Color OutOfBandColor = new(0.85f, 0.35f, 0.35f);

    public void SetReading(float value, float max, bool inBand)
    {
        MinValue = 0f;
        MaxValue = max > 0f ? max : 1f;
        Value = value;

        ForegroundStyleBoxOverride ??= new StyleBoxFlat();
        var foreground = (StyleBoxFlat) ForegroundStyleBoxOverride!;
        foreground.BackgroundColor = inBand ? InBandColor : OutOfBandColor;
    }
}
