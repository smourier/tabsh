namespace Tabsh;

internal static class ShellCommands
{
    public static int Echo(BuiltinContext context)
    {
        var text = string.Join(' ', context.Arguments);
        context.Output.WriteLine(text == "." ? string.Empty : text);
        return 0;
    }

    public static int Set(BuiltinContext context)
    {
        if (context.Arguments.Count == 0)
        {
            foreach (var variable in context.Environment.Variables)
            {
                context.Output.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.NameValueLine, variable.Key, variable.Value));
            }

            return 0;
        }

        if (string.Equals(context.Arguments[0], "/p", StringComparison.OrdinalIgnoreCase))
            return Prompted(context);

        if (string.Equals(context.Arguments[0], "/a", StringComparison.OrdinalIgnoreCase))
            return Calculate(context);

        // the lexer split on spaces, so an assignment whose value contained one is put back together here.
        var text = string.Join(' ', context.Arguments);
        var equals = text.IndexOf('=');
        if (equals < 0)
        {
            var matches = context.Environment.Variables.Where(v => v.Key.StartsWith(text, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0)
                return context.Fail(string.Format(CultureInfo.CurrentCulture, Res.VariableNotDefined, text));

            foreach (var match in matches)
            {
                context.Output.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.NameValueLine, match.Key, match.Value));
            }

            return 0;
        }

        context.Environment.Set(text[..equals], text[(equals + 1)..]);
        return 0;
    }

    // set /a, which is the only place in the shell where a value is worked out rather than copied.
    private static int Calculate(BuiltinContext context)
    {
        var expression = string.Join(' ', context.Arguments.Skip(1));
        if (!new Arithmetic(context.Environment).TryEvaluate(expression, out var value))
            return context.Fail(Res.SyntaxIncorrect);

        // cmd writes the answer only when the expression was typed rather than run from a script,
        // and there are no scripts here, so it is always written.
        context.Output.WriteLine(value.ToString(CultureInfo.InvariantCulture));
        context.Environment.LastExitCode = value == 0 ? 1 : 0;
        return 0;
    }

    private static int Prompted(BuiltinContext context)
    {
        var text = string.Join(' ', context.Arguments.Skip(1));
        var equals = text.IndexOf('=');
        if (equals < 0)
            return context.Fail(Res.SyntaxIncorrect);

        context.Output.Write(text[(equals + 1)..]);
        context.Output.Flush();

        var value = context.Input.ReadLine();
        if (value == null)
            return 1;

        context.Environment.Set(text[..equals], value);
        return 0;
    }

    public static int Path(BuiltinContext context)
    {
        if (context.Arguments.Count == 0)
        {
            context.Output.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.PathLine, context.Environment.Get("PATH") ?? string.Empty));
            return 0;
        }

        var value = string.Join(' ', context.Arguments);

        // "path ;" is how cmd empties it, leaving only the current directory to search.
        context.Environment.Set("PATH", value == ";" ? string.Empty : value);
        return 0;
    }

    public static int Prompt(BuiltinContext context)
    {
        if (context.Arguments.Count == 0)
        {
            context.Output.WriteLine(context.Environment.Prompt);
            return 0;
        }

        context.Environment.Prompt = string.Join(' ', context.Arguments);
        return 0;
    }

    public static int Alias(BuiltinContext context)
    {
        if (context.Arguments.Count == 0)
        {
            foreach (var alias in context.Shell.Aliases.All)
            {
                context.Output.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.NameValueLine, alias.Key, alias.Value));
            }

            return 0;
        }

        var text = string.Join(' ', context.Arguments);
        var equals = text.IndexOf('=');
        if (equals < 0)
        {
            var body = context.Shell.Aliases.Get(text);
            if (body == null)
                return context.Fail(string.Format(CultureInfo.CurrentCulture, Res.NotAnAlias, text));

            context.Output.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.NameValueLine, text, body));
            return 0;
        }

        context.Shell.Aliases.Set(text[..equals], text[(equals + 1)..]);
        return 0;
    }

    public static int Exit(BuiltinContext context)
    {
        var code = 0;
        foreach (var argument in context.Arguments)
        {
            if (argument.StartsWith('/'))
                continue;

            if (int.TryParse(argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                code = parsed;
            }

            break;
        }

        context.Shell.RequestExit(code);
        return code;
    }

    public static int Clear(BuiltinContext context)
    {
        if (context.WritesToConsole)
        {
            Console.Clear();
        }

        return 0;
    }

    // read back off the token class itself, so a token added there turns up here without being written down twice.
    // read back off the token class and the formatter themselves, so nothing here is a list written down twice.
    // Both commands answer with the same one, because both are templates over the same words.
    public static string TokenHelp() =>
        string.Format(CultureInfo.CurrentCulture, Res.TokenList, string.Join(", ", TokenFormatter.Names<ShellTokens>())) +
        System.Environment.NewLine +
        string.Format(CultureInfo.CurrentCulture, Res.OperatorList, string.Join(", ", TokenFormatter.Operators)) +
        System.Environment.NewLine +
        Res.ComputedList;

    public static int Title(BuiltinContext context)
    {
        // shown rather than emptied when nothing is given, the way prompt does it, and the rendering underneath it,
        // since a window title is the one thing a shell writes that a script can never read back.
        if (context.Arguments.Count == 0)
        {
            context.Output.WriteLine(context.Environment.Title);
            context.Output.WriteLine(context.Environment.Render(context.Environment.Title));
            return 0;
        }

        context.Environment.Title = string.Join(' ', context.Arguments);
        context.Environment.ApplyTitle();
        return 0;
    }

    public static int TabColor(BuiltinContext context)
    {
        if (context.Arguments.Count == 0)
        {
            var current = context.Environment.TabColor;
            if (current.Length == 0)
            {
                context.Output.WriteLine(Res.TabColorNotSet);
                return 0;
            }

            context.Output.WriteLine(current);
            context.Output.WriteLine(context.Environment.Render(current));
            return 0;
        }

        var format = string.Join(' ', context.Arguments);
        var text = context.Environment.Render(format);

        // said here and once, since a template is rendered again at every prompt and complaining there would be noise.
        if (text.Length > 0 && !D3DCOLORVALUE.TryParseFromName(text, out _))
            return context.Fail(string.Format(CultureInfo.CurrentCulture, Res.ColourNotUnderstood, text));

        context.Environment.TabColor = format;
        context.Environment.ApplyTabColor();
        return 0;
    }

    public static int Color(BuiltinContext context)
    {
        if (context.Arguments.Count == 0)
        {
            Console.ResetColor();
            return 0;
        }

        var text = context.Arguments[0];
        if (text.Length != 2 || !TryParseColor(text[0], out var background) || !TryParseColor(text[1], out var foreground))
            return context.Fail(Res.ColourExpected);

        // the same colour twice would leave nothing readable, which cmd reports rather than does.
        if (background == foreground)
            return 1;

        Console.BackgroundColor = background;
        Console.ForegroundColor = foreground;
        return 0;
    }

    private static bool TryParseColor(char digit, out ConsoleColor color)
    {
        color = ConsoleColor.Black;
        if (!Uri.IsHexDigit(digit))
            return false;

        color = (ConsoleColor)Convert.ToInt32(digit.ToString(), 16);
        return true;
    }

    public static int Version(BuiltinContext context)
    {
        var assembly = Assembly.GetExecutingAssembly();
        context.Output.WriteLine(string.Format(
            CultureInfo.CurrentCulture,
            Res.ProductHeadline,
            assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company,
            assembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title,
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion));
        context.Output.WriteLine();
        WindowsVersion.Write(context.Output);
        return 0;
    }

    // what the console is set to. Colour arriving as escape sequences is a console mode question and nothing else,
    // and this is the answer to it.
    public static int ConsoleModes(BuiltinContext context)
    {
        ConsoleSession.Describe(context.Output);
        return 0;
    }

    public static int History(BuiltinContext context)
    {
        if (context.Arguments.Count > 0 && context.Arguments[0] is "-c" or "/c")
        {
            context.Shell.Editor.History.Clear();

            // written out at once rather than at the end of the session,
            // since a window closed with its cross never reaches the end of one and the history would come back.
            context.Shell.Editor.History.Save(Shell.HistoryPath);
            return 0;
        }

        var number = 1;
        foreach (var entry in context.Shell.Editor.History.Entries)
        {
            context.Output.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.HistoryLine, number, entry));
            number++;
        }

        return 0;
    }

    public static int Start(BuiltinContext context)
    {
        var arguments = new List<string>(context.Arguments);
        var wait = false;
        while (arguments.Count > 0 && arguments[0].StartsWith('/'))
        {
            if (string.Equals(arguments[0], "/wait", StringComparison.OrdinalIgnoreCase))
            {
                wait = true;
            }

            arguments.RemoveAt(0);
        }

        // cmd reads a first quoted word as the window title, which is why "start "" prog" is the usual spelling.
        // Only a word that arrived in quotes can be one, or a program named in quotes would be taken for a title.
        var consumed = context.Arguments.Count - arguments.Count;
        if (arguments.Count > 1 && consumed < context.RawArguments.Count && context.RawArguments[consumed].StartsWith('"'))
        {
            arguments.RemoveAt(0);
        }

        if (arguments.Count == 0)
        {
            arguments.Add(context.Environment.CurrentDirectory);
        }

        var resolved = CommandResolver.Resolve(context.Environment, arguments);
        try
        {
            if (resolved.Kind == ResolvedCommandKind.Executable)
            {
                using var child = ProcessLauncher.Start(
                    resolved.CommandLine,
                    context.Environment.CurrentDirectory,
                    context.Environment.BuildEnvironmentBlock(),
                    StandardHandles.FromConsole(),
                    redirected: false,
                    newConsole: true);

                return wait ? child.Wait() : 0;
            }

            // a directory, a document, or a name only the association database knows.
            var target = resolved.Kind == ResolvedCommandKind.Document ? resolved.Path : arguments[0];
            using var opened = ShellExecutor.Execute(target, resolved.Arguments, context.Environment.CurrentDirectory, null);
            return wait && opened != null ? opened.Wait() : 0;
        }
        catch (Win32Exception exception)
        {
            return context.Fail(exception.Message);
        }
    }

    // what TAB would offer for a line, in the order it would offer it.
    // Useful on its own, and it is the only way the completion can be checked without a person at the keyboard.
    public static int Complete(BuiltinContext context)
    {
        var line = string.Join(' ', context.Arguments);
        var session = context.Shell.Completer.Create(line, line.Length);
        if (session == null)
            return 1;

        foreach (var candidate in session.Candidates)
        {
            context.Output.WriteLine(candidate.Text);
        }

        return 0;
    }

    // runs a written key script through the real line editor and shows what the line came out as.
    // The other half of "complete": that one checks what TAB offers, this one checks what pressing it does.
    public static int Keys(BuiltinContext context)
    {
        var line = context.Shell.Editor.Simulate(KeyScript.Parse(context.Arguments), out var cursor);
        context.Output.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.KeysResult, line, cursor));
        return 0;
    }

    public static int Help(BuiltinContext context)
    {
        context.Output.WriteLine(Res.HelpHeader);
        context.Output.WriteLine();
        foreach (var builtin in context.Shell.Builtins.All)
        {
            context.Output.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.HelpLine, builtin.Name, builtin.Description));
        }

        context.Output.WriteLine();
        context.Output.WriteLine(Res.HelpTab);
        context.Output.WriteLine(Res.HelpShiftTab);
        context.Output.WriteLine(Res.HelpAutoCd);
        return 0;
    }
}
