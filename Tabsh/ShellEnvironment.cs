namespace Tabsh;

// Windows keeps one current directory per process, cmd keeps one per drive on top of it so that "d:" goes back,
// and nothing maintains that map for us. It lives in "=D:" variables only cmd writes.
internal sealed class ShellEnvironment
{
    private readonly Dictionary<string, string> _variables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<char, string> _driveDirectories = new();
    private const string _defaultPrompt = "$P$G";

    // what takes cd to the root of the shell namespace, and what an absolute name in it starts with.
    private const string _shellRoot = "@";
    private const string _parsingNamePrefix = "::";

    // a code point because a raw escape character in source is invisible.
    private const char _escape = (char)0x1b;

    public ShellEnvironment()
    {
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var name = entry.Key.ToString();
            if (string.IsNullOrEmpty(name))
                continue;

            _variables[name] = entry.Value?.ToString() ?? string.Empty;
        }

        RecordDrive(Environment.CurrentDirectory);
        PreviousDirectory = Environment.CurrentDirectory;
    }

    public int LastExitCode { get; set; }
    public string CurrentDirectory => Environment.CurrentDirectory;
    public string PreviousDirectory { get; private set; }
    public Stack<string> DirectoryStack { get; } = new();

    public IEnumerable<KeyValuePair<string, string>> Variables => _variables.OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase);

    public string Prompt
    {
        get => Get("PROMPT") ?? _defaultPrompt;
        set => Set("PROMPT", value);
    }

    public string? Get(string name) => _variables.TryGetValue(name, out var value) ? value : null;

    public void Set(string name, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            _variables.Remove(name);
        }
        else
        {
            _variables[name] = value;
        }
    }

    // what %NAME% resolves to, null when nothing by that name exists so the lexer can leave the text alone.
    public string? Resolve(string name)
    {
        switch (name.ToUpperInvariant())
        {
            case "CD":
                return CurrentDirectory;

            case "ERRORLEVEL":
                return LastExitCode.ToString(CultureInfo.InvariantCulture);

            case "RANDOM":
                return Random.Shared.Next(0, 32768).ToString(CultureInfo.InvariantCulture);

            case "DATE":
                return DateTime.Now.ToString("d", CultureInfo.CurrentCulture);

            case "TIME":
                return DateTime.Now.ToString("T", CultureInfo.CurrentCulture);
        }

        return Get(name);
    }

    // where the shell is in the namespace, when that is somewhere the file system cannot name. See ShellLocation.
    public ShellLocation Location { get; } = new();

    // the file system comes first, always, and only when it has no answer is the namespace asked,
    // which is how "cd This PC" works from the Desktop where the directory has no such child.
    public void ChangeDirectory(string path)
    {
        // "@" behaves like a drive,
        // so "@Downloads" and "@:\Downloads" are the same place and a prompt can be copied back into a cd.
        if (path.StartsWith(_shellRoot, StringComparison.Ordinal))
        {
            var below = path[_shellRoot.Length..];
            if (below.StartsWith(':'))
            {
                below = below[1..];
            }

            if (!Location.EnterPath(below, fromRoot: true))
                throw new DirectoryNotFoundException(string.Format(CultureInfo.CurrentCulture, Res.PathNotFound, path));

            SyncRealDirectory();
            return;
        }

        // an absolute name in the namespace parses on its own, without reference to where we are.
        if (path.StartsWith(_parsingNamePrefix, StringComparison.Ordinal))
        {
            if (!Location.EnterParsingName(path))
                throw new DirectoryNotFoundException(string.Format(CultureInfo.CurrentCulture, Res.PathNotFound, path));

            SyncRealDirectory();
            return;
        }

        // "d:" on its own means the directory we were last in on D, not the root of it.
        if (path.Length == 2 && path[1] == ':' && char.IsAsciiLetter(path[0]))
        {
            path = DirectoryOnDrive(path[0]);
        }

        // a leading separator means the root of the drive you are on, which here is "@:".
        // Only a fully qualified path names the file system and leaves.
        if (Location.IsVirtual && !Path.IsPathFullyQualified(path))
        {
            var fromRoot = path.Length > 0 && (path[0] == Path.DirectorySeparatorChar || path[0] == Path.AltDirectorySeparatorChar);
            if (!Location.EnterPath(path, fromRoot))
                throw new DirectoryNotFoundException(string.Format(CultureInfo.CurrentCulture, Res.PathNotFound, path));

            SyncRealDirectory();
            return;
        }

        if (TryRealDirectory(path))
            return;

        // nothing on disk by that name, so the shell folder for this directory is asked.
        if (TryShellChild(path))
            return;

        throw new DirectoryNotFoundException(string.Format(CultureInfo.CurrentCulture, Res.PathNotFound, path));
    }

    private bool TryRealDirectory(string path)
    {
        string full;
        try
        {
            full = ShellPath.Resolve(path, CurrentDirectory);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (!Directory.Exists(full))
            return false;

        SetRealDirectory(full);
        return true;
    }

    // worth doing for the Desktop directory, This PC and the Recycle Bin are children of it and of no directory.
    private bool TryShellChild(string name)
    {
        if (!Location.EnterParsingName(CurrentDirectory))
            return false;

        if (!Location.EnterChild(name))
        {
            Location.Leave();
            return false;
        }

        SyncRealDirectory();
        return true;
    }

    // most namespace places are ordinary directories under another name,
    // and the process follows them there so that anything started from here has a real directory to run in.
    private void SyncRealDirectory()
    {
        var path = Location.FileSystemPath;
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return;

        var previous = CurrentDirectory;
        Environment.CurrentDirectory = path;
        PreviousDirectory = previous;
        RecordDrive(Environment.CurrentDirectory);
    }

    private void SetRealDirectory(string full)
    {
        var previous = CurrentDirectory;
        Environment.CurrentDirectory = full;
        PreviousDirectory = previous;
        RecordDrive(Environment.CurrentDirectory);
        Location.Leave();
    }

    // where "d:" typed on its own should land.
    public string DirectoryOnDrive(char drive)
    {
        drive = char.ToUpperInvariant(drive);
        if (_driveDirectories.TryGetValue(drive, out var directory))
            return directory;

        return drive + ":\\";
    }

    private void RecordDrive(string directory)
    {
        if (directory.Length >= 2 && directory[1] == ':')
        {
            _driveDirectories[char.ToUpperInvariant(directory[0])] = directory;
        }
    }

    // the per drive directories go in under the names cmd uses, so a cmd.exe started from here agrees about D:.
    public string BuildEnvironmentBlock()
    {
        var entries = new List<string>(_variables.Count + _driveDirectories.Count);
        foreach (var variable in _variables)
        {
            entries.Add(variable.Key + "=" + variable.Value);
        }

        foreach (var drive in _driveDirectories)
        {
            entries.Add("=" + drive.Key + ":=" + drive.Value);
        }

        // CreateProcess wants the block sorted, and the hidden "=X:" names have to sort ahead of the rest.
        entries.Sort(StringComparer.OrdinalIgnoreCase);

        var builder = new StringBuilder();
        foreach (var entry in entries)
        {
            builder.Append(entry).Append('\0');
        }

        builder.Append('\0');
        return builder.ToString();
    }

    // cmd's $ codes, so an existing PROMPT setting keeps working.
    public string FormatPrompt()
    {
        var format = Prompt;
        var builder = new StringBuilder();
        for (var i = 0; i < format.Length; i++)
        {
            if (format[i] != '$' || i + 1 >= format.Length)
            {
                builder.Append(format[i]);
                continue;
            }

            i++;
            builder.Append(ExpandPromptCode(format[i]));
        }

        return builder.ToString();
    }

    private string ExpandPromptCode(char code) => char.ToUpperInvariant(code) switch
    {
        'A' => "&",
        'B' => "|",
        'C' => "(",
        'D' => DateTime.Now.ToString("d", CultureInfo.CurrentCulture),
        'E' => _escape.ToString(),
        'F' => ")",
        'G' => ">",
        'L' => "<",
        'N' => CurrentDirectory.Length > 0 ? CurrentDirectory[..1] : "?",
        'P' => Location.IsVirtual ? Location.Path : CurrentDirectory,
        'Q' => "=",
        'S' => " ",
        'T' => DateTime.Now.ToString("T", CultureInfo.CurrentCulture),
        'V' => Environment.OSVersion.Version.ToString(),
        '_' => Environment.NewLine,
        '$' => "$",
        _ => "$" + code,
    };
}
