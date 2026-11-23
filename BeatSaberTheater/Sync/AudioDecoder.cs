using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BeatSaberTheater.Sync;

internal static class AudioDecoder
{
    public static async Task<float[]> DecodeAsync(string ffmpegPath, string inputPath, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!File.Exists(ffmpegPath))
            throw new SyncException($"ffmpeg was not found at '{ffmpegPath}'.");

        var sampleRate = SyncSettings.SampleRate.ToString(CultureInfo.InvariantCulture);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                // Pad a delayed audio stream so sample zero is media time zero.
                Arguments = $"-v error -nostdin -i \"{inputPath}\" -vn -map 0:a:0 -af aresample=async=1:first_pts=0 -ac 1 -ar {sampleRate} -f f32le -",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        try
        {
            process.Start();
        }
        catch (Win32Exception exception)
        {
            token.ThrowIfCancellationRequested();
            throw new SyncException($"Could not start ffmpeg: {exception.Message}");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var registration = timeout.Token.Register(() => KillIfRunning(process));
        timeout.CancelAfter(TimeSpan.FromSeconds(SyncSettings.DecodeTimeoutSeconds));
        using var audio = new MemoryStream();
        var fileName = Path.GetFileName(inputPath);
        try
        {
            // ffmpeg must be able to write to either pipe while the other is being read.
            var stdout = process.StandardOutput.BaseStream.CopyToAsync(audio, 81920, timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync();
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            process.WaitForExit();
            timeout.Token.ThrowIfCancellationRequested();

            if (process.ExitCode != 0)
            {
                var error = await stderr.ConfigureAwait(false);
                var lines = error.TrimEnd().Split('\n');
                var lastLine = lines[lines.Length - 1].Trim();
                if (error.IndexOf("matches no streams", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new SyncException($"'{fileName}' has no audio stream: {lastLine}");
                throw new SyncException($"ffmpeg could not decode '{fileName}' (exit {process.ExitCode}): {lastLine}");
            }

            var sampleCount = (int)(audio.Length / sizeof(float));
            if (sampleCount < 5 * SyncSettings.SampleRate)
                throw new SyncException($"'{fileName}' contains fewer than 5 seconds of audio.");

            var samples = new float[sampleCount];
            var bytes = audio.GetBuffer();
            // f32le matches the native float layout on both supported Windows runtimes.
            for (var first = 0; first < sampleCount; first += 4096)
            {
                token.ThrowIfCancellationRequested();
                var count = Math.Min(4096, sampleCount - first);
                Buffer.BlockCopy(bytes, first * sizeof(float), samples, first * sizeof(float), count * sizeof(float));
            }
            return samples;
        }
        catch (Exception) when (timeout.IsCancellationRequested)
        {
            token.ThrowIfCancellationRequested();
            throw new SyncException($"Decoding '{fileName}' timed out after {SyncSettings.DecodeTimeoutSeconds} seconds.");
        }
        finally
        {
            KillIfRunning(process);
        }
    }

    private static void KillIfRunning(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill();
        }
        catch (InvalidOperationException)
        {
            // The process may exit between the check and the kill.
        }
        catch (Win32Exception)
        {
            // Windows may report a racing exit as a failed termination.
        }
    }
}
