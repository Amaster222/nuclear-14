using System.Numerics;
using Content.Shared.Weather;
using Robust.Client.Graphics;

namespace Content.Client.Overlays;

public sealed partial class StencilOverlay
{
    private void DrawWeather(in OverlayDrawArgs args, WeatherPrototype weatherProto, float alpha, Matrix3x2 invMatrix)
    {
        if (weatherProto.Sprite == null)
            return;
        var worldHandle = args.WorldHandle;
        var worldAABB = args.WorldAABB;
        var position = args.Viewport.Eye?.Position.Position ?? Vector2.Zero;
        var viewport = args.Viewport;
        var renderScale = viewport.RenderScale.X;
        var viewportSize = viewport.Size;
        var hasEye = viewport.Eye != null;
        var eyePosition = viewport.Eye?.Position.Position ?? Vector2.Zero;
        var eyeZoom = viewport.Eye?.Zoom ?? Vector2.One;

        worldHandle.SetTransform(Matrix3x2.Identity);
        var curTime = _timing.RealTime;
        var sprite = _sprite.GetFrame(weatherProto.Sprite, curTime);

        if (weatherProto.VisibilityClearRadius > 0f && hasEye)
        {
            var length = eyeZoom.X;
            var pixelCenter = Vector2.Transform(eyePosition, invMatrix);
            var pixelMaxRange = weatherProto.VisibilityClearRadius * renderScale / length * EyeManager.PixelsPerMeter;
            var pixelBufferRange = MathF.Max(1f, weatherProto.VisibilityClearBuffer * renderScale / length * EyeManager.PixelsPerMeter);
            var pixelMinRange = MathF.Max(0f, pixelMaxRange - pixelBufferRange);

            _weatherDrawShader.SetParameter("position", new Vector2(pixelCenter.X, viewportSize.Y - pixelCenter.Y));
            _weatherDrawShader.SetParameter("maxRange", pixelMaxRange);
            _weatherDrawShader.SetParameter("minRange", pixelMinRange);
            _weatherDrawShader.SetParameter("bufferRange", pixelBufferRange);
        }
        else
        {
            _weatherDrawShader.SetParameter("maxRange", 0f);
            _weatherDrawShader.SetParameter("minRange", 0f);
            _weatherDrawShader.SetParameter("bufferRange", 1f);
        }

        _weatherDrawShader.SetParameter("gradient", 0.80f);
        worldHandle.UseShader(_weatherDrawShader);

        _parallax.DrawParallax(worldHandle, worldAABB, sprite, curTime, position, Vector2.Zero, modulate: (weatherProto.Color ?? Color.White).WithAlpha(alpha));

        worldHandle.SetTransform(Matrix3x2.Identity);
        worldHandle.UseShader(null);
    }
}
