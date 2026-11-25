using System;
using BeatSaberTheater.Settings;
using UnityEngine;

namespace BeatSaberTheater.Screen;

internal sealed class BrightnessLimiter
{
    // Tune these in game: dim quickly, wait out the flash, then bring brightness back gradually.
    private const float AttackSeconds = 0.05f;
    private const float HoldSeconds = 0.3f;
    private const float ReleaseSeconds = 1.0f;

    private float _screenLinear;
    private float _holdRemaining;

    public float Luminance { get; set; }
    // The video shader's source is unavailable; its brightness and exposure are approximated as a product.
    public float MapFactor { get; set; } = 1f;
    public int Limit { get; private set; } = DisplaySettings.MaxBrightnessLimit;
    public float LimitLinear { get; private set; }
    public float Gain { get; private set; } = 1f;

    // The material multiplies gamma-space colour values, which Unity converts to linear in a linear-space project.
    // A linear factor k therefore needs the gamma-space multiplier (approximately k^(1/2.2)).
    public float MaterialGain => Mathf.LinearToGammaSpace(Gain);

    public void SetLevels(int brightness, int limit)
    {
        _screenLinear = ToLinearLevel(brightness);
        LimitLinear = ToLinearLevel(limit);
        Limit = limit;
    }

    public void Step(float dt)
    {
        var target = GetTargetGain();
        if (target < Gain)
        {
            Gain += (target - Gain) * (float)(1 - Math.Exp(-dt / AttackSeconds));
            _holdRemaining = HoldSeconds;
        }
        else if (target > Gain)
        {
            // Only time left after the hold contributes to the release on the frame that crosses it.
            var releaseTime = dt - _holdRemaining;
            _holdRemaining = Mathf.Max(0f, -releaseTime);
            if (releaseTime > 0f)
                Gain += (target - Gain) * (float)(1 - Math.Exp(-releaseTime / ReleaseSeconds));
        }
    }

    public void Snap()
    {
        Gain = GetTargetGain();
        _holdRemaining = 0f;
    }

    public void Reset()
    {
        Luminance = 0f;
        Gain = 1f;
        _holdRemaining = 0f;
    }

    private float GetTargetGain()
    {
        var average = Luminance * _screenLinear * MapFactor;
        // The 5% minimum at full Brightness needs a small gain, with a floor above zero.
        return Limit >= DisplaySettings.MaxBrightnessLimit || average <= LimitLinear
            ? 1f
            : Mathf.Clamp(LimitLinear / average, 0.001f, 1f);
    }

    private static float ToLinearLevel(int percent) =>
        Mathf.GammaToLinearSpace(CustomVideoPlayer.MAX_BRIGHTNESS * Mathf.Clamp(percent, 0, 100) / 100f);
}
