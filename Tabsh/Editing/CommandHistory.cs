namespace Tabsh.Editing;

internal sealed class CommandHistory
{
    private const int _maximumEntries = 1000;

    private readonly List<string> _entries = [];

    // where the file is, kept from the load so that a line can be written the moment it is entered.
    // A window closed with its cross kills the process where it stands, so nothing held back for the end survives.
    private string? _path;
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

        Append(line);
        ResetCursor();
    }

    // one line, straight out, which leaves the file holding the repeats that moving an entry to the end removes.
    // Loading reads it as a set and rewrites it, so the repeats last until the next start and no longer.
    private void Append(string line)
    {
        if (_path == null)
            return;

        try
        {
            File.AppendAllText(_path, line + Environment.NewLine);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // a history that cannot be written is not a reason to refuse the command.
        }
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
        // remembered before the file is read, since a history that does not exist yet still has somewhere to go.
        _path = path;
        if (!File.Exists(path))
            return;

        var read = 0;
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                read++;

                // the same rule the prompt adds by, since a file written before this existed may hold repeats.
                _entries.RemoveAll(entry => string.Equals(entry, line, StringComparison.Ordinal));
                _entries.Add(line);
            }

            if (_entries.Count > _maximumEntries)
            {
                _entries.RemoveRange(0, _entries.Count - _maximumEntries);
            }

            // what the appends left behind is compacted here rather than growing a line at a time forever.
            if (read != _entries.Count)
            {
                Save(path);
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
