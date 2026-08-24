namespace Tabsh;

internal static class NavigationCommands
{
    public static int ChangeDirectory(BuiltinContext context)
    {
        var arguments = context.Arguments.Where(a => !a.StartsWith('/')).ToList();
        if (arguments.Count == 0)
        {
            context.Output.WriteLine(Where(context));
            return 0;
        }

        // "cd -" goes back to wherever the last one of these came from, which cmd has never had and everyone wants.
        var target = arguments[0] == "-" ? context.Environment.PreviousDirectory : arguments[0];
        try
        {
            context.Environment.ChangeDirectory(target);
            return 0;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return context.Fail(exception.Message);
        }
    }

    public static int PushDirectory(BuiltinContext context)
    {
        if (context.Arguments.Count == 0)
        {
            foreach (var entry in context.Environment.DirectoryStack)
            {
                context.Output.WriteLine(entry);
            }

            return 0;
        }

        var current = context.Environment.CurrentDirectory;
        try
        {
            context.Environment.ChangeDirectory(context.Arguments[0]);
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return context.Fail(exception.Message);
        }

        context.Environment.DirectoryStack.Push(current);
        return 0;
    }

    public static int PopDirectory(BuiltinContext context)
    {
        if (context.Environment.DirectoryStack.Count == 0)
            return 0;

        var target = context.Environment.DirectoryStack.Pop();
        try
        {
            context.Environment.ChangeDirectory(target);
            return 0;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return context.Fail(exception.Message);
        }
    }

    public static int PrintDirectory(BuiltinContext context)
    {
        context.Output.WriteLine(Where(context));
        return 0;
    }

    // the same answer the prompt gives, because two names for where you are standing is one too many.
    private static string Where(BuiltinContext context) =>
        context.Environment.Location.IsVirtual ? context.Environment.Location.Path : context.Environment.CurrentDirectory;
}
