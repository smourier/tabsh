using DirectN.Extensions.Utilities;

namespace Tabsh;

// the property store behind Explorer's Details tab, which no command line has ever been able to read.
internal static class PropertyCommands
{
    private const string _namespaceRoot = "@";
    private const int _nameWidth = 40;
    private const string _indent = "  ";

    // not localized on purpose, every other category is a name out of the property system.
    private const string _uncategorized = "Unspecified";

    private static readonly char[] _separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];
    private static readonly char[] _wildcards = ['*', '?'];
    private static readonly PROPERTYKEY[] _hiddenKeys =
    [
        // redundant or useless.
        ShellN.PropertyKeys.System.SFGAOFlags,
        ShellN.PropertyKeys.System.Security.AllowedEnterpriseDataProtectionIdentities,
        ShellN.PropertyKeys.System.Document.DateCreated,
        ShellN.PropertyKeys.System.Document.DateSaved,
        ShellN.PropertyKeys.System.ItemTypeText,
    ];

    public static int Properties(BuiltinContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // with nothing named, the thing being asked about is where we are standing.
        var targets = context.Arguments.Count == 0 ? [_currentPlace] : context.Arguments;

        var code = 0;
        foreach (var target in targets)
        {
            code = Describe(context, target) != 0 ? 1 : code;
        }

        return code;
    }

    private const string _currentPlace = ".";

    private static int Describe(BuiltinContext context, string target)
    {
        var found = Expand(context, target);
        if (found.Count == 0)
        {
            var wildcard = target.IndexOfAny(_wildcards) >= 0;
            return context.Fail(string.Format(CultureInfo.CurrentCulture, wildcard ? Res.NoMatch : Res.PathNotFound, target));
        }

        var code = 0;
        foreach (var one in found)
        {
            try
            {
                code = Describe(context, one, target) != 0 ? 1 : code;
            }
            finally
            {
                if (one.Owned)
                {
                    one.Item.Dispose();
                }
            }
        }

        return code;
    }

    private static int Describe(BuiltinContext context, Target found, string target)
    {
        var item = found.Item;

        context.Output.WriteLine();
        context.Output.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.PropertiesOf, found.Name));
        context.Output.WriteLine();

        // the default store, the fast one leaves out everything a handler has to open the file to answer.
        item.NativeObject.GetPropertyStoreWithCreateObject(ShellN.GETPROPERTYSTOREFLAGS.GPS_DEFAULT, 0, typeof(IPropertyStore).GUID, out var ppv);
        using var store = DirectN.Extensions.Com.ComObject.FromPointer<IPropertyStore>(ppv);
        if (store == null)
            return context.Fail(string.Format(CultureInfo.CurrentCulture, Res.NoProperties, target));

        // grouped by the last name of a key's namespace, so System.GPS.Latitude sits under GPS.
        var categories = new SortedDictionary<string, SortedDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        store.Object.GetCount(out var count);
        for (uint i = 0; i < count; i++)
        {
            store.Object.GetAt(i, out var key);
            if (store.Object.GetValue(key, out var pvValue).IsError || pvValue.Anonymous.Anonymous.vt == VARENUM.VT_EMPTY)
                continue;

            if (_hiddenKeys.Contains(key))
                continue;

            using var pv = PropVariant.Attach(ref pvValue);

            string name;
            string category;
            string value;
            using var ps = key.ToDescription();
            if (ps is not null)
            {
                if (!ps.TypeFlags.HasFlag(PROPDESC_TYPE_FLAGS.PDTF_ISVIEWABLE))
                    continue;

                name = ps.CanonicalName ?? key.ToString();
                category = CategoryOf(ps.Namespace);
                ps.NativeObject.FormatForDisplay(pv.Detached, PROPDESC_FORMAT_FLAGS.PDFF_DEFAULT, out var display);
                value = display.Value == 0 ? pv.ToString() : display.ToStringAndDispose()!;
            }
            else
            {
                name = key.ToString();
                category = _uncategorized;
                value = pv.ToString();
            }

            if (!categories.TryGetValue(category, out var values))
            {
                values = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                categories.Add(category, values);
            }

            values[name] = value;
        }

        var written = 0;
        foreach (var category in Ordered(categories))
        {
            context.Output.WriteLine(category.Key);
            foreach (var pair in category.Value)
            {
                context.Output.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.PropertyLine, (_indent + pair.Key).PadRight(_nameWidth), pair.Value));
                written++;
            }

            context.Output.WriteLine();
        }

        context.Output.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.PropertyCount, written));
        return 0;
    }

    // a key with a single word canonical name, or none at all, belongs nowhere in particular.
    private static string CategoryOf(string? nameSpace)
    {
        if (string.IsNullOrEmpty(nameSpace))
            return _uncategorized;

        var dot = nameSpace.LastIndexOf('.');
        var last = dot < 0 ? nameSpace : nameSpace[(dot + 1)..];
        return last.Length == 0 ? _uncategorized : last;
    }

    // sorted by name, except that the properties nobody named come last whatever the alphabet says.
    private static IEnumerable<KeyValuePair<string, SortedDictionary<string, string>>> Ordered(SortedDictionary<string, SortedDictionary<string, string>> categories)
    {
        foreach (var category in categories)
        {
            if (category.Key != _uncategorized)
                yield return category;
        }

        if (categories.TryGetValue(_uncategorized, out var last))
            yield return new KeyValuePair<string, SortedDictionary<string, string>>(_uncategorized, last);
    }

    // every item a target names, which is more than one when it was written as a pattern.
    // Only the last segment may hold a wildcard, the way Windows has always read a name.
    private static List<Target> Expand(BuiltinContext context, string target)
    {
        if (target.IndexOfAny(_wildcards) < 0)
        {
            var single = Open(context, target);
            return single == null ? [] : [single];
        }

        var found = new List<Target>();
        var absolute = target.StartsWith(_namespaceRoot, StringComparison.Ordinal);
        var path = absolute ? target[_namespaceRoot.Length..].TrimStart(':') : target;

        var separator = path.LastIndexOfAny(_separators);
        var below = separator >= 0 ? path[..separator] : string.Empty;
        var pattern = separator >= 0 ? path[(separator + 1)..] : path;
        if (below.IndexOfAny(_wildcards) >= 0)
            return found;

        var current = context.Environment.Location;
        if (absolute || (current.IsVirtual && !Path.IsPathFullyQualified(path)))
        {
            var location = absolute ? new ShellLocation() : new ShellLocation(current);
            var fromRoot = absolute || (path.Length > 0 && _separators.Contains(path[0]));
            if (location.EnterPath(below, fromRoot))
            {
                foreach (var child in location.Children())
                {
                    if (!System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, child.Name, ignoreCase: true))
                        continue;

                    var item = ShellItem.FromParsingName(child.ParsingName, throwOnError: false);
                    if (item != null)
                    {
                        found.Add(new Target(item, owned: true, Path.Combine(location.Path, child.Name)));
                    }
                }
            }

            // "@" means the namespace and nothing else, anything else falls through to the file system.
            if (absolute || found.Count > 0)
                return Sorted(found);
        }

        string directory;
        try
        {
            directory = below.Length == 0 ? context.Environment.CurrentDirectory : ShellPath.Resolve(below, context.Environment.CurrentDirectory);
        }
        catch (ArgumentException)
        {
            return found;
        }

        try
        {
            foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos(pattern))
            {
                var item = ShellItem.FromParsingName(entry.FullName, throwOnError: false);
                if (item != null)
                {
                    found.Add(new Target(item, owned: true, entry.FullName));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // a directory that will not be listed has nothing to describe, and neither has a pattern Windows refuses.
        }

        return Sorted(found);
    }

    private static List<Target> Sorted(List<Target> found)
    {
        found.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return found;
    }

    // read the way cd and dir read a name, except that this also ends on things that are not folders,
    // so a name matching no folder is looked for among the children of the folder above it.
    private static Target? Open(BuiltinContext context, string target)
    {
        if (target.StartsWith(_namespaceRoot, StringComparison.Ordinal))
            return FromNamespace(new ShellLocation(), target[_namespaceRoot.Length..].TrimStart(':'), fromRoot: true);

        var location = context.Environment.Location;
        if (location.IsVirtual && !Path.IsPathFullyQualified(target))
        {
            var fromRoot = target.Length > 0 && _separators.Contains(target[0]);
            var found = FromNamespace(new ShellLocation(location), target, fromRoot);
            if (found != null)
                return found;
        }

        string full;
        try
        {
            full = ShellPath.Resolve(target, context.Environment.CurrentDirectory);
        }
        catch (ArgumentException)
        {
            return null;
        }

        var item = ShellItem.FromParsingName(full, throwOnError: false);
        return item == null ? null : new Target(item, owned: true, full);
    }

    private static Target? FromNamespace(ShellLocation location, string path, bool fromRoot)
    {
        // the root answers as the Desktop,
        // so it is named for the namespace place it is rather than for the directory it reports.
        if (location.EnterPath(path, fromRoot))
        {
            var folder = location.Open(out var owned);
            return folder == null ? null : new Target(folder, owned, location.Path);
        }

        var separator = path.LastIndexOfAny(_separators);
        var below = separator >= 0 ? path[..separator] : string.Empty;
        var leaf = separator >= 0 ? path[(separator + 1)..] : path;
        if (leaf.Length == 0 || !location.EnterPath(below, fromRoot))
            return null;

        foreach (var child in location.Children())
        {
            if (!string.Equals(child.Name, leaf, StringComparison.CurrentCultureIgnoreCase))
                continue;

            var item = ShellItem.FromParsingName(child.ParsingName, throwOnError: false);
            return item == null ? null : new Target(item, owned: true, Path.Combine(location.Path, child.Name));
        }

        return null;
    }

    private sealed class Target(ShellItem item, bool owned, string name)
    {
        public ShellItem Item { get; } = item;

        // the Desktop is shared and must not be disposed.
        public bool Owned { get; } = owned;

        // for a place in the namespace this is the "@:" path, not the directory it sits on.
        public string Name { get; } = name;
    }
}
