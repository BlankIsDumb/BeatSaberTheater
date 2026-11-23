using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BeatSaberTheater.Sync;
using Newtonsoft.Json.Linq;

namespace SyncHarness;

internal static class Scan
{
    public static async Task RunAsync(string game, string ffmpeg, string? only, string? listenDir, bool mix,
        CancellationToken token)
    {
        game = Path.GetFullPath(game);
        var customLevels = Path.Combine(game, "Beat Saber_Data", "CustomLevels");
        var hashPattern = new Regex(@"custom_level_([0-9a-f]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var songHashes = new Lazy<Dictionary<string, string>>(() => ReadSongHashes(game));
        var entries = new List<Entry>();
        var skipped = new List<string>();
        var configs = Directory.EnumerateFiles(customLevels, "*-video.json", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
        foreach (var configPath in configs)
        {
            token.ThrowIfCancellationRequested();
            var configName = Path.GetFileName(configPath);
            if (!string.Equals(configName, "cinema-video.json", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(configName, "theater-video.json", StringComparison.OrdinalIgnoreCase))
                continue;

            var folder = Path.GetDirectoryName(configPath)!;
            var folderName = Path.GetFileName(folder);
            if (only != null && folderName.IndexOf(only, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            try
            {
                var config = JObject.Parse(File.ReadAllText(configPath));
                var video = FindVideo(folder, (string?)config["videoFile"]);
                if (video == null)
                    throw new SyncException("video file not found.");

                var mapFolder = folder;
                var shortName = folderName;
                var hash = hashPattern.Match(folderName);
                if (hash.Success)
                {
                    if (!songHashes.Value.TryGetValue(hash.Groups[1].Value, out var mappedFolder))
                        throw new SyncException("map hash not found in SongCore's SongHashData.dat.");
                    mapFolder = mappedFolder;
                    shortName = folderName.Substring(0, hash.Index).TrimEnd(' ', '-');
                    if (shortName.Length == 0)
                        shortName = Path.GetFileName(mapFolder);
                }

                var song = MapSongFile.Find(mapFolder);
                if (song == null)
                    throw new SyncException("song file not found.");

                var offset = (int?)config["offset"] ?? 0;
                var speed = (float?)config["playbackSpeed"] ?? 1;
                var result = await SyncAnalyzer.AnalyzeAsync(ffmpeg, song, video, speed, token).ConfigureAwait(false);
                entries.Add(new Entry(shortName, song, video, offset, result));
            }
            catch (Exception exception) when (!(exception is OperationCanceledException))
            {
                skipped.Add($"Skipped {folderName} ({configName}): {exception.Message}");
            }
        }

        PrintTable(entries);
        foreach (var entry in entries)
        {
            Console.WriteLine(entry.Name);
            Report.PrintDetails(entry.Result);
        }
        foreach (var message in skipped)
            Console.WriteLine(message);

        var longest = entries.OrderByDescending(entry => entry.Result.SongSeconds).FirstOrDefault();
        if (longest != null)
        {
            var result = longest.Result;
            Console.WriteLine($"Longest map  {longest.Name}   Song {result.SongSeconds:F2} s   Video {result.VideoSeconds:F2} s");
            Console.WriteLine(Report.Timings(result));
        }

        if (listenDir == null)
            return;

        var renderFailures = 0;
        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                await RenderListeningAsync(entry, ffmpeg, listenDir, mix, token).ConfigureAwait(false);
            }
            catch (Exception exception) when (!(exception is OperationCanceledException))
            {
                renderFailures++;
                Console.Error.WriteLine($"Could not render listening files for {entry.Name}: {exception.Message}");
            }
        }
        if (renderFailures != 0)
            throw new SyncException($"Listening renders failed for {renderFailures} map(s).");
    }

    private static Dictionary<string, string> ReadSongHashes(string game)
    {
        var path = Path.Combine(game, "UserData", "SongCore", "SongHashData.dat");
        var data = JObject.Parse(File.ReadAllText(path));
        var folders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in data.Properties())
        {
            var hash = (string?)(entry.Value as JObject)?["songHash"];
            if (string.IsNullOrEmpty(hash))
                continue;
            var folder = Path.GetFullPath(Path.Combine(game, entry.Name));
            if (Directory.Exists(folder) && !folders.ContainsKey(hash!))
                folders.Add(hash!, folder);
        }
        return folders;
    }

    private static string? FindVideo(string folder, string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            var video = Path.GetFullPath(Path.Combine(folder, name!));
            if (string.Equals(Path.GetDirectoryName(video), folder, StringComparison.OrdinalIgnoreCase) && File.Exists(video))
                return video;
        }

        return Directory.EnumerateFiles(folder)
            .Where(path =>
            {
                var extension = Path.GetExtension(path).ToLowerInvariant();
                return extension == ".mp4" || extension == ".webm" || extension == ".mkv";
            })
            .OrderBy(path =>
            {
                var file = Path.GetFileName(path);
                return file.IndexOf(".vp9.", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    file.IndexOf(".vp8.", StringComparison.OrdinalIgnoreCase) >= 0;
            })
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static void PrintTable(List<Entry> entries)
    {
        Console.WriteLine($"{"Map",-42} {"Config ms",10} {"Auto ms",10} {"Diff ms",10} {"Class",-7} " +
            $"{"Score",7} {"Margin",7} {"Agree",7} {"Time ms",8}");
        foreach (var entry in entries)
        {
            var result = entry.Result;
            var name = entry.Name.Length <= 42 ? entry.Name : entry.Name.Substring(0, 39) + "...";
            Console.WriteLine($"{name,-42} {entry.OffsetMs,10} {result.OffsetMs,10:F2} " +
                $"{result.OffsetMs - entry.OffsetMs,10:F2} {result.Confidence,-7} " +
                $"{result.Score,7:F4} {result.Margin,7:F3} {result.Agreement,7:P0} {result.TotalMs,8}");
        }
        Console.WriteLine();
    }

    private static async Task RenderListeningAsync(Entry entry, string ffmpeg, string listenDir, bool mix,
        CancellationToken token)
    {
        var result = entry.Result;
        await RenderAsync("current", entry.OffsetMs).ConfigureAwait(false);
        await RenderAsync("auto", result.OffsetMs).ConfigureAwait(false);
        if (result.Confidence != SyncConfidence.Strong)
        {
            for (var i = 1; i < result.Candidates.Count; i++)
                await RenderAsync($"cand{i + 1}", result.Candidates[i].OffsetMs).ConfigureAwait(false);
        }

        Task RenderAsync(string label, double offset)
        {
            var file = Path.Combine(listenDir, $"{entry.Name} - {label} {offset:0.##}ms.split.mp3");
            return Listening.RenderAsync(ffmpeg, entry.Song, entry.Video, offset, result.PlaybackSpeed, file, mix, 0, null, token);
        }
    }

    private sealed class Entry
    {
        public Entry(string name, string song, string video, int offsetMs, SyncResult result)
        {
            Name = name;
            Song = song;
            Video = video;
            OffsetMs = offsetMs;
            Result = result;
        }

        public string Name { get; }
        public string Song { get; }
        public string Video { get; }
        public int OffsetMs { get; }
        public SyncResult Result { get; }
    }
}
