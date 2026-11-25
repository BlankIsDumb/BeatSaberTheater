using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BeatmapEditor3D.DataModels;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using BeatSaberMarkupLanguage.GameplaySetup;
using BeatSaberMarkupLanguage.Parser;
using BeatSaberTheater.Download;
using BeatSaberTheater.Playback;
using BeatSaberTheater.Services;
using BeatSaberTheater.Settings;
using BeatSaberTheater.Sync;
using BeatSaberTheater.Util;
using BeatSaberTheater.Video;
using BeatSaberTheater.Video.Config;
using HMUI;
using JetBrains.Annotations;
using SongCore.Data;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using Zenject;
using Object = UnityEngine.Object;

namespace BeatSaberTheater.VideoMenu;

public class VideoMenuUI : IInitializable, IDisposable
{
    private const float MinPlaybackSpeed = 0.25f;
    private const float MaxPlaybackSpeed = 2f;

    private readonly GameplaySetup _gameplaySetup;
    private readonly LoggingService _loggingService;
    private readonly PlaybackManager _playbackManager;
    private readonly PluginConfig _config;

    [UIObject("root-object")] private readonly GameObject _root = null!;
    [UIComponent("no-video-bg")] private readonly RectTransform _noVideoViewRect = null!;
    [UIComponent("video-details")] private readonly RectTransform _videoDetailsViewRect = null!;
    [UIComponent("video-search-results")] private readonly RectTransform _videoSearchResultsViewRect = null!;
    [UIComponent("video-list")] private readonly CustomListTableData _customListTableData = null!;

    [UIComponent("search-results-loading")]
    private readonly TextMeshProUGUI _searchResultsLoadingText = null!;

    [UIComponent("search-keyboard")] private readonly ModalKeyboard _searchKeyboard = null!;
    [UIComponent("video-title")] private readonly TextMeshProUGUI _videoTitleText = null!;
    [UIComponent("no-video-text")] private readonly TextMeshProUGUI _noVideoText = null!;
    [UIComponent("video-author")] private readonly TextMeshProUGUI _videoAuthorText = null!;
    [UIComponent("video-duration")] private readonly TextMeshProUGUI _videoDurationText = null!;
    [UIComponent("video-status")] private readonly TextMeshProUGUI _videoStatusText = null!;
    [UIComponent("video-offset-label")] private readonly TextMeshProUGUI _videoOffsetLabel = null!;
    [UIComponent("video-offset")] private readonly TextMeshProUGUI _videoOffsetText = null!;
    [UIComponent("video-thumbnail")] private readonly Image _videoThumnnail = null!;
    [UIComponent("preview-button")] private readonly TextMeshProUGUI _previewButtonText = null!;
    [UIComponent("preview-button")] private readonly Button _previewButton = null!;
    [UIComponent("search-button")] private readonly Button _searchButton = null!;
    [UIComponent("delete-config-button")] private readonly Button _deleteButton = null!;
    [UIComponent("delete-video-button")] private readonly Button _deleteVideoButton = null!;
    [UIComponent("delete-video-button")] private readonly TextMeshProUGUI _deleteVideoButtonText = null!;
    [UIComponent("download-button")] private readonly Button _downloadButton = null!;
    [UIComponent("revert-offset-button")] private readonly Button _revertOffsetButton = null!;
    [UIObject("title-buttons")] private readonly GameObject _titleButtons = null!;
    [UIComponent("display-settings-view")] private readonly RectTransform _displaySettingsViewRect = null!;
    [UIComponent("turn-on-button")] private readonly Button _turnOnButton = null!;
    [UIObject("offset-controls")] private readonly GameObject _offsetControls = null!;
    [UIObject("customize-offset-toggle")] private readonly GameObject _customizeOffsetToggle = null!;
    [UIComponent("auto-sync-button")] private readonly Button _autoSyncButton = null!;
    [UIComponent("auto-sync-button")] private readonly TextMeshProUGUI _autoSyncButtonText = null!;
    [UIComponent("auto-sync-modal")] private readonly ModalView _autoSyncModal = null!;
    [UIComponent("auto-sync-title")] private readonly TextMeshProUGUI _autoSyncTitle = null!;
    [UIComponent("auto-sync-subtitle")] private readonly TextMeshProUGUI _autoSyncSubtitle = null!;
    [UIObject("auto-sync-rows")] private readonly GameObject _autoSyncRows = null!;

    [UIObject("auto-sync-row-0")] private readonly GameObject _autoSyncRow0 = null!;
    [UIComponent("auto-sync-offset-0")] private readonly TextMeshProUGUI _autoSyncOffset0 = null!;
    [UIComponent("auto-sync-tag-0")] private readonly TextMeshProUGUI _autoSyncTag0 = null!;
    [UIComponent("auto-sync-info-0")] private readonly TextMeshProUGUI _autoSyncInfo0 = null!;
    [UIComponent("auto-sync-try-0")] private readonly Button _autoSyncTry0 = null!;
    [UIComponent("auto-sync-try-0")] private readonly TextMeshProUGUI _autoSyncTryText0 = null!;
    [UIComponent("auto-sync-apply-0")] private readonly Button _autoSyncApply0 = null!;
    [UIComponent("auto-sync-note-0")] private readonly TextMeshProUGUI _autoSyncNote = null!;

    [UIObject("auto-sync-row-1")] private readonly GameObject _autoSyncRow1 = null!;
    [UIComponent("auto-sync-offset-1")] private readonly TextMeshProUGUI _autoSyncOffset1 = null!;
    [UIComponent("auto-sync-tag-1")] private readonly TextMeshProUGUI _autoSyncTag1 = null!;
    [UIComponent("auto-sync-info-1")] private readonly TextMeshProUGUI _autoSyncInfo1 = null!;
    [UIComponent("auto-sync-try-1")] private readonly Button _autoSyncTry1 = null!;
    [UIComponent("auto-sync-try-1")] private readonly TextMeshProUGUI _autoSyncTryText1 = null!;
    [UIComponent("auto-sync-apply-1")] private readonly Button _autoSyncApply1 = null!;

    [UIObject("auto-sync-row-2")] private readonly GameObject _autoSyncRow2 = null!;
    [UIComponent("auto-sync-offset-2")] private readonly TextMeshProUGUI _autoSyncOffset2 = null!;
    [UIComponent("auto-sync-tag-2")] private readonly TextMeshProUGUI _autoSyncTag2 = null!;
    [UIComponent("auto-sync-info-2")] private readonly TextMeshProUGUI _autoSyncInfo2 = null!;
    [UIComponent("auto-sync-try-2")] private readonly Button _autoSyncTry2 = null!;
    [UIComponent("auto-sync-try-2")] private readonly TextMeshProUGUI _autoSyncTryText2 = null!;
    [UIComponent("auto-sync-apply-2")] private readonly Button _autoSyncApply2 = null!;

    [UIParams] private readonly BSMLParserParams _bsmlParserParams = null!;

    private Coroutine? _searchLoadingCoroutine;
    private Coroutine? _updateSearchResultsCoroutine;

    [UIValue("customize-offset")]
    public bool CustomizeOffset
    {
        get => _currentVideo != null && (!_currentVideo.IsOfficialConfig ||
                                         _currentVideo.userSettings?.customOffset == true);
        set
        {
            if (_currentVideo == null || !value) return;

            _currentVideo.userSettings ??= new UserSettings();
            _currentVideo.userSettings.customOffset = true;
            _currentVideo.userSettings.originalOffset = _currentVideo.offset;
            _currentVideo.userSettings.originalPlaybackSpeed = _currentVideo.PlaybackSpeed;
            _currentVideo.NeedsToSave = true;
            _customizeOffsetToggle.SetActive(false);
            _offsetControls.SetActive(true);
            UpdateRevertOffsetButton();
            UpdateAutoSyncButton();
        }
    }

    [UIValue("playback-speed")]
    public float PlaybackSpeed
    {
        get => ClampSpeed(_currentVideo?.PlaybackSpeed ?? 1f);
        set
        {
            if (_currentVideo == null) return;

            var speed = ClampSpeed(value);
            // A slider refresh can echo its displayed value, even if a config's speed is outside the slider range.
            if (Mathf.Approximately(speed, ClampSpeed(_currentVideo.PlaybackSpeed))) return;

            RecordOriginalSync(_currentVideo);
            _currentVideo.playbackSpeed = Mathf.Approximately(speed, 1f) ? null : speed;
            _currentVideo.NeedsToSave = true;
            _loggingService.Info($"Playback speed set to {speed:0.00}x");
            _playbackManager.ApplyPlaybackSpeed();
            UpdateOffsetControls();
        }
    }

    private static float ClampSpeed(float speed) =>
        Mathf.Clamp(Mathf.Round(speed * 100f) / 100f, MinPlaybackSpeed, MaxPlaybackSpeed);

    private VideoMenuStatus _menuStatus = null!;
    private LevelDetailViewController? _levelDetailMenu;
    private bool _videoMenuInitialized;

    private BeatmapLevel? _currentLevel;
    private bool _currentLevelIsPlaylistSong;
    private SongData? _extraSongData;
    private SongData.DifficultyData? _difficultyData;
    private VideoConfig? _currentVideo;
    private bool _videoMenuActive;
    private int _selectedCell;
    private string _searchText = "";

    private string? _thumbnailURL;

    private bool _displaySettingsOpen;

    private (GameObject Row, TextMeshProUGUI Offset, TextMeshProUGUI Tag, TextMeshProUGUI Info, Button Try,
        TextMeshProUGUI TryText, Button Apply)[] _autoSyncRowControls = [];
    private CancellationTokenSource? _autoSyncCts;
    private VideoConfig? _autoSyncVideo;
    private bool _autoSyncPopupOpen;
    private int _autoSyncStartOffset;
    private int _autoSyncTriedIndex = -1;
    private readonly List<int> _autoSyncOffsets = [];
    private float? _autoSyncTryStart;
    private SyncConfidence _autoSyncConfidence;
    private bool _editorBeatmapSelected;

    private readonly TheaterCoroutineStarter _coroutineStarter;
    private readonly DisplaySettings _displaySettings;
    private readonly DownloadService _downloadService;
    private readonly SearchService _searchService;
    private readonly VideoLoader _videoLoader;

    private readonly List<YTResult> _searchResults = [];

    internal VideoMenuUI(TheaterCoroutineStarter coroutineStarter, DisplaySettings displaySettings,
        DownloadService downloadService, GameplaySetup gameplaySetup, LoggingService loggingService,
        PlaybackManager playbackManager, PluginConfig config, SearchService searchService, VideoLoader videoLoader)
    {
        _config = config;
        _coroutineStarter = coroutineStarter;
        _displaySettings = displaySettings;
        _downloadService = downloadService;
        _gameplaySetup = gameplaySetup;
        _loggingService = loggingService;
        _playbackManager = playbackManager;
        _searchService = searchService;
        _videoLoader = videoLoader;
    }

    public void Initialize()
    {
        _loggingService.Debug("Adding tab");
        _gameplaySetup.AddTab("Theater", "BeatSaberTheater.VideoMenu.Views.video-menu.bsml", this);

        _loggingService.Debug("Initializing VideoMenu");
        Events.LevelSelected -= OnLevelSelected;
        Events.LevelSelected += OnLevelSelected;
        Events.DifficultySelected -= OnDifficultySelected;
        Events.DifficultySelected += OnDifficultySelected;

        if (_root == null)
        {
            _loggingService.Debug("RootObject is null!");
            return;
        }

        if (_levelDetailMenu != null) _levelDetailMenu.ButtonPressedAction -= OnDeleteVideoAction;

        _levelDetailMenu = new LevelDetailViewController();
        _levelDetailMenu.Initialize();
        _levelDetailMenu.ButtonPressedAction += OnDeleteVideoAction;
        CreateStatusListener();
        // Shrink the gear and trash buttons together, anchored at the right edge, so they sit side by side in the
        // corner. Scaling each button on its own left half-empty slots between them.
        var titleButtons = (RectTransform)_titleButtons.transform;
        titleButtons.pivot = new Vector2(1f, 0.5f);
        titleButtons.localScale = new Vector3(0.5f, 0.5f, 1f);
        // Pin the title to its 82-unit slot. BSML's text tag ignores preferred-width, so the title took the width of
        // the video name and pushed the gear and trash buttons around.
        var titleLayout = _videoTitleText.GetComponent<LayoutElement>();
        if (titleLayout == null) titleLayout = _videoTitleText.gameObject.AddComponent<LayoutElement>();
        titleLayout.minWidth = 82f;
        titleLayout.preferredWidth = 82f;
        titleLayout.flexibleWidth = 0f;

        _searchKeyboard.ClearOnOpen = false;

        _autoSyncRowControls =
        [
            (_autoSyncRow0, _autoSyncOffset0, _autoSyncTag0, _autoSyncInfo0, _autoSyncTry0, _autoSyncTryText0, _autoSyncApply0),
            (_autoSyncRow1, _autoSyncOffset1, _autoSyncTag1, _autoSyncInfo1, _autoSyncTry1, _autoSyncTryText1, _autoSyncApply1),
            (_autoSyncRow2, _autoSyncOffset2, _autoSyncTag2, _autoSyncInfo2, _autoSyncTry2, _autoSyncTryText2, _autoSyncApply2)
        ];

        // BSML's text tag has no LayoutElement to receive preferred-width.
        SetAutoSyncTextWidth(_autoSyncSubtitle, 104f);
        foreach (var row in _autoSyncRowControls)
            SetAutoSyncTextWidth(row.Offset, 28f);

        if (_videoMenuInitialized) return;

        _videoMenuInitialized = true;
        _videoDetailsViewRect.gameObject.SetActive(false);
        _videoSearchResultsViewRect.gameObject.SetActive(false);

        _searchService.SearchProgress += SearchProgress;
        _searchService.SearchFinished += SearchFinished;
        _downloadService.DownloadProgress += OnDownloadProgress;
        _downloadService.DownloadFinished += OnDownloadFinished;
        VideoLoader.ConfigChanged += OnConfigChanged;
        _displaySettings.TheaterEnabledChanged += OnTheaterEnabledChanged;

        if (!_downloadService.LibrariesAvailable())
            _loggingService.Warn(
                $"One or more of the libraries are missing. Downloading videos will not work. To fix this, reinstall Theater and make sure yt-dlp and ffmpeg are in the Theater Libs folder, which is located at {TheaterFileHelpers.TheaterLibsPath}.");
    }

    public void Dispose()
    {
        CancelAutoSync();
        _gameplaySetup.RemoveTab("Theater");
        _displaySettings.TheaterEnabledChanged -= OnTheaterEnabledChanged;
    }

    public void CreateStatusListener()
    {
        //This needs to be reinitialized every time a fresh menu scene load happens
        if (_menuStatus != null)
        {
            _menuStatus.DidEnable -= StatusViewerDidEnable;
            _menuStatus.DidDisable -= StatusViewerDidDisable;
            Object.Destroy(_menuStatus);
        }

        _menuStatus = _root.AddComponent<VideoMenuStatus>();
        _loggingService.Debug("Adding status listener to: " + _menuStatus.name);
        _menuStatus.DidEnable += StatusViewerDidEnable;
        _menuStatus.DidDisable += StatusViewerDidDisable;
    }

    public void ResetVideoMenu()
    {
        _bsmlParserParams.EmitEvent("hide-keyboard");
        _noVideoViewRect.gameObject.SetActive(true);
        _videoDetailsViewRect.gameObject.SetActive(false);
        SetButtonState(false);
        _turnOnButton.gameObject.SetActive(false);

        // Checked before the libraries so there's always a way to turn Theater back on from here
        if (!_config.TheaterEnabled)
        {
            _noVideoText.text = "Theater is turned off.";
            _searchButton.gameObject.SetActive(false);
            _turnOnButton.gameObject.SetActive(true);
            return;
        }

        if (!_downloadService.LibrariesAvailable())
        {
            _noVideoText.text =
                "Libraries not found. Please reinstall Theater.\r\nMake sure you unzip the files from the Libs folder into 'Beat Saber\\Libs'.";
            _searchButton.gameObject.SetActive(false);
            return;
        }

        if (!_config.TheaterActive)
        {
            _noVideoText.text = "Theater is disabled in the mod list.";
            _searchButton.gameObject.SetActive(false);
            return;
        }

        if (_currentLevel == null)
        {
            _noVideoText.text = "No level selected";
            return;
        }

        _noVideoText.text = "No video configured";
    }

    private void OnDownloadProgress(VideoConfig videoConfig)
    {
        UpdateStatusText(videoConfig);
        SetupLevelDetailView(videoConfig);
    }

    public void SetButtonState(bool state)
    {
        _previewButton.interactable = state;
        _deleteButton.interactable = state;
        _deleteVideoButton.interactable = state;
        _searchButton.gameObject.SetActive(_currentLevel != null &&
                                           !VideoLoader.IsDlcSong(_currentLevel) &&
                                           _downloadService.LibrariesAvailable());
        _previewButtonText.text = _playbackManager.IsPreviewPlaying ? "Stop preview" : "Preview";
        UpdateAutoSyncButton();

        if (_currentLevel != null && VideoLoader.IsDlcSong(_currentLevel) && _downloadService.LibrariesAvailable())
            CheckEntitlementAndEnableSearch(_currentLevel).LogErrors(_loggingService, "Entitlement check");

        if (_currentVideo == null) return;

        //Hide delete config button for mapper-made configs
        var officialConfig = _currentVideo.configByMapper == true;
        _deleteButton.gameObject.SetActive(!officialConfig);

        switch (_currentVideo.DownloadState)
        {
            case DownloadState.Converting:
            case DownloadState.Preparing:
            case DownloadState.Downloading:
            case DownloadState.DownloadingVideo:
            case DownloadState.DownloadingAudio:
                _deleteVideoButtonText.SetText("Cancel");
                _previewButton.interactable = false;
                _deleteVideoButton.transform.Find("Underline").gameObject.GetComponent<Image>().color = Color.grey;
                break;
            case DownloadState.NotDownloaded:
            case DownloadState.Cancelled:
                _deleteVideoButtonText.SetText("Download");
                _deleteVideoButton.interactable = false;
                var underlineColor = Color.clear;
                if (state && _downloadService.LibrariesAvailable())
                {
                    underlineColor = Color.green;
                    _deleteVideoButton.interactable = true;
                }

                _deleteVideoButton.transform.Find("Underline").gameObject.GetComponent<Image>().color = underlineColor;
                _previewButton.interactable = false;
                break;
            default:
                _deleteVideoButtonText.SetText("Delete Video");
                _deleteVideoButton.transform.Find("Underline").gameObject.GetComponent<Image>().color = Color.grey;
                _previewButton.interactable = state;
                break;
        }
    }

    private async Task CheckEntitlementAndEnableSearch(BeatmapLevel level)
    {
        var entitlement = await VideoLoader.GetEntitlementForLevel(level);
        if (entitlement == EntitlementStatus.Owned && _currentLevel == level) _searchButton.gameObject.SetActive(true);
    }

    public void SetupVideoDetails()
    {
        if (!_videoSearchResultsViewRect)
        {
            _loggingService.Warn("Video search results view rect is null, skipping UI setup");
            return;
        }

        // The Display view stays up until its Back button is pressed; it re-runs this when it closes
        if (_displaySettingsOpen) return;

        _videoSearchResultsViewRect.gameObject.SetActive(false);
        _levelDetailMenu?.SetActive(false);

        if (_currentVideo == null || !_config.TheaterActive || !_downloadService.LibrariesAvailable())
        {
            ResetVideoMenu();
            _loggingService.Debug("No video configured");
            return;
        }

        // Update download state based on currently selected format
        _currentVideo.UpdateDownloadState(_config.Format);

        SetupLevelDetailView(_currentVideo!);

        //Skip setting up the video menu if it's not showing. Prevents an unnecessary web request for the thumbnail.
        if (!_videoMenuActive)
        {
            ResetVideoMenu();
            _loggingService.Debug("Video Menu is not active");
            return;
        }

        if (_currentVideo!.videoID == null && _currentVideo.videoUrl == null)
        {
            ResetVideoMenu();
            if (_currentVideo.forceEnvironmentModifications != true) return;

            _noVideoText.text =
                "This map uses Theater to modify the environment\r\nwithout displaying a video.\r\n\r\nNo configuration options available.";
            _searchButton.interactable = false;
            _searchButton.gameObject.SetActive(false);

            return;
        }

        _noVideoViewRect.gameObject.SetActive(false);
        _videoDetailsViewRect.gameObject.SetActive(true);

        SetButtonState(true);

        _videoTitleText.text = TheaterFileHelpers.FilterEmoji(_currentVideo.title ?? "Untitled Video");
        _videoAuthorText.text = "Author: " + TheaterFileHelpers.FilterEmoji(_currentVideo.author ?? "Unknown Author");
        _videoDurationText.text = "Duration: " + TheaterFileHelpers.SecondsToString(_currentVideo.duration);

        SetThumbnail(_currentVideo.videoID != null
            ? $"https://i.ytimg.com/vi/{_currentVideo.videoID}/hqdefault.jpg"
            : null);

        UpdateStatusText(_currentVideo);
        UpdateOffsetControls();
    }

    private void UpdateOffsetControls()
    {
        if (_currentVideo == null) return;

        _videoOffsetText.text = $"{_currentVideo.offset:n0}" + " ms";
        var original = GetOriginalOffset(_currentVideo);
        if (original == null || original == _currentVideo.offset)
        {
            _videoOffsetLabel.text = "Video Offset";
        }
        else
        {
            var delta = _currentVideo.offset - original.Value;
            var source = _currentVideo.IsOfficialConfig || _currentVideo.bundledConfig == true ? "default" : "original";
            _videoOffsetLabel.text = $"Video Offset <color=#A0A8B4>({delta.ToString("+#,0;-#,0")} ms from {source})</color>";
        }

        if (CustomizeOffset)
        {
            _customizeOffsetToggle.SetActive(false);
            _offsetControls.SetActive(true);
        }
        else
        {
            _customizeOffsetToggle.SetActive(true);
            _offsetControls.SetActive(false);
        }

        _bsmlParserParams.EmitEvent("update-customize-offset");
        UpdateRevertOffsetButton();
        UpdateAutoSyncButton();
    }

    private void UpdateAutoSyncButton()
    {
        // Already destroyed when this runs from Dispose while the game closes
        if (_autoSyncButton == null) return;

        var active = _currentVideo != null && CustomizeOffset && !_editorBeatmapSelected &&
                     _currentLevel != null && !VideoLoader.IsDlcSong(_currentLevel);
        _autoSyncButton.gameObject.SetActive(active);
        _autoSyncButton.interactable = active && _currentVideo?.DownloadState == DownloadState.Downloaded &&
                                       _autoSyncCts == null;
        _autoSyncButtonText.text = _autoSyncCts != null ? "Syncing..." : "Auto sync";
    }

    private void UpdateRevertOffsetButton()
    {
        var originalOffset = _currentVideo == null ? null : GetOriginalOffset(_currentVideo);
        _revertOffsetButton.interactable = _currentVideo != null && originalOffset != null &&
                                           (originalOffset != _currentVideo.offset ||
                                            !Mathf.Approximately(GetOriginalPlaybackSpeed(_currentVideo), _currentVideo.PlaybackSpeed) ||
                                            _currentVideo.userSettings?.customOffset == true);
    }

    // The offset this video had before the user changed it: recorded on the first change (or when "Customize sync"
    // is ticked on a mapper's config). Configs that came from the bundled list and were edited before this was
    // recorded fall back to the bundled value.
    private int? GetOriginalOffset(VideoConfig video)
    {
        if (video.userSettings?.originalOffset != null) return video.userSettings.originalOffset;
        if (video.bundledConfig == true && _currentLevel != null)
            return _videoLoader.GetBundledConfigOffset(_currentLevel);
        return null;
    }

    private static float GetOriginalPlaybackSpeed(VideoConfig video) =>
        video.userSettings?.originalPlaybackSpeed ?? video.PlaybackSpeed;

    public void SetupLevelDetailView(VideoConfig videoConfig)
    {
        if (videoConfig != _currentVideo) return;

        // This is the case if the map only uses environment modifications
        if ((_currentVideo.videoID == null && _currentVideo.videoUrl == null) || _levelDetailMenu == null) return;

        switch (videoConfig.DownloadState)
        {
            case DownloadState.Downloaded:
                if (_difficultyData?.HasTheater() == false &&
                    _extraSongData?.HasTheaterInAnyDifficulty() == false)
                {
                    _levelDetailMenu.SetActive(true);
                    _levelDetailMenu.SetText("Please add Theater as a suggestion", null, Color.red);
                }
                else if (videoConfig.ErrorMessage != null)
                {
                    _levelDetailMenu.SetActive(true);
                    _levelDetailMenu.SetText(videoConfig.ErrorMessage, null, Color.red, Color.red);
                }
                else
                {
                    _levelDetailMenu.SetText("Video ready!", null, Color.green);
                }

                break;
            case DownloadState.Preparing:
                _levelDetailMenu.SetActive(true);
                _levelDetailMenu.SetText($"Preparing download...", "Cancel", Color.yellow, Color.red);
                break;
            case DownloadState.Downloading:
                _levelDetailMenu.SetActive(true);
                _levelDetailMenu.SetText(
                    $"Downloading ({Convert.ToInt32(videoConfig.DownloadProgress * 100).ToString()}%)", "Cancel",
                    Color.yellow, Color.red);
                break;
            case DownloadState.DownloadingVideo:
                _levelDetailMenu.SetActive(true);
                _levelDetailMenu.SetText(
                    $"Downloading video ({Convert.ToInt32(videoConfig.DownloadProgress * 100).ToString()}%)", "Cancel",
                    Color.yellow, Color.red);
                break;
            case DownloadState.DownloadingAudio:
                _levelDetailMenu.SetActive(true);
                _levelDetailMenu.SetText(
                    $"Downloading audio ({Convert.ToInt32(videoConfig.DownloadProgress * 100).ToString()}%)", "Cancel",
                    Color.yellow, Color.red);
                break;
            case DownloadState.Converting:
                _levelDetailMenu.SetActive(true);
                _levelDetailMenu.SetText(GetConversionProgressDisplayString(videoConfig),
                    "Cancel", Color.yellow, Color.red);
                break;
            case DownloadState.NotDownloaded:
                _levelDetailMenu.SetActive(true);
                if (videoConfig.ErrorMessage != null)
                    _levelDetailMenu.SetText(videoConfig.ErrorMessage, "Retry", Color.red, Color.red);
                else if (_difficultyData?.HasTheaterRequirement() == true)
                    _levelDetailMenu.SetText("Video required to play this map", "Download", Color.red, Color.green);
                else
                    _levelDetailMenu.SetText("Video available", "Download Video", null, Color.green);

                break;
            case DownloadState.Cancelled:
                _levelDetailMenu.SetActive(true);
                _levelDetailMenu.SetText("Cancelling...", "Download Video", Color.red, Color.green);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void UpdateStatusText(VideoConfig videoConfig)
    {
        if (videoConfig != _currentVideo || !_videoMenuActive) return;

        switch (videoConfig.DownloadState)
        {
            case DownloadState.Downloaded:
                _videoStatusText.text = "Downloaded";
                _videoStatusText.color = Color.green;
                break;
            case DownloadState.Preparing:
                _videoStatusText.text = $"Preparing download...";
                _videoStatusText.color = Color.yellow;
                _previewButton.interactable = false;
                break;
            case DownloadState.Downloading:
                _videoStatusText.text =
                    $"Downloading ({Convert.ToInt32(videoConfig.DownloadProgress * 100).ToString()}%)";
                _videoStatusText.color = Color.yellow;
                _previewButton.interactable = false;
                break;
            case DownloadState.DownloadingVideo:
                _videoStatusText.text =
                    $"Downloading video ({Convert.ToInt32(videoConfig.DownloadProgress * 100).ToString()}%)";
                _videoStatusText.color = Color.yellow;
                _previewButton.interactable = false;
                break;
            case DownloadState.DownloadingAudio:
                _videoStatusText.text =
                    $"Downloading audio ({Convert.ToInt32(videoConfig.DownloadProgress * 100).ToString()}%)";
                _videoStatusText.color = Color.yellow;
                _previewButton.interactable = false;
                break;
            case DownloadState.Converting:
                var convertingText = GetConversionProgressDisplayString(videoConfig);
                _videoStatusText.text = convertingText;
                _videoStatusText.color = Color.yellow;
                _previewButton.interactable = false;
                break;
            case DownloadState.NotDownloaded:
                _videoStatusText.text = videoConfig.ErrorMessage ?? "Not downloaded";
                _videoStatusText.color = Color.red;
                _previewButton.interactable = false;
                break;
            case DownloadState.Cancelled:
                _videoStatusText.text = "Cancelling...";
                _videoStatusText.color = Color.red;
                _previewButton.interactable = false;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void SetThumbnail(string? url)
    {
        if (url != null && url == _thumbnailURL) return;

        _thumbnailURL = url;

        if (url == null)
        {
            SetThumbnailFromCover(_currentLevel).LogErrors(_loggingService, "Loading cover as thumbnail");
            return;
        }

        _videoThumnnail.SetImageAsync(url);
    }

    private async Task SetThumbnailFromCover(BeatmapLevel? level)
    {
        if (level == null) return;

        var coverSprite = await level.previewMediaData.GetCoverSpriteAsync();
        _videoThumnnail.sprite = coverSprite;
    }

    public void HandleDidSelectEditorBeatmap(BeatmapDataModel beatmapData, string originalPath)
    {
        _editorBeatmapSelected = true;
        CancelAutoSync();
        if (!_config.TheaterActive) return;

        _playbackManager.StopPreview(true);
        if (_currentVideo?.NeedsToSave == true) _videoLoader.SaveVideoConfig(_currentVideo);

        _currentVideo = _videoLoader.GetConfigForEditorLevel(beatmapData, originalPath);
        _videoLoader.SetupFileSystemWatcher(originalPath);
        _playbackManager.SetSelectedLevel(null, _currentVideo);
    }

    public void HandleDidSelectLevel(BeatmapLevel? level)
    {
        //These will be set a bit later by a Harmony patch. Clear them to not accidentally access outdated info.
        _extraSongData = null;
        _difficultyData = null;

        //Ignores the duplicate event that occurs when selecting a playlist song
        if (_currentLevel == level && _currentLevelIsPlaylistSong) return;

        CancelAutoSync();
        _editorBeatmapSelected = false;
        _currentLevelIsPlaylistSong = InstalledMods.BeatSaberPlaylistsLib && level.IsPlaylistLevel();
        if (InstalledMods.BeatSaberPlaylistsLib && _currentLevelIsPlaylistSong)
            level = level.GetLevelFromPlaylistIfAvailable();

        if (!_config.TheaterActive)
        {
            // Keep track of the selection so turning Theater back on can load it
            if (_currentVideo?.NeedsToSave == true) _videoLoader.SaveVideoConfig(_currentVideo);
            _currentLevel = level;
            _currentVideo = null;
            return;
        }

        _playbackManager.StopPreview(true);

        if (_currentVideo?.NeedsToSave == true) _videoLoader.SaveVideoConfig(_currentVideo);
        _currentLevel = level;
        if (_currentLevel == null)
        {
            _currentVideo = null;
            _playbackManager.SetSelectedLevel(null, null);
            SetupVideoDetails();
            return;
        }

        _currentVideo = _videoLoader.GetConfigForLevel(_currentLevel);

        _videoLoader.SetupFileSystemWatcher(_currentLevel);
        _playbackManager.SetSelectedLevel(_currentLevel, _currentVideo);
        SetupVideoDetails();

        _searchText = _currentLevel.songName +
                      (!string.IsNullOrEmpty(_currentLevel.songAuthorName) ? " " + _currentLevel.songAuthorName : "");
    }

    private void OnLevelSelected(LevelSelectedArgs levelSelectedArgs)
    {
        if (!_videoMenuInitialized)
        {
            _loggingService.Debug("Initializing video menu (late)");
            Initialize();
        }

        if (levelSelectedArgs.BeatmapData != null)
        {
            _loggingService.Debug("Level selected from VideoMenuUI");
            HandleDidSelectEditorBeatmap(levelSelectedArgs.BeatmapData, levelSelectedArgs.OriginalPath!);
            return;
        }

        HandleDidSelectLevel(levelSelectedArgs.BeatmapLevel);
    }

    private void OnDifficultySelected(ExtraSongDataArgs extraSongDataArgs)
    {
        _extraSongData = extraSongDataArgs.SongData;
        _difficultyData = extraSongDataArgs.SelectedDifficultyData;
        if (_currentVideo != null) SetupLevelDetailView(_currentVideo);
    }

    public void OnConfigChanged(VideoConfig? config)
    {
        CancelAutoSync();
        _currentVideo = config;
        SetupVideoDetails();
    }

    public void StatusViewerDidEnable(object sender, EventArgs e)
    {
        _videoMenuActive = true;
        _playbackManager.StopPreview(false);
        SetupVideoDetails();
    }

    public void StatusViewerDidDisable(object sender, EventArgs e)
    {
        CancelAutoSync();
        _videoMenuActive = false;
        CloseDisplaySettings(false);
        if (_currentVideo?.NeedsToSave == true) _videoLoader.SaveVideoConfig(_currentVideo);

        _searchService.StopSearch();

        try
        {
            _playbackManager.StopPreview(true);
        }
        catch (Exception exception)
        {
            //This can happen when closing the game
            _loggingService.Debug(exception);
        }
    }

    // Remember the starting offset and speed on the first sync change, so "Revert sync" can go back to them
    private void RecordOriginalSync(VideoConfig video)
    {
        var originalOffset = GetOriginalOffset(video) ?? video.offset;
        video.userSettings ??= new UserSettings();
        video.userSettings.originalOffset ??= originalOffset;
        video.userSettings.originalPlaybackSpeed ??= video.PlaybackSpeed;
    }

    private void ApplyOffset(int offset)
    {
        if (_currentVideo == null) return;

        RecordOriginalSync(_currentVideo);
        ChangeOffset(offset);
    }

    private void ChangeOffset(int delta)
    {
        if (_currentVideo == null) return;

        _currentVideo.offset += delta;
        _currentVideo.NeedsToSave = true;
        if (delta != 0) _playbackManager.ApplyOffset(delta);
        UpdateOffsetControls();
    }

    [UIAction("on-search-action")]
    [UsedImplicitly]
    public void SearchAction()
    {
        if (_currentLevel == null)
        {
            _loggingService.Warn("Selected level was null on search action");
            return;
        }

        OnQueryAction(_searchText);
        _customListTableData.TableView.ScrollToCellWithIdx(0, TableView.ScrollPositionType.Beginning, false);
        _customListTableData.TableView.ClearSelection();
    }

    private IEnumerator UpdateSearchResults(YTResult result)
    {
        var title =
            $"[{TheaterFileHelpers.SecondsToString(result.Duration)}] {TheaterFileHelpers.FilterEmoji(result.Title)}";
        var description = $"{TheaterFileHelpers.FilterEmoji(result.Author)}";

        try
        {
            var stillImage = result.IsStillImage();
            string descriptionAddition;
            if (stillImage)
                descriptionAddition = "Likely a still image";
            else
                descriptionAddition = result.GetQualityString() ?? "";

            if (descriptionAddition.Length > 0) description += "   |   " + descriptionAddition;
        }
        catch (Exception e)
        {
            _loggingService.Warn(e);
        }

        var item = new CustomListTableData.CustomCellInfo(title, description);
        var request = UnityWebRequestTexture.GetTexture($"https://i.ytimg.com/vi/{result.ID}/mqdefault.jpg");
        yield return request.SendWebRequest();
        if (request.result != UnityWebRequest.Result.ConnectionError &&
            request.result != UnityWebRequest.Result.ProtocolError)
        {
            var tex = ((DownloadHandlerTexture)request.downloadHandler).texture;
            item.Icon = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.one * 0.5f, 100, 1);
        }
        else
        {
            _loggingService.Debug(request.error);
        }

        _customListTableData.Data.Add(item);
        _customListTableData.TableView.ReloadDataKeepingPosition();

        _downloadButton.interactable = _selectedCell != -1;
        _downloadButton.transform.Find("Underline").gameObject.GetComponent<Image>().color = Color.green;
        _searchResultsLoadingText.gameObject.SetActive(false);
    }

    private void OnDownloadFinished(VideoConfig video)
    {
        if (_currentVideo != video) return;

        if (video.ErrorMessage != null)
        {
            SetupVideoDetails();
            return;
        }

        // Update state to reflect the currently selected format
        video.UpdateDownloadState(_config.Format);

        _playbackManager.PrepareVideo(video);

        if (_currentLevel != null) _videoLoader.RemoveConfigFromCache(_currentLevel);

        SetupVideoDetails();
        _levelDetailMenu?.SetActive(true);
        _levelDetailMenu?.RefreshContent();
    }

    public void ShowKeyboard()
    {
        _searchKeyboard.SetText(_searchText);
        _bsmlParserParams.EmitEvent("show-keyboard");
    }

    [UIAction("on-refine-action")]
    [UsedImplicitly]
    private void OnRefineAction()
    {
        ShowKeyboard();
    }

    [UIAction("on-delete-video-action")]
    [UsedImplicitly]
    private void OnDeleteVideoAction()
    {
        CancelAutoSync();
        if (_currentVideo == null)
        {
            _loggingService.Warn("Current video was null on delete action");
            return;
        }

        _playbackManager.StopPreview(true);

        switch (_currentVideo.DownloadState)
        {
            case DownloadState.Preparing:
            case DownloadState.Downloading:
            case DownloadState.DownloadingAudio:
            case DownloadState.DownloadingVideo:
                _downloadService.CancelDownload(_currentVideo);
                break;
            case DownloadState.NotDownloaded:
            case DownloadState.Cancelled:
                _currentVideo.DownloadProgress = 0;
                _searchService.StopSearch();
                _downloadService.StartDownload(_currentVideo, _config.QualityMode, _config.Format);
                _currentVideo.NeedsToSave = true;
                _videoLoader.AddConfigToCache(_currentVideo, _currentLevel!);
                break;
            default:
                // Unload before deleting so the next load cannot try the deleted file.
                _playbackManager.StopAndUnloadVideo();
                _videoLoader.DeleteVideo(_currentVideo, _config.Format);
                // Any playback error described the file that's now deleted
                _currentVideo.ErrorMessage = null;
                SetupLevelDetailView(_currentVideo);
                _levelDetailMenu?.RefreshContent();
                break;
        }

        UpdateStatusText(_currentVideo);
        SetButtonState(true);
    }

    [UIAction("on-delete-config-action")]
    [UsedImplicitly]
    private void OnDeleteConfigAction()
    {
        CancelAutoSync();
        if (_currentVideo == null || _currentLevel == null)
        {
            _loggingService.Warn("Failed to delete config: Either currentVideo or currentLevel is null");
            return;
        }

        _playbackManager.StopPreview(true);
        _playbackManager.StopPlayback();
        _playbackManager.HideVideoPlayer();
        // Unload before deleting. A stopped player keeps the file's path, and the next load would try the deleted
        // file again and fail (2026-11-25: this broke all playback until a restart).
        _playbackManager.UnloadVideo();

        if (_currentVideo.IsDownloading) _downloadService.CancelDownload(_currentVideo);

        // Delete all downloaded formats when user explicitly deletes the video
        foreach (VideoFormats.Format format in Enum.GetValues(typeof(VideoFormats.Format)))
        {
            _videoLoader.DeleteVideo(_currentVideo, format);
        }
        var success = _videoLoader.DeleteConfig(_currentVideo, _currentLevel);
        if (success) _currentVideo = null;

        _levelDetailMenu?.SetActive(false);
        ResetVideoMenu();
    }

    [UIAction("on-back-action")]
    [UsedImplicitly]
    private void OnBackAction()
    {
        _videoDetailsViewRect.gameObject.SetActive(true);
        _videoSearchResultsViewRect.gameObject.SetActive(false);
        SetupVideoDetails();
    }

    [UIAction("on-query")]
    [UsedImplicitly]
    private void OnQueryAction(string query)
    {
        _noVideoViewRect.gameObject.SetActive(false);
        _videoDetailsViewRect.gameObject.SetActive(false);
        _videoSearchResultsViewRect.gameObject.SetActive(true);

        ResetSearchView();
        _downloadButton.interactable = false;
        _searchLoadingCoroutine = _coroutineStarter.StartCoroutine(SearchLoadingCoroutine());

        _searchService.Search(query);
        _searchText = query;
    }

    private void SearchProgress(YTResult result)
    {
        //Event is being invoked twice for whatever reason, so keep a list of what has been added before
        if (_searchResults.Contains(result)) return;

        _searchResults.Add(result);
        var updateSearchResultsCoroutine = UpdateSearchResults(result);
        _updateSearchResultsCoroutine = _coroutineStarter.StartCoroutine(updateSearchResultsCoroutine);
    }

    private void SearchFinished()
    {
        if (_searchResults.Count != 0) return;

        ResetSearchView();
        _searchResultsLoadingText.gameObject.SetActive(true);
        _searchResultsLoadingText.SetText(
            "No search results found.\r\nUse the Refine Search button in the bottom right to choose a different search query.");
    }

    private void ResetSearchView()
    {
        if (_searchLoadingCoroutine != null) _coroutineStarter.StopCoroutine(_searchLoadingCoroutine);
        if (_updateSearchResultsCoroutine != null)
            _coroutineStarter.StopCoroutine(_updateSearchResultsCoroutine);

        if (_customListTableData.Data != null && _customListTableData.Data.Count > 0)
        {
            _customListTableData.Data.Clear();
            _customListTableData.TableView.ReloadData();
        }

        _downloadButton.interactable = false;
        _downloadButton.transform.Find("Underline").gameObject.GetComponent<Image>().color = Color.grey;
        _selectedCell = -1;
        _searchResults.Clear();
        _bsmlParserParams.EmitEvent("hide-keyboard");
    }

    private IEnumerator SearchLoadingCoroutine()
    {
        var count = 0;
        const string loadingText = "Searching for videos, please wait";
        _searchResultsLoadingText.gameObject.SetActive(true);

        //Loading animation
        while (_searchResultsLoadingText.gameObject.activeInHierarchy)
        {
            var periods = string.Empty;
            count++;

            for (var i = 0; i < count; i++) periods += ".";

            if (count == 3) count = 0;

            _searchResultsLoadingText.SetText(loadingText + periods);

            yield return new WaitForSeconds(0.5f);
        }
    }

    [UIAction("on-select-cell")]
    [UsedImplicitly]
    private void OnSelectCell(TableView view, int selection)
    {
        if (_customListTableData.Data.Count > selection)
        {
            _selectedCell = selection;
            _downloadButton.interactable = true;
        }
        else
        {
            _downloadButton.interactable = false;
            _selectedCell = -1;
        }
    }

    [UIAction("on-download-action")]
    [UsedImplicitly]
    private void OnDownloadAction()
    {
        _loggingService.Debug("Download pressed");
        if (_selectedCell < 0 || _currentLevel == null)
        {
            _loggingService.Error("No cell or level selected on download action");
            return;
        }

        _downloadButton.interactable = false;
        var config =
            new VideoConfig(_searchService.SearchResults[_selectedCell], VideoLoader.GetTheaterLevelPath(_currentLevel))
            { NeedsToSave = true };
        _videoLoader.AddConfigToCache(config, _currentLevel);
        _searchService.StopSearch();
        _downloadService.StartDownload(config, _config.QualityMode, _config.Format);
        _currentVideo = config;
        SetupVideoDetails();
    }

    [UIAction("on-preview-action")]
    [UsedImplicitly]
    private void OnPreviewAction()
    {
        _playbackManager.StartPreview().LogErrors(_loggingService, "Preview");
        SetButtonState(true);
    }

    [UIAction("on-revert-offset-action")]
    [UsedImplicitly]
    private void OnRevertOffsetAction()
    {
        if (_currentVideo == null) return;

        var originalOffset = GetOriginalOffset(_currentVideo);
        if (originalOffset == null) return;

        var originalSpeed = GetOriginalPlaybackSpeed(_currentVideo);
        _loggingService.Info(
            $"Reverting sync from {_currentVideo.offset} ms at {_currentVideo.PlaybackSpeed}x to {originalOffset.Value} ms at {originalSpeed}x");

        // Clear the saved offset and speed baselines so mapper configs show "Customize sync" again.
        _currentVideo.userSettings = null;
        _currentVideo.playbackSpeed = Mathf.Approximately(originalSpeed, 1f) ? null : originalSpeed;
        _currentVideo.NeedsToSave = true;
        _playbackManager.ApplyPlaybackSpeed();
        ChangeOffset(originalOffset.Value - _currentVideo.offset);
    }

    #region Auto sync

    private static void SetAutoSyncTextWidth(TextMeshProUGUI text, float width)
    {
        var layout = text.GetComponent<LayoutElement>();
        if (layout == null) layout = text.gameObject.AddComponent<LayoutElement>();
        layout.preferredWidth = width;
    }

    [UIAction("on-auto-sync-action")]
    [UsedImplicitly]
    private void OnAutoSyncAction()
    {
        if (_autoSyncCts != null || _currentVideo == null || _currentLevel == null || _autoSyncPopupOpen) return;

        var mapPath = _videoLoader.GetMapPath(_currentLevel);
        var songPath = mapPath == null ? null : MapSongFile.Find(mapPath);
        if (songPath == null)
        {
            ShowAutoSyncMessage("Auto sync failed", "Couldn't find this map's song file.");
            return;
        }

        var videoPath = _currentVideo.GetVideoPathForFormat(_config.Format);
        if (videoPath == null || !File.Exists(videoPath))
        {
            ShowAutoSyncMessage("Auto sync failed", "Couldn't find the downloaded video file.");
            return;
        }

        _autoSyncVideo = _currentVideo;
        _autoSyncCts = new CancellationTokenSource();
        var token = _autoSyncCts.Token;
        var speed = _currentVideo.PlaybackSpeed;
        UpdateAutoSyncButton();

        _loggingService.Info($"Auto sync: analysing {songPath} against {videoPath} at {speed:0.00}x");
        RunAutoSync(songPath, videoPath, speed, token).LogErrors(_loggingService, "Auto sync");
    }

    private async Task RunAutoSync(string songPath, string videoPath, float speed, CancellationToken token)
    {
        // The run owns this source even if a context change clears the field.
        var runCts = _autoSyncCts!;
        try
        {
            var ffmpegPath = Path.Combine(TheaterFileHelpers.TheaterLibsPath, "ffmpeg.exe");
            var result = await SyncAnalyzer.AnalyzeAsync(ffmpegPath, songPath, videoPath, speed, token);
            if (token.IsCancellationRequested || _currentVideo != _autoSyncVideo || _currentVideo == null) return;

            if (!Mathf.Approximately(_currentVideo.PlaybackSpeed, speed))
            {
                ShowAutoSyncMessage("Auto sync failed", "The playback speed changed while syncing. Press Auto sync again.");
                return;
            }

            var candidates = string.Join(", ", result.Candidates.Select(candidate =>
                string.Format(CultureInfo.InvariantCulture, "{0:+0;-0;0} ms ({1:0.00})", candidate.OffsetMs, candidate.Score)));
            _loggingService.Info(string.Format(CultureInfo.InvariantCulture,
                "Auto sync: {0}, best {1:0} ms, score {2:0.00}, margin {3:0.00}, agreement {4:P0}, exact slices {5}, {6} ms; candidates: {7}",
                result.Confidence, result.OffsetMs, result.Score, result.Margin, result.Agreement, result.ExactSlices,
                result.TotalMs, candidates));
            ShowAutoSyncResults(result);
        }
        catch (OperationCanceledException)
        {
            _loggingService.Debug("Auto sync cancelled");
        }
        catch (SyncException e)
        {
            _loggingService.Warn($"Auto sync failed: {e.Message}");
            if (!token.IsCancellationRequested && _currentVideo == _autoSyncVideo)
                ShowAutoSyncMessage("Auto sync failed", e.Message);
        }
        catch (Exception e)
        {
            _loggingService.Error(e);
            if (!token.IsCancellationRequested && _currentVideo == _autoSyncVideo)
                ShowAutoSyncMessage("Auto sync failed", "Something went wrong. See the log for details.");
        }
        finally
        {
            if (!token.IsCancellationRequested) _playbackManager.RecheckSync("auto sync analysis");
            runCts.Dispose();
            if (ReferenceEquals(_autoSyncCts, runCts))
            {
                _autoSyncCts = null;
                UpdateAutoSyncButton();
            }
        }
    }

    private static string FormatOffset(int ms) => ms.ToString("+#,0;-#,0;0") + " ms";

    private static string FormatSongTime(double s)
    {
        var total = (int)Math.Floor(s);
        return $"{total / 60}:{total % 60:00}";
    }

    private void OpenAutoSyncPopup()
    {
        if (_autoSyncModal == null) return;

        _autoSyncPopupOpen = true;
        _bsmlParserParams.EmitEvent("show-auto-sync");
    }

    private void HideAutoSyncPopup()
    {
        _autoSyncPopupOpen = false;
        _bsmlParserParams.EmitEvent("hide-auto-sync");
        _autoSyncTriedIndex = -1;
        _autoSyncOffsets.Clear();
        _autoSyncVideo = null;
        _autoSyncTryStart = null;
    }

    private void ShowAutoSyncMessage(string title, string body)
    {
        if (_currentVideo == null) return;

        _autoSyncTitle.text = title;
        _autoSyncSubtitle.text = body + "\n<color=#A0A8B4>Your offset was not changed.</color>";
        _autoSyncRows.SetActive(false);
        _autoSyncOffsets.Clear();
        _autoSyncTriedIndex = -1;
        _autoSyncStartOffset = _currentVideo.offset;
        _autoSyncVideo = _currentVideo;
        OpenAutoSyncPopup();
    }

    private void ShowAutoSyncResults(SyncResult result)
    {
        if (_currentVideo == null) return;
        if (result.Confidence == SyncConfidence.None || result.Candidates.Count == 0)
        {
            ShowAutoSyncMessage("Auto sync couldn't find this song in the video",
                "It may be a different version (live, remix, mashup) or a different speed.");
            return;
        }

        _autoSyncStartOffset = _currentVideo.offset;
        _autoSyncTriedIndex = -1;
        _autoSyncVideo = _currentVideo;
        _autoSyncConfidence = result.Confidence;
        var count = Math.Min(result.Candidates.Count, _autoSyncRowControls.Length);
        _autoSyncOffsets.Clear();
        _autoSyncOffsets.AddRange(result.Candidates.Take(count).Select(candidate => (int)Math.Round(candidate.OffsetMs)));

        _autoSyncTitle.text = count == 1 ? "Auto sync found 1 match" : $"Auto sync found {count} matches";
        SetIdleSubtitle();
        _autoSyncRows.SetActive(true);

        for (var i = 0; i < _autoSyncRowControls.Length; i++)
        {
            var row = _autoSyncRowControls[i];
            row.Row.SetActive(i < count);
            if (i >= count) continue;

            var offset = _autoSyncOffsets[i];
            row.Tag.text = i switch
            {
                0 when _autoSyncConfidence == SyncConfidence.Strong => "<color=#78DC8C>SURE</color>",
                0 => "<color=#F0C85A>BEST GUESS</color>",
                _ => "<color=#A0A8B4>unlikely</color>"
            };
            var pct = Mathf.Clamp(Mathf.RoundToInt(result.Candidates[i].Score * 100f), 0, 100);
            var delta = offset == _autoSyncStartOffset
                ? "same as current"
                : $"{FormatOffset(offset - _autoSyncStartOffset)} from current";
            row.Info.text = $"<color=#A0A8B4>match {pct}%   {delta}</color>";
            row.Apply.interactable = true;
        }

        var showNote = _autoSyncConfidence == SyncConfidence.Unsure &&
                       result.Agreement < SyncSettings.StrongMinAgreement && result.MatchedRanges.Count > 0;
        _autoSyncNote.gameObject.SetActive(showNote);
        if (showNote)
        {
            var ranges = string.Join(", ", result.MatchedRanges.Take(2).Select(range =>
                $"{FormatSongTime(range.StartSeconds)}-{FormatSongTime(range.EndSeconds)}"));
            _autoSyncNote.text = "partial: " + ranges + (result.MatchedRanges.Count > 2 ? ", ..." : "");
        }

        _autoSyncTryStart = result.MatchedRanges.Count > 0 ? (float)result.MatchedRanges[0].StartSeconds : null;
        UpdateAutoSyncTryRows();
        OpenAutoSyncPopup();
    }

    private void SetIdleSubtitle()
    {
        var confidence = _autoSyncConfidence == SyncConfidence.Strong
            ? "<color=#78DC8C>Best match is sure</color>"
            : "<color=#F0C85A>Not sure, try them by ear</color>";
        _autoSyncSubtitle.text = $"Current offset: {FormatOffset(_autoSyncStartOffset)}   {confidence}";
    }

    private void UpdateAutoSyncTryRows()
    {
        for (var i = 0; i < _autoSyncOffsets.Count; i++)
        {
            var row = _autoSyncRowControls[i];
            var trying = i == _autoSyncTriedIndex;
            var offset = FormatOffset(_autoSyncOffsets[i]);
            row.Offset.text = trying ? $"<color=#00BEFF>{offset}</color>" : offset;
            row.TryText.text = trying ? "Trying" : "Try";
            row.Try.interactable = !trying;
        }
    }

    [UIAction("on-auto-sync-try-0")]
    [UsedImplicitly]
    private void OnAutoSyncTry0() => TryAutoSync(0);

    [UIAction("on-auto-sync-try-1")]
    [UsedImplicitly]
    private void OnAutoSyncTry1() => TryAutoSync(1);

    [UIAction("on-auto-sync-try-2")]
    [UsedImplicitly]
    private void OnAutoSyncTry2() => TryAutoSync(2);

    private void TryAutoSync(int i)
    {
        if (!_autoSyncPopupOpen || _currentVideo == null || _currentVideo != _autoSyncVideo ||
            i >= _autoSyncOffsets.Count) return;

        var offset = _autoSyncOffsets[i];
        _currentVideo.offset = offset;
        _autoSyncTriedIndex = i;

        var speed = _currentVideo.PlaybackSpeed;
        var kickIn = offset < 0 ? (-offset / 1000f) / speed : 0f;
        var start = Math.Max(kickIn, i == 0 ? _autoSyncTryStart ?? 0f : 0f);
        if (_currentLevel != null && _currentLevel.songDuration > 0)
            start = Math.Min(start, Math.Max(0f, _currentLevel.songDuration - 10f));

        if (_playbackManager.IsPreviewPlaying) _playbackManager.StopPreview(true);
        _playbackManager.StartPreview(start).LogErrors(_loggingService, "Auto sync preview");

        UpdateAutoSyncTryRows();
        _autoSyncSubtitle.text = $"Trying {FormatOffset(offset)}. Close goes back to {FormatOffset(_autoSyncStartOffset)}.";
        _loggingService.Info($"Auto sync: trying {offset} ms from song time {start:0.0} s");
    }

    [UIAction("on-auto-sync-apply-0")]
    [UsedImplicitly]
    private void OnAutoSyncApply0() => ApplyAutoSync(0);

    [UIAction("on-auto-sync-apply-1")]
    [UsedImplicitly]
    private void OnAutoSyncApply1() => ApplyAutoSync(1);

    [UIAction("on-auto-sync-apply-2")]
    [UsedImplicitly]
    private void OnAutoSyncApply2() => ApplyAutoSync(2);

    private void ApplyAutoSync(int i)
    {
        if (!_autoSyncPopupOpen || _currentVideo == null || _currentVideo != _autoSyncVideo ||
            i >= _autoSyncOffsets.Count) return;

        var target = _autoSyncOffsets[i];
        var playerOffset = _autoSyncTriedIndex >= 0 ? _autoSyncOffsets[_autoSyncTriedIndex] : _autoSyncStartOffset;

        // Revert sync needs the offset from before any temporary preview.
        _currentVideo.offset = _autoSyncStartOffset;
        RecordOriginalSync(_currentVideo);
        _currentVideo.offset = target;
        _currentVideo.NeedsToSave = true;

        // Like the offset buttons: also moves the video that plays behind the song select, not just Preview
        if (target != playerOffset) _playbackManager.ApplyOffset(target - playerOffset);

        _loggingService.Info($"Auto sync: applied {target} ms (was {_autoSyncStartOffset} ms)");
        HideAutoSyncPopup();
        UpdateOffsetControls();
        _previewButtonText.text = _playbackManager.IsPreviewPlaying ? "Stop preview" : "Preview";
    }

    [UIAction("on-auto-sync-close")]
    [UsedImplicitly]
    private void CloseAutoSyncPopup()
    {
        if (!_autoSyncPopupOpen) return;

        if (_autoSyncTriedIndex >= 0 && _autoSyncVideo != null)
        {
            _autoSyncVideo.offset = _autoSyncStartOffset;
            try
            {
                if (_playbackManager.IsPreviewPlaying) _playbackManager.StopPreview(true);
            }
            catch (Exception exception)
            {
                // This can happen when closing the game, as in StatusViewerDidDisable
                _loggingService.Debug(exception);
            }
        }

        HideAutoSyncPopup();
        if (_currentVideo != null) UpdateOffsetControls();
        _previewButtonText.text = _playbackManager.IsPreviewPlaying ? "Stop preview" : "Preview";
    }

    private void CancelAutoSync()
    {
        if (_autoSyncCts != null)
        {
            _autoSyncCts.Cancel();
            _autoSyncCts = null;
            UpdateAutoSyncButton();
        }

        if (_autoSyncPopupOpen) CloseAutoSyncPopup();
    }

    #endregion

    #region Display view

    [UIValue("display-theater-enabled")]
    public bool DisplayTheaterEnabled
    {
        get => _displaySettings.TheaterEnabled;
        set => _displaySettings.TheaterEnabled = value;
    }

    [UIValue("display-brightness")]
    public int DisplayBrightness
    {
        get => _displaySettings.Brightness;
        set => _displaySettings.Brightness = value;
    }

    [UIValue("display-brightness-limit")]
    public int DisplayBrightnessLimit
    {
        get => _displaySettings.BrightnessLimit;
        set => _displaySettings.BrightnessLimit = value;
    }

    [UIValue("display-screen-size")]
    public int DisplayScreenSize
    {
        get => _displaySettings.ScreenScale;
        set => _displaySettings.ScreenScale = value;
    }

    [UIValue("display-corner-roundness")]
    public int DisplayCornerRoundness
    {
        get => _displaySettings.CornerRoundness;
        set => _displaySettings.CornerRoundness = value;
    }

    [UIValue("display-edge-softness")]
    public int DisplayEdgeSoftness
    {
        get => _displaySettings.EdgeSoftness;
        set => _displaySettings.EdgeSoftness = value;
    }

    [UIValue("display-bloom")]
    public int DisplayBloom
    {
        get => _displaySettings.Bloom;
        set => _displaySettings.Bloom = value;
    }

    [UIAction("format-percent")]
    [UsedImplicitly]
    private string FormatPercent(object value)
    {
        return DisplaySettings.FormatPercent(value);
    }

    [UIAction("format-brightness-limit")]
    [UsedImplicitly]
    private string FormatBrightnessLimit(object value) => DisplaySettings.FormatBrightnessLimit(value);

    [UIAction("format-speed")]
    [UsedImplicitly]
    private string FormatSpeed(object value) => Convert.ToSingle(value).ToString("0.00", CultureInfo.InvariantCulture) + "x";

    [UIAction("on-display-settings-action")]
    [UsedImplicitly]
    private void OnDisplaySettingsAction()
    {
        // The video keeps playing behind this view, so slider changes can be judged live
        _displaySettingsOpen = true;
        _bsmlParserParams.EmitEvent("hide-keyboard");
        _noVideoViewRect.gameObject.SetActive(false);
        _videoDetailsViewRect.gameObject.SetActive(false);
        _videoSearchResultsViewRect.gameObject.SetActive(false);
        _displaySettingsViewRect.gameObject.SetActive(true);
        _bsmlParserParams.EmitEvent("refresh-display-values");
        _playbackManager.SetBrightnessPreview(true);
    }

    [UIAction("on-display-settings-back-action")]
    [UsedImplicitly]
    private void OnDisplaySettingsBackAction()
    {
        CloseDisplaySettings(true);
    }

    private void CloseDisplaySettings(bool refreshMenu)
    {
        if (!_displaySettingsOpen) return;

        _displaySettingsOpen = false;
        _playbackManager.SetBrightnessPreview(false);
        _displaySettingsViewRect.gameObject.SetActive(false);
        if (refreshMenu) SetupVideoDetails();
    }

    [UIAction("on-turn-on-action")]
    [UsedImplicitly]
    private void OnTurnOnAction()
    {
        _displaySettings.TheaterEnabled = true;
    }

    private void OnTheaterEnabledChanged(bool enabled)
    {
        CancelAutoSync();
        if (enabled)
        {
            // Load the selected level again, now that Theater is on
            var level = _currentLevel;
            _currentLevel = null;
            _currentLevelIsPlaylistSong = false;
            HandleDidSelectLevel(level);
            if (_displaySettingsOpen) _playbackManager.SetBrightnessPreview(true);
            return;
        }

        if (_currentVideo?.NeedsToSave == true) _videoLoader.SaveVideoConfig(_currentVideo);
        _currentVideo = null;
        SetupVideoDetails();
    }

    #endregion

    [UIAction("on-offset-decrease-action-high")]
    [UsedImplicitly]
    private void DecreaseOffsetHigh()
    {
        ApplyOffset(-1000);
    }

    [UIAction("on-offset-decrease-action-mid")]
    [UsedImplicitly]
    private void DecreaseOffsetMid()
    {
        ApplyOffset(-100);
    }

    [UIAction("on-offset-decrease-action-low")]
    [UsedImplicitly]
    private void DecreaseOffsetLow()
    {
        ApplyOffset(-20);
    }

    [UIAction("on-offset-decrease-action-fine")]
    [UsedImplicitly]
    private void DecreaseOffsetFine()
    {
        ApplyOffset(-5);
    }

    [UIAction("on-offset-increase-action-high")]
    [UsedImplicitly]
    private void IncreaseOffsetHigh()
    {
        ApplyOffset(1000);
    }

    [UIAction("on-offset-increase-action-mid")]
    [UsedImplicitly]
    private void IncreaseOffsetMid()
    {
        ApplyOffset(100);
    }

    [UIAction("on-offset-increase-action-low")]
    [UsedImplicitly]
    private void IncreaseOffsetLow()
    {
        ApplyOffset(20);
    }

    [UIAction("on-offset-increase-action-fine")]
    [UsedImplicitly]
    private void IncreaseOffsetFine()
    {
        ApplyOffset(5);
    }

    private string GetConversionProgressDisplayString(VideoConfig config)
    {
        var convertingText = config.ConvertingProgress.HasValue
            ? $"Converting ({config.ConvertingProgress:##}%)"
            : "Converting...";

        return convertingText;
    }
}

public class VideoMenuStatus : MonoBehaviour
{
    public event EventHandler DidEnable = null!;
    public event EventHandler DidDisable = null!;

    private void OnEnable()
    {
        var handler = DidEnable;

        handler.Invoke(this, EventArgs.Empty);
    }

    private void OnDisable()
    {
        var handler = DidDisable;

        handler.Invoke(this, EventArgs.Empty);
    }
}