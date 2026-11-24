using System;
using System.IO;
using System.Text;

namespace SmbAutoMount.Core;

/// <summary>Tiny rolling text log. Never logs passwords.</summary>
internal static class Log
{
    private static readonly object Gate = new();
    private const long MaxBytes = 1024 * 1024;

    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex == null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                var file = Paths.LogFile;
                var fi = new FileInfo(file);
                if (fi.Exists && fi.Length > MaxBytes)
                {
                    var old = file + ".1";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(file, old);
                }
                File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch
        {
            // logging must never crash the app
        }
    }
}
