namespace Tabsh;

internal static partial class FileCommands
{
    public static int List(BuiltinContext context)
    {
        var options = new ListOptions();
        var targets = new List<string>();

        foreach (var argument in context.Arguments)
        {
            if (!argument.StartsWith('/'))
            {
                targets.Add(argument);
                continue;
            }

            var option = argument[1..];
            switch (char.ToUpperInvariant(option.Length > 0 ? option[0] : ' '))
            {
                case 'B':
                    options.Bare = true;
                    break;

                case 'S':
                    options.Recurse = true;
                    break;

                case 'A':
                    options.Attributes = option[1..].TrimStart(':');
                    options.All = true;
                    break;

                case 'T':
                    var field = option[1..].TrimStart(':');
                    options.TimeField = field.Length > 0 ? char.ToUpperInvariant(field[0]) : 'W';
                    break;

                case 'W':
                    options.Wide = true;
                    break;

                case 'D':
                    options.Wide = true;
                    options.DownColumns = true;
                    break;

                case 'P':
                    options.Pause = true;
                    break;

                case 'L':
                    options.Lower = true;
                    break;

                case 'Q':
                    options.Owner = true;
                    break;

                case 'X':
                    options.ShortNames = true;
                    break;

                // thousand separators, the long list format and four digit years are what this already does.
                case 'C':
                case 'N':
                case '4':
                    break;

                case 'R':
                    options.Streams = true;
                    break;

                case 'O':
                    options.Order = option[1..].TrimStart(':');
                    break;

                default:
                    return context.Fail(string.Format(CultureInfo.CurrentCulture, Res.InvalidSwitch, argument));
            }
        }

        // standing somewhere in the shell namespace,
        // a dir with nothing named is a listing of that, not of the directory the process is still sitting in.
        if (targets.Count == 0 && context.Environment.Location.IsVirtual)
            return ListShell(context, context.Environment.Location, options);

        if (targets.Count == 0)
        {
            targets.Add(".");
        }

        var code = 0;
        foreach (var target in targets)
        {
            var listed = Resolve(context, target) is ShellLocation location
                ? ListShell(context, location, options)
                : ListTarget(context, target, options);

            code = listed != 0 ? 1 : code;
        }

        return code;
    }

    // the namespace folder a target names, or null when the file system should answer instead.
    // A bare name is read in the namespace only while standing in it, so a wildcard still reaches the file system.
    private static ShellLocation? Resolve(BuiltinContext context, string target)
    {
        var location = context.Environment.Location;
        if (target.StartsWith(_namespaceRoot))
            return Entered(new ShellLocation(), target[_namespaceRoot.Length..].TrimStart(':'), fromRoot: true);

        if (!location.IsVirtual || Path.IsPathFullyQualified(target))
            return null;

        var fromRoot = target.Length > 0 && (target[0] == Path.DirectorySeparatorChar || target[0] == Path.AltDirectorySeparatorChar);
        return Entered(new ShellLocation(location), target, fromRoot);
    }

    private static ShellLocation? Entered(ShellLocation location, string path, bool fromRoot) => location.EnterPath(path, fromRoot) ? location : null;

    // a shell folder has names and little else,
    // and a size or free space means nothing for This PC, so those columns are left empty rather than filled with a nought.
    private static int ListShell(BuiltinContext context, ShellLocation location, ListOptions options)
    {
        var children = location.Children();
        children.Sort((a, b) =>
        {
            var byKind = b.IsFolder.CompareTo(a.IsFolder);
            return byKind != 0 ? byKind : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
        });

        if (options.Bare)
        {
            foreach (var child in children)
            {
                context.Output.WriteLine(child.Name);
            }

            return 0;
        }

        context.Output.WriteLine();
        context.Output.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.DirectoryOf, location.Path));
        context.Output.WriteLine();

        foreach (var child in children)
        {
            var date = child.Modified?.ToString("dd/MM/yyyy  HH:mm", CultureInfo.CurrentCulture) ?? string.Empty;
            var size = Marker(child) is string marker
                ? (_markerIndent + marker).PadRight(_sizeWidth)
                : child.Size.ToString("N0", CultureInfo.CurrentCulture).PadLeft(_sizeWidth);

            context.Output.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.ShellEntryLine, date, size, child.Name));
        }

        context.Output.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.ShellItemCount, children.Count));
        return 0;
    }

    // null when the entry has a real size to put there. Not everything in a shell folder is a folder or a file,
    // the desktop holds a Control Panel that only opens and reports no SFGAO_FOLDER.
    private static string? Marker(ShellChild child)
    {
        if (child.IsFolder)
            return Res.DirectoryMarker;

        return child.Size < 0 ? Res.ItemMarker : null;
    }

    private static int ListTarget(BuiltinContext context, string target, ListOptions options)
    {
        string full;
        try
        {
            full = ShellPath.Resolve(target, context.Environment.CurrentDirectory);
        }
        catch (ArgumentException exception)
        {
            return context.Fail(exception.Message);
        }

        string directory;
        string pattern;
        if (Directory.Exists(full))
        {
            directory = full;
            pattern = "*";
        }
        else
        {
            directory = Path.GetDirectoryName(full) ?? full;
            pattern = Path.GetFileName(full);
            if (pattern.Length == 0)
            {
                pattern = "*";
            }
        }

        if (!Directory.Exists(directory))
            return context.Fail(Res.FileNotFoundShort);

        return ListDirectory(context, directory, pattern, options);
    }

    private static int ListDirectory(BuiltinContext context, string directory, string pattern, ListOptions options)
    {
        List<FileSystemInfo> entries;
        try
        {
            entries = [.. new DirectoryInfo(directory).EnumerateFileSystemInfos(pattern).Where(e => Selected(e, options))];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return context.Fail(exception.Message);
        }

        Sort(entries, options.Order, options.TimeField);

        var pager = new ScreenPager(context, options.Pause);
        if (options.Bare)
        {
            foreach (var entry in entries)
            {
                pager.WriteLine(Cased(options.Recurse ? entry.FullName : entry.Name, options));
            }
        }
        else if (options.Wide)
        {
            WriteHeader(context, directory);
            WriteWide(pager, entries, options);
            WriteSummary(context, entries, directory);
        }
        else
        {
            WriteHeader(context, directory);

            foreach (var entry in entries)
            {
                var isDirectory = (entry.Attributes & FileAttributes.Directory) != 0;
                var size = Marker(entry, isDirectory);
                pager.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.DirectoryEntryLine, Stamp(entry, options.TimeField), size, Decorate(entry, options)));

                if (options.Streams)
                {
                    WriteStreams(context, entry);
                }
            }

            WriteSummary(context, entries, directory);
        }

        if (!options.Recurse)
            return 0;

        foreach (var child in SafeSubdirectories(directory))
        {
            ListDirectory(context, child, pattern, options);
        }

        return 0;
    }

    private static void WriteSummary(BuiltinContext context, List<FileSystemInfo> entries, string directory)
    {
        long bytes = 0;
        var files = 0;
        var directories = 0;
        foreach (var entry in entries)
        {
            if ((entry.Attributes & FileAttributes.Directory) != 0)
            {
                directories++;
            }
            else
            {
                files++;
                bytes += entry is FileInfo counted ? counted.Length : 0;
            }
        }

        context.Output.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.FileCountSummary, files, bytes));
        context.Output.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.DirectoryCountSummary, directories, FreeSpace(directory)));
    }

    // /w and /d, names in columns with a directory in brackets. /w reads across the rows, /d down the columns.
    private static void WriteWide(ScreenPager pager, List<FileSystemInfo> entries, ListOptions options)
    {
        if (entries.Count == 0)
            return;

        var names = new List<string>();
        foreach (var entry in entries)
        {
            var name = Cased(entry.Name, options);
            names.Add((entry.Attributes & FileAttributes.Directory) != 0 ? string.Format(CultureInfo.CurrentCulture, Res.WideDirectory, name) : name);
        }

        var widest = 0;
        foreach (var name in names)
        {
            widest = Math.Max(widest, name.Length);
        }

        var width = Math.Max(Console.IsOutputRedirected ? _defaultWidth : Console.WindowWidth, widest + _wideGap + 1);
        var columns = Math.Max(1, (width - 1) / (widest + _wideGap));
        var rows = (names.Count + columns - 1) / columns;

        for (var row = 0; row < rows; row++)
        {
            var line = new StringBuilder();
            for (var column = 0; column < columns; column++)
            {
                var index = options.DownColumns ? column * rows + row : row * columns + column;
                if (index >= names.Count)
                    continue;

                line.Append(names[index].PadRight(widest + _wideGap));
            }

            pager.WriteLine(line.ToString().TrimEnd());
        }
    }

    // cmd's /a selectors, with D for a directory on top of the file attributes del understands.
    private static bool Selected(FileSystemInfo entry, ListOptions options)
    {
        if (options.Attributes.Length > 0)
            return HasAttributes(entry.Attributes, options.Attributes);

        return options.All || (entry.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0;
    }

    private static DateTime Stamp(FileSystemInfo entry, char field) => field switch
    {
        'C' => entry.CreationTime,
        'A' => entry.LastAccessTime,
        _ => entry.LastWriteTime,
    };

    private static string Cased(string text, ListOptions options) => options.Lower ? text.ToLower(CultureInfo.CurrentCulture) : text;

    // the name, and whatever the switches asked to be shown beside it.
    private static string Decorate(FileSystemInfo entry, ListOptions options)
    {
        var name = Cased(Name(entry), options);
        if (options.ShortNames)
        {
            name = string.Format(CultureInfo.CurrentCulture, Res.ShortNameLine, ShortName(entry.FullName), name);
        }

        if (options.Owner)
        {
            name = string.Format(CultureInfo.CurrentCulture, Res.OwnerLine, OwnerOf(entry.FullName), name);
        }

        return name;
    }

    private const int _defaultWidth = 80;
    private const int _wideGap = 2;

    // the size column, or what stands in for it: a directory says so, and a reparse point says which kind it is,
    // because a junction and a symbolic link are not the same thing however alike they look here.
    private static string Marker(FileSystemInfo entry, bool isDirectory)
    {
        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            var marker = FileLink.KindOf(entry.FullName) switch
            {
                FileLinkKind.Junction => Res.JunctionMarker,
                FileLinkKind.Symlink => isDirectory ? Res.SymlinkDirectoryMarker : Res.SymlinkMarker,
                _ => isDirectory ? Res.DirectoryMarker : null,
            };

            if (marker != null)
                return (_markerIndent + marker).PadRight(_sizeWidth);
        }

        if (isDirectory)
            return (_markerIndent + Res.DirectoryMarker).PadRight(_sizeWidth);

        return (entry is FileInfo file ? file.Length : 0).ToString("N0", CultureInfo.CurrentCulture).PadLeft(_sizeWidth);
    }

    // a link with nothing to say about where it points is a link you have to go and look up.
    private static string Name(FileSystemInfo entry)
    {
        var target = entry.LinkTarget;
        if (string.IsNullOrEmpty(target))
            return entry.Name;

        return entry.Name + string.Format(CultureInfo.CurrentCulture, Res.LinkTargetSuffix, target);
    }

    private static void WriteStreams(BuiltinContext context, FileSystemInfo entry)
    {
        // a directory has streams of its own and they are worth the same look,
        // but walking into a reparse point to find them would be walking somewhere else entirely.
        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            return;

        foreach (var stream in FileStreams.Of(entry.FullName))
        {
            context.Output.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.StreamLine, stream.Size, entry.Name + stream.Name));
        }
    }

    private const string _namespaceRoot = "@";
    private const int _sizeWidth = 19;
    private const string _markerIndent = "    ";

    private static void WriteHeader(BuiltinContext context, string directory)
    {
        context.Output.WriteLine();
        var root = Path.GetPathRoot(directory);
        if (!string.IsNullOrEmpty(root) && root.Length >= 2 && root[1] == ':')
        {
            try
            {
                var drive = new DriveInfo(root);
                context.Output.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.VolumeInDrive, root[0], drive.VolumeLabel));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // an unlabelled or unavailable volume is not worth a message.
            }
        }

        context.Output.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.DirectoryOf, directory));
        context.Output.WriteLine();
    }

    private static long FreeSpace(string directory)
    {
        try
        {
            var root = Path.GetPathRoot(directory);
            return string.IsNullOrEmpty(root) ? 0 : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return 0;
        }
    }

    private static IEnumerable<string> SafeSubdirectories(string directory)
    {
        try
        {
            return Directory.EnumerateDirectories(directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static void Sort(List<FileSystemInfo> entries, string order, char timeField)
    {
        var descending = order.StartsWith('-');
        var key = descending ? order[1..] : order;

        Comparison<FileSystemInfo> comparison = key.ToUpperInvariant() switch
        {
            "S" => (a, b) => SizeOf(a).CompareTo(SizeOf(b)),
            "D" => (a, b) => Stamp(a, timeField).CompareTo(Stamp(b, timeField)),
            "E" => (a, b) => string.Compare(a.Extension, b.Extension, StringComparison.OrdinalIgnoreCase),
            "N" => (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),

            // "G" and the default both put directories first, which is what makes a listing useful for getting around.
            _ => (a, b) =>
            {
                var byKind = IsDirectory(b).CompareTo(IsDirectory(a));
                return byKind != 0 ? byKind : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            },
        };

        entries.Sort(descending ? (a, b) => comparison(b, a) : comparison);
    }

    // the 8.3 name Windows keeps beside the long one, which is the only name some very old tools will take.
    private static string ShortName(string path)
    {
        var buffer = new char[_maximumPath];
        var length = GetShortPathNameW(path, buffer, (uint)buffer.Length);
        if (length == 0 || length > buffer.Length)
            return string.Empty;

        return Path.GetFileName(new string(buffer, 0, (int)length));
    }

    // the account the file belongs to, which the shell already knows and nothing else here has to work out.
    private static string OwnerOf(string path)
    {
        try
        {
            using var item = ShellItem.FromParsingName(path, throwOnError: false);
            // the fast store does not carry the owner, which is one of the values a handler has to be asked for.
            if (item != null && item.TryGetPropertyValue<string>(ShellN.PropertyKeys.System.FileOwner, ShellN.GETPROPERTYSTOREFLAGS.GPS_DEFAULT, out var owner))
                return owner ?? string.Empty;

            return string.Empty;
        }
        catch (Exception exception) when (exception is COMException or ArgumentException)
        {
            return string.Empty;
        }
    }

    private const int _maximumPath = 260;

    [LibraryImport("kernel32", EntryPoint = "GetShortPathNameW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetShortPathNameW(string lpszLongPath, [Out] char[] lpszShortPath, uint cchBuffer);

    private static bool IsDirectory(FileSystemInfo entry) => (entry.Attributes & FileAttributes.Directory) != 0;

    private static long SizeOf(FileSystemInfo entry) => entry is FileInfo file ? file.Length : 0;

    public static int Type(BuiltinContext context)
    {
        if (context.Arguments.Count == 0)
            return context.Fail(Res.SyntaxIncorrect);

        var code = 0;
        foreach (var argument in context.Arguments)
        {
            var matches = Expand(context.Environment, argument, includeDirectories: false);
            if (matches.Count == 0)
            {
                code = context.Fail(string.Format(CultureInfo.CurrentCulture, Res.FileNotFound, argument));
                continue;
            }

            foreach (var match in matches)
            {
                if (matches.Count > 1)
                {
                    context.Output.WriteLine();
                    context.Output.WriteLine(Path.GetFileName(match));
                    context.Output.WriteLine();
                }

                try
                {
                    using var reader = new StreamReader(match, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        context.Output.WriteLine(line);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    code = context.Fail(exception.Message);
                }
            }
        }

        return code;
    }

    public static int Copy(BuiltinContext context) => Transfer(context, move: false);

    public static int Move(BuiltinContext context) => Transfer(context, move: true);

    private static int Transfer(BuiltinContext context, bool move)
    {
        var overwrite = (bool?)null;
        var arguments = new List<string>();
        foreach (var argument in context.Arguments)
        {
            if (!argument.StartsWith('/'))
            {
                arguments.Add(argument);
                continue;
            }

            if (string.Equals(argument, _noOverwriteSwitch, StringComparison.OrdinalIgnoreCase))
            {
                overwrite = false;
                continue;
            }

            switch (char.ToUpperInvariant(argument.Length > 1 ? argument[1] : ' '))
            {
                case 'Y':
                    overwrite = true;
                    break;

                // what cmd does with these is either a hint or a thing this copy already does.
                case 'A':
                case 'B':
                case 'D':
                case 'L':
                case 'N':
                case 'V':
                case 'Z':
                    break;

                default:
                    return context.Fail(string.Format(CultureInfo.CurrentCulture, Res.InvalidSwitch, argument));
            }
        }

        if (arguments.Count < 2)
            return context.Fail(Res.SyntaxIncorrect);

        // "copy a+b c" joins the sources into the destination, which is the one thing copy does that move does not.
        if (!move && arguments.Count == 2 && arguments[0].Contains('+', StringComparison.Ordinal))
            return Join(context, arguments[0], arguments[1], overwrite);

        var destination = ShellPath.Resolve(arguments[^1], context.Environment.CurrentDirectory);
        var intoDirectory = Directory.Exists(destination) || arguments.Count > 2;
        var count = 0;

        for (var i = 0; i < arguments.Count - 1; i++)
        {
            var matches = Expand(context.Environment, arguments[i], includeDirectories: move);
            if (matches.Count == 0)
                return context.Fail(string.Format(CultureInfo.CurrentCulture, Res.FileNotFound, arguments[i]));

            foreach (var source in matches)
            {
                var target = intoDirectory ? Path.Combine(destination, Path.GetFileName(source)) : destination;
                if (!Allowed(context, target, overwrite))
                    continue;

                try
                {
                    if (move)
                    {
                        if (Directory.Exists(source))
                        {
                            Directory.Move(source, target);
                        }
                        else
                        {
                            File.Move(source, target, overwrite: true);
                        }
                    }
                    else
                    {
                        File.Copy(source, target, overwrite: true);
                    }

                    count++;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    return context.Fail(exception.Message);
                }
            }
        }

        context.Output.WriteLine(string.Format(CultureInfo.CurrentCulture, move ? Res.FilesMoved : Res.FilesCopied, count));
        return 0;
    }

    public static int Rename(BuiltinContext context)
    {
        if (context.Arguments.Count < 2)
            return context.Fail(Res.SyntaxIncorrect);

        var matches = Expand(context.Environment, context.Arguments[0], includeDirectories: true);
        if (matches.Count == 0)
            return context.Fail(string.Format(CultureInfo.CurrentCulture, Res.FileNotFound, context.Arguments[0]));

        // the second argument of a rename is a name, never a path, which is the one thing that separates it from move.
        var pattern = Path.GetFileName(context.Arguments[1]);
        var code = 0;
        foreach (var source in matches)
        {
            var directory = Path.GetDirectoryName(source);
            if (string.IsNullOrEmpty(directory))
            {
                code = context.Fail(Res.SyntaxIncorrect);
                continue;
            }

            var target = Path.Combine(directory, Rewrite(Path.GetFileName(source), pattern));
            try
            {
                if (Directory.Exists(source))
                {
                    Directory.Move(source, target);
                }
                else
                {
                    File.Move(source, target);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                code = context.Fail(exception.Message);
            }
        }

        return code;
    }

    // "*.txt" to "*.bak" and its like, where a "*" in the new name takes the rest of that half of the old name,
    // and a "?" takes one character of it. The stem and the extension are matched apart, which is how cmd reads them.
    private static string Rewrite(string name, string pattern)
    {
        if (pattern.IndexOfAny(['*', '?']) < 0)
            return pattern;

        var dot = pattern.LastIndexOf('.');
        var nameDot = name.LastIndexOf('.');
        var stem = Apply(dot < 0 ? name : name[..(nameDot < 0 ? name.Length : nameDot)], dot < 0 ? pattern : pattern[..dot]);
        if (dot < 0)
            return stem;

        var extension = Apply(nameDot < 0 ? string.Empty : name[(nameDot + 1)..], pattern[(dot + 1)..]);
        return extension.Length == 0 ? stem : stem + "." + extension;
    }

    private static string Apply(string source, string pattern)
    {
        var built = new StringBuilder();
        var at = 0;
        foreach (var c in pattern)
        {
            if (c == '*')
            {
                built.Append(source.AsSpan(Math.Min(at, source.Length)));
                at = source.Length;
                continue;
            }

            if (c == '?')
            {
                if (at < source.Length)
                {
                    built.Append(source[at]);
                    at++;
                }

                continue;
            }

            built.Append(c);
            if (at < source.Length)
            {
                at++;
            }
        }

        return built.ToString();
    }

    public static int Delete(BuiltinContext context)
    {
        var force = false;
        var recurse = false;
        var prompt = false;
        var quiet = false;
        string? attributes = null;
        var patterns = new List<string>();
        foreach (var argument in context.Arguments)
        {
            if (!argument.StartsWith('/'))
            {
                patterns.Add(argument);
                continue;
            }

            switch (char.ToUpperInvariant(argument.Length > 1 ? argument[1] : ' '))
            {
                case 'F':
                    force = true;
                    break;

                case 'S':
                    recurse = true;
                    break;

                case 'P':
                    prompt = true;
                    break;

                case 'Q':
                    quiet = true;
                    break;

                case 'A':
                    attributes = argument[2..].TrimStart(':');
                    break;

                default:
                    return context.Fail(string.Format(CultureInfo.CurrentCulture, Res.InvalidSwitch, argument));
            }
        }

        if (patterns.Count == 0)
            return context.Fail(Res.SyntaxIncorrect);

        var code = 0;
        foreach (var pattern in patterns)
        {
            var matches = Expand(context.Environment, pattern, includeDirectories: false);
            if (recurse)
            {
                matches.AddRange(ExpandRecursive(context.Environment, pattern));
            }

            if (matches.Count == 0)
            {
                code = context.Fail(string.Format(CultureInfo.CurrentCulture, Res.CouldNotFind, pattern));
                continue;
            }

            // a pattern that names everything is the one worth asking about, which is the question cmd asks.
            if (!quiet && IsEverything(pattern) && !Agreed(context, string.Format(CultureInfo.CurrentCulture, Res.DeleteEverything, pattern)))
                continue;

            foreach (var match in matches)
            {
                if (attributes != null && !HasAttributes(match, attributes))
                    continue;

                if (prompt && !Agreed(context, string.Format(CultureInfo.CurrentCulture, Res.DeleteFile, match)))
                    continue;

                try
                {
                    if (force)
                    {
                        File.SetAttributes(match, FileAttributes.Normal);
                    }

                    File.Delete(match);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    code = context.Fail(exception.Message);
                }
            }
        }

        return code;
    }

    public static int MakeDirectory(BuiltinContext context)
    {
        if (context.Arguments.Count == 0)
            return context.Fail(Res.SyntaxIncorrect);

        foreach (var argument in context.Arguments)
        {
            try
            {
                Directory.CreateDirectory(ShellPath.Resolve(argument, context.Environment.CurrentDirectory));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return context.Fail(exception.Message);
            }
        }

        return 0;
    }

    public static int RemoveDirectory(BuiltinContext context)
    {
        var recurse = false;
        var quiet = false;
        var targets = new List<string>();
        foreach (var argument in context.Arguments)
        {
            if (!argument.StartsWith('/'))
            {
                targets.Add(argument);
                continue;
            }

            switch (char.ToUpperInvariant(argument.Length > 1 ? argument[1] : ' '))
            {
                case 'S':
                    recurse = true;
                    break;

                case 'Q':
                    quiet = true;
                    break;

                default:
                    return context.Fail(string.Format(CultureInfo.CurrentCulture, Res.InvalidSwitch, argument));
            }
        }

        if (targets.Count == 0)
            return context.Fail(Res.SyntaxIncorrect);

        foreach (var target in targets)
        {
            // a tree goes with everything under it, which is the one thing cmd stops to ask about.
            if (recurse && !quiet && !Agreed(context, string.Format(CultureInfo.CurrentCulture, Res.RemoveTree, target)))
                continue;

            var path = ShellPath.Resolve(target, context.Environment.CurrentDirectory);
            try
            {
                if (recurse)
                {
                    DeleteTree(path);
                }
                else
                {
                    Writable(path);
                    Directory.Delete(path);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return context.Fail(exception.Message);
            }
        }

        return 0;
    }

    // cmd's rd /s takes a read only file with it rather than stopping on one, and Directory.Delete does not.
    // Git marks its pack and commit graph files read only, so deleting a repository stopped at the first of them.
    private static void DeleteTree(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            Writable(file);
            File.Delete(file);
        }

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            // a link is unlinked and never walked into, or removing a junction would empty what it points at.
            if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
            {
                Writable(child);
                Directory.Delete(child);
                continue;
            }

            DeleteTree(child);
        }

        Writable(directory);
        Directory.Delete(directory);
    }

    private static void Writable(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReadOnly) != 0)
        {
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        }
    }

    private static int Join(BuiltinContext context, string sources, string destination, bool? overwrite)
    {
        var target = ShellPath.Resolve(destination, context.Environment.CurrentDirectory);
        if (!Allowed(context, target, overwrite))
            return 0;

        var parts = new List<string>();
        foreach (var source in sources.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var matches = Expand(context.Environment, source, includeDirectories: false);
            if (matches.Count == 0)
                return context.Fail(string.Format(CultureInfo.CurrentCulture, Res.FileNotFound, source));

            parts.AddRange(matches);
        }

        try
        {
            // written to a new file rather than appended to, since one of the parts may be the destination itself.
            var joined = Path.GetTempFileName();
            using (var output = new FileStream(joined, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                foreach (var part in parts)
                {
                    using var input = new FileStream(part, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    input.CopyTo(output);
                }
            }

            File.Move(joined, target, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return context.Fail(exception.Message);
        }

        context.Output.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.FilesCopied, parts.Count));
        return 0;
    }

    // cmd asks before it overwrites a file or removes a tree, and a shell that does not ask loses work cmd would keep.
    // With no console there is nobody to ask, which is the case cmd itself documents as going ahead.
    private static bool Agreed(BuiltinContext context, string question)
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected || !context.WritesToConsole)
            return true;

        context.Output.Write(question);
        context.Output.Flush();

        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            var answer = key.KeyChar.ToString();
            if (string.Equals(answer, Res.YesKey, StringComparison.CurrentCultureIgnoreCase))
            {
                context.Output.WriteLine(Res.YesKey);
                return true;
            }

            if (string.Equals(answer, Res.NoKey, StringComparison.CurrentCultureIgnoreCase) || key.Key is ConsoleKey.Escape or ConsoleKey.Enter)
            {
                context.Output.WriteLine(Res.NoKey);
                return false;
            }
        }
    }

    // an existing destination is the only one worth a question, and /y or /-y answers it in advance.
    private static bool Allowed(BuiltinContext context, string target, bool? overwrite)
    {
        if (overwrite == true || !File.Exists(target))
            return true;

        if (overwrite == false)
            return false;

        return Agreed(context, string.Format(CultureInfo.CurrentCulture, Res.OverwriteFile, target));
    }

    // "*" and "*.*" are the patterns that mean the lot, which is what cmd stops for.
    private static bool IsEverything(string pattern)
    {
        var name = Path.GetFileName(pattern);
        return name is "*" or "*.*";
    }

    private static bool HasAttributes(string path, string selectors)
    {
        try
        {
            return HasAttributes(File.GetAttributes(path), selectors);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    // cmd's selectors, D R H A S I L O, each of which may be turned around by a leading "-".
    private static bool HasAttributes(FileAttributes actual, string selectors)
    {
        var negate = false;
        foreach (var selector in selectors)
        {
            if (selector == '-')
            {
                negate = true;
                continue;
            }

            var wanted = char.ToUpperInvariant(selector) switch
            {
                'D' => FileAttributes.Directory,
                'R' => FileAttributes.ReadOnly,
                'S' => FileAttributes.System,
                'H' => FileAttributes.Hidden,
                'A' => FileAttributes.Archive,
                'I' => FileAttributes.NotContentIndexed,
                'L' => FileAttributes.ReparsePoint,
                'O' => FileAttributes.Offline,
                _ => (FileAttributes)0,
            };

            if (wanted != 0 && (actual & wanted) != 0 == negate)
                return false;

            negate = false;
        }

        return true;
    }

    private const string _noOverwriteSwitch = "/-y";

    private static List<string> Expand(ShellEnvironment environment, string pattern, bool includeDirectories)
    {
        string full;
        try
        {
            full = ShellPath.Resolve(pattern, environment.CurrentDirectory);
        }
        catch (ArgumentException)
        {
            return [];
        }

        if (File.Exists(full))
            return [full];

        if (includeDirectories && Directory.Exists(full))
            return [full];

        var directory = Path.GetDirectoryName(full);
        var name = Path.GetFileName(full);
        if (string.IsNullOrEmpty(directory) || name.Length == 0 || !Directory.Exists(directory))
            return [];

        try
        {
            return includeDirectories
                ? [.. Directory.EnumerateFileSystemEntries(directory, name)]
                : [.. Directory.EnumerateFiles(directory, name)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static List<string> ExpandRecursive(ShellEnvironment environment, string pattern)
    {
        var full = ShellPath.Resolve(pattern, environment.CurrentDirectory);
        var directory = Path.GetDirectoryName(full);
        var name = Path.GetFileName(full);
        if (string.IsNullOrEmpty(directory) || name.Length == 0 || !Directory.Exists(directory))
            return [];

        var found = new List<string>();
        foreach (var child in SafeSubdirectories(directory))
        {
            try
            {
                found.AddRange(Directory.EnumerateFiles(child, name, SearchOption.AllDirectories));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // continue, an unreadable subtree does not stop the rest.
            }
        }

        return found;
    }
}
