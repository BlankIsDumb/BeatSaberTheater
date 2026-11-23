using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BeatSaberTheater.Sync;

namespace SyncHarness;

internal static class Synthetic
{
    public static async Task<bool> RunAsync(string ffmpeg, string song, string other, string outDir, CancellationToken token)
    {
        Directory.CreateDirectory(outDir);
        var songInput = "-i " + Ffmpeg.Quote(song);
        var otherInput = "-i " + Ffmpeg.Quote(other);
        const string mono = "aformat=sample_rates=44100:channel_layouts=mono";

        var a = await RenderAsync("a-delay-aac.m4a", $"{songInput} -af adelay=2500:all=1 -c:a aac -b:a 128k");
        var b = await RenderAsync("b-delay-mp3.mp3", $"{songInput} -af adelay=12340:all=1 -c:a libmp3lame -b:a 128k");
        var c = await RenderAsync("c-cut-opus.opus", $"{songInput} -af atrim=start=3.210,asetpts=PTS-STARTPTS -c:a libopus -b:a 96k");
        var d = await RenderAsync("d-pink-noise.m4a",
            $"-f lavfi -i anoisesrc=color=pink:amplitude=0.3:duration=10 {songInput} " +
            $"-filter_complex \"[0:a:0]{mono}[noise];[1:a:0]{mono}[song];[noise][song]concat=n=2:v=0:a=1[a]\" -map \"[a]\" -c:a aac");
        var e = await RenderAsync("e-cut-mp3.mp3", $"{songInput} -af atrim=start=20,asetpts=PTS-STARTPTS -c:a libmp3lame");
        var f = await RenderAsync("f-tv-size.ogg", $"{songInput} -af atrim=start=30:end=120,asetpts=PTS-STARTPTS -c:a libvorbis");
        // The video is the song slowed to 0.8x (like an original-speed MV for a sped-up map), so Theater must play it
        // at 1.25x: song time t is video time 1 + t / 0.8 = 1 + 1.25 t.
        var g = await RenderAsync("g-speed.m4a", $"{songInput} -af atempo=0.8,adelay=1000:all=1 -c:a aac");
        var h = await RenderAsync("h-splice.m4a", $"{songInput} {otherInput} " +
            $"-filter_complex \"[0:a:0]atrim=end=60,asetpts=PTS-STARTPTS,{mono}[first];" +
            $"[1:a:0]atrim=start=60,asetpts=PTS-STARTPTS,{mono}[second];[first][second]concat=n=2:v=0:a=1[a]\" -map \"[a]\" -c:a aac");
        var i = await RenderAsync("i-unrelated.m4a", $"{otherInput} -c:a aac");

        (string Name, string Song, string Video, float Speed, int Expected)[] cases =
        {
            ("a  Silence + AAC", song, a, 1f, 2500),
            ("b  Silence + MP3", song, b, 1f, 12340),
            ("c  Cut + Opus", song, c, 1f, -3210),
            ("d  Pink noise", song, d, 1f, 10000),
            ("e  Cut + MP3", song, e, 1f, -20000),
            ("f  TV-size song", f, song, 1f, 30000),
            ("g  Speed 1.25x", song, g, 1.25f, 1000),
            ("h  Splice", song, h, 1f, 0),
            ("i  Unrelated", song, i, 1f, 0)
        };

        Console.WriteLine($"{"Case",-18} {"Expected",10} {"Detected",10} {"Error ms",9} {"Class",-7} " +
            $"{"Score",7} {"Margin",7} {"Agree",7} {"Time ms",8} {"Check",-8}");
        var allPassed = true;
        for (var index = 0; index < cases.Length; index++)
        {
            var test = cases[index];
            var result = await SyncAnalyzer.AnalyzeAsync(ffmpeg, test.Song, test.Video, test.Speed, token).ConfigureAwait(false);
            var error = result.OffsetMs - test.Expected;
            var check = "info";
            if (index < 7 || index == 8)
            {
                // atempo's time-stretching smears attacks by a few ms, so the speed case gets a wider tolerance
                var tolerance = index == 6 ? 15 : 10;
                var passed = index == 8 ? result.Confidence != SyncConfidence.Strong : Math.Abs(error) <= tolerance;
                check = passed ? "PASS" : "FAIL";
                allPassed &= passed;
            }

            var expected = index == 8 ? "-" : $"{test.Expected}";
            var errorText = index == 8 ? "-" : $"{error:F2}";
            Console.WriteLine($"{test.Name,-18} {expected,10} {result.OffsetMs,10:F2} {errorText,9} {result.Confidence,-7} " +
                $"{result.Score,7:F4} {result.Margin,7:F3} {result.Agreement,7:P0} {result.TotalMs,8} {check}");
        }
        return allPassed;

        async Task<string> RenderAsync(string name, string arguments)
        {
            var path = Path.GetFullPath(Path.Combine(outDir, name));
            await Ffmpeg.RunAsync(ffmpeg, $"{arguments} -vn {Ffmpeg.Quote(path)}", token).ConfigureAwait(false);
            return path;
        }
    }
}
