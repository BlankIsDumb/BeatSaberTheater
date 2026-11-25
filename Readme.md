# BeatSaberTheater

This is a Beat Saber mod that will play a video in the background of your maps. Use this to see the music
videos for your favorite songs while you play!

This mod is a rewrite/successor to the [Cinema mod](https://github.com/Kevga/BeatSaberCinema).

> **This is a fork** of [BeatSaberTheater](https://github.com/BeatSaberTheater/BeatSaberTheater), made for **Beat Saber 1.40.8**
> and mostly vibecoded, multiple independent agents check and in game test were run (no shit, im the player).
> For the original mod, see the upstream repository.

### What this fork adds

* **Auto sync:** matches the video's audio to the song and suggests offsets you can try and apply.
* **Finer sync controls:** ±5 ms steps, a playback speed slider, the offset shown against the map's default, and
  Revert sync.
* **Start sync:** every video start is measured against the song and nudged into place, including after pauses,
  seeks and Intro Skip. The Video Lead and Sync Correction settings control it.
* **Display settings in the Theater tab:** on/off, brightness, screen size, corner roundness, edge softness and
  bloom, applied live.
* **Brightness Limit:** dims bright scenes and flashes so the screen stays below a level you set.
* **Fixes:** the environment override on 1.40.8, deleting a loaded video breaking all playback until a restart, and
  more.

## Installation

This mod is not yet available on BeatMods and must be installed manually.

### Manual Installation

1. Download the latest release from [here](https://github.com/BlankIsDumb/BeatSaberTheater/releases/latest).
2. Copy the contents of the zip into your current Beat Saber directory (the one with `Beat Saber.exe`).
3. That's it! Theater is now installed

### Dependencies

This mod has several dependencies, ensure they are all installed before launching the game. These dependencies
can be installed through your favorite mod manager (BSManager, ModAssistant, etc.).

#### Required

* BSIPA
* SongCore
* BS Utils
* SiraUtil
* BeatSaberPlaylistsLib
* ffmpeg
* BeatSaberMarkupLanguage

#### Optional

* BetterSongList

## Usage

Before you can see the videos for you songs, you must download them. Some songs are preconfigured with a video and
you will see a download button for them as shown below.

![Preconfigured Video Example](https://github.com/neboman11/BeatSaberTheater/blob/main/docs/images/preconfigured-video-example.png)

If no video is found, you can search for one to use in the `Gameplay Setup` menu on the left. Simply go to the
`Mods` tab, and then open the `Theater` menu.

![Search for Video Example](https://github.com/neboman11/BeatSaberTheater/blob/main/docs/images/search-for-video-example.png)

After you select `Search`, Theater will search YouTube for videos matching the selected song. Results will begin to
appear in a list as Theater finds them. Choose whichever video you want, and then hit download.

![Search Download Example](https://github.com/neboman11/BeatSaberTheater/blob/main/docs/images/search-download-example.png)

After the song downloads, you will be given a menu to adjust the timing of the video with the song.

![Timing Adjust Menu](https://github.com/neboman11/BeatSaberTheater/blob/main/docs/images/timing-adjust-menu.png)

This will allow you to sync them together and skip any non-music sections at the beginning of the video.
Hitting the `Preview`button will play both the video and song from the beginning, with the Beat Saber song
playing only in your right ear and the video audio playing in your left ear.

## Advanced Configuration

### Custom yt-dlp Parameters

Theater uses [yt-dlp](https://github.com/yt-dlp/yt-dlp) to download videos from YouTube and other sources. If you need to customize how yt-dlp behaves, you can provide a custom configuration file.

#### Configuration Options

Most Theater settings can be configured through the in-game settings menu. To access the settings:

1. In Beat Saber, in the list of mods on the left of the main menu, select `Theater`
2. The "General" tab contains timeout and yt-dlp auto-config settings
3. The "Visuals" tab contains visual customization options

The **yt-dlp Auto Config** toggle controls how Theater handles yt-dlp configuration files:

* **Off (Default)**: Theater will search for a `yt-dlp.conf` file in the following locations (in order):
  1. `BeatSaber/UserData/yt-dlp.conf` - User-specific configuration
  2. `BeatSaber/Libs/Theater/yt-dlp.conf` - Global Theater configuration
  
  If found, Theater uses that configuration. If neither file exists, yt-dlp runs with `--ignore-config` to ignore system-wide configuration files.

* **On**: Theater skips all configuration file searches and allows yt-dlp to automatically resolve configuration according to its own rules, including system-wide configuration files.

#### Using a yt-dlp Configuration File

You can create a `yt-dlp.conf` file in either the `UserData` or `Library/Theater` directory to customize yt-dlp behavior. Example:

```conf
# Proxy settings
--proxy [PROXY URL]

# Network settings
--socket-timeout 30

# Video quality preferences
--format "bestvideo[height<=1080]+bestaudio/best"

# Output format
--prefer-ffmpeg
```

For a complete list of yt-dlp options, see the [yt-dlp documentation](https://github.com/yt-dlp/yt-dlp#general-options) and [yt-dlp configuration guide](https://github.com/yt-dlp/yt-dlp#configuration).

### Debug logging

For detailed sync and performance logs, set `"DebugLogging": true` in `UserData/BeatSaberTheater.json`. It's off by
default, because it costs a little every frame in songs and fills the log.

