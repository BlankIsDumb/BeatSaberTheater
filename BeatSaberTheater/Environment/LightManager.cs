using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using BeatSaberTheater.Harmony.Patches;
using BeatSaberTheater.Screen;
using BeatSaberTheater.Settings;
using BeatSaberTheater.Util;
using BS_Utils.Utilities;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Video;
using Zenject;

namespace BeatSaberTheater.Environment;

public class LightManager : MonoBehaviour
{
    private bool _readbackPending;
    private bool _useReadbackLuminance;
    private bool _snapOnNextReadback;
    private bool? _decodeReadbackColor;
    private Action<AsyncGPUReadbackRequest> _readbackCallback = null!;
    private readonly BrightnessLimiter _limiter = new();
    private float _lastMaterialGain = 1f;

    // Temporary brightness limiter logging. The 5 s window fits up to ~400 readbacks/sec without growing the buffer.
    private readonly Stopwatch _readbackRequestStopwatch = new();
    private bool _collectLimiterPerf;
    private int _limiterDimmedFrames;
    private float _lowestLimiterGain = 1f;
    private readonly double[] _readbackDelaysMs = new double[2048];
    private int _readbackDelayCount;
    private double _maxReadbackDelayMs;

    private GameObject _lightGameObject = null!;
    private DirectionalLight _light = null!;
    private List<RenderTexture> _downscaleTextures = null!;
    private Color _color;
    private MaterialLightWithId? _menuFloorLight;
    private InstancedMaterialLightWithId? _menuFogRing;
    private bool _menuReferencesSet;
    private bool _useMenuLighting;
    private CustomVideoPlayer _videoPlayer = null!;

    private const int INITIAL_DOWNSCALING_SIZE = 128;
    private const float DIRECTIONAL_LIGHT_INTENSITY_MENU = 1.1f;

    private const float DIRECTIONAL_LIGHT_INTENSITY_GAMEPLAY = 2.0f;

    //TODO: Radius should ideally depend on screen size and maybe distance
    private const int LIGHT_RADIUS = 250;
    private const int LIGHT_X_ROTATION = 15;
    private const float MENU_FLOOR_INTENSITY = 0.7f;
    private const float MENU_DARKENING_INTENSITY = 0.6f;
    private const float MENU_FOG_INTENSITY = 0.35f;
    private const float MAX_BYTE_AS_FLOAT = byte.MaxValue;

    [Inject] private readonly LoggingService _loggingService = null!;
    [Inject] private readonly PluginConfig _config = null!;

    internal void Startup(CustomVideoPlayer videoPlayer)
    {
        _videoPlayer = videoPlayer;
        if (_lightGameObject == null) CreateLight();
        _useMenuLighting = TheaterFileHelpers.GetEnvironmentName() == "MainMenu";
        _limiter.SetLevels(_config.ScreenBrightness, GetBrightnessLimit());
        _readbackCallback = OnReadbackComplete;

        _videoPlayer.AddFrameReadyEventHandler(ProcessFrame);
        _videoPlayer.stopped += VideoStopped;
        _videoPlayer.AddEasingUpdateEventHandler(OnFadeUpdate);
        Events.LevelSelected += OnLevelSelected;
        BSEvents.menuSceneLoaded += OnMenuSceneLoaded;
        BSEvents.lateMenuSceneLoadedFresh += OnMenuSceneLoadedFresh;
    }

    #region Unity Event Functions

    private void Awake()
    {
        var textureCount = (int)Math.Ceiling(Math.Log(INITIAL_DOWNSCALING_SIZE, 2)) + 1;
        _downscaleTextures = new List<RenderTexture>(textureCount);
        for (var i = 1; i <= textureCount; i++)
        {
            var size = (int)Math.Pow(2, textureCount - i);
            _downscaleTextures.Add(new RenderTexture(size, size, 0));
        }
    }

    private void Update()
    {
        // SnapLimiter applies unity gain as soon as Off is selected, so this is the whole idle path.
        if (_limiter.Limit >= DisplaySettings.MaxBrightnessLimit) return;

        _limiter.Step(Time.unscaledDeltaTime);

        // Temporary: count dimmed Unity frames here. The song perf window already counts every Unity frame.
        if (_collectLimiterPerf)
        {
            _lowestLimiterGain = Mathf.Min(_lowestLimiterGain, _limiter.Gain);
            if (_limiter.Gain < 0.98f) _limiterDimmedFrames++;
        }

        var materialGain = _limiter.MaterialGain;
        if (Mathf.Abs(materialGain - _lastMaterialGain) <= 0.002f) return;

        PushLimiterGain(materialGain);
        UpdateColor(_color);
    }

    private void OnDisable()
    {
        _videoPlayer.RemoveFrameReadyEventHandler(ProcessFrame);
        _videoPlayer.stopped -= VideoStopped;
        _videoPlayer.RemoveEasingUpdateEventHandler(OnFadeUpdate);
        Events.LevelSelected -= OnLevelSelected;
        BSEvents.menuSceneLoaded -= OnMenuSceneLoaded;
        BSEvents.lateMenuSceneLoadedFresh -= OnMenuSceneLoadedFresh;

        VideoStopped();
    }

    #endregion

    #region Event Handlers

    private void OnLevelSelected(LevelSelectedArgs levelSelectedArgs)
    {
        if (_menuReferencesSet) return;

        try
        {
            GetMenuReferences();
        }
        catch (Exception e)
        {
            _loggingService.Error(e);
        }
    }

    internal void OnGameSceneLoaded()
    {
        var euler = _lightGameObject.transform.eulerAngles;
        euler.x = LIGHT_X_ROTATION;
        _light.intensity = DIRECTIONAL_LIGHT_INTENSITY_GAMEPLAY;

        // Cache the scene check so limiter pushes don't enumerate scenes and fetch their names each frame.
        var environmentName = TheaterFileHelpers.GetEnvironmentName();
        _useMenuLighting = environmentName == "MainMenu";
        switch (environmentName)
        {
            case "BillieEnvironment":
                //Tone down lighting on this env a bit, since clouds get pretty bright
                _light.intensity = 1.2f;
                euler.x = 42;
                break;
            case "BTSEnvironment":
                //Same as with Billie, clouds are too bright
                _light.intensity = 1.6f;
                euler.x = 55;
                break;
            case "LizzoEnvironment":
                //Background objects behind player too bright
                _light.intensity = 1f;
                euler.x = 42;
                break;
        }

        _lightGameObject.transform.eulerAngles = euler;
    }

    private void OnMenuSceneLoaded()
    {
        _useMenuLighting = true;
        var euler = _lightGameObject.transform.eulerAngles;
        euler.x = LIGHT_X_ROTATION;
        _lightGameObject.transform.eulerAngles = euler;
        _light.intensity = DIRECTIONAL_LIGHT_INTENSITY_MENU;
    }

    private void OnMenuSceneLoadedFresh(ScenesTransitionSetupDataSO scenesTransitionSetupDataSo)
    {
        try
        {
            if (_lightGameObject == null) CreateLight();
            GetMenuReferences();
        }
        catch (Exception e)
        {
            _loggingService.Error(e);
        }
    }

    #endregion

    private void GetMenuReferences()
    {
        _menuFloorLight = Resources.FindObjectsOfTypeAll<MaterialLightWithId>()
            .FirstOrDefault(x => x.gameObject.name == "BasicMenuGround");
        _menuFogRing = Resources.FindObjectsOfTypeAll<InstancedMaterialLightWithId>()
            .FirstOrDefault(x => x.gameObject.name == "MenuFogRing");
        _menuReferencesSet = true;
    }

    private void OnFadeUpdate(float f)
    {
        UpdateColor(_color);
    }

    private void CreateLight()
    {
        var screen = _videoPlayer.GetFirstScreen();
        if (screen != null)
        {
            _lightGameObject = new GameObject("TheaterDirectionalLight");
            _lightGameObject.transform.parent = screen.transform;
            _lightGameObject.transform.forward = -screen.transform.forward;
            var euler = _lightGameObject.transform.eulerAngles;
            euler.x = LIGHT_X_ROTATION;
            _lightGameObject.transform.eulerAngles = euler;

            _light = _lightGameObject.AddComponent<DirectionalLight>();
            _light.radius = LIGHT_RADIUS;
            _light.intensity = DIRECTIONAL_LIGHT_INTENSITY_MENU;
            _light.color = Color.black;
        }
    }

    private void VideoStopped()
    {
        // A readback from the stopped video can still finish; it must not dim the next video.
        _useReadbackLuminance = false;
        _limiter.Reset();
        PushLimiterGain(1f);
        UpdateColor(Color.black);
    }

    private void ProcessFrame(VideoPlayer source, long frameIdx)
    {
        if (_light == null || !source.isPlaying) return;

        var lowResTex = Downscale(source.texture);

        // Keep one readback in flight so the callback gets the matching timing and luminance validity.
        if (_readbackPending) return;

        _readbackPending = true;
        _useReadbackLuminance = true;
        _readbackRequestStopwatch.Restart(); // Temporary brightness limiter timing
        AsyncGPUReadback.Request(lowResTex, 0, _readbackCallback);
    }

    private void OnReadbackComplete(AsyncGPUReadbackRequest request)
    {
        _readbackPending = false;
        if (request.hasError) return;
        if (!_useReadbackLuminance) return;

        var limiterOff = _limiter.Limit >= DisplaySettings.MaxBrightnessLimit;
        var delayMs = limiterOff ? 0d : _readbackRequestStopwatch.Elapsed.TotalMilliseconds; // Temporary brightness limiter timing
        // Read the RGBA channels directly to avoid allocating an array for the single pixel.
        var pixel = request.GetData<Color32>()[0];
        var color = new Color(pixel.r / MAX_BYTE_AS_FLOAT, pixel.g / MAX_BYTE_AS_FLOAT, pixel.b / MAX_BYTE_AS_FLOAT);
        UpdateColor(color);

        if (_decodeReadbackColor is not { } decode)
        {
            var colorSpace = QualitySettings.activeColorSpace;
            var readbackSrgb = _downscaleTextures[_downscaleTextures.Count - 1].sRGB;
            decode = colorSpace != ColorSpace.Linear || readbackSrgb;
            _decodeReadbackColor = decode;

            // Temporary brightness limiter logging
            _loggingService.Info($"Brightness limiter: colour space {colorSpace}, readback texture sRGB " +
                                 $"{(readbackSrgb ? "true" : "false")}, decoding {(decode ? "true" : "false")}");
        }

        if (limiterOff)
        {
            _limiter.Luminance = 0f;
            return;
        }

        // A linear project's non-sRGB render target already stores linear values.
        var r = decode ? Mathf.GammaToLinearSpace(color.r) : color.r;
        var g = decode ? Mathf.GammaToLinearSpace(color.g) : color.g;
        var b = decode ? Mathf.GammaToLinearSpace(color.b) : color.b;
        _limiter.Luminance = 0.2126f * r + 0.7152f * g + 0.0722f * b;
        if (_snapOnNextReadback)
        {
            _snapOnNextReadback = false;
            _limiter.Snap();
            PushLimiterGain(_limiter.MaterialGain);
            UpdateColor(_color);
        }

        RecordLimiterReadbackDelay(delayMs); // Temporary brightness limiter timing
    }

    internal void SetMapFactor(float mapFactor)
    {
        _limiter.MapFactor = mapFactor;
    }

    internal void SnapOnFirstFrame()
    {
        // Keep the current gain until the new video's luminance is available.
        _snapOnNextReadback = true;
    }

    internal void SnapLimiter()
    {
        var limit = GetBrightnessLimit();
        var limitChanged = limit != _limiter.Limit; // Temporary brightness limiter logging
        _limiter.SetLevels(_config.ScreenBrightness, limit);
        _limiter.Snap();
        PushLimiterGain(_limiter.MaterialGain);
        UpdateColor(_color);

        // Temporary brightness limiter logging, only with DebugLogging on (one line per slider step)
        if (limitChanged && _config.DebugLogging)
            _loggingService.Info(limit >= DisplaySettings.MaxBrightnessLimit
                ? "Brightness limiter: off"
                : FormattableString.Invariant($"Brightness limiter: limit {limit}% ({_limiter.LimitLinear:0.000} linear)"));
    }

    internal void ShowWhiteTest(bool on)
    {
        // The test owns luminance until video playback takes over, even if an earlier readback is still pending.
        _useReadbackLuminance = false;
        if (on)
        {
            _limiter.Luminance = 1f;
            SnapLimiter();
        }
        else
        {
            _limiter.Reset();
            PushLimiterGain(1f);
            UpdateColor(_color);
        }
    }

    private int GetBrightnessLimit() =>
        Mathf.Clamp(_config.BrightnessLimit, DisplaySettings.MinBrightnessLimit, DisplaySettings.MaxBrightnessLimit);

    private void PushLimiterGain(float materialGain)
    {
        _videoPlayer.SetLimiterGain(materialGain);
        _lastMaterialGain = materialGain;
    }

    // ---- Temporary brightness limiter performance logging ----
    private void RecordLimiterReadbackDelay(double delayMs)
    {
        if (!_collectLimiterPerf) return;
        if (_readbackDelayCount < _readbackDelaysMs.Length)
            _readbackDelaysMs[_readbackDelayCount++] = delayMs;
        _maxReadbackDelayMs = Math.Max(_maxReadbackDelayMs, delayMs);
    }

    internal void LogLimiterPerfWindow(float songTime, int frameCount)
    {
        var median = 0d;
        if (_readbackDelayCount > 0)
        {
            Array.Sort(_readbackDelaysMs, 0, _readbackDelayCount);
            var middle = _readbackDelayCount / 2;
            median = (_readbackDelaysMs[middle] + _readbackDelaysMs[(_readbackDelayCount - 1) / 2]) / 2;
        }

        var setting = _limiter.Limit >= DisplaySettings.MaxBrightnessLimit
            ? "off"
            : _limiter.Limit.ToString(CultureInfo.InvariantCulture) + "%";
        var dimmedPercent = 100f * _limiterDimmedFrames / frameCount;
        _loggingService.Info(string.Format(CultureInfo.InvariantCulture,
            "Brightness limiter (song at {0:0.0} s): {1}, lowest gain {2:0.00}, dimmed {3:0} % of frames, " +
            "readback delay median {4:0.0} ms, max {5:0.0} ms",
            songTime, setting, _lowestLimiterGain, dimmedPercent, median, _maxReadbackDelayMs));
    }

    internal void ResetLimiterPerfWindow(bool active)
    {
        _collectLimiterPerf = active;
        _limiterDimmedFrames = 0;
        _lowestLimiterGain = 1f;
        _readbackDelayCount = 0;
        _maxReadbackDelayMs = 0d;
    }

    private void UpdateColor(Color color)
    {
        _color = color;
        _light.color = _color * _videoPlayer.ScreenColor;

        if (!_useMenuLighting) return;

        //Darken the base menu lighting
        var baseColor = MenuColorPatch.BaseColor *
                        (Color.white - _videoPlayer.ScreenColor * MENU_DARKENING_INTENSITY);
        baseColor.a = 1;

        if (_menuFloorLight != null)
        {
            var combinedColor = baseColor + _light.color * MENU_FLOOR_INTENSITY;
            combinedColor.a = 1f;
            _menuFloorLight.ColorWasSet(combinedColor);
        }

        if (_menuFogRing != null)
        {
            var combinedColor = baseColor + _light.color * MENU_FOG_INTENSITY;
            combinedColor.a = 1f;
            _menuFogRing.ColorWasSet(combinedColor);
        }
    }

    private RenderTexture Downscale(Texture tex)
    {
        // Blit the video texture into a texture of size INITIAL_DOWNSCALING_SIZE^2
        Graphics.Blit(tex, _downscaleTextures[0]);

        // Blit into textures of decreasing size with the last one being a single pixel to get the average color
        for (var i = 0; i < _downscaleTextures.Count - 1; i++)
            Graphics.Blit(_downscaleTextures[i], _downscaleTextures[i + 1]);

        return _downscaleTextures[_downscaleTextures.Count - 1];
    }
}