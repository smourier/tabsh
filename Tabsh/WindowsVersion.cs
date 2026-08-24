namespace Tabsh;

// read from the registry rather than from Environment.OSVersion, which reports what the manifest allows,
// knows nothing of the update build revision and cannot tell an edition. Windows 7 has almost none of these values.
internal static class WindowsVersion
{
    private const string _currentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    // the Feature Experience Pack without going near the packaging APIs.
    // The per user repository keeps every version ever installed, which is why the highest one wins there.
    private const string _inboxApplicationsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Appx\AppxAllUserStore\InboxApplications";
    private const string _packageRepositoryKey = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";
    private const string _experiencePackage = "MicrosoftWindows.Client.CBS";
    private const string _experienceName = "Windows Feature Experience Pack";

    // ProductName was frozen at "Windows 10" when 11 shipped,
    // so the build number is the only value that tells the two apart.
    private const int _firstWindows11Build = 22000;

    private const int _labelWidth = 17;

    public static void Write(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        using var key = Open(RegistryHive.LocalMachine, _currentVersionKey);
        writer.WriteLine(Describe(key));
        writer.WriteLine();

        foreach (var row in Specifications(key))
        {
            writer.WriteLine(string.Format(CultureInfo.CurrentCulture, Res.SpecificationLine, row.Key.PadRight(_labelWidth), row.Value));
        }
    }

    // the rows Settings shows, in its order and under its labels.
    private static IEnumerable<KeyValuePair<string, string>> Specifications(RegistryKey? key)
    {
        var edition = Edition(key);
        if (edition != null)
            yield return new KeyValuePair<string, string>(Res.LabelEdition, edition);

        // DisplayVersion only exists from Windows 10 2004 on, before that the same idea was ReleaseId.
        var release = (key?.GetValue("DisplayVersion") ?? key?.GetValue("ReleaseId")) as string;
        if (!string.IsNullOrEmpty(release))
            yield return new KeyValuePair<string, string>(Res.LabelVersion, release);

        var installed = InstalledOn(key);
        if (installed != null)
            yield return new KeyValuePair<string, string>(Res.LabelInstalledOn, installed);

        var build = Build(key);
        if (build != null)
            yield return new KeyValuePair<string, string>(Res.LabelOsBuild, build);

        var experience = ExperiencePack();
        if (experience != null)
            yield return new KeyValuePair<string, string>(Res.LabelExperience, experience);

        yield return new KeyValuePair<string, string>(Res.LabelSystemType, SystemType());
    }

    // the line cmd's own ver prints, assembled rather than read, because no single value holds it.
    private static string Describe(RegistryKey? key)
    {
        var reported = Environment.OSVersion.Version;
        var major = ReadInt32(key, "CurrentMajorVersionNumber");
        var minor = ReadInt32(key, "CurrentMinorVersionNumber");

        // Windows 8.1 and older have neither number, their version is a string like "6.1" in CurrentVersion.
        if (major == null && key?.GetValue("CurrentVersion") is string legacy)
        {
            var parts = legacy.Split('.');
            if (parts.Length == 2 &&
                int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var legacyMajor) &&
                int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var legacyMinor))
            {
                major = legacyMajor;
                minor = legacyMinor;
            }
        }

        var version = new StringBuilder()
            .Append(major ?? reported.Major)
            .Append('.')
            .Append(minor ?? reported.Minor)
            .Append('.')
            .Append(BuildNumber(key) ?? reported.Build);

        var revision = ReadInt32(key, "UBR");
        if (revision != null)
        {
            version.Append('.').Append(revision.Value);
        }

        return string.Format(CultureInfo.CurrentCulture, Res.WindowsHeadline, Edition(key) ?? Res.WindowsUnknown, version);
    }

    private static string? Edition(RegistryKey? key)
    {
        if (key?.GetValue("ProductName") is not string name || name.Length == 0)
            return null;

        if ((BuildNumber(key) ?? Environment.OSVersion.Version.Build) >= _firstWindows11Build)
        {
            name = name.Replace("Windows 10", "Windows 11", StringComparison.OrdinalIgnoreCase);
        }

        // where a service pack still exists it is part of the name, the way Windows 7 always presented it.
        if (key?.GetValue("CSDVersion") is string servicePack && servicePack.Length > 0)
        {
            name += " " + servicePack;
        }

        return name;
    }

    // Settings shows the build and the update build revision together, as "26200.9168", and no major or minor.
    private static string? Build(RegistryKey? key)
    {
        var build = BuildNumber(key);
        if (build == null)
            return null;

        var revision = ReadInt32(key, "UBR");
        return revision == null
            ? build.Value.ToString(CultureInfo.CurrentCulture)
            : $"{build.Value}.{revision.Value}";
    }

    // a date with no time, which is what Settings shows and all InstallDate is good for anyway.
    private static string? InstalledOn(RegistryKey? key)
    {
        if (key?.GetValue("InstallDate") is not int seconds)
            return null;

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime.ToString("d", CultureInfo.CurrentCulture);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string? ExperiencePack()
    {
        var version = HighestPackageVersion(RegistryHive.LocalMachine, _inboxApplicationsKey)
            ?? HighestPackageVersion(RegistryHive.CurrentUser, _packageRepositoryKey);

        return version == null ? null : _experienceName + " " + version;
    }

    private static string? HighestPackageVersion(RegistryHive hive, string path)
    {
        using var key = Open(hive, path);
        if (key == null)
            return null;

        string[] names;
        try
        {
            names = key.GetSubKeyNames();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        Version? highest = null;
        string? highestText = null;
        foreach (var name in names)
        {
            // a package full name is Name_Version_Architecture__PublisherId.
            if (!name.StartsWith(_experiencePackage + "_", StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = name.Split('_');
            if (parts.Length < 2 || !Version.TryParse(parts[1], out var version))
                continue;

            if (highest == null || version > highest)
            {
                highest = version;
                highestText = parts[1];
            }
        }

        return highestText;
    }

    private static string SystemType()
    {
        var bits = Environment.Is64BitOperatingSystem ? Res.OperatingSystem64 : Res.OperatingSystem32;
        var processor = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => Res.ProcessorX64,
            Architecture.X86 => Res.ProcessorX86,
            Architecture.Arm64 => Res.ProcessorArm64,
            Architecture.Arm => Res.ProcessorArm,
            _ => string.Format(CultureInfo.CurrentCulture, Res.ProcessorOther, RuntimeInformation.OSArchitecture),
        };

        var text = string.Format(CultureInfo.CurrentCulture, Res.SystemTypeAndProcessor, bits, processor);

        // a 32 bit process on a 64 bit Windows is given a different System32,
        // a different PATH and a different registry than the one described here.
        if (RuntimeInformation.ProcessArchitecture != RuntimeInformation.OSArchitecture)
        {
            text += string.Format(CultureInfo.CurrentCulture, Res.ProcessArchitectureNote, RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant());
        }

        return text;
    }

    // left to itself a 32 bit build reads the Wow6432Node copy, a stale subset written at install time.
    private static RegistryKey? Open(RegistryHive hive, string path)
    {
        try
        {
            var view = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default;
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            return baseKey.OpenSubKey(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    private static int? ReadInt32(RegistryKey? key, string name) => key?.GetValue(name) is int value ? value : null;

    private static int? BuildNumber(RegistryKey? key)
    {
        foreach (var name in new[] { "CurrentBuildNumber", "CurrentBuild" })
        {
            if (key?.GetValue(name) is string text &&
                int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var build))
                return build;
        }

        return null;
    }
}
