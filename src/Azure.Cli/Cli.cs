namespace Azure;

/// <summary>
/// Resolves the Payload's <c>az</c> executable copied next to the app as <c>az/bin/az</c>.
/// </summary>
public static class Cli
{
    /// <summary>
    /// Returns the full path to <c>az.cmd</c> (Windows) or <c>az</c> under
    /// <paramref name="baseDirectory"/>/<c>az/bin</c>. Defaults to <see cref="AppContext.BaseDirectory"/>.
    /// </summary>
    public static string ResolvePath(string? baseDirectory = null)
    {
        var dir = baseDirectory ?? AppContext.BaseDirectory;
        var name = OperatingSystem.IsWindows() ? "az.cmd" : "az";
        var path = Path.GetFullPath(Path.Combine(dir, "az", "bin", name));
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Azure CLI payload was not found at '{path}'. PackageReference azx.cli and publish/pack for your RID.",
                path);
        }

        if (!OperatingSystem.IsWindows())
            EnsureUnixExecuteBits(path);

        return path;
    }

    // NuGet restore does not honor zip Unix modes; chmod on first resolve.
    internal static void EnsureUnixExecuteBits(string azPath)
    {
        AddExecute(azPath);
        var pythonBin = Path.GetFullPath(Path.Combine(azPath, "..", "..", "python", "bin"));
        if (!Directory.Exists(pythonBin))
            return;

        foreach (var file in Directory.EnumerateFiles(pythonBin))
            AddExecute(file);
    }

    static void AddExecute(string path)
    {
        if (OperatingSystem.IsWindows())
            return;

        var mode = File.GetUnixFileMode(path);
        const UnixFileMode exec = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        if ((mode & UnixFileMode.UserExecute) != 0)
            return;

        File.SetUnixFileMode(path, mode | exec);
    }
}
