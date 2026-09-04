namespace ClaudeAllowHelper;

internal sealed class FileTailer
{
    private readonly string _path;
    private long _position = -1;

    public FileTailer(string path)
    {
        _path = path;
    }

    public string? ReadNewText()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (_position < 0 || _position > stream.Length)
        {
            _position = stream.Length;
            return null;
        }

        if (_position == stream.Length)
        {
            return null;
        }

        stream.Seek(_position, SeekOrigin.Begin);
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();
        _position = stream.Position;
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
