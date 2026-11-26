using System.Globalization;
using System.Text;

namespace ShrinkRay.Core.Diagnostics;

/// <summary>
/// A plain text log kept beside the journal: what each run did and why anything went wrong. It
/// holds library paths, so it stays on this machine. Past <see cref="MaxBytes"/> the file moves
/// to <c>.old</c>, replacing the one before. Writing never throws. Thread safe.
/// </summary>
public sealed class FileLog
{
    public const long DefaultMaxBytes = 2_000_000;

    private readonly string _path;
    private readonly long _maxBytes;
    private readonly Func<DateTimeOffset> _now;
    private readonly object _lock = new();

    public FileLog(string path, long maxBytes = DefaultMaxBytes, Func<DateTimeOffset>? now = null)
    {
        _path = path;
        _maxBytes = maxBytes;
        _now = now ?? (() => DateTimeOffset.Now);
    }

    public string FilePath => _path;

    public string OldPath => _path + ".old";

    public long MaxBytes => _maxBytes;

    /// <summary>Adds an entry. Lines after the first are indented under it.</summary>
    public void Write(string message)
    {
        var stamp = _now().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var text = message.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Replace("\n", Environment.NewLine + "    ", StringComparison.Ordinal);
        lock (_lock)
        {
            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (File.Exists(_path) && new FileInfo(_path).Length >= _maxBytes)
                {
                    File.Move(_path, OldPath, overwrite: true);
                }

                File.AppendAllText(_path, $"{stamp}  {text}{Environment.NewLine}", new UTF8Encoding(false));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A log that cannot be written must not stop a run.
            }
        }
    }
}
