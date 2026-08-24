namespace Tabsh;

// This PC and the Recycle Bin are folders to Explorer and nothing at all to SetCurrentDirectory,
// so the shell keeps this beside the process directory. A parsing name, not a COM item held across every prompt.
internal sealed class ShellLocation
{
    // the Desktop is the root of the namespace, and the one folder ShellN owns rather than us.
    private const string _root = "";

    // the namespace written as though it were a drive, so that a place in it looks like somewhere rather than a mode.
    private const string _rootPath = "@:\\";

    private static readonly char[] _separators = [System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar];

    private string? _parsingName;

    // the parsing name says what a place is, this says how it was reached.
    private readonly List<string> _segments = [];

    public ShellLocation()
    {
    }

    // a copy, for asking about somewhere else without going there. dir names a folder, it does not enter it.
    public ShellLocation(ShellLocation other)
    {
        ArgumentNullException.ThrowIfNull(other);
        _parsingName = other._parsingName;
        _segments.AddRange(other._segments);
    }

    public bool IsVirtual => _parsingName != null;

    // the root shares a path with the Desktop directory but not its children, so it is never traded for that path.
    public bool IsRoot => _parsingName == _root;

    // back to being an ordinary directory, which is what happens the moment cd lands on a real one.
    public void Leave() => _parsingName = null;

    public void EnterRoot()
    {
        _parsingName = _root;
        _segments.Clear();
    }

    // written the way a drive is written, so the prompt can be copied back into a cd.
    public string Path
    {
        get
        {
            var path = new StringBuilder(_rootPath);
            for (var i = 0; i < _segments.Count; i++)
            {
                if (i > 0)
                {
                    path.Append(System.IO.Path.DirectorySeparatorChar);
                }

                path.Append(_segments[i]);
            }

            return path.ToString();
        }
    }

    // nothing moves unless every step works, so a wrong name leaves you where you were rather than half way down.
    public bool EnterPath(string path, bool fromRoot)
    {
        var savedName = _parsingName;
        var savedSegments = new List<string>(_segments);

        if (fromRoot)
        {
            EnterRoot();
        }

        foreach (var segment in path.Split(_separators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
                continue;

            var moved = segment == ".." ? GoUp() : EnterChild(segment);
            if (!moved)
            {
                _parsingName = savedName;
                _segments.Clear();
                _segments.AddRange(savedSegments);
                return false;
            }
        }

        return true;
    }

    // for a name typed absolutely, "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}" and its like.
    public bool EnterParsingName(string parsingName)
    {
        using var item = ShellItem.FromParsingName(parsingName, throwOnError: false);
        if (item == null || !item.IsFolder)
            return false;

        _parsingName = item.SIGDN_DESKTOPABSOLUTEPARSING ?? parsingName;
        RebuildSegments();
        return true;
    }

    // an absolute name says nothing about the way down to it, so the way down is read back off its parents.
    private void RebuildSegments()
    {
        _segments.Clear();

        var names = new List<string>();
        var folder = Open(out var owned);
        if (folder == null)
            return;

        try
        {
            var current = folder;
            var currentOwned = owned;
            while (current != null && !current.IsDesktop)
            {
                names.Add(current.SIGDN_NORMALDISPLAY ?? string.Empty);
                var parent = current.GetParent();
                if (currentOwned)
                {
                    current.Dispose();
                }

                current = parent as ShellFolder;
                currentOwned = true;
            }

            if (current != null && currentOwned)
            {
                current.Dispose();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or COMException)
        {
            // a place that will not name its parents simply has a shorter path shown for it.
        }

        names.Reverse();
        _segments.AddRange(names);
    }

    // matched on the name Explorer shows, which is the name a person would type.
    public bool EnterChild(string name)
    {
        foreach (var child in Children())
        {
            if (child.IsFolder && string.Equals(child.Name, name, StringComparison.CurrentCultureIgnoreCase))
            {
                _parsingName = child.ParsingName;
                _segments.Add(child.Name);
                return true;
            }
        }

        return false;
    }

    // up one, and off the top of the namespace is simply the Desktop again.
    public bool GoUp()
    {
        if (_parsingName == _root)
            return false;

        var folder = Open(out var owned);
        if (folder == null)
            return false;

        try
        {
            using var parent = folder.GetParent();
            _parsingName = parent == null || parent.IsDesktop ? _root : parent.SIGDN_DESKTOPABSOLUTEPARSING ?? _root;
            if (_segments.Count > 0)
            {
                _segments.RemoveAt(_segments.Count - 1);
            }

            return true;
        }
        finally
        {
            if (owned)
            {
                folder.Dispose();
            }
        }
    }

    // what the prompt shows, which is the name rather than the GUID wherever there is one.
    public string Display
    {
        get
        {
            var folder = Open(out var owned);
            if (folder == null)
                return _root;

            try
            {
                return folder.SIGDN_NORMALDISPLAY ?? _parsingName ?? _root;
            }
            finally
            {
                if (owned)
                {
                    folder.Dispose();
                }
            }
        }
    }

    // how a virtual place that turns out to be a real directory hands navigation back to the file system.
    public string? FileSystemPath
    {
        get
        {
            var folder = Open(out var owned);
            if (folder == null)
                return null;

            try
            {
                return folder.SIGDN_FILESYSPATH;
            }
            finally
            {
                if (owned)
                {
                    folder.Dispose();
                }
            }
        }
    }

    public List<ShellChild> Children()
    {
        var children = new List<ShellChild>();
        var folder = Open(out var owned);
        if (folder == null)
            return children;

        try
        {
            // no flags, so this is the shell's own default, folders and files and nothing hidden.
            foreach (var child in folder.EnumerateChildren())
            {
                using (child)
                {
                    children.Add(new ShellChild(
                        child.SIGDN_NORMALDISPLAY ?? string.Empty,
                        child.SIGDN_DESKTOPABSOLUTEPARSING ?? string.Empty,
                        child.IsFolder,
                        child.IsFolder ? -1 : child.Size ?? -1,
                        child.DateModified));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or COMException)
        {
            // a place that refuses to be listed is no reason to take the shell down with it.
        }
        finally
        {
            if (owned)
            {
                folder.Dispose();
            }
        }

        return children;
    }

    // the Desktop belongs to the library and must not be disposed, everything else is ours for the length of the call.
    public ShellFolder? Open(out bool owned)
    {
        if (_parsingName == null || _parsingName == _root)
        {
            owned = false;
            return ShellFolder.Desktop;
        }

        owned = true;
        return ShellItem.FromParsingName(_parsingName, throwOnError: false) as ShellFolder;
    }
}
