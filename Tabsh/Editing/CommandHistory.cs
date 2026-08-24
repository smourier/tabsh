namespace Tabsh.Editing;

internal sealed class CommandHistory
{
    private const int _maximumEntries = 1000;

    private readonly List<string> _entries = [];
    private int _index;
    private string _pending = string.Empty;

    // what the entries have to start with to be worth stopping at, captured when the walk begins and kept for it,
    // since recomputing it from what is now on the line would search for the entry already found.
    private string _prefix = string.Empty;
    private bool _walking;

    public IReadOnlyList<string> Entries => _entries;

    public void Add(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        // a command already in the list moves to the end rather than being kept a second time,
        // so the history is a set and reads in the order things were last used.
        _entries.RemoveAll(entry => string.Equals(entry, line, StringComparison.Ordinal));
        _entries.Add(line);
        if (_entries.Count > _maximumEntries)
        {
            _entries.RemoveRange(0, _entries.Count - _maximumEntries);
        }

        ResetCursor();
    }

    public void Clear()
    {
        _entries.Clear();
        ResetCursor();
    }

    public void ResetCursor()
    {
        _index = _entries.Count;
        _pending = string.Empty;
        _prefix = string.Empty;
        _walking = false;
    }

    // the previous entry starting with the prefix, so "r" then Up reaches the last command that began with an r.
    // No prefix is no restriction.
    public string? Previous(string current, string prefix)
    {
        if (!_walking)
        {
            _pending = current;
            _prefix = prefix;
            _index = _entries.Count;
            _walking = true;
        }

        for (var i = _index - 1; i >= 0; i--)
        {
            if (Matches(i))
            {
                _index = i;
                return _entries[i];
            }
        }

        return null;
    }

    // and back down through the same ones, ending at whatever had been typed before the walk started.
    public string? Next()
    {
        if (!_walking || _index >= _entries.Count)
            return null;

        for (var i = _index + 1; i < _entries.Count; i++)
        {
            if (Matches(i))
            {
                _index = i;
                return _entries[i];
            }
        }

        _index = _entries.Count;
        return _pending;
    }

    private bool Matches(int index) =>
        _prefix.Length == 0 || (_entries[index].StartsWith(_prefix, StringComparison.OrdinalIgnoreCase) && _entries[index].Length > _prefix.Length);

    public void Load(string path)
    {
        if (!File.Exists(path))
            return;

        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // the same rule the prompt adds by, since a file written before this existed may hold repeats.
                _entries.RemoveAll(entry => string.Equals(entry, line, StringComparison.Ordinal));
                _entries.Add(line);
            }

            if (_entries.Count > _maximumEntries)
            {
                _entries.RemoveRange(0, _entries.Count - _maximumEntries);
            }
        }
        catch (IOException)
        {
            // a history that cannot be read is not a reason to refuse to start.
        }

        ResetCursor();
    }

    public void Save(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllLines(path, _entries);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // continue, losing the history is not worth a message on the way out.
        }
    }
}
