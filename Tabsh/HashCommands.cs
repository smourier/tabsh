using System.Security.Cryptography;

namespace Tabsh;

internal static class HashCommands
{
    private const string _defaultAlgorithm = "SHA256";
    private const string _hexFormat = "HEX";
    private const string _base64Format = "BASE64";
    private static readonly string[] _algorithms = ["MD5", "SHA1", "SHA256", "SHA384", "SHA512", "SHA3-256", "SHA3-384", "SHA3-512"];

    public static int Compute(BuiltinContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var algorithm = _defaultAlgorithm;
        var format = _hexFormat;
        var upper = false;
        var text = false;
        var inputs = new List<string>();

        foreach (var argument in context.Arguments)
        {
            if (!argument.StartsWith('/'))
            {
                inputs.Add(argument);
                continue;
            }

            var option = argument[1..];
            switch (char.ToUpperInvariant(option.Length > 0 ? option[0] : ' '))
            {
                case 'A':
                    algorithm = option[1..].TrimStart(':').ToUpperInvariant();
                    if (!_algorithms.Contains(algorithm, StringComparer.Ordinal))
                        return context.Fail(string.Format(CultureInfo.CurrentCulture, Res.InvalidAlgorithm, algorithm, string.Join(' ', _algorithms)));

                    break;

                case 'F':
                    format = option[1..].TrimStart(':').ToUpperInvariant();
                    if (format != _hexFormat && format != _base64Format)
                        return context.Fail(string.Format(CultureInfo.CurrentCulture, Res.InvalidFormat, format));

                    break;

                case 'U':
                    upper = true;
                    break;

                case 'T':
                    text = true;
                    break;

                default:
                    return context.Fail(string.Format(CultureInfo.CurrentCulture, Res.InvalidSwitch, argument));
            }
        }

        if (inputs.Count == 0)
            return context.Fail(Res.NameExpected);

        var code = 0;
        foreach (var input in inputs)
        {
            code = Write(context, input, algorithm, format, upper, text) != 0 ? 1 : code;
        }

        return code;
    }

    private static int Write(BuiltinContext context, string input, string algorithm, string format, bool upper, bool text)
    {
        byte[]? hash;
        try
        {
            hash = text ? Compute(algorithm, Encoding.UTF8.GetBytes(input)) : Compute(context, algorithm, input);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return context.Fail(exception.Message);
        }

        if (hash == null)
            return context.Fail(string.Format(CultureInfo.CurrentCulture, Res.AlgorithmNotSupported, algorithm));

        if (format == _base64Format)
        {
            context.Output.WriteLine(Convert.ToBase64String(hash));
            return 0;
        }

        context.Output.WriteLine(upper ? Convert.ToHexString(hash) : Convert.ToHexStringLower(hash));
        return 0;
    }

    // a name that is a file on disk is hashed as the file, which is what verifying a download needs.
    // Anything else is the text itself, and /t says so outright for a name that happens to exist.
    private static byte[]? Compute(BuiltinContext context, string algorithm, string input)
    {
        string full;
        try
        {
            full = ShellPath.Resolve(input, context.Environment.CurrentDirectory);
        }
        catch (ArgumentException)
        {
            return Compute(algorithm, Encoding.UTF8.GetBytes(input));
        }

        if (!File.Exists(full))
            return Compute(algorithm, Encoding.UTF8.GetBytes(input));

        using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Compute(algorithm, stream);
    }

    private static byte[]? Compute(string algorithm, byte[] bytes) => algorithm switch
    {
        "MD5" => MD5.HashData(bytes),
        "SHA1" => SHA1.HashData(bytes),
        "SHA256" => SHA256.HashData(bytes),
        "SHA384" => SHA384.HashData(bytes),
        "SHA512" => SHA512.HashData(bytes),
        "SHA3-256" => SHA3_256.IsSupported ? SHA3_256.HashData(bytes) : null,
        "SHA3-384" => SHA3_384.IsSupported ? SHA3_384.HashData(bytes) : null,
        "SHA3-512" => SHA3_512.IsSupported ? SHA3_512.HashData(bytes) : null,
        _ => null,
    };

    private static byte[]? Compute(string algorithm, Stream stream) => algorithm switch
    {
        "MD5" => MD5.HashData(stream),
        "SHA1" => SHA1.HashData(stream),
        "SHA256" => SHA256.HashData(stream),
        "SHA384" => SHA384.HashData(stream),
        "SHA512" => SHA512.HashData(stream),
        "SHA3-256" => SHA3_256.IsSupported ? SHA3_256.HashData(stream) : null,
        "SHA3-384" => SHA3_384.IsSupported ? SHA3_384.HashData(stream) : null,
        "SHA3-512" => SHA3_512.IsSupported ? SHA3_512.HashData(stream) : null,
        _ => null,
    };
}
