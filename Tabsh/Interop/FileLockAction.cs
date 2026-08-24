namespace Tabsh.Interop;

internal enum FileLockAction
{
    // asks the application to close the way a shutdown would, which is the only one that lets it save anything.
    Close,

    // and starts it again afterwards, which Restart Manager can only do for what it knows how to relaunch.
    Restart,

    // no asking, no saving.
    Terminate,
}
