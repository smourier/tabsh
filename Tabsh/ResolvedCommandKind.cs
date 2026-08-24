namespace Tabsh;

internal enum ResolvedCommandKind
{
    NotFound,

    // something CreateProcess can start, either the program itself or the interpreter that was chosen for it.
    Executable,

    // not a program at all, so the association database decides what opens it.
    Document,
}
