namespace ClaudeAllowHelper.Core;

public sealed class FileLogger
{
    private readonly object _gate = new();
    public string LogPath { get; }

    public FileLogger(string logPath)
    {
        LogPath = logPath;
        var directory = Path.GetDirectoryName(logPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message) => Write("ERROR", message);

    public string ReadTail(int maxCharacters = 100_000)
    {
        lock (_gate)
        {
            if (!File.Exists(LogPath))
            {
                return "";
            }

            using var stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length <= maxCharacters)
            {
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }

            stream.Seek(-maxCharacters, SeekOrigin.End);
            using var tailReader = new StreamReader(stream);
            return tailReader.ReadToEnd();
        }
    }

    private void Write(string level, string message)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
        lock (_gate)
        {
            File.AppendAllText(LogPath, line);
        }
    }
}
