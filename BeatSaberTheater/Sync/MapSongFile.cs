using System;
using System.IO;
using System.Security;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BeatSaberTheater.Sync;

internal static class MapSongFile
{
    public static string? Find(string mapFolder)
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(mapFolder))
            {
                if (!string.Equals(Path.GetFileName(path), "Info.dat", StringComparison.OrdinalIgnoreCase))
                    continue;

                var info = JObject.Parse(File.ReadAllText(path));
                var name = (string?)info["_songFilename"] ?? (string?)(info["audio"] as JObject)?["songFilename"];
                if (string.IsNullOrWhiteSpace(name))
                    return null;

                var songPath = Path.GetFullPath(Path.Combine(mapFolder, name!));
                return File.Exists(songPath) ? songPath : null;
            }
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
            exception is JsonException || exception is ArgumentException || exception is InvalidCastException ||
            exception is NotSupportedException || exception is SecurityException)
        {
            return null;
        }

        return null;
    }
}
