using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BeatSaberTheater.Sync;

namespace SyncHarness;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancel;
        try
        {
            if (args.Length == 0)
                throw new ArgumentException("Choose a command: analyze, scan, synthetic, or listen.");

            var options = ParseOptions(args);
            var game = Get(options, "--game") ?? typeof(Program).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == "BeatSaberDir").Value;
            var ffmpeg = Get(options, "--ffmpeg") ?? Path.Combine(game, "Libs", "Theater", "ffmpeg.exe");
            var speed = (float)Number(options, "--speed", 1);
            var token = cancellation.Token;
            switch (args[0])
            {
                case "analyze":
                    var result = await SyncAnalyzer.AnalyzeAsync(ffmpeg, Require(options, "--song"),
                        Require(options, "--video"), speed, token).ConfigureAwait(false);
                    Report.PrintAnalysis(result);
                    break;
                case "scan":
                    await Scan.RunAsync(game, ffmpeg, Get(options, "--only"), Get(options, "--listen"),
                        options.ContainsKey("--mix"), token).ConfigureAwait(false);
                    break;
                case "synthetic":
                    var passed = await Synthetic.RunAsync(ffmpeg, Require(options, "--song"),
                        Require(options, "--other"), Require(options, "--out"), token).ConfigureAwait(false);
                    if (!passed)
                    {
                        Console.Error.WriteLine("One or more synthetic checks failed.");
                        return 1;
                    }
                    break;
                case "listen":
                    await Listening.RenderAsync(ffmpeg, Require(options, "--song"), Require(options, "--video"),
                        Number(options, "--offset"), speed, Require(options, "--out"), options.ContainsKey("--mix"),
                        Number(options, "--start", 0),
                        options.ContainsKey("--length") ? Number(options, "--length") : (double?)null,
                        token).ConfigureAwait(false);
                    break;
            }
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Canceled.");
            return 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var allowed = args[0] switch
        {
            "analyze" => new[] { "--song", "--video", "--speed" },
            "scan" => new[] { "--only", "--listen", "--mix" },
            "synthetic" => new[] { "--song", "--other", "--out" },
            "listen" => new[] { "--song", "--video", "--offset", "--speed", "--out", "--mix", "--start", "--length" },
            _ => throw new ArgumentException($"Unknown command '{args[0]}'.")
        };

        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i < args.Length; i++)
        {
            var option = args[i];
            if (option != "--game" && option != "--ffmpeg" && !allowed.Contains(option))
                throw new ArgumentException($"Unknown option '{option}' for {args[0]}.");
            if (options.ContainsKey(option))
                throw new ArgumentException($"Option '{option}' was supplied twice.");
            if (option == "--mix")
            {
                options.Add(option, "");
                continue;
            }
            if (i + 1 == args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Missing value for {option}.");

            options.Add(option, args[++i]);
        }
        return options;
    }

    private static string? Get(Dictionary<string, string> options, string name)
    {
        return options.TryGetValue(name, out var value) ? value : null;
    }

    private static string Require(Dictionary<string, string> options, string name)
    {
        return Get(options, name) ?? throw new ArgumentException($"Missing required option {name}.");
    }

    private static double Number(Dictionary<string, string> options, string name, double? fallback = null)
    {
        if (fallback.HasValue && !options.ContainsKey(name))
            return fallback.Value;
        if (!double.TryParse(Require(options, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentException($"{name} must be a finite number.");
        }
        return value;
    }
}
