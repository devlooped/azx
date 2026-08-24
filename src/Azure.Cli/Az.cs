namespace Azure.Cli;

/// <summary>
/// Resolves the Payload's <c>az</c> executable copied next to the app as <c>az/bin/az</c>.
/// </summary>
public static class Az
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
                $"Azure CLI payload was not found at '{path}'. PackageReference Azure.Cli and publish/pack for your RID.",
                path);
        }

        return path;
    }
}
