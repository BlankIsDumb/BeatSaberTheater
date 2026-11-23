using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BeatSaberTheater.Sync;

namespace SyncHarness;

internal static class Ffmpeg
{
    public static string Quote(string value) => $"\"{value}\"";

    public static async Task RunAsync(string ffmpegPath, string arguments, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!File.Exists(ffmpegPath))
            throw new SyncException($"ffmpeg was not found at '{ffmpegPath}'.");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = "-v error -nostdin -y " + arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            }
        };
        process.Start();
        using var registration = token.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch (InvalidOperationException)
            {
                // The process can finish before the cancellation callback runs.
            }
            catch (Win32Exception)
            {
                // Windows can report a racing exit as a failed termination.
            }
        });

        var error = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        process.WaitForExit();
        token.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
            throw new SyncException($"ffmpeg failed (exit {process.ExitCode}): {error.Trim()}");
    }
}
