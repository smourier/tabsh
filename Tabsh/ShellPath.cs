namespace Tabsh;

// three or more dots is TCC's shorthand for going up that many levels less one,
// and every typed path comes through here because Windows normalisation would strip it back to a plain "..".
internal static class ShellPath
{
    public static string Resolve(string path, string baseDirectory) => Path.GetFullPath(Expand(path), baseDirectory);

    public static string Expand(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!path.Contains("...", StringComparison.Ordinal))
            return path;

        var builder = new StringBuilder(path.Length + 8);
        var start = 0;
        for (var i = 0; i <= path.Length; i++)
        {
            if (i < path.Length && path[i] != '\\' && path[i] != '/')
                continue;

            var segment = path[start..i];
            if (IsDotRun(segment) && segment.Length >= 3)
            {
                for (var level = 1; level < segment.Length; level++)
                {
                    if (level > 1)
                    {
                        builder.Append('\\');
                    }

                    builder.Append("..");
                }
            }
            else
            {
                builder.Append(segment);
            }

            if (i < path.Length)
            {
                builder.Append(path[i]);
            }

            start = i + 1;
        }

        return builder.ToString();
    }

    public static bool IsDotRun(string segment)
    {
        if (segment.Length == 0)
            return false;

        foreach (var c in segment)
        {
            if (c != '.')
                return false;
        }

        return true;
    }
}
