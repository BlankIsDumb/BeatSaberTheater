using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SyncHarness;

internal static class Listening
{
    public static async Task RenderAsync(string ffmpeg, string song, string video, double offsetMs, float speed,
        string outFile, bool mix, double start, double? length, CancellationToken token)
    {
        if (speed < 0.25f || speed > 100f || float.IsNaN(speed))
            throw new ArgumentException("Listening speed must be between 0.25 and 100.");
        if (start < 0 || length <= 0)
            throw new ArgumentException("Start must be nonnegative and length must be positive.");

        outFile = Path.GetFullPath(outFile);
        Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
        const int sampleRate = 44100;
        // Use the same media-time origin as the decoder before applying the offset.
        const string mediaOrigin = "aresample=async=1:first_pts=0";
        var videoChain = new List<string> { mediaOrigin };
        if (offsetMs >= 0)
        {
            videoChain.Add($"atrim=start={offsetMs / 1000:0.#########}");
            videoChain.Add("asetpts=PTS-STARTPTS");
        }
        if (speed < 0.5f)
        {
            // Each atempo filter bottoms out at 0.5x.
            videoChain.Add("atempo=0.5");
            videoChain.Add($"atempo={speed * 2:R}");
        }
        else if (speed != 1f)
        {
            videoChain.Add($"atempo={speed:R}");
        }

        videoChain.Add("aformat=channel_layouts=mono");
        videoChain.Add($"aresample={sampleRate}");
        if (offsetMs < 0)
        {
            // Negative video offsets are a delay on the song axis. Use whole samples to keep fractional ms.
            var delay = Math.Round(-offsetMs / speed / 1000 * sampleRate);
            videoChain.Add($"adelay={delay:0}S");
        }
        videoChain.Add("dynaudnorm");
        // ffmpeg 4.3 needs explicit channel layouts at amerge's inputs.
        videoChain.Add("aformat=channel_layouts=mono");
        videoChain.Add("apad");

        var filters = $"[1:a:0]{string.Join(",", videoChain)}[video];" +
            $"[0:a:0]{mediaOrigin},aformat=channel_layouts=mono,aresample={sampleRate},dynaudnorm,aformat=channel_layouts=mono[song];" +
            "[video][song]amerge=inputs=2";
        if (start > 0 || length.HasValue)
        {
            filters += $",atrim=start={start:0.#########}";
            if (length.HasValue)
                filters += $":duration={length.Value:0.#########}";
            filters += ",asetpts=PTS-STARTPTS";
        }
        filters += mix ? ",asplit=2[split][mix];[mix]pan=mono|c0=0.5*c0+0.5*c1[mixed]" : "[split]";

        var arguments = $"-i {Ffmpeg.Quote(song)} -i {Ffmpeg.Quote(video)} -filter_complex \"{filters}\" " +
            $"-map \"[split]\" -c:a libmp3lame -b:a 192k {Ffmpeg.Quote(outFile)}";
        if (mix)
        {
            var baseName = Path.GetFileNameWithoutExtension(outFile);
            if (baseName.EndsWith(".split", StringComparison.OrdinalIgnoreCase))
                baseName = baseName.Substring(0, baseName.Length - ".split".Length);
            var mixFile = Path.Combine(Path.GetDirectoryName(outFile)!, baseName + ".mix.mp3");
            arguments += $" -map \"[mixed]\" -c:a libmp3lame -b:a 192k {Ffmpeg.Quote(mixFile)}";
        }

        await Ffmpeg.RunAsync(ffmpeg, arguments, token).ConfigureAwait(false);
    }
}
