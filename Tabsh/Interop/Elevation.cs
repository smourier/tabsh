namespace Tabsh.Interop;

// whether this shell is running elevated, which the window title is the natural place to say.
// Asked of the process token rather than through WindowsIdentity, which drags a whole security stack into the binary.
internal static unsafe class Elevation
{
    public static bool IsElevated()
    {
        if (!Functions.OpenProcessToken(Functions.GetCurrentProcess(), TOKEN_ACCESS_MASK.TOKEN_QUERY, out var token))
            return false;

        try
        {
            uint elevated;
            return Functions.GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenElevation, (nint)(&elevated), sizeof(uint), out _) && elevated != 0;
        }
        finally
        {
            Functions.CloseHandle(token);
        }
    }
}
