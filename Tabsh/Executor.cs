namespace Tabsh;

// runs what the parser produced.
internal sealed class Executor(Shell shell)
{
    // what cmd returns for a name it could not resolve, and what scripts check for.
    // the children this shell is waiting on, so that an interrupt arriving on another thread can find them.
    private readonly HashSet<ChildProcess> _running = [];

    private const int _commandNotFound = 9009;
    private const int _failure = 1;
    private const string _nulDevice = @"\\.\NUL";

    public int Execute(SequenceNode sequence, StandardHandles handles)
    {
        ArgumentNullException.ThrowIfNull(sequence);

        var code = shell.Environment.LastExitCode;
        foreach (var item in sequence.Items)
        {
            if (item.Operator == SequenceOperator.OnSuccess && code != 0)
                continue;

            if (item.Operator == SequenceOperator.OnFailure && code == 0)
                continue;

            code = ExecutePipeline(item.Pipeline, handles);
            shell.Environment.LastExitCode = code;

            if (shell.ExitRequested)
                break;
        }

        return code;
    }

    // a thread per stage, each owning the pipe ends it was handed.
    // Running the stages in order on this thread deadlocks as soon as one writes more than a pipe buffer holds.
    private int ExecutePipeline(PipelineNode pipeline, StandardHandles handles)
    {
        var count = pipeline.Commands.Count;
        if (count == 1)
            return ExecuteCommand(pipeline.Commands[0], handles);

        var pipes = new List<NativePipe>(count - 1);
        var codes = new int[count];
        var threads = new Thread[count];
        try
        {
            for (var i = 0; i < count - 1; i++)
            {
                pipes.Add(new NativePipe());
            }

            for (var i = 0; i < count; i++)
            {
                var index = i;
                var stage = new StandardHandles(
                    index == 0 ? handles.Input : pipes[index - 1].ReadEnd,
                    index == count - 1 ? handles.Output : pipes[index].WriteEnd,
                    handles.Error);

                threads[index] = new Thread(() =>
                {
                    try
                    {
                        codes[index] = ExecuteCommand(pipeline.Commands[index], stage);
                    }
                    catch (Exception exception)
                    {
                        WriteError(handles, exception.Message);
                        codes[index] = _failure;
                    }
                    finally
                    {
                        // the reader only sees end of file once every copy of the write end is gone.
                        if (index > 0)
                        {
                            pipes[index - 1].CloseReadEnd();
                        }

                        if (index < pipes.Count)
                        {
                            pipes[index].CloseWriteEnd();
                        }
                    }
                })
                { IsBackground = true, Name = "tabsh pipeline stage " + index.ToString(CultureInfo.InvariantCulture) };

                threads[index].Start();
            }

            foreach (var thread in threads)
            {
                thread.Join();
            }
        }
        finally
        {
            foreach (var pipe in pipes)
            {
                pipe.Dispose();
            }
        }

        // a pipeline is worth the exit code of its last stage, as everywhere else.
        return codes[count - 1];
    }

    private int ExecuteCommand(CommandNode node, StandardHandles handles)
    {
        var opened = new List<FileStream>();
        try
        {
            var effective = ApplyRedirections(node, handles, opened);
            if (node is CommandGroup group)
                return Execute(group.Body, effective);

            return ExecuteSimple((SimpleCommand)node, effective);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or Win32Exception)
        {
            WriteError(handles, exception.Message);
            return _failure;
        }
        finally
        {
            foreach (var stream in opened)
            {
                stream.Dispose();
            }
        }
    }

    private static StandardHandles ApplyRedirections(CommandNode node, StandardHandles handles, List<FileStream> opened)
    {
        foreach (var redirection in node.Redirections)
        {
            if (redirection.Kind == RedirectionKind.Duplicate)
            {
                var source = redirection.Target switch
                {
                    "0" => handles.Input,
                    "1" => handles.Output,
                    "2" => handles.Error,
                    _ => throw new ArgumentException(string.Format(CultureInfo.CurrentCulture, Res.NotAFileDescriptor, redirection.Target)),
                };

                handles = Assign(handles, redirection.FileDescriptor, source);
                continue;
            }

            var path = redirection.Target;
            if (string.Equals(path, "nul", StringComparison.OrdinalIgnoreCase))
            {
                path = _nulDevice;
            }
            else
            {
                path = ShellPath.Expand(path);
            }

            var stream = redirection.Kind switch
            {
                RedirectionKind.Input => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete),
                RedirectionKind.Append => new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete),
                _ => new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete),
            };

            opened.Add(stream);
            handles = Assign(handles, redirection.FileDescriptor, stream.SafeFileHandle.DangerousGetHandle());
        }

        return handles;
    }

    private static StandardHandles Assign(StandardHandles handles, int fileDescriptor, nint handle) => fileDescriptor switch
    {
        0 => handles.WithInput(handle),
        2 => handles.WithError(handle),
        _ => handles.WithOutput(handle),
    };

    private int ExecuteSimple(SimpleCommand command, StandardHandles handles)
    {
        // a line that was nothing but a redirection has already done its work by opening the file.
        if (command.Words.Count == 0)
            return 0;

        // aliases were already substituted into the line before it was parsed, so the words are final by now.
        var words = command.Words;
        var name = words[0];

        // "d:" on its own goes back to where we last were on that drive,
        // which is the one piece of cmd's model Windows does not keep for us.
        if (name.Length == 2 && name[1] == ':' && char.IsAsciiLetter(name[0]) && words.Count == 1)
        {
            shell.Environment.ChangeDirectory(name);
            return 0;
        }

        var builtin = shell.Builtins.Find(name);
        if (builtin != null)
            return shell.Builtins.Run(builtin, words, handles);

        var resolved = CommandResolver.Resolve(shell.Environment, words, command.RawWords);
        if (resolved.Kind == ResolvedCommandKind.NotFound)
        {
            // "cd\" only means the cd command once nothing else has claimed the word.
            // Trying it last rather than splitting at parse time means a real cd.exe, were there one, would still win.
            var attached = shell.Builtins.FindAttached(name, out var argument);
            if (attached != null)
                return shell.Builtins.Run(attached, Reattach(attached.Name, argument, words), handles);

            WriteError(handles, string.Format(CultureInfo.CurrentCulture, Res.CommandNotRecognized, name));
            return _commandNotFound;
        }

        return RunResolved(resolved, handles);
    }

    private static List<string> Reattach(string name, string argument, IReadOnlyList<string> words)
    {
        var separated = new List<string>(words.Count + 1) { name, argument };
        for (var i = 1; i < words.Count; i++)
        {
            separated.Add(words[i]);
        }

        return separated;
    }

    private int RunResolved(ResolvedCommand resolved, StandardHandles handles)
    {

        if (resolved.Kind == ResolvedCommandKind.Document)
        {
            // opening a document is not something to wait on, the editor that takes it may already be running.
            ShellExecutor.Execute(resolved.Path, resolved.Arguments, shell.Environment.CurrentDirectory, null)?.Dispose();
            return 0;
        }

        Console.Out.Flush();

        // handed over in a known state,
        // or a child that read keys raw and exited without restoring processed input takes Ctrl+C from the next one.
        ConsoleSession.Normalize();

        try
        {
            using var child = ProcessLauncher.Start(
                resolved.CommandLine,
                shell.Environment.CurrentDirectory,
                shell.Environment.BuildEnvironmentBlock(),
                handles,
                StandardStreams.IsRedirected(handles),
                newConsole: false);

            lock (_running)
            {
                _running.Add(child);
            }

            try
            {
                return child.Wait();
            }
            finally
            {
                lock (_running)
                {
                    _running.Remove(child);
                    if (child.Usage != null)
                    {
                        _measured?.Add(child.Usage);
                    }
                }
            }
        }
        finally
        {
            // and taken back in one, whether the child tidied up after itself or was interrupted before it could.
            ConsoleSession.Normalize();
        }
    }

    // where to put what each child cost, for as long as someone is asking.
    // Null the rest of the time, so nothing accumulates in a shell that has been running for a week.
    private List<ResourceUsage>? _measured;

    public IDisposable Measure(List<ResourceUsage> into)
    {
        lock (_running)
        {
            _measured = into;
        }

        return new Measurement(this);
    }

    private sealed class Measurement(Executor executor) : IDisposable
    {
        public void Dispose()
        {
            lock (executor._running)
            {
                executor._measured = null;
            }
        }
    }

    // whatever this shell is waiting on right now.
    // A pipeline has one per stage and an interrupt is aimed at all of them, so the caller gets the whole set.
    public IReadOnlyList<ChildProcess> RunningChildren()
    {
        lock (_running)
        {
            return [.. _running];
        }
    }

    private static void WriteError(StandardHandles handles, string message)
    {
        var writer = StandardStreams.CreateWriter(handles.Error);
        try
        {
            writer.WriteLine(message);
        }
        finally
        {
            StandardStreams.Release(writer);
        }
    }
}
