using System;
using System.Threading.Tasks;

namespace BeatSaberTheater.Util;

internal static class TaskExtensions
{
    /// <summary>
    /// Lets a Task returned by an async method keep running without awaiting it, and logs any exception instead of
    /// losing it. Use this instead of <c>task.Start()</c>: Tasks from async methods are already running, and calling
    /// Start() on them always throws InvalidOperationException.
    /// </summary>
    internal static async void LogErrors(this Task task, LoggingService loggingService, string description)
    {
        try
        {
            await task;
        }
        catch (Exception e)
        {
            loggingService.Error($"{description} failed");
            loggingService.Error(e);
        }
    }
}
