using System;
using System.IO;

namespace LyricOverlay.Diagnostics;

/// <summary>Tiny append-only log so the overlay can be diagnosed when it runs without a console.</summary>
internal static class Log
{
    private static readonly object Gate = new();

    private static string PathFor(string file) =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LyricOverlay",
            file);

    public static void Info(string message) => Write("overlay.log", "INFO", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("overlay.log", "ERROR", ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string file, string level, string message)
    {
        try
        {
            var path = PathFor(file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            lock (Gate)
            {
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take the overlay down.
        }
    }
}
