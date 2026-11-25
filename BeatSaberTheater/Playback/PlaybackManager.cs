using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BeatSaberTheater.Download;
using BeatSaberTheater.Environment.Interfaces;
using BeatSaberTheater.Harmony.Patches;
using BeatSaberTheater.Screen;
using BeatSaberTheater.Screen.Interfaces;
using BeatSaberTheater.Settings;
using BeatSaberTheater.Util;
using BeatSaberTheater.Video;
using BeatSaberTheater.Video.Config;
using BS_Utils.Gameplay;
using BS_Utils.Utilities;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;
using Zenject;
using Scene = BeatSaberTheater.Util.Scene;

namespace BeatSaberTheater.Playback;

public class PlaybackManager : MonoBehaviour
{
    public bool IsPreviewPlaying { get; private set; }

    private AudioSource? _activeAudioSource;
    private Scene _activeScene = Scene.Other;
    private SongPreviewPlayer.AudioSourceVolumeController[]? _audioSourceControllers;
    private DateTime _audioSourceStartTime;
    private BeatmapLevel? _currentLevel;
    private float _lastKnownAudioSourceTime;
    private Environment.LightManager _lightManager = null!;
    private float _offsetAfterPrepare;
    private Coroutine? _delayedPlayRoutine;
    private bool _lateStartPending;   // Play() was called before the video finished loading; place it in OnPrepareComplete
    private DiContainer? _gameSceneContainer;
    private bool _gameSceneVideoPending;
    private IEnumerator? _prepareVideoCoroutine;
    private int _previewGeneration;
    private float _previewStartTime;
    private DateTime _previewSyncStartTime;
    private float _previewTimeRemaining;
    private bool _previewWaitingForPreviewPlayer;
    private bool _previewWaitingForVideoPlayer = true;
    private SettingsManager? _settingsManager;
    private SongPreviewPlayer? _songPreviewPlayer;
    private float _songSpeed = 1f;
    private bool _speedPendingFirstFrame;
    private bool _whiteTestShown;
    private bool _brightnessPreviewOpen;

    private enum StartSyncPhase
    {
        Off,
        WaitingForFirstFrame,
        Measuring,
        Nudging,
        Done
    }

    private StartSyncPhase _startSyncPhase;
    private bool _startSyncLearnable;
    private bool _startSyncJumped;
    // Preview's 5% nudge has about 100 ms at 1x over 2 s, with some of that window spent measuring.
    private const double PreviewStartSyncJumpThresholdMs = 80;
    // Menu audio is separate and the muted video can use a stronger nudge; jump past the old 250 ms cutoff.
    private const double MenuStartSyncJumpThresholdMs = 250;
    private float _startSyncFirstFrameTime;
    private float _startSyncNudgeEndTime;
    private readonly double[] _startSyncSamples = new double[6];
    private int _startSyncSampleCount;
    private double _startSyncFirstErrorMs;
    private bool _startSyncLearned;
    private float? _firstNoteSongTime;
    // Start sync stops this long before the first note is hit, and never re-arms after that point
    private const float FirstNoteMarginSeconds = 0.15f;
    private bool _firstNoteReached;   // this play reached its first note; start sync never re-arms after that

    // Temporary sync check logger: remove together with the performance logger
    private int _syncCheckCount;
    private double _syncCheckMax;
    private double _syncCheckMin;
    private float _syncCheckPlayStart;
    private double _syncCheckSum;
    private float _syncCheckWindowStart;
    private const float SongStartWaitLimitSeconds = 3f;
    private AudioTimeSyncController? _timeSyncController;
    private VideoConfig? _videoConfig;
    private CustomVideoPlayer _videoPlayer = null!;

    // ---- Temporary performance logger: remove together with the sync check logger ----
    private sealed class PerfStat
    {
        private double _sum;
        private double _max;
        private int _count;

        public void Add(double value)
        {
            _sum += value;
            if (_count == 0 || value > _max) _max = value;
            _count++;
        }

        public void Reset()
        {
            _sum = 0;
            _max = 0;
            _count = 0;
        }

        // "avg/max" after multiplying by scale, or "n/a" if nothing was recorded
        public string Format(double scale, string format) =>
            _count == 0
                ? "n/a"
                : (_sum / _count * scale).ToString(format, CultureInfo.InvariantCulture) + "/" +
                  (_max * scale).ToString(format, CultureInfo.InvariantCulture);
    }

    private const float PerfWindowSeconds = 5f;
    private bool _perfActive;
    private float _perfWindowStart;
    private int _perfWindowGcStart;
    // 5 s at up to ~400 fps, so the list never grows (and allocates) mid-window
    private readonly List<float> _frameTimesMs = new(2048);
    private float _worstFrameMs;
    private float _worstFrameSongTime;

    private ProfilerRecorder _recMainThread;
    private ProfilerRecorder _recRenderThread;
    private ProfilerRecorder _recDrawCalls;
    private ProfilerRecorder _recBatches;
    private ProfilerRecorder _recSetPass;
    private ProfilerRecorder _recTriangles;
    private ProfilerRecorder _recGcAlloc;
    private bool _frameTimingEnabled;
    private readonly FrameTiming[] _frameTimings = new FrameTiming[1];
    private readonly List<UnityEngine.XR.XRDisplaySubsystem> _xrDisplays = new();
    private UnityEngine.XR.XRDisplaySubsystem? _xrDisplay;
    private int _xrDroppedStart = -1;
    private int _xrDroppedEnd = -1;
    private int _xrPresentedStart = -1;
    private int _xrPresentedEnd = -1;
    private float _xrRefreshRate = -1f;

    private readonly PerfStat _statMainThread = new();        // ns
    private readonly PerfStat _statRenderThread = new();      // ns
    private readonly PerfStat _statDrawCalls = new();
    private readonly PerfStat _statBatches = new();
    private readonly PerfStat _statSetPass = new();
    private readonly PerfStat _statTriangles = new();
    private readonly PerfStat _statGcAlloc = new();           // bytes
    private readonly PerfStat _statCpuMain = new();           // ms
    private readonly PerfStat _statCpuRender = new();         // ms
    private readonly PerfStat _statGpu = new();               // ms
    private readonly PerfStat _statXrAppGpu = new();          // raw value as reported, unit not assumed
    private readonly PerfStat _statXrCompositorGpu = new();   // raw value as reported, unit not assumed
    private readonly PerfStat _statTheaterFrameReady = new(); // ms

    private int _videoFramesShown;
    private int _videoFramesSkipped;
    private float _videoLongestGapMs;
    private long _lastVideoFrameIndex = -1;
    private float _lastVideoFrameTime;
    private readonly Stopwatch _frameReadyStopwatch = new();

    [Inject] private readonly PluginConfig _config = null!;
    [Inject] private readonly DisplaySettings _displaySettings = null!;
    [Inject] private readonly ILightManagerFactory _lightManagerFactory = null!;
    [Inject] private readonly LoggingService _loggingService = null!;
    [Inject] private readonly VideoLoader _videoLoader = null!;

    // [Inject] private readonly VideoMenuUI _videoMenu = null!;
    [Inject] private readonly ICustomVideoPlayerFactory _videoPlayerFactory = null!;

    #region Unity Event Functions

    private void Start()
    {
        BSEvents.gameSceneActive += GameSceneActive;
        BSEvents.gameSceneLoaded += GameSceneLoaded;
        BSEvents.lateMenuSceneLoadedFresh += OnMenuSceneLoadedFresh;
        BSEvents.menuSceneLoaded += OnMenuSceneLoaded;
        BSEvents.songPaused += PauseVideo;
        BSEvents.songUnpaused += ResumeVideo;
        VideoLoader.ConfigChanged += OnConfigChanged;
        Events.DifficultySelected += DifficultySelected;
        _displaySettings.Changed += OnDisplaySettingsChanged;
        _displaySettings.TheaterEnabledChanged += OnTheaterEnabledChanged;
        SongStartWaitPatch.ReadyToStart = IsReadyForSongStart;
    }

    private void OnDestroy()
    {
        StopPerfLogger(); // Temporary performance logger: remove together with the sync check logger

        if (_videoPlayer != null)
            _videoPlayer.Shutdown(FrameReady, OnPrepareComplete, VideoPlayerErrorReceived);

        BSEvents.gameSceneActive -= GameSceneActive;
        BSEvents.gameSceneLoaded -= GameSceneLoaded;
        BSEvents.lateMenuSceneLoadedFresh -= OnMenuSceneLoadedFresh;
        BSEvents.menuSceneLoaded -= OnMenuSceneLoaded;
        BSEvents.songPaused -= PauseVideo;
        BSEvents.songUnpaused -= ResumeVideo;
        VideoLoader.ConfigChanged -= OnConfigChanged;
        Events.DifficultySelected -= DifficultySelected;
        _displaySettings.Changed -= OnDisplaySettingsChanged;
        _displaySettings.TheaterEnabledChanged -= OnTheaterEnabledChanged;
        if (SongStartWaitPatch.ReadyToStart == IsReadyForSongStart) SongStartWaitPatch.ReadyToStart = null;
    }

    private void Update()
    {
        // Permanent Display preview checks: keep them in Update when the performance logger is removed.
        // The white test can end without SetBrightnessPreview, for example when a Stop() clears it or a video takes over.
        // Release it then, so the flag never outlives the texture.
        if (_whiteTestShown && !_videoPlayer.IsShowingTestTexture) ReleaseWhiteTest();
        // Once playback and its fade are finished, and no Play() is waiting for its first frame, an open Display view can
        // show the white test.
        if (_brightnessPreviewOpen && !_whiteTestShown && _activeScene == Scene.Menu &&
            !_videoPlayer.IsPlaying && !_videoPlayer.IsStarting && !_videoPlayer.IsFading)
            ShowWhiteTestIfPreviewOpen();

        // Temporary performance logger: remove together with the sync check logger
        if (_activeScene != Scene.SoloGameplay)
        {
            StopPerfLogger();
            return;
        }

        if (!_perfActive) StartPerfLogger();

        var now = Time.realtimeSinceStartup;
        var frameMs = Time.unscaledDeltaTime * 1000f;
        _frameTimesMs.Add(frameMs);
        if (frameMs > _worstFrameMs)
        {
            _worstFrameMs = frameMs;
            _worstFrameSongTime = _activeAudioSource != null ? _activeAudioSource.time : -1f;
        }

        if (_recMainThread.Valid) _statMainThread.Add(_recMainThread.LastValue);
        if (_recRenderThread.Valid) _statRenderThread.Add(_recRenderThread.LastValue);
        if (_recDrawCalls.Valid) _statDrawCalls.Add(_recDrawCalls.LastValue);
        if (_recBatches.Valid) _statBatches.Add(_recBatches.LastValue);
        if (_recSetPass.Valid) _statSetPass.Add(_recSetPass.LastValue);
        if (_recTriangles.Valid) _statTriangles.Add(_recTriangles.LastValue);
        if (_recGcAlloc.Valid) _statGcAlloc.Add(_recGcAlloc.LastValue);

        if (_frameTimingEnabled)
        {
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, _frameTimings) > 0)
            {
                _statCpuMain.Add(_frameTimings[0].cpuMainThreadFrameTime);
                _statCpuRender.Add(_frameTimings[0].cpuRenderThreadFrameTime);
                _statGpu.Add(_frameTimings[0].gpuFrameTime);
            }
        }

        if (_xrDisplay != null && _xrDisplay.running)
        {
            if (_xrDisplay.TryGetAppGPUTimeLastFrame(out var appGpu)) _statXrAppGpu.Add(appGpu);
            if (_xrDisplay.TryGetCompositorGPUTimeLastFrame(out var compositorGpu)) _statXrCompositorGpu.Add(compositorGpu);
            if (_xrDisplay.TryGetDroppedFrameCount(out var dropped))
            {
                if (_xrDroppedStart < 0) _xrDroppedStart = dropped;
                _xrDroppedEnd = dropped;
            }
            if (_xrDisplay.TryGetFramePresentCount(out var presented))
            {
                if (_xrPresentedStart < 0) _xrPresentedStart = presented;
                _xrPresentedEnd = presented;
            }
            if (_xrDisplay.TryGetDisplayRefreshRate(out var refreshRate)) _xrRefreshRate = refreshRate;
        }

        if (now - _perfWindowStart < PerfWindowSeconds) return;
        LogPerfWindow(now);
        ResetPerfWindow(now);
    }

    // Temporary performance logger: remove together with the sync check logger
    private void StartPerfLogger()
    {
        _perfActive = true;
        ResetVideoCadence();
        _recMainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1, ProfilerRecorderOptions.Default);
        _recRenderThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Render Thread", 1, ProfilerRecorderOptions.Default);
        _recDrawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count", 1, ProfilerRecorderOptions.Default);
        _recBatches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count", 1, ProfilerRecorderOptions.Default);
        _recSetPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count", 1, ProfilerRecorderOptions.Default);
        _recTriangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count", 1, ProfilerRecorderOptions.Default);
        _recGcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1, ProfilerRecorderOptions.Default);
        _frameTimingEnabled = FrameTimingManager.IsFeatureEnabled();

        _xrDisplay = null;
        SubsystemManager.GetSubsystems(_xrDisplays);
        foreach (var display in _xrDisplays)
        {
            if (!display.running) continue;
            _xrDisplay = display;
            break;
        }

        _loggingService.Info(string.Format(CultureInfo.InvariantCulture,
            "Perf logger: main thread {0}, render thread {1}, draw calls {2}, batches {3}, setpass {4}, triangles {5}, " +
            "GC alloc {6}, frame timing {7}, XR display {8}",
            _recMainThread.Valid, _recRenderThread.Valid, _recDrawCalls.Valid, _recBatches.Valid, _recSetPass.Valid,
            _recTriangles.Valid, _recGcAlloc.Valid, _frameTimingEnabled, _xrDisplay != null));
        _loggingService.Info(string.Format(CultureInfo.InvariantCulture,
            "Perf context: environment {0}, Theater {1}, video {2}, screen scale {3}%, brightness {4}%, edge softness {5}, " +
            "bloom {6}, curved {7}, transparent {8}",
            TheaterFileHelpers.GetEnvironmentName(), _config.TheaterActive ? "on" : "off",
            _videoConfig?.IsPlayable == true ? "configured" : "none", _config.ScreenScale, _config.ScreenBrightness,
            _config.EdgeSoftness, _config.BloomIntensity, _config.CurvedScreen, _config.TransparencyEnabled));

        ResetPerfWindow(Time.realtimeSinceStartup);
    }

    // Temporary performance logger: remove together with the sync check logger
    private void StopPerfLogger()
    {
        if (!_perfActive) return;
        _perfActive = false;
        ResetVideoCadence();
        _recMainThread.Dispose();
        _recRenderThread.Dispose();
        _recDrawCalls.Dispose();
        _recBatches.Dispose();
        _recSetPass.Dispose();
        _recTriangles.Dispose();
        _recGcAlloc.Dispose();
        _xrDisplay = null;
        _frameTimesMs.Clear();
        _lightManager?.ResetLimiterPerfWindow(active: false); // Temporary brightness limiter logging
    }

    // Temporary performance logger: remove together with the sync check logger.
    // Forgets the last video frame, so the next gap measurement doesn't span a pause, a restart or a new play.
    private void ResetVideoCadence()
    {
        _lastVideoFrameIndex = -1;
    }

    // Temporary performance logger: remove together with the sync check logger
    private void ResetPerfWindow(float now)
    {
        // Per window, so a window without a refresh rate reading prints n/a, not a stale value
        _xrRefreshRate = -1f;
        _perfWindowStart = now;
        _perfWindowGcStart = GC.CollectionCount(0);
        _frameTimesMs.Clear();
        _worstFrameMs = 0f;
        _worstFrameSongTime = -1f;
        _statMainThread.Reset();
        _statRenderThread.Reset();
        _statDrawCalls.Reset();
        _statBatches.Reset();
        _statSetPass.Reset();
        _statTriangles.Reset();
        _statGcAlloc.Reset();
        _statCpuMain.Reset();
        _statCpuRender.Reset();
        _statGpu.Reset();
        _statXrAppGpu.Reset();
        _statXrCompositorGpu.Reset();
        _statTheaterFrameReady.Reset();
        _xrDroppedStart = -1;
        _xrDroppedEnd = -1;
        _xrPresentedStart = -1;
        _xrPresentedEnd = -1;
        _videoFramesShown = 0;
        _videoFramesSkipped = 0;
        _videoLongestGapMs = 0f;
        _lightManager?.ResetLimiterPerfWindow(active: true); // Temporary brightness limiter logging
    }

    // Temporary performance logger: remove together with the sync check logger
    private void LogPerfWindow(float now)
    {
        var count = _frameTimesMs.Count;
        if (count == 0) return;
        _frameTimesMs.Sort();
        var median = _frameTimesMs[count / 2];
        var p99 = _frameTimesMs[Math.Min(count - 1, (int)(count * 0.99f))];
        var slow = 0;
        foreach (var ms in _frameTimesMs)
            if (ms > median * 1.5f) slow++;

        var songTime = _activeAudioSource != null ? _activeAudioSource.time : -1f;
        var videoState = _videoPlayer != null && _videoPlayer.IsPlaying ? "playing" : "not playing";
        _loggingService.Info(string.Format(CultureInfo.InvariantCulture,
            "Perf (Theater {0}, video {1}, song at {2:0.0} s): {3:0.0} fps, frame median {4:0.0} ms, p99 {5:0.0} ms, " +
            "max {6:0.0} ms at song {7:0.0} s, {8} slow (>1.5x median), {9} GC, alloc {10} KB/frame",
            _config.TheaterActive ? "on" : "off", videoState, songTime, count / (now - _perfWindowStart), median, p99,
            _worstFrameMs, _worstFrameSongTime, slow, GC.CollectionCount(0) - _perfWindowGcStart,
            _statGcAlloc.Format(1.0 / 1024, "0.0")));

        var dropped = _xrDroppedStart >= 0 ? (_xrDroppedEnd - _xrDroppedStart).ToString(CultureInfo.InvariantCulture) : "n/a";
        var presented = _xrPresentedStart >= 0 ? (_xrPresentedEnd - _xrPresentedStart).ToString(CultureInfo.InvariantCulture) : "n/a";
        var refresh = _xrRefreshRate > 0 ? _xrRefreshRate.ToString("0", CultureInfo.InvariantCulture) : "n/a";
        _loggingService.Info(string.Format(CultureInfo.InvariantCulture,
            "Perf detail (avg/max): main thread {0} ms, render thread {1} ms, frame timing cpu main {2} / cpu render {3} / " +
            "gpu {4} ms; XR app GPU {5}, compositor GPU {6} (raw), dropped {7}, presented {8}, refresh {9} Hz; " +
            "draw calls {10}, batches {11}, setpass {12}, triangles {13}k; video frames {14}, skipped {15}, " +
            "longest gap {16:0} ms; Theater video-frame code {17} us",
            _statMainThread.Format(1e-6, "0.0"), _statRenderThread.Format(1e-6, "0.0"), _statCpuMain.Format(1, "0.0"),
            _statCpuRender.Format(1, "0.0"), _statGpu.Format(1, "0.0"), _statXrAppGpu.Format(1, "0.####"),
            _statXrCompositorGpu.Format(1, "0.####"), dropped, presented, refresh, _statDrawCalls.Format(1, "0"),
            _statBatches.Format(1, "0"), _statSetPass.Format(1, "0"), _statTriangles.Format(0.001, "0"),
            _videoFramesShown, _videoFramesSkipped, _videoLongestGapMs, _statTheaterFrameReady.Format(1000, "0")));
        _lightManager?.LogLimiterPerfWindow(songTime, count); // Temporary brightness limiter logging
    }

    #endregion

    #region Display settings

    // Brightness, corner roundness and edge softness apply to the screen right away. Screen size applies the next
    // time the gameplay placement is set, and bloom is read every frame.
    private void OnDisplaySettingsChanged()
    {
        if (_videoPlayer == null) return;

        _videoPlayer.RefreshBrightness();
        SetVideoPlayerScreenShaderParameters(_videoConfig);
        _lightManager.SnapLimiter();
    }

    public void SetBrightnessPreview(bool open)
    {
        _brightnessPreviewOpen = open;
        if (open)
        {
            ShowWhiteTestIfPreviewOpen();
        }
        else if (_whiteTestShown)
        {
            _lightManager.ShowWhiteTest(false);
            _videoPlayer.FadeOut();
            _videoPlayer.ClearTexture();
            _whiteTestShown = false;
            SetVideoPlayerScreenShaderParameters(_videoConfig);
        }
        else if (_videoPlayer.IsShowingTestTexture)
        {
            // A Play() has taken over from the test but its first frame hasn't arrived yet. Don't leave white on screen
            // after Display closes; the video's own frames replace the cleared texture.
            _videoPlayer.ClearTexture();
        }
    }

    private void ShowWhiteTestIfPreviewOpen()
    {
        if (!_brightnessPreviewOpen || _whiteTestShown || _activeScene != Scene.Menu || !_config.TheaterActive ||
            _videoPlayer.IsPlaying || _videoPlayer.IsStarting)
            return;

        _videoPlayer.ShowTestTexture(Texture2D.whiteTexture);
        _lightManager.SetMapFactor(1f);
        _lightManager.ShowWhiteTest(true);
        _whiteTestShown = true;
    }

    // The white test lost the screen without SetBrightnessPreview(false). Hand the screen back to the video settings
    // the same way StartVideoPlayback does.
    private void ReleaseWhiteTest()
    {
        _loggingService.Info(_videoPlayer.IsPlaying
            ? "Display preview: a video replaced the white test; restoring its settings"
            : "Display preview: the white test was cleared by a stop");
        _whiteTestShown = false;
        SetVideoPlayerScreenShaderParameters(_videoConfig);
        _lightManager.SnapOnFirstFrame();
    }

    private void OnTheaterEnabledChanged(bool enabled)
    {
        // Turning Theater back on is handled by the video menu, which reloads the selected level
        if (enabled || _videoPlayer == null) return;

        SetBrightnessPreview(false);
        StopPreview(true);
        StopPlayback();
        _videoPlayer.Hide();
    }

    #endregion

    public void StopPlayback()
    {
        CancelResyncAudioPause();
        _lateStartPending = false;
        _videoPlayer.Stop();
        StopAllCoroutines();
        // A slow-speed start that never reached its first frame shouldn't switch the speed of whatever plays next
        _videoPlayer.RemoveFrameReadyEventHandler(ApplySpeedOnFirstFrame);
        _speedPendingFirstFrame = false;
    }

    public void StopAndUnloadVideo()
    {
        // Full teardown before unloading, as the trash button does. Its StopAllCoroutines also cancels a load waiting
        // for its file lock and a delayed start waiting for the song clock, either of which would undo the unload.
        StopPlayback();
        _videoPlayer.UnloadVideo();
    }

    public void UnloadVideo()
    {
        _videoPlayer.UnloadVideo();
    }

    public void HideVideoPlayer()
    {
        _videoPlayer.Hide();
    }

    #region Event handlers

    private void ConfigChangedFrameReadyHandler(VideoPlayer sender, long frameIdx)
    {
        _loggingService.Info("First frame after config change is ready");
        sender.frameReady -= ConfigChangedFrameReadyHandler;
        if (_activeAudioSource == null) return;

        if (!_activeAudioSource.isPlaying)
        {
            _videoPlayer.Pause();
            _videoPlayer.SetBrightness(1f);
        }

        _videoPlayer.UpdateScreenContent();
    }

    private void ConfigChangedPrepareHandler(VideoPlayer sender)
    {
        sender.prepareCompleted -= ConfigChangedPrepareHandler;
        if (_activeScene == Scene.Menu || _activeAudioSource == null) return;

        sender.frameReady += ConfigChangedFrameReadyHandler;
        PlayVideo(_lastKnownAudioSourceTime);
    }

    private void DifficultySelected(ExtraSongDataArgs extraSongDataArgs)
    {
        if (_videoConfig == null) return;

        var difficultyData = extraSongDataArgs.SelectedDifficultyData;
        var songData = extraSongDataArgs.SongData;

        // If there is any difficulty that has a Theater suggestion but the current one doesn't, disable playback. The current difficulty most likely has the suggestion missing on purpose.
        // If there are no difficulties that have the suggestion set, play the video. It might be a video added by the user.
        // Otherwise, if the map is WIP, disable playback even when no difficulty has the suggestion, to convince the mapper to add it.
        if (difficultyData?.HasTheater() == false && songData?.HasTheaterInAnyDifficulty() == true)
            _videoConfig.PlaybackDisabledByMissingSuggestion = true;
        else if (_videoConfig.IsWIPLevel && difficultyData?.HasTheater() == false)
            _videoConfig.PlaybackDisabledByMissingSuggestion = true;
        else
            _videoConfig.PlaybackDisabledByMissingSuggestion = false;

        if (_videoConfig.PlaybackDisabledByMissingSuggestion)
        {
            if (!_whiteTestShown) _videoPlayer.FadeOut(0.1f);
        }
        else
        {
            if (!_videoPlayer.IsPlaying) StartSongPreview();
        }
    }

    // Temporary performance logger: remove together with the sync check logger
    public void FrameReady(VideoPlayer videoPlayer, long frame)
    {
        if (_perfActive)
        {
            var now = Time.realtimeSinceStartup;
            _videoFramesShown++;
            // Count frames the player skipped. Big or backward jumps are seeks, not skips.
            if (_lastVideoFrameIndex >= 0 && frame > _lastVideoFrameIndex && frame - _lastVideoFrameIndex <= 30)
            {
                _videoFramesSkipped += (int)(frame - _lastVideoFrameIndex - 1);
                _videoLongestGapMs = Math.Max(_videoLongestGapMs, (now - _lastVideoFrameTime) * 1000f);
            }

            _lastVideoFrameIndex = frame;
            _lastVideoFrameTime = now;
        }

        _frameReadyStopwatch.Restart();
        try
        {
            FrameReadyCore(videoPlayer, frame);
        }
        finally
        {
            if (_perfActive) _statTheaterFrameReady.Add(_frameReadyStopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private void FrameReadyCore(VideoPlayer videoPlayer, long frame)
    {
        if (_activeAudioSource == null || _videoConfig == null) return;

        var audioSourceTime = _activeAudioSource.time;
        // Once this play reaches its first note, start sync never re-arms (the user's rule: nothing changes after that)
        if (!_firstNoteReached && _firstNoteSongTime is { } firstNoteTime &&
            audioSourceTime >= firstNoteTime - FirstNoteMarginSeconds)
            _firstNoteReached = true;

        if (_videoPlayer.IsFading)
        {
            // Keep the last known song time current during fades, or the first frame after a fade looks like a seek
            if (_activeAudioSource.isPlaying && audioSourceTime > 0) _lastKnownAudioSourceTime = audioSourceTime;
            StartSyncStep(audioSourceTime);
            return;
        }

        var playerTime = _videoPlayer.PlayerTime;
        var targetTime = GetTargetTime();
        var referenceTime = GetReferenceTime();
        var videoDuration = _videoPlayer.VideoDuration;
        if (videoDuration > 0)
        {
            targetTime %= videoDuration;
            referenceTime %= videoDuration;
        }
        var error = targetTime - playerTime;

        if (!_activeAudioSource.isPlaying) return;

        var startSyncHadJumped = _startSyncJumped;
        StartSyncStep(audioSourceTime);
        if (!startSyncHadJumped && _startSyncJumped)
        {
            // The positions sampled above predate the jump. Let the next frame check the new position.
            if (audioSourceTime > 0) _lastKnownAudioSourceTime = audioSourceTime;
            return;
        }

        if (frame % 120 == 0)
            _loggingService.Debug("Frame: " + frame + " - Player: " +
                                  TheaterFileHelpers.FormatFloat((float)playerTime) +
                                  " - AudioSource: " +
                                  TheaterFileHelpers.FormatFloat(audioSourceTime) + " - Error (ms): " +
                                  Math.Round(error * 1000));

        // Temporary sync check logger: remove together with the performance logger
        if (_videoPlayer.IsPlaying && !_videoPlayer.IsSyncing) RecordSyncCheck(playerTime - referenceTime, audioSourceTime);

        if (_videoConfig.endVideoAt.HasValue)
        {
            if (referenceTime >= _videoConfig.endVideoAt - 1f)
            {
                var brightness = Math.Max(0f, _videoConfig.endVideoAt.Value - referenceTime);
                _videoPlayer.SetBrightness(brightness);
            }
        }
        else if (referenceTime >= _videoPlayer.PlayerLength - 1f && _videoConfig.loop != true)
        {
            var brightness = Math.Max(0f, _videoPlayer.PlayerLength - referenceTime);
            _videoPlayer.SetBrightness((float)brightness);
        }

        if (Math.Abs(audioSourceTime - _lastKnownAudioSourceTime) > 0.3f && _videoPlayer.IsPlaying)
        {
            // Temporarily Info (was Debug) for the sync check; back to Debug when the temporary loggers are removed
            _loggingService.Info("Detected AudioSource seek, resyncing...");
            ResyncVideo();
        }

        //Sync if the error exceeds a threshold, but not if the video is close to the looping point
        if (Math.Abs(error) > 0.3f && Math.Abs(videoDuration - playerTime) > 0.5f &&
            _videoPlayer.IsPlaying)
            // A failed level deliberately desyncs the audio, so songs still need the time sync controller's approval.
            // A running start check owns big errors above 250 ms in the menu and 80 ms in Preview.
            // The 300 ms rule is only the fallback once that check is idle or complete.
            if (_activeScene == Scene.SoloGameplay
                    ? _timeSyncController != null && !_timeSyncController.forcedNoAudioSync
                    : (_activeAudioSource?.isPlaying == true && !_videoPlayer.IsSyncing &&
                       _startSyncPhase is (StartSyncPhase.Off or StartSyncPhase.Done)))
            {
                // Temporarily Info (was Debug) for the sync check; back to Debug when the temporary loggers are removed
                _loggingService.Info(
                    $"Detected desync (target {targetTime}, actual {playerTime}), resyncing...");
                ResyncVideo();
            }

        if (audioSourceTime > 0) _lastKnownAudioSourceTime = audioSourceTime;
    }

    // Temporary sync check logger: remove together with the performance logger.
    // Logs how far the video is from the song at Info level, so a release build's log shows real sync error.
    // Averaged per window, since both clocks only move in steps of a video frame or an audio buffer.
    private void RecordSyncCheck(double aheadSeconds, float songTime)
    {
        var aheadMs = aheadSeconds * 1000;
        // Delayed starts and loop points show up as huge jumps; they aren't sync error
        if (Math.Abs(aheadMs) > 5000) return;

        var now = Time.realtimeSinceStartup;
        if (_syncCheckCount == 0)
        {
            _syncCheckWindowStart = now;
            _syncCheckMin = double.MaxValue;
            _syncCheckMax = double.MinValue;
            _syncCheckSum = 0;
        }

        _syncCheckSum += aheadMs;
        _syncCheckCount++;
        _syncCheckMin = Math.Min(_syncCheckMin, aheadMs);
        _syncCheckMax = Math.Max(_syncCheckMax, aheadMs);

        // 1 s windows show the start in detail, 5 s windows after that keep a whole song readable
        var sincePlay = now - _syncCheckPlayStart;
        if (now - _syncCheckWindowStart < (sincePlay < 10f ? 1f : 5f)) return;

        _loggingService.Info(string.Format(CultureInfo.InvariantCulture,
            "Sync check ({0}, {1:0.0} s in, song at {2:0.0} s): video {3:+0;-0;0} ms ahead of the song " +
            "(target {4:+0;-0;0}, min {5:+0;-0;0}, max {6:+0;-0;0}, {7} frames)",
            SyncCheckMode, sincePlay, songTime, _syncCheckSum / _syncCheckCount, LeadVideoSeconds * 1000,
            _syncCheckMin, _syncCheckMax, _syncCheckCount));
        _syncCheckCount = 0;
    }

    private void GameSceneActive()
    {
        _loggingService.Info("Moving to game scene");
    }

    public void GameSceneLoaded()
    {
        CancelResyncAudioPause();
        _brightnessPreviewOpen = false;
        SetBrightnessPreview(false);
        CancelStartSync();
        _gameSceneContainer = Plugin.gameCoreContainer;
        _gameSceneVideoPending = false;
        _lateStartPending = false;
        _timeSyncController = null;
        _firstNoteReached = false;
        StopAllCoroutines();
        _loggingService.Info("GameSceneLoaded");

        // If BSUtils has no level data, we're probably in the tutorial
        if (BS_Utils.Plugin.LevelData.IsSet)
        {
            // Move to the environment scene (only guaranteed loaded by this point) so Chroma's
            // environment enhancement pass can find and duplicate our screen via "_id": "CinemaScreen$".
            // Doing this earlier, in GameSceneActive, races the environment scene's async load.
            var sceneName = TheaterFileHelpers.GetEnvironmentSceneName(
                BS_Utils.Plugin.LevelData.GameplayCoreSceneSetupData.targetEnvironmentInfo);
            var scene = SceneManager.GetSceneByName(sceneName);
            SceneManager.MoveGameObjectToScene(gameObject, scene);
        }

        _activeScene = Scene.SoloGameplay;

        if (!_config.TheaterActive)
        {
            _loggingService.Info("Plugin disabled");
            _videoPlayer.Hide();
            return;
        }

        _lightManager.OnGameSceneLoaded();

        StopPlayback();
        _videoPlayer.Hide();
        _videoPlayer.ResetScreens();

        if (!TheaterFileHelpers.IsInEditor())
        {
            if (BS_Utils.Plugin.LevelData.Mode == Mode.None)
            {
                _loggingService.Info("Level mode is None");
                return;
            }

            var bsUtilsLevel = BS_Utils.Plugin.LevelData.GameplayCoreSceneSetupData.beatmapLevel;
            if (_currentLevel?.levelID != bsUtilsLevel.levelID)
            {
                var video = _videoLoader.GetConfigForLevel(bsUtilsLevel);
                SetSelectedLevel(bsUtilsLevel, video);
            }
        }

        if (_videoConfig == null || !_videoConfig.IsPlayable)
        {
            var videoPath = _videoConfig?.GetVideoPathForFormat(_config.Format);
            _loggingService.Info("No video configured or video is not playable: " + videoPath);

            if (_config.CoverEnabled && (_videoConfig?.forceEnvironmentModifications == null ||
                                         _videoConfig.forceEnvironmentModifications == false))
                ShowSongCover().LogErrors(_loggingService, "Showing song cover");
            return;
        }

        // Some mappers disable this accidentally
        gameObject.SetActive(true);

        if (_videoConfig.NeedsToSave) _videoLoader.SaveVideoConfig(_videoConfig);

        UpdateVideoPlayerPlacement(_videoConfig, _activeScene);

        _videoPlayer.Show();
        // Fixes rough pop-in at the start of the song when transparency is disabled
        if (!(_videoConfig.TransparencyEnabled && _config.TransparencyEnabled) && _activeScene != Scene.Menu)
        {
            _videoPlayer.ScreenColor = Color.black;
            _videoPlayer.ShowScreenBody();
        }

        SetAudioSourcePanning(0);
        _videoPlayer.Mute();
        _loggingService.Info("Playing video from GameSceneLoaded");
        // StopPlayback() above unloaded the video. Start loading it now, at scene load, instead of when the song starts.
        var gameVideoPath = _videoConfig.GetVideoPathForFormat(_config.Format);
        if (!_videoPlayer.PlayerIsPrepared && gameVideoPath != null && _videoPlayer.Url == gameVideoPath)
            _videoPlayer.Prepare();
        _gameSceneVideoPending = true;
        StartCoroutine(PlayVideoAfterAudioSourceCoroutine(false));
        SceneChanged();
    }

    private void OnConfigChanged(VideoConfig? config)
    {
        OnConfigChanged(config, false);
    }

    private void OnConfigChanged(VideoConfig? config, bool? reloadVideo)
    {
        var previousVideoPath = _videoConfig?.GetVideoPathForFormat(_config.Format);
        _videoConfig = config;

        if (config == null)
        {
            if (!_whiteTestShown) _videoPlayer.Hide();
            return;
        }

        if (!config.IsPlayable &&
            (config.forceEnvironmentModifications == null || config.forceEnvironmentModifications == false)) return;

        if (_activeScene == Scene.Menu)
        {
            StopPreview(true);
        }
        else
        {
            UpdateVideoPlayerPlacement(config, _activeScene);
            ResyncVideo();
        }

        var currentVideoPath = config.GetVideoPathForFormat(_config.Format);
        if (previousVideoPath != currentVideoPath || reloadVideo == true)
        {
            _videoPlayer.AddPrepareCompletedEventHandler(ConfigChangedPrepareHandler);
            PrepareVideo(config);
        }
        else
        {
            _videoPlayer.LoopVideo(config.loop == true);
            SetVideoPlayerScreenShaderParameters(config);
            _videoPlayer.SetBloomIntensity(config.bloom);
        }

        if (config.TransparencyEnabled)
            _videoPlayer.HideScreenBody();
        else
            _videoPlayer.ShowScreenBody();

        // if (_activeScene == Scene.SoloGameplay) EnvironmentController.VideoConfigSceneModifications(_videoConfig);
    }

    private bool IsReadyForSongStart(DiContainer? container, float heldSeconds)
    {
        if (!_config.WaitForVideoBeforeSong) return true;
        var timedOut = heldSeconds >= SongStartWaitLimitSeconds;

        // GameSceneLoaded for this level hasn't run yet: wait for it (it normally comes within a frame or two)
        if (container == null || !ReferenceEquals(container, _gameSceneContainer))
        {
            if (timedOut) _loggingService.Info("Wait for video: Theater didn't see this level load, starting the song");
            return timedOut;
        }

        if (!_gameSceneVideoPending) return true;   // no playable video, or Theater is off

        if (_videoPlayer.PlayerIsPrepared)
        {
            if (heldSeconds >= 0.05f)
                _loggingService.Info(FormattableString.Invariant(
                    $"Wait for video: held the song start {heldSeconds * 1000:0} ms until the video loaded"));
            return true;
        }

        if (!timedOut) return false;
        _loggingService.Info("Wait for video: still loading after 3 s, starting the song");
        return true;
    }

    private void OnMenuSceneLoaded()
    {
        CancelResyncAudioPause();
        _brightnessPreviewOpen = false;
        SetBrightnessPreview(false);
        _lateStartPending = false;
        _timeSyncController = null;
        _loggingService.Info("MenuSceneLoaded");
        _activeScene = Scene.Menu;
        _firstNoteSongTime = null;
        _firstNoteReached = false;
        _videoPlayer.Hide();
        _videoPlayer.ResetScreens();
        StopAllCoroutines();
        _previewWaitingForPreviewPlayer = true;
        gameObject.SetActive(true);
        SceneChanged();
    }

    private void OnMenuSceneLoadedFresh(ScenesTransitionSetupDataSO? scenesTransition)
    {
        _songPreviewPlayer = Resources.FindObjectsOfTypeAll<SongPreviewPlayer>().LastOrDefault();
        _videoPlayer = _videoPlayerFactory.Create(gameObject);
        _videoPlayer.Startup(FrameReady, OnPrepareComplete, VideoPlayerErrorReceived);
        _lightManager = _lightManagerFactory.Create(gameObject);
        _lightManager.Startup(_videoPlayer);
        OnMenuSceneLoaded();
        if (_settingsManager == null)
        {
            StartCoroutine(OnMenuSceneLoadedFreshCoroutine());
        }
        else
        {
            _videoPlayer.SetVolumeScale(_settingsManager.settings.audio.volume);
            _videoPlayer.ScreenMenuLoadedFresh();
        }
    }

    private IEnumerator OnMenuSceneLoadedFreshCoroutine()
    {
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        yield return new WaitUntil(() => Plugin._menuContainer != null);
        _settingsManager = Plugin._menuContainer.Resolve<SettingsManager>();
        _videoPlayer.SetVolumeScale(_settingsManager.settings.audio.volume);
        _videoPlayer.ScreenMenuLoadedFresh();
    }

    private void OnPrepareComplete(VideoPlayer player)
    {
        if (_offsetAfterPrepare > 0)
        {
            var offset = (DateTime.Now - _audioSourceStartTime).TotalSeconds * TargetVideoSpeed + _offsetAfterPrepare;
            _loggingService.Info($"Adjusting offset after prepare to {offset}");
            player.time = offset;
        }
        else if (_lateStartPending)
        {
            _lateStartPending = false;
            var startAt = ClampToVideo(GetTargetTime());
            player.time = startAt;
            if (_activeAudioSource != null && _activeAudioSource.time > 0) _lastKnownAudioSourceTime = _activeAudioSource.time;
            _loggingService.Info(FormattableString.Invariant($"Start sync: video loaded late, starting it at {startAt:0.00} s"));
        }

        _offsetAfterPrepare = 0;
        if (!_whiteTestShown) _videoPlayer.ClearTexture();

        if (_activeScene != Scene.Menu) return;

        _previewWaitingForVideoPlayer = false;
        StartSongPreview();
    }

    private void SceneChanged()
    {
        SetVideoPlayerScreenShaderParameters(_videoConfig);
    }

    #endregion

    #region Harmony Patch Hooks

    public void UpdateSongPreviewPlayer(SongPreviewPlayer.AudioSourceVolumeController[] audioSourceControllers,
        AudioSource? activeAudioSource, float startTime, float timeRemaining, bool isDefault)
    {
        _audioSourceControllers = audioSourceControllers;
        _activeAudioSource = activeAudioSource;
        _lastKnownAudioSourceTime = 0;
        if (_activeAudioSource == null) _loggingService.Debug("Active AudioSource null in SongPreviewPlayer update");

        if (IsPreviewPlaying)
        {
            if (isDefault)
            {
                StopPreview(false);
                return;
            }

            _previewWaitingForPreviewPlayer = true;
            _loggingService.Debug($"Ignoring SongPreviewPlayer update");
            return;
        }

        if (isDefault)
        {
            StopPreview(true);
            if (!_whiteTestShown) _videoPlayer.FadeOut();
            _previewWaitingForPreviewPlayer = true;

            _loggingService.Debug("SongPreviewPlayer reverting to default loop");
            return;
        }

        // This allows the short preview for the practice offset to play
        if (!_previewWaitingForPreviewPlayer && Math.Abs(timeRemaining - 2.5f) > 0.001f)
        {
            StopPreview(true);
            if (!_whiteTestShown) _videoPlayer.FadeOut();

            _loggingService.Debug("Unexpected SongPreviewPlayer update, ignoring.");
            return;
        }

        if (_activeScene != Scene.Menu) return;

        if (_currentLevel != null && _currentLevel.songDuration < startTime)
        {
            _loggingService.Debug("Song preview start time was greater than song duration. Resetting start time to 0");
            startTime = 0;
        }

        _previewStartTime = startTime;
        _previewTimeRemaining = timeRemaining;
        _previewSyncStartTime = DateTime.Now;
        _previewWaitingForPreviewPlayer = false;
        StartSongPreview();
    }

    #endregion

    #region Video Playback

    private float TargetVideoSpeed => _songSpeed * (_videoConfig?.PlaybackSpeed ?? 1f);

    // Lead in video seconds. Preview keeps a target of 0, since it compares two audio streams.
    private float LeadVideoSeconds => IsPreviewPlaying ? 0f : _config.VideoLeadMs / 1000f * TargetVideoSpeed;

    private float GetTargetTime() => GetReferenceTime() + LeadVideoSeconds;

    private string SyncCheckMode => IsPreviewPlaying ? "preview" : _activeScene == Scene.Menu ? "menu" : "song";

    private void PlayVideo(float startTime)
    {
        CancelStartSync();
        _lateStartPending = false;
        if (_videoConfig == null)
        {
            _loggingService.Warn("VideoConfig null in PlayVideo");
            return;
        }

        _videoPlayer.IsSyncing = false;

        // Always hide screen body in the menu, since the drawbacks of the body being visible are large
        if (!(_videoConfig.TransparencyEnabled && _config.TransparencyEnabled) && _activeScene != Scene.Menu)
            _videoPlayer.ShowScreenBody();
        else
            _videoPlayer.HideScreenBody();

        _songSpeed = 1f;
        if (_activeScene != Scene.Menu && BS_Utils.Plugin.LevelData.IsSet)
        {
            _songSpeed = BS_Utils.Plugin.LevelData.GameplayCoreSceneSetupData.gameplayModifiers.songSpeedMul;

            if (BS_Utils.Plugin.LevelData.GameplayCoreSceneSetupData?.practiceSettings != null)
                _songSpeed = BS_Utils.Plugin.LevelData.GameplayCoreSceneSetupData.practiceSettings.songSpeedMul;
        }

        var totalOffset = startTime * _videoConfig.PlaybackSpeed + _videoConfig.GetOffsetInSec();

        // Cinema blocked starts below 1x at a positive video position because Unity 2019 could crash.
        // The game now runs Unity 2022.3. Starting at 1x and switching on the first frame avoids that start condition.
        if (TargetVideoSpeed < 1f)
        {
            _videoPlayer.PlaybackSpeed = 1f;
            _speedPendingFirstFrame = true;
            _videoPlayer.RemoveFrameReadyEventHandler(ApplySpeedOnFirstFrame);
            _videoPlayer.AddFrameReadyEventHandler(ApplySpeedOnFirstFrame);
            _loggingService.Info($"Starting video at 1x, switching to {TargetVideoSpeed:0.00}x on its first frame");
        }
        else
        {
            _videoPlayer.PlaybackSpeed = TargetVideoSpeed;
            _speedPendingFirstFrame = false;
        }

        // Start at the configured picture lead. Preview keeps its audio-to-audio target.
        totalOffset += LeadVideoSeconds;

        if (_videoConfig.endVideoAt != null && totalOffset > _videoConfig.endVideoAt)
            totalOffset = _videoConfig.endVideoAt.Value;

        // This will fail if the video is not prepared yet
        if (_videoPlayer.VideoDuration > 0) totalOffset %= _videoPlayer.VideoDuration;

        // This fixes an issue where the Unity video player sometimes ignores a change in the .time property if the time is very small and the player is currently playing
        if (Math.Abs(totalOffset) < 0.001f)
        {
            totalOffset = 0;
            _loggingService.Debug("Set very small offset to 0");
        }

        _loggingService.Debug(
            $"Total offset: {totalOffset}, startTime: {startTime}, songSpeed: {_songSpeed}, PlaybackSpeed: {_videoConfig.PlaybackSpeed}, player time: {_videoPlayer.PlayerTime}");

        StopAllCoroutines();

        // Temporary sync check and performance loggers: remove together
        _syncCheckPlayStart = Time.realtimeSinceStartup;
        _syncCheckCount = 0;
        ResetVideoCadence();

        if (_activeAudioSource != null && _activeAudioSource.time > 0)
            _lastKnownAudioSourceTime = _activeAudioSource.time;

        if (totalOffset < 0)
        {
            // Only song starts teach the start-up delay: menu starts follow the preview audio and vary far more
            if (!IsPreviewPlaying)
                StartDelayedPlay(armStartSync: true, learnable: _activeScene == Scene.SoloGameplay);
            else
            {
                // In menus we don't need to wait, instead the preview player starts earlier
                ArmStartSync(learnable: false);
                StartVideoPlayback();
            }
        }
        else
        {
            ArmStartSync(learnable: false);
            StartVideoPlayback();
            if (!_videoPlayer.PlayerIsPrepared)
            {
                _audioSourceStartTime = DateTime.Now;
                _offsetAfterPrepare = totalOffset;
            }
            else
            {
                _videoPlayer.PlayerTime = totalOffset;
            }
        }
    }

    private void StartVideoPlayback()
    {
        if (_whiteTestShown)
        {
            // A delayed start can keep showing the test until Play is called. The first live texture replaces it.
            _whiteTestShown = false;
            _lightManager.SnapOnFirstFrame();
            SetVideoPlayerScreenShaderParameters(_videoConfig);
        }

        _lightManager.SnapOnFirstFrame();
        _videoPlayer.Play();
    }

    private void ApplySpeedOnFirstFrame(VideoPlayer player, long frame)
    {
        _videoPlayer.RemoveFrameReadyEventHandler(ApplySpeedOnFirstFrame);
        _speedPendingFirstFrame = false;
        _videoPlayer.PlaybackSpeed = TargetVideoSpeed;
    }

    private void StartDelayedPlay(bool armStartSync, bool learnable)
    {
        if (_delayedPlayRoutine != null) StopCoroutine(_delayedPlayRoutine);
        _delayedPlayRoutine = StartCoroutine(PlayVideoDelayedCoroutine(armStartSync, learnable));
    }

    private IEnumerator PlayVideoDelayedCoroutine(bool armStartSync, bool learnable)
    {
        var compensationMs = _config.StartDelayCompensationMs;
        // The learned start-up delay, in video seconds: Play() is called this much before the video is due
        var compensationVideo = compensationMs / 1000f * TargetVideoSpeed;
        _loggingService.Debug($"Waiting for the song clock before playing video (start delay compensation {compensationMs} ms)");
        _videoPlayer.Pause();
        // Not Hide(): it would stop and unload the video loaded at scene start (see HideKeepLoaded)
        _videoPlayer.HideKeepLoaded();
        _videoPlayer.PlayerTime = 0;

        // Follows the song clock, so a skip (Intro Skip, practice seeks) or a game pause moves the start with the song
        yield return new WaitUntil(() => _activeAudioSource != null && GetTargetTime() + compensationVideo >= 0);
        _delayedPlayRoutine = null;

        if (_activeAudioSource!.time > 0) _lastKnownAudioSourceTime = _activeAudioSource.time;

        var lateBy = GetTargetTime() + compensationVideo;   // video seconds past the planned Play() call
        var prepared = _videoPlayer.PlayerIsPrepared;

        if (prepared && lateBy < 0.1f)
        {
            // Normal start: from frame 0, timed by the learned delay. Learn only if this check ran on time: a frame hitch
            // that delayed it would be learned as start-up delay. Under 40 ms covers one frame plus one audio buffer.
            var lateMs = lateBy / TargetVideoSpeed * 1000f;
            var onTime = lateMs < 40f;
            if (armStartSync && learnable && !onTime)
                _loggingService.Info(FormattableString.Invariant(
                    $"Start sync: the video start ran {lateMs:0} ms late (frame hitch), not learning from it"));
            if (armStartSync) ArmStartSync(learnable && onTime);
            StartVideoPlayback();
            yield break;
        }

        // Late start: never learn from it, since loading time or a skip isn't start-up delay
        if (armStartSync) ArmStartSync(learnable: false);
        if (prepared)
        {
            var startAt = ClampToVideo(GetTargetTime());
            _loggingService.Info(FormattableString.Invariant(
                $"Start sync: song jumped {lateBy * 1000:0} ms past the video start, starting it at {startAt:0.00} s"));
            _videoPlayer.PlayerTime = startAt;
        }
        else
        {
            _lateStartPending = true;
            _loggingService.Info("Start sync: video not loaded when due, it will start in place once loaded");
        }

        StartVideoPlayback();
    }

    // Wraps to the video length (like the existing resync code) and never goes below 0
    private float ClampToVideo(float videoTime)
    {
        var duration = _videoPlayer.VideoDuration;
        if (duration > 0) videoTime %= duration;
        return Math.Max(0f, videoTime);
    }

    private IEnumerator PlayVideoAfterAudioSourceCoroutine(bool preview)
    {
        float startTime;

        if (!preview)
        {
            _loggingService.Debug("Waiting for ATSC to be ready");

            try
            {
                if (TheaterFileHelpers.IsInEditor() && SceneManager.GetActiveScene().name != "GameCore")
                {
                    var songPreviewPlayer = Plugin.gameCoreContainer.Resolve<SongPreviewPlayer>();
                    if (songPreviewPlayer._audioSourceControllers.Any())
                    {
                        _activeAudioSource = songPreviewPlayer._audioSourceControllers.First().audioSource;
                        _loggingService.Debug("Got ATSC from SongPreviewPlayer");
                    }
                }
                else
                {
                    var atsc = Plugin.gameCoreContainer.Resolve<AudioTimeSyncController>();
                    _activeAudioSource = atsc._audioSource;
                    _timeSyncController = atsc;
                    _loggingService.Debug("Got ATSC from ATSC");
                }
            }
            catch
            {
                _loggingService.Debug("Failed to get AudioSource from DiContainer");
            }

            if (_activeAudioSource == null)
            {
                if (TheaterFileHelpers.IsInEditor() && SceneManager.GetActiveScene().name != "GameCore")
                {
                    // _editorTimeSyncController = Resources.FindObjectsOfTypeAll<BeatmapEditorAudioTimeSyncController>()
                    //     .FirstOrDefault(atsc => atsc.name == "BeatmapEditorAudioTimeSyncController");
                    _activeAudioSource = Resources.FindObjectsOfTypeAll<AudioSource>()
                        .FirstOrDefault(audioSource =>
                            audioSource.name == "SongPreviewAudioSource(Clone)" &&
                            audioSource.transform.parent == null);
                }
                else
                {
                    yield return new WaitUntil(() => Resources.FindObjectsOfTypeAll<AudioTimeSyncController>().Any());

                    // Hierarchy: Wrapper/StandardGameplay/GameplayCore/SongController
                    _timeSyncController = Resources.FindObjectsOfTypeAll<AudioTimeSyncController>()
                        .FirstOrDefault(atsc => atsc.transform.parent.parent.name.Contains("StandardGameplay"));

                    if (_timeSyncController == null)
                    {
                        _loggingService.Warn(
                            "Could not find ATSC the usual way. Did the object hierarchy change? Current scene name is " +
                            SceneManager.GetActiveScene().name);

                        // This throws an exception if we still don't find the ATSC
                        _timeSyncController = Resources.FindObjectsOfTypeAll<AudioTimeSyncController>().Last();
                        _loggingService.Warn("Selected ATSC: " + _timeSyncController.name);
                    }

                    _activeAudioSource = _timeSyncController._audioSource;
                }
            }
        }

        if (_activeAudioSource != null)
        {
            _lastKnownAudioSourceTime = 0;
            _loggingService.Debug($"Waiting for AudioSource {_activeAudioSource.name} to start playing");
            yield return new WaitUntil(() => _activeAudioSource.isPlaying);
            startTime = _activeAudioSource.time;
        }
        else
        {
            _loggingService.Warn("Active AudioSource was null, cannot wait for it to start");
            StopPreview(true);
            yield break;
        }

        _firstNoteSongTime = preview ? null : FindFirstNoteSongTime(startTime);
        _firstNoteReached = false;
        PlayVideo(startTime);
    }

    public void PauseVideo()
    {
        ResetVideoCadence(); // Temporary performance logger: a pause isn't a video stall
        CancelStartSync();
        StopAllCoroutines();
        if (_videoPlayer.IsPlaying && _videoConfig != null) _videoPlayer.Pause();
    }

    public void ResumeVideo()
    {
        ResetVideoCadence(); // Temporary performance logger: a pause isn't a video stall
        if (!_config.TheaterActive || _videoPlayer.IsPlaying || _videoConfig == null || !_videoConfig.IsPlayable ||
            (_videoPlayer.VideoEnded && _videoConfig.loop != true)) return;

        // PauseVideo cancelled any running start check; a pause before the first note gets a fresh one
        var recheck = CanRearmStartSync("resume");
        var targetTime = GetTargetTime();
        if (targetTime > 0)
        {
            StartVideoPlayback();
            if (recheck) ArmStartSync(learnable: false);
        }
        else
            StartDelayedPlay(armStartSync: recheck, learnable: false);
    }

    public void ResyncVideo(float? referenceTime = null)
    {
        if (_activeAudioSource == null || _videoConfig == null || !_videoConfig.IsPlayable) return;

        float newTime;
        if (referenceTime != null)
            newTime = referenceTime.Value;
        else
            newTime = GetTargetTime();

        if (newTime < 0)
        {
            CancelStartSync();
            _videoPlayer.HideKeepLoaded();
            StopAllCoroutines();
            // Menu and preview keep their own start check; in a song only before the first note (CanRearmStartSync)
            StartDelayedPlay(armStartSync: _activeScene != Scene.SoloGameplay || CanRearmStartSync("seek"),
                learnable: false);
            return;
        }
        else if (newTime > _videoPlayer.VideoDuration && _videoPlayer.VideoDuration > 0)
        {
            newTime %= _videoPlayer.VideoDuration;
        }

        if (Math.Abs(_videoPlayer.PlayerTime - newTime) < 0.2f) return;

        CancelStartSync();
        _videoPlayer.PlayerTime = newTime;
        if (_activeScene != Scene.SoloGameplay || IsPreviewPlaying)
        {
            _loggingService.Info($"Start sync: re-checking after a seek ({SyncCheckMode})");
            ArmStartSync(learnable: false);
        }
        else
        {
            // A seek before the first note (Intro Skip, or the 300 ms rule at the start) gets a fresh start check
            if (CanRearmStartSync("seek")) ArmStartSync(learnable: false);
        }
    }

    // Called when the user moves the speed slider: re-time the running video to the new speed
    public void ApplyPlaybackSpeed()
    {
        CancelStartSync();
        if (_videoConfig == null || !_videoPlayer.IsPlaying) return;
        if (!_speedPendingFirstFrame) _videoPlayer.PlaybackSpeed = TargetVideoSpeed;
        ResyncVideo();
    }

    private void VideoPlayerErrorReceived(string message)
    {
        // An offset seek may hold the preview audio paused until the next video frame, which won't come now
        if (_videoPlayer.IsSyncing) EndResyncAudioPause();
        // The path the player failed on. Read it before anything below changes the player.
        var failedUrl = _videoPlayer.Url;
        StopPlayback();

        // GetVideoPathForFormat returns null when the selected video's file is missing, so a deleted file never counts
        // as the selected video's own.
        var ownPath = _videoConfig?.GetVideoPathForFormat(_config.Format);
        var isOwnFile = string.IsNullOrEmpty(failedUrl) ||
                        string.Equals(failedUrl, ownPath, StringComparison.OrdinalIgnoreCase);

        // A path that failed must not stay on the player. The next load's Pause() would try it again, and that error would
        // stop the next load. Selecting or retrying the video loads it again from PrepareVideo.
        if (!string.IsNullOrEmpty(failedUrl))
        {
            _loggingService.Warn($"Unloading the video file that failed to play: {failedUrl}");
            _videoPlayer.UnloadVideo();
        }

        if (_videoConfig == null) return;

        _videoConfig.UpdateDownloadState(_config.Format);
        if (!isOwnFile)
        {
            // The selected video's status comes from UpdateDownloadState above (for example "Not downloaded").
            _loggingService.Warn("The playback error was for a different file than the selected video's; not marking it");
            return;
        }

        _videoConfig.ErrorMessage = "Theater playback error.";
        if (message.Contains("Unexpected error code (10)") && SystemInfo.graphicsDeviceVendor == "NVIDIA")
            _videoConfig.ErrorMessage += " Try disabling NVIDIA Fast Sync.";
        else if (message.Contains("It seems that the Microsoft Media Foundation is not installed on this machine"))
            _videoConfig.ErrorMessage += " Install Microsoft Media Foundation.";
        else
            _videoConfig.ErrorMessage += " See logs for details.";

        // _videoMenu.SetupLevelDetailView(VideoConfig);
    }

    #endregion

    #region Start Sync

    // Re-runs the start check on the playing menu or Preview video, e.g. after Auto sync's analysis, which has been seen
    // to knock the playing video off by 70–300 ms. Songs are never touched.
    public void RecheckSync(string reason)
    {
        if (_activeScene == Scene.SoloGameplay || !_videoPlayer.IsPlaying || _activeAudioSource == null ||
            _videoPlayer.IsSyncing || _startSyncPhase is not (StartSyncPhase.Off or StartSyncPhase.Done)) return;

        _loggingService.Info($"Start sync: re-checking after {reason} ({SyncCheckMode})");
        CancelStartSync();
        ArmStartSync(learnable: false);
    }

    private void ArmStartSync(bool learnable, bool afterJump = false)
    {
        _startSyncPhase = StartSyncPhase.WaitingForFirstFrame;
        _startSyncLearnable = learnable;
        _startSyncJumped = afterJump;
        Array.Clear(_startSyncSamples, 0, _startSyncSamples.Length);
        _startSyncSampleCount = 0;
        _startSyncLearned = false;
    }

    private void CancelStartSync()
    {
        if (_startSyncPhase == StartSyncPhase.Nudging) RestoreStartSyncSpeed();
        _startSyncPhase = StartSyncPhase.Off;
    }

    // Start sync may run again after a seek or a resume, in songs only. With Sync Correction "Before first note" (the
    // user's original rule) only while this play hasn't reached its first note; an unknown first note means no. With
    // "Any time", always.
    private bool CanRearmStartSync(string reason)
    {
        if (_activeScene != Scene.SoloGameplay || IsPreviewPlaying || _activeAudioSource == null) return false;
        var songTime = _activeAudioSource.time;

        if (_config.SyncCorrectionAnyTime)
        {
            _loggingService.Info(FormattableString.Invariant(
                $"Start sync: re-checking after a {reason} at song {songTime:0.00} s (correction any time)"));
            return true;
        }

        if (_firstNoteReached || _firstNoteSongTime is not { } firstNoteTime) return false;
        if (songTime >= firstNoteTime - FirstNoteMarginSeconds) return false;

        _loggingService.Info(FormattableString.Invariant(
            $"Start sync: re-checking after a {reason} at song {songTime:0.00} s (first note at {firstNoteTime:0.00} s)"));
        return true;
    }

    private void RestoreStartSyncSpeed()
    {
        // A slow start applies its own speed in ApplySpeedOnFirstFrame.
        if (!_speedPendingFirstFrame) _videoPlayer.PlaybackSpeed = TargetVideoSpeed;
    }

    private void StartSyncStep(float audioSourceTime)
    {
        if (_startSyncPhase is StartSyncPhase.Off or StartSyncPhase.Done) return;
        if (_videoPlayer.IsSyncing || !_videoPlayer.IsPlaying || _activeAudioSource?.isPlaying != true ||
            _speedPendingFirstFrame) return;

        // Wrap like FrameReadyCore, so a looping video past its length isn't measured against the unwrapped target
        var startSyncTarget = GetTargetTime();
        var startSyncDuration = _videoPlayer.VideoDuration;
        if (startSyncDuration > 0) startSyncTarget %= startSyncDuration;
        var aheadMs = (_videoPlayer.PlayerTime - startSyncTarget) * 1000;
        var now = Time.realtimeSinceStartup;
        if (_startSyncPhase == StartSyncPhase.WaitingForFirstFrame)
        {
            _startSyncFirstFrameTime = now;
            _startSyncPhase = StartSyncPhase.Measuring;
        }

        if (_startSyncPhase == StartSyncPhase.Nudging)
        {
            if (now >= _startSyncNudgeEndTime || now >= GetStartSyncWindowEnd(now, audioSourceTime))
            {
                RestoreStartSyncSpeed();
                // The next six frames measure the result at the normal playback speed.
                _startSyncPhase = StartSyncPhase.Measuring;
            }
            return;
        }

        // Both clocks advance in steps. Use the median of six frames, so a stale first frame or two (seen after
        // some starts) can't skew the learned delay or the nudge.
        _startSyncSamples[_startSyncSampleCount++] = aheadMs;
        if (_startSyncSampleCount < _startSyncSamples.Length) return;

        var sorted = (double[])_startSyncSamples.Clone();
        Array.Sort(sorted);
        var meanMs = (sorted[2] + sorted[3]) / 2;
        Array.Clear(_startSyncSamples, 0, _startSyncSamples.Length);
        _startSyncSampleCount = 0;

        var mode = SyncCheckMode;
        var absoluteErrorMs = Math.Abs(meanMs);
        var isMenuOrPreview = _activeScene != Scene.SoloGameplay || IsPreviewPlaying;
        if (!isMenuOrPreview && absoluteErrorMs > 250)
        {
            _loggingService.Info(FormattableString.Invariant(
                $"Start sync: {mode} first frame {meanMs:+0;-0;0} ms from target, too far to nudge"));
            _startSyncPhase = StartSyncPhase.Done;
            return;
        }

        if (!_startSyncLearned)
        {
            _startSyncFirstErrorMs = meanMs;
            var lead = IsPreviewPlaying ? 0 : _config.VideoLeadMs;
            if (_startSyncLearnable && !IsPreviewPlaying && absoluteErrorMs <= 150)
            {
                var oldComp = _config.StartDelayCompensationMs;
                var step = Math.Max(-15, Math.Min(15, (int)Math.Round(-0.5 * meanMs)));
                var newComp = Math.Max(0, Math.Min(200, oldComp + step));
                if (newComp != oldComp) _config.StartDelayCompensationMs = newComp;
                _loggingService.Info(FormattableString.Invariant(
                    $"Start sync: {mode} first frame {_startSyncFirstErrorMs:+0;-0;0} ms from target (lead {lead} ms); start delay compensation {oldComp} -> {newComp} ms"));
            }
            else if (_startSyncLearnable && !IsPreviewPlaying && absoluteErrorMs > 150)
            {
                _loggingService.Info(FormattableString.Invariant(
                    $"Start sync: {mode} first frame {meanMs:+0;-0;0} ms from target (lead {lead} ms); not learning from an outlier"));
            }
            else
            {
                _loggingService.Info(FormattableString.Invariant(
                    $"Start sync: {mode} first frame {_startSyncFirstErrorMs:+0;-0;0} ms from target (lead {lead} ms)"));
            }

            _startSyncLearned = true;
        }

        var jumpThresholdMs = IsPreviewPlaying ? PreviewStartSyncJumpThresholdMs : MenuStartSyncJumpThresholdMs;
        if (isMenuOrPreview && absoluteErrorMs > jumpThresholdMs)
        {
            if (!_startSyncJumped)
            {
                RestoreStartSyncSpeed();
                if (IsPreviewPlaying)
                {
                    // Preview audio is audible. Hold its reference clock until the seek delivers a new video frame.
                    _videoPlayer.IsSyncing = true;
                    _activeAudioSource.Pause();
                    _videoPlayer.PlayerTime = startSyncTarget;
                    _videoPlayer.RemoveFrameReadyEventHandler(PlayerStartedAfterResync);
                    _videoPlayer.AddFrameReadyEventHandler(PlayerStartedAfterResync);
                }
                else
                {
                    // The muted menu video can seek without interrupting the song preview audio.
                    _videoPlayer.PlayerTime = startSyncTarget;
                }

                ArmStartSync(learnable: false, afterJump: true);
                var seekType = IsPreviewPlaying ? "held" : "plain";
                _loggingService.Info(FormattableString.Invariant(
                    $"Start sync: {mode} {meanMs:+0;-0;0} ms from target, jumping to it ({seekType})"));
                return;
            }

            _loggingService.Info(FormattableString.Invariant(
                $"Start sync: {mode} still {meanMs:+0;-0;0} ms off after a jump"));
        }

        if (absoluteErrorMs <= 10)
        {
            _startSyncPhase = StartSyncPhase.Done;
            _loggingService.Info(FormattableString.Invariant($"Start sync: {mode} on target ({meanMs} ms)"));
            return;
        }

        // Preview audio is audible, so its playback speed changes by a smaller amount.
        var rate = IsPreviewPlaying ? 0.05f : 0.15f;
        var duration = absoluteErrorMs / 1000 / (TargetVideoSpeed * rate);
        var windowEnd = GetStartSyncWindowEnd(now, audioSourceTime);
        duration = Math.Min(duration, windowEnd - now);
        if (duration < 0.05)
        {
            _startSyncPhase = StartSyncPhase.Done;
            _loggingService.Info(FormattableString.Invariant(
                $"Start sync: {mode} {meanMs} ms from target, no time left in the correction window"));
            return;
        }

        _videoPlayer.PlaybackSpeed = TargetVideoSpeed * (meanMs < 0 ? 1 + rate : 1 - rate);
        _startSyncNudgeEndTime = (float)(now + duration);
        _startSyncPhase = StartSyncPhase.Nudging;
    }

    private float GetStartSyncWindowEnd(float now, float audioSourceTime)
    {
        if (IsPreviewPlaying || _activeScene == Scene.Menu) return _startSyncFirstFrameTime + 2f;
        // Sync Correction "Any time": the full 3 s after the first frame in songs, whether or not notes have started
        if (_config.SyncCorrectionAnyTime) return _startSyncFirstFrameTime + 3f;
        if (_firstNoteSongTime is not { } firstNoteTime) return _startSyncFirstFrameTime + 1.5f;

        // Song time advances at the selected gameplay or practice speed.
        var noteDeadline = now + (firstNoteTime - FirstNoteMarginSeconds - audioSourceTime) / _songSpeed;
        return Math.Min(_startSyncFirstFrameTime + 3f, noteDeadline);
    }

    private float? FindFirstNoteSongTime(float audioSourceTime)
    {
        IReadonlyBeatmapData? beatmapData = null;
        try
        {
            beatmapData = Plugin.gameCoreContainer.TryResolve<IReadonlyBeatmapData>();
        }
        catch
        {
            // Try the scene setup data if the gameplay container can't resolve the beatmap.
        }

        float? firstNoteSongTime = null;
        try
        {
            beatmapData ??= BS_Utils.Plugin.LevelData.GameplayCoreSceneSetupData.transformedBeatmapData;
            if (beatmapData != null)
            {
                foreach (var note in beatmapData.GetBeatmapDataItems<NoteData>(0))
                {
                    if (note.colorType != ColorType.None && note.time >= audioSourceTime &&
                        (firstNoteSongTime == null || note.time < firstNoteSongTime.Value))
                        firstNoteSongTime = note.time;
                }
            }
        }
        catch
        {
            firstNoteSongTime = null;
        }

        var correction = _config.SyncCorrectionAnyTime ? "any time" : "before first note";
        if (firstNoteSongTime is { } time)
            _loggingService.Info(FormattableString.Invariant(
                $"Start sync: first note at {time:0.00} s (correction {correction})"));
        else if (_config.SyncCorrectionAnyTime)
            _loggingService.Info("Start sync: first note time unavailable (correction any time)");
        else
            _loggingService.Info("Start sync: first note time unavailable, using a 1.5 s window");

        return firstNoteSongTime;
    }

    #endregion

    #region Video Prepare

    public void PrepareVideo(VideoConfig video)
    {
        CancelResyncAudioPause();
        _previewWaitingForVideoPlayer = true;

        if (_prepareVideoCoroutine != null) StopCoroutine(_prepareVideoCoroutine);

        if (!_whiteTestShown) _videoPlayer.ClearTexture();

        _prepareVideoCoroutine = PrepareVideoCoroutine(video);
        StartCoroutine(_prepareVideoCoroutine);
    }

    private IEnumerator PrepareVideoCoroutine(VideoConfig video)
    {
        _videoConfig = video;

        // A player still pointing at a file that has since been deleted would load it again on Pause() and fail. The error
        // handler would then stop this load before it sets the new URL.
        var previousUrl = _videoPlayer.Url;
        if (!string.IsNullOrEmpty(previousUrl) && !File.Exists(previousUrl))
        {
            _loggingService.Warn($"Previous video file no longer exists, unloading it: {previousUrl}");
            _videoPlayer.UnloadVideo();
        }
        else
        {
            _videoPlayer.Pause();
        }
        if (_videoConfig.DownloadState != DownloadState.Downloaded)
        {
            _loggingService.Debug("Video is not downloaded, stopping prepare");
            if (!_whiteTestShown) _videoPlayer.FadeOut();
            yield break;
        }

        _videoPlayer.LoopVideo(video.loop == true);
        SetVideoPlayerScreenShaderParameters(video);
        _videoPlayer.SetBloomIntensity(video.bloom);

        var videoPath = video.GetVideoPathForFormat(_config.Format);
        if (videoPath == null)
        {
            _loggingService.Debug("Video file with desired format not found, stopping prepare");
            if (!_whiteTestShown) _videoPlayer.FadeOut();
            yield break;
        }
        _loggingService.Info($"Loading video: {videoPath}");

        var videoFileInfo = new FileInfo(videoPath);
        var timeout = new DownloadTimeout(0.25f);
        if (_videoPlayer.Url != videoPath)
            yield return new WaitUntil(() =>
                !TheaterFileHelpers.IsFileLocked(videoFileInfo) || timeout.HasTimedOut);

        timeout.Stop();
        if (timeout.HasTimedOut && TheaterFileHelpers.IsFileLocked(videoFileInfo))
            _loggingService.Warn("Video file locked: " + videoPath);

        _videoPlayer.Url = videoPath;
        _videoPlayer.Prepare();
        // Prepare raises the stopped event, which resets the limiter. The test still owns the screen until Play.
        if (_whiteTestShown) _lightManager.ShowWhiteTest(true);
    }

    #endregion

    #region Video Preview

    public void ApplyOffset(int offset)
    {
        CancelStartSync();
        if (!_videoPlayer.IsPlaying || _activeAudioSource == null) return;

        // The callers have already changed _videoConfig.offset, so GetTargetTime() is where the video belongs now. Moving it
        // there, not by `offset` from PlayerTime, keeps quick clicks: during a seek PlayerTime still reports the old
        // position, so every click used to start from it and the earlier clicks were lost.
        if (GetTargetTime() < 0)
        {
            // The video now starts later than the song's current point. There's no seek to wait for: keep the audio running
            // and let the delayed play start the video on the song clock. An earlier click's seek may still hold the audio
            // paused, and the paused video would never deliver the frame that resumes it, so resume it here.
            if (_videoPlayer.IsSyncing) EndResyncAudioPause();
            ApplyOffsetToVideo();
            _loggingService.Info("Applying offset: " + offset);
            return;
        }

        // Pause the preview audio source and start seeking. Audio Source will be re-enabled after video player draws its next frame
        _videoPlayer.IsSyncing = true;
        _activeAudioSource.Pause();

        ApplyOffsetToVideo();
        // A quick second click must not hook the handler twice
        _videoPlayer.RemoveFrameReadyEventHandler(PlayerStartedAfterResync);
        _videoPlayer.AddFrameReadyEventHandler(PlayerStartedAfterResync);
        _loggingService.Info("Applying offset: " + offset);
    }

    // Moves the video to where the current offset puts it (GetTargetTime() includes _videoConfig.offset), or hands it to
    // the delayed play when that point is before the video's start. Repeated calls land on the same spot.
    public void ApplyOffsetToVideo()
    {
        CancelStartSync();
        if (_activeAudioSource == null || _videoConfig == null || !_videoConfig.IsPlayable) return;

        var newTime = GetTargetTime();

        if (newTime < 0)
        {
            _videoPlayer.HideKeepLoaded();
            StopAllCoroutines();
            StartDelayedPlay(armStartSync: true, learnable: false);
            return;
        }
        else if (newTime >= _videoPlayer.VideoDuration && _videoPlayer.VideoDuration > 0)
        {
            newTime %= _videoPlayer.VideoDuration;
        }

        _videoPlayer.PlayerTime = newTime;
    }

    private void PlayerStartedAfterResync(VideoPlayer player, long frame)
    {
        EndResyncAudioPause();
    }

    private void CancelResyncAudioPause()
    {
        // Playback is being torn down, so nothing should resume.
        _videoPlayer.RemoveFrameReadyEventHandler(PlayerStartedAfterResync);
        _videoPlayer.IsSyncing = false;
    }

    // Ends the audio pause a held seek started: unhooks the frame handler and plays the preview audio again
    private void EndResyncAudioPause()
    {
        _videoPlayer.RemoveFrameReadyEventHandler(PlayerStartedAfterResync);
        _videoPlayer.IsSyncing = false;
        if (_activeAudioSource == null)
        {
            _loggingService.Warn("Active audio source was null in frame ready after resync");
            return;
        }

        if (!_activeAudioSource.isPlaying) _activeAudioSource.Play();
    }

    public void SetAudioSourcePanning(float pan)
    {
        try
        {
            if (_audioSourceControllers == null) return;

            // If resetting the panning back to neutral (0f), set all audio sources.
            // Otherwise only change the active channel.
            if (pan == 0f || _activeAudioSource == null)
                foreach (var sourceVolumeController in _audioSourceControllers)
                    sourceVolumeController.audioSource.panStereo = pan;
            else
                _activeAudioSource.panStereo = pan;
        }
        catch (Exception e)
        {
            _loggingService.Warn(e);
        }
    }

    public async Task StartPreview(float? songStartTime = null)
    {
        if (_videoConfig == null || _currentLevel == null)
        {
            _loggingService.Warn("No video or level selected in OnPreviewAction");
            return;
        }

        if (IsPreviewPlaying)
        {
            _loggingService.Debug("Stopping preview");
            StopPreview(true);
        }
        else
        {
            _loggingService.Debug("Starting preview");
            IsPreviewPlaying = true;
            var generation = _previewGeneration;

            if (_videoPlayer.IsPlaying) StopPlayback();

            if (!_videoPlayer.IsPrepared) _loggingService.Debug("Video not prepared yet");

            // Start the preview at the point the video kicks in
            var startTime = 0f;
            if (_videoConfig.offset < 0) startTime = -_videoConfig.GetOffsetInSec() / _videoConfig.PlaybackSpeed;
            if (songStartTime != null) startTime = Math.Max(0f, songStartTime.Value);

            if (_songPreviewPlayer == null)
            {
                _loggingService.Error("Failed to get reference to SongPreviewPlayer during preview");
                return;
            }

            try
            {
                _loggingService.Debug($"Preview start time: {startTime}, offset: {_videoConfig.GetOffsetInSec()}");
                var audioClip = await VideoLoader.GetAudioClipForLevel(_currentLevel);
                // Stopped (or stopped and restarted) while the clip loaded: that newer call owns the preview now
                if (generation != _previewGeneration) return;
                if (audioClip != null)
                    _songPreviewPlayer.CrossfadeTo(audioClip, -5f, startTime,
                        _currentLevel.songDuration, null);
                else
                    _loggingService.Error("AudioClip for level failed to load");
            }
            catch (Exception e)
            {
                _loggingService.Error(e);
                IsPreviewPlaying = false;
                return;
            }

            // +1.0 is hard right. only pan "mostly" right, because for some reason the video player audio doesn't
            // pan hard left either. Also, it sounds a bit more comfortable.
            SetAudioSourcePanning(0.9f);
            StartCoroutine(PlayVideoAfterAudioSourceCoroutine(true));
            _videoPlayer.PanStereo = -1f; // -1 is hard left
            _videoPlayer.Unmute();
        }
    }

    private void StartSongPreview()
    {
        if (!_config.TheaterActive || _videoConfig is not { IsPlayable: true }) return;

        if (_previewWaitingForPreviewPlayer || _previewWaitingForVideoPlayer || IsPreviewPlaying) return;

        if (_currentLevel != null && VideoLoader.IsDlcSong(_currentLevel)) return;

        var delay = DateTime.Now.Subtract(_previewSyncStartTime);
        var delaySeconds = (float)delay.TotalSeconds;

        _loggingService.Debug($"Starting song preview playback with a delay of {delaySeconds}");

        var timeRemaining = _previewTimeRemaining - delaySeconds;
        if (timeRemaining > 1 || _previewTimeRemaining == 0)
            PlayVideo(_previewStartTime + delaySeconds);
        else
            _loggingService.Debug(
                $"Not playing song preview, because delay was too long. Remaining preview time: {_previewTimeRemaining}");
    }

    public void StopPreview(bool stopPreviewMusic)
    {
        CancelResyncAudioPause();
        _lateStartPending = false;
        CancelStartSync();
        if (!IsPreviewPlaying) return;
        _loggingService.Debug($"Stopping preview (stop audio source: {stopPreviewMusic}");
        _previewGeneration++;

        if (!_whiteTestShown) _videoPlayer.FadeOut();
        StopAllCoroutines();

        if (stopPreviewMusic && _songPreviewPlayer != null)
        {
            _songPreviewPlayer.CrossfadeToDefault();
            _videoPlayer.Mute();
        }

        IsPreviewPlaying = false;

        SetAudioSourcePanning(0f); // 0f is neutral
        _videoPlayer.Mute();
    }

    #endregion

    public void AddScreenToVideoPlayer(GameObject screen, CurvedSurface curvedSurface,
        CustomBloomPrePass customBloomPrePass)
    {
        _videoPlayer.AddScreen(screen, curvedSurface, customBloomPrePass);
    }

    public void DisableVideoPlayerScreens()
    {
        _videoPlayer.DisableScreen();
    }

    public GameObject? FindVideoPlayerScreen(Predicate<ScreenObjectGroup> predicate)
    {
        return _videoPlayer.FindScreen(predicate);
    }

    private float GetReferenceTime()
    {
        if (_activeAudioSource == null || _videoConfig == null) return 0;

        float time;
        if (_activeAudioSource.time == 0)
            time = _lastKnownAudioSourceTime;
        else
            time = _activeAudioSource.time;

        var speed = _videoConfig.PlaybackSpeed;
        return time * speed + _videoConfig.offset / 1000f;
    }

    public VideoConfig? GetVideoConfig()
    {
        return _videoConfig;
    }

    public GameObject? GetVideoPlayerFirstScreen()
    {
        return _videoPlayer.GetFirstScreen();
    }

    public void SetSelectedLevel(BeatmapLevel? level, VideoConfig? config)
    {
        _previewWaitingForPreviewPlayer = true;
        _previewWaitingForVideoPlayer = true;

        _currentLevel = level;
        _videoConfig = config;
        _loggingService.Debug($"Selected Level: {level?.levelID ?? "null"}");

        if (_videoConfig == null)
        {
            if (!_whiteTestShown) _videoPlayer.FadeOut();
            StopAllCoroutines();
            return;
        }

        PrepareVideo(_videoConfig);
        if (!_whiteTestShown && level != null && VideoLoader.IsDlcSong(level)) _videoPlayer.FadeOut();
    }

    public void SetVideoPlayerScreenShaderParameters(VideoConfig? config)
    {
        // Keep the calibration texture on default shader settings, including during slider changes and preparation.
        var appliedConfig = _whiteTestShown ? null : config;
        _videoPlayer.SetScreenShaderParameters(appliedConfig);
        var correction = appliedConfig?.colorCorrection;
        _lightManager.SetMapFactor((correction?.brightness ?? 1f) * (correction?.exposure ?? 1f));
    }

    private async Task ShowSongCover()
    {
        if (_currentLevel == null) return;

        try
        {
            var coverSprite = await _currentLevel.previewMediaData.GetCoverSpriteAsync();
            _videoPlayer.SetCoverTexture(coverSprite.texture);
            _videoPlayer.FadeIn();
        }
        catch (Exception e)
        {
            _loggingService.Error(e);
        }
    }

    public void UpdateVideoPlayerPlacement(VideoConfig config, Scene scene)
    {
        var placement = Placement.CreatePlacementForConfig(config, scene, _videoPlayer.GetVideoAspectRatio());
        if (scene == Scene.SoloGameplay) placement.ApplyUserScale(_displaySettings.ScreenScale / 100f);
        _videoPlayer.SetPlacement(placement);
    }

    public void SetVideoPlayerSoftParent(Transform transform)
    {
        _videoPlayer.SetSoftParent(transform);
    }

    internal GameObject CreateScreen(Transform parent)
    {
        return _videoPlayer.CreateScreen(parent);
    }
}