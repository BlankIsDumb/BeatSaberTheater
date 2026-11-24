using System.Runtime.CompilerServices;
using BeatSaberTheater.Settings;
using IPA.Config.Stores;
using IPA.Config.Stores.Attributes;

[assembly: InternalsVisibleTo(GeneratedStore.AssemblyVisibilityTarget)]

namespace BeatSaberTheater;

public class PluginConfig
{
    // Runtime state set by BSIPA enabling/disabling the plugin (Plugin.OnEnable/OnDisable). Not saved: it only
    // describes this run, and saving it let another game process's shutdown switch Theater off in this one
    // (IPA reloads the config file when it changes on disk).
    [Ignore] public virtual bool PluginEnabled { get; set; } = true;

    // The player's on/off switch (Display view in the Theater tab, main menu settings). Survives restarts.
    public virtual bool TheaterEnabled { get; set; } = true;

    // Whether Theater should play videos and modify environments right now
    [Ignore] public bool TheaterActive => PluginEnabled && TheaterEnabled;

    public virtual bool OverrideEnvironment { get; set; } = true;
    public virtual bool DisableCustomPlatforms { get; set; } = true;
    public virtual bool Enable360Rotation { get; set; } = true;
    public virtual bool CurvedScreen { get; set; } = true;
    public virtual bool TransparencyEnabled { get; set; } = true;
    public virtual bool ColorBlendingEnabled { get; set; } = true;
    public virtual bool CoverEnabled { get; set; }
    public virtual int BloomIntensity { get; set; } = 100;
    public virtual float CornerRoundness { get; set; }

    // How far ahead of the song the video picture is shown, in ms, to make up for the delay before the picture is
    // visible. Not applied to Preview. Most setups keep the default.
    public virtual int VideoLeadMs { get; set; } = 67;

    // Learned: how much earlier (ms) a delayed video start calls Play(), to make up for the player's start-up delay.
    // Updated by PlaybackManager after each delayed start. Not shown in the UI.
    public virtual int StartDelayCompensationMs { get; set; } = 0;

    // Hold the start of a solo song (at most 3 s) until its video has loaded, so it starts in sync from its first frame.
    public virtual bool WaitForVideoBeforeSong { get; set; } = false;

    // Sync Correction setting. False: start sync stops 0.15 s before a song's first note (the user's original rule).
    // True: in songs, every start, seek and resume gets the full 3 s correction window, even after notes have started.
    // The video is muted in songs, so the speed nudge can't be heard there.
    public virtual bool SyncCorrectionAnyTime { get; set; } = true;

    // Display settings, in percent. See Settings/DisplaySettings.cs for ranges.
    public virtual int ScreenBrightness { get; set; } = 100;
    public virtual int ScreenScale { get; set; } = 100;
    public virtual int EdgeSoftness { get; set; }
    public virtual VideoQuality.Mode QualityMode { get; set; } = VideoQuality.Mode.Q720P;
    public virtual VideoFormats.Format Format { get; set; } = VideoFormats.Format.Mp4;
    public virtual bool ForceDisableEnvironmentOverrides { get; set; } = false;

    // Timeouts
    public virtual int DownloadTimeoutSeconds { get; set; } = 5 * 60;
    public virtual int SearchTimeoutSeconds { get; set; } = 45;

    // yt-dlp Configuration
    public virtual bool YtDlpAutoConfig { get; set; } = false;
}