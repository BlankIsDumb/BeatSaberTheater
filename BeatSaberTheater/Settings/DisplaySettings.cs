using System;

namespace BeatSaberTheater.Settings;

/// <summary>
/// The player's display settings, shared by the Display view in the Theater tab and the main menu settings.
/// Both UIs read and write the same PluginConfig values through this class, and the video player listens to the
/// events to apply changes immediately.
/// </summary>
public class DisplaySettings
{
    public const int MinBrightness = 10;
    public const int MaxBrightness = 100;

    // 25% keeps the screen readable; up to 300% allows a dim, huge "ambient cinema" screen
    public const int MinScreenScale = 25;
    public const int MaxScreenScale = 300;

    public const int MaxCornerRoundness = 100;
    public const int MaxEdgeSoftness = 100;
    public const int MaxBloom = 150;

    // Shader softness at EdgeSoftness = 100%. 0% keeps the default anti-aliased edge.
    public const float MaxVignetteSoftness = 0.5f;

    private readonly PluginConfig _config;

    /// <summary>A visual setting (brightness, size, corners, edges, bloom) changed.</summary>
    public event Action? Changed;

    /// <summary>The player turned Theater on or off.</summary>
    public event Action<bool>? TheaterEnabledChanged;

    public DisplaySettings(PluginConfig config)
    {
        _config = config;
    }

    public bool TheaterEnabled
    {
        get => _config.TheaterEnabled;
        set
        {
            if (_config.TheaterEnabled == value) return;
            _config.TheaterEnabled = value;
            TheaterEnabledChanged?.Invoke(value);
        }
    }

    public int Brightness
    {
        get => Clamp(_config.ScreenBrightness, MinBrightness, MaxBrightness);
        set => SetAndNotify(() => _config.ScreenBrightness = Clamp(value, MinBrightness, MaxBrightness));
    }

    public int ScreenScale
    {
        get => Clamp(_config.ScreenScale, MinScreenScale, MaxScreenScale);
        set => SetAndNotify(() => _config.ScreenScale = Clamp(value, MinScreenScale, MaxScreenScale));
    }

    public int CornerRoundness
    {
        get => (int)Math.Round(_config.CornerRoundness * 100);
        set => SetAndNotify(() => _config.CornerRoundness = Clamp(value, 0, MaxCornerRoundness) / 100f);
    }

    public int EdgeSoftness
    {
        get => Clamp(_config.EdgeSoftness, 0, MaxEdgeSoftness);
        set => SetAndNotify(() => _config.EdgeSoftness = Clamp(value, 0, MaxEdgeSoftness));
    }

    public int Bloom
    {
        get => Clamp(_config.BloomIntensity, 0, MaxBloom);
        set => SetAndNotify(() => _config.BloomIntensity = Clamp(value, 0, MaxBloom));
    }

    /// <summary>Slider label for percentage settings. BSML passes the slider value boxed, as int or float.</summary>
    public static string FormatPercent(object value)
    {
        return $"{Convert.ToSingle(value):0}%";
    }

    private void SetAndNotify(Action set)
    {
        set();
        Changed?.Invoke();
    }

    private static int Clamp(int value, int min, int max)
    {
        return Math.Max(min, Math.Min(max, value));
    }
}
