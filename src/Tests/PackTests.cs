using System.IO.Compression;
using System.Text.Json;

namespace Tests;

public class PackTests
{
    static readonly string[] SupportedRids =
    [
        "win-x64",
        "linux-x64",
        "linux-arm64",
        "osx-x64",
        "osx-arm64",
    ];

    [Fact]
    public void Pointer_and_rid_csproj_use_pack_split()
    {
        var repo = FindRepoRoot();
        var slnx = File.ReadAllText(Path.Combine(repo, "azx.slnx"));
        Assert.Contains("src/Azure.Cli/Azure.Cli.csproj", slnx);
        Assert.Contains("src/azx/azx.csproj", slnx);

        var azureCli = File.ReadAllText(Path.Combine(repo, "src", "Azure.Cli", "Azure.Cli.csproj"));
        Assert.Contains("<PackageId>azx.cli</PackageId>", azureCli);
        Assert.Contains("<AssemblyName>Azure.Cli</AssemblyName>", azureCli);
        Assert.Contains("<RootNamespace>Azure</RootNamespace>", azureCli);
        Assert.Contains("<RuntimeIdentifiers>win-x64;linux-x64;linux-arm64;osx-x64;osx-arm64</RuntimeIdentifiers>", azureCli);
        Assert.DoesNotContain("NuGetizer", azureCli, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<PackAsTool", azureCli);
        Assert.Contains("Azure.Cli.pack.targets", azureCli);
        Assert.Contains("buildTransitive\\azx.cli.targets", azureCli.Replace('/', '\\'));
        Assert.Contains("buildTransitive\\$(PackageId).targets", azureCli.Replace('/', '\\'));
        Assert.Contains("Readme", azureCli);
        Assert.DoesNotContain("win-arm64", azureCli);

        var payload = File.ReadAllText(Path.Combine(repo, "src", "Azure.Cli", "payload.ps1"));
        // PowerShell automatic OS variables are case-insensitive and read-only; assigning
        // $isLinux overwrites $IsLinux and fails the linux Payload restore on CI.
        Assert.DoesNotMatch(@"(?im)^\s*\$is(Linux|Windows|MacOS)\s*=", payload);
        Assert.Contains("payload.functions.ps1", payload);
        Assert.Contains("Repair-CaseCollisions", payload);
        Assert.Contains("chmod +x $launcher", payload);
        Assert.Contains("python/bin", payload);

        var unixExec = File.ReadAllText(Path.Combine(repo, "src", "Azure.Cli", "unix-exec.ps1"));
        Assert.Contains("Test-UnixExecuteEntry", unixExec);
        Assert.Contains("Set-NupkgUnixExecuteBits", unixExec);
        Assert.Contains("Assert-NupkgUnixExecuteBits", unixExec);
        Assert.Contains("Expand-NupkgWithUnixModes", unixExec);

        var directoryTargets = File.ReadAllText(Path.Combine(repo, "src", "Directory.Build.targets"));
        Assert.Contains("StampUnixExecuteBitsOnNupkg", directoryTargets);
        Assert.Contains("unix-exec.ps1", directoryTargets);
        var buildYml = File.ReadAllText(Path.Combine(repo, ".github", "workflows", "build.yml"));
        Assert.Contains("unix-exec.ps1", buildYml);
        Assert.Contains("-Assert", buildYml);

        var packTargets = File.ReadAllText(Path.Combine(repo, "src", "Azure.Cli", "Azure.Cli.pack.targets"));
        Assert.Contains("WriteAzureCliRuntimeJson", packTargets);
        Assert.Contains("PackAzureCliPayload", packTargets);
        Assert.Contains("payload.functions.ps1", packTargets);
        Assert.Contains("$(AzureCliPackageId).$(RuntimeIdentifier)", packTargets);
        Assert.DoesNotContain("runtimes/$(RuntimeIdentifier)/native/", packTargets);

        var consumer = File.ReadAllText(Path.Combine(repo, "src", "Azure.Cli", "buildTransitive", "azx.cli.targets"));
        Assert.Contains("IncludeAzureCliPayload", consumer);
        Assert.Contains(@"TargetPath>az\", consumer.Replace('/', '\\'));
        Assert.DoesNotContain("runtimes/$(RuntimeIdentifier)/native/", consumer);

        var azx = File.ReadAllText(Path.Combine(repo, "src", "azx", "azx.csproj"));
        Assert.Contains("<PackageId>azx</PackageId>", azx);
        Assert.Contains("<PackAsTool>true</PackAsTool>", azx);
        Assert.Contains("<PublishAot>true</PublishAot>", azx);
        Assert.Contains("<ToolCommandName>azx</ToolCommandName>", azx);
        Assert.Contains("<ToolPackageRuntimeIdentifiers>win-x64;linux-x64;linux-arm64;osx-x64;osx-arm64</ToolPackageRuntimeIdentifiers>", azx);
        Assert.Contains("""<PackageReference Include="azx.cli" Version="$(Version)" />""", azx);
        Assert.DoesNotContain("NuGetizer", azx, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Readme", azx);
        var nuget = File.ReadAllText(Path.Combine(repo, "src", "azx", "nuget.config"));
        Assert.Contains("key=\"local\"", nuget);
        Assert.Contains("../../bin", nuget);
        Assert.Contains("pattern=\"azx.cli\"", nuget);
        Assert.Contains("pattern=\"azx.cli.*\"", nuget);
        Assert.DoesNotContain("Azure.Cli", nuget);
        Assert.DoesNotContain("win-arm64", azx);
        Assert.DoesNotContain("azx.$(RuntimeIdentifier)", azx);

        Assert.False(File.Exists(Path.Combine(repo, "src", "Azure.Cli", "runtime.json")));
        foreach (var rid in SupportedRids)
        {
            Assert.Contains(rid, azureCli);
            Assert.Contains(rid, azx);
        }
    }

    [Fact]
    public void WriteAzureCliRuntimeJson_maps_five_rids()
    {
        var repo = FindRepoRoot();
        var project = Path.Combine(repo, "src", "Azure.Cli", "Azure.Cli.csproj");
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var start = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repo,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("msbuild");
        start.ArgumentList.Add(project);
        start.ArgumentList.Add("-t:WriteAzureCliRuntimeJson");
        start.ArgumentList.Add("-p:Configuration=" + configuration);
        start.ArgumentList.Add("-p:DesignTimeBuild=true");
        start.ArgumentList.Add("-p:GeneratePackageOnBuild=false");
        start.ArgumentList.Add("-nologo");

        using var process = System.Diagnostics.Process.Start(start)
            ?? throw new InvalidOperationException("Failed to start dotnet msbuild.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000));
        Assert.True(process.ExitCode == 0, stdout + Environment.NewLine + stderr);

        var runtimeJson = Path.Combine(repo, "src", "Azure.Cli", "obj", configuration, "net10.0", "runtime.json");
        Assert.True(File.Exists(runtimeJson), runtimeJson);
        using var doc = JsonDocument.Parse(File.ReadAllText(runtimeJson));
        var runtimes = doc.RootElement.GetProperty("runtimes");
        Assert.Equal(SupportedRids.Length, runtimes.EnumerateObject().Count());
        foreach (var rid in SupportedRids)
        {
            var range = runtimes
                .GetProperty(rid)
                .GetProperty("azx.cli")
                .GetProperty("azx.cli." + rid)
                .GetString();
            Assert.False(string.IsNullOrWhiteSpace(range));
            Assert.StartsWith("[", range);
            Assert.EndsWith(", )", range);
        }

        Assert.False(runtimes.TryGetProperty("win-arm64", out _));
    }

    [Fact]
    public void Packed_nupkgs_have_pointer_and_rid_layout()
    {
        var bin = Path.Combine(FindRepoRoot(), "bin");
        if (!Directory.Exists(bin))
            return;

        var nupkgs = Directory.GetFiles(bin, "azx.cli*.nupkg")
            .Where(f => !f.Contains(".symbols.", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (nupkgs.Length == 0)
            return;

        var pointer = nupkgs.FirstOrDefault(f =>
            !SupportedRids.Any(r => Path.GetFileName(f).Contains("." + r + ".", StringComparison.Ordinal)));
        if (pointer is not null)
        {
            var names = ZipNames(pointer);
            Assert.Contains(names, n => n == "runtime.json" || n == "runtime.json/");
            Assert.Contains(names, n => n.Replace('\\', '/').StartsWith("lib/", StringComparison.Ordinal));
            Assert.Contains(names, n => n.Replace('\\', '/').Contains("buildTransitive/azx.cli.targets", StringComparison.Ordinal));
            Assert.DoesNotContain(names, n => n.Replace('\\', '/').StartsWith("az/", StringComparison.Ordinal));
        }

        var ridPkg = nupkgs.FirstOrDefault(f =>
            SupportedRids.Any(r => Path.GetFileName(f).Contains("." + r + ".", StringComparison.Ordinal)));
        if (ridPkg is not null)
        {
            var names = ZipNames(ridPkg);
            Assert.Contains(names, n => n.Replace('\\', '/').StartsWith("az/bin/", StringComparison.Ordinal));
            Assert.DoesNotContain(names, n => n.Replace('\\', '/').StartsWith("lib/", StringComparison.Ordinal));
            Assert.DoesNotContain(names, n => n.Replace('\\', '/').Contains("buildTransitive/", StringComparison.Ordinal));
            var fileName = Path.GetFileName(ridPkg);
            if (!fileName.Contains(".win-", StringComparison.Ordinal))
                AssertUnixExecuteBits(ridPkg);
        }

        foreach (var azxRid in Directory.GetFiles(bin, "azx.*.nupkg")
            .Where(f => !f.Contains(".symbols.", StringComparison.OrdinalIgnoreCase)
                && SupportedRids.Any(r => Path.GetFileName(f).Contains("." + r + ".", StringComparison.Ordinal)
                    && !Path.GetFileName(f).Contains(".win-", StringComparison.Ordinal))))
        {
            AssertUnixExecuteBits(azxRid);
        }
    }

    [Fact]
    public void Unix_exec_script_stamps_and_asserts_zip_modes()
    {
        var repo = FindRepoRoot();
        var script = Path.Combine(repo, "src", "Azure.Cli", "unix-exec.ps1");
        var scratch = Path.Combine(Path.GetTempPath(), "azx-unix-exec-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(scratch);
        try
        {
            var nupkg = Path.Combine(scratch, "azx.linux-x64.1.0.0.nupkg");
            using (var archive = ZipFile.Open(nupkg, ZipArchiveMode.Create))
            {
                archive.CreateEntry("tools/any/linux-x64/az/bin/az");
                archive.CreateEntry("tools/any/linux-x64/az/python/bin/python3");
                archive.CreateEntry("tools/any/linux-x64/az/python/bin/__pycache__/x.pyc");
                archive.CreateEntry("tools/any/linux-x64/azx");
                archive.CreateEntry("tools/any/linux-x64/az/readme.txt");
            }

            using (var zip = ZipFile.OpenRead(nupkg))
            {
                Assert.False(HasUnixExecute(zip.GetEntry("tools/any/linux-x64/az/bin/az")!.ExternalAttributes));
            }

            RunPwsh(repo, $"""
                $ErrorActionPreference = 'Stop'
                Set-StrictMode -Version Latest
                . '{script.Replace("'", "''", StringComparison.Ordinal)}'
                Set-NupkgUnixExecuteBits '{nupkg.Replace("'", "''", StringComparison.Ordinal)}'
                Assert-NupkgUnixExecuteBits '{nupkg.Replace("'", "''", StringComparison.Ordinal)}'
                """);

            using (var zip = ZipFile.OpenRead(nupkg))
            {
                Assert.True(HasUnixExecute(zip.GetEntry("tools/any/linux-x64/az/bin/az")!.ExternalAttributes));
                Assert.True(HasUnixExecute(zip.GetEntry("tools/any/linux-x64/az/python/bin/python3")!.ExternalAttributes));
                Assert.True(HasUnixExecute(zip.GetEntry("tools/any/linux-x64/azx")!.ExternalAttributes));
                Assert.False(HasUnixExecute(zip.GetEntry("tools/any/linux-x64/az/readme.txt")!.ExternalAttributes));
                Assert.False(HasUnixExecute(zip.GetEntry("tools/any/linux-x64/az/python/bin/__pycache__/x.pyc")!.ExternalAttributes));
            }

            var dest = Path.Combine(scratch, "out");
            RunPwsh(repo, $"""
                $ErrorActionPreference = 'Stop'
                Set-StrictMode -Version Latest
                . '{script.Replace("'", "''", StringComparison.Ordinal)}'
                Expand-NupkgWithUnixModes '{nupkg.Replace("'", "''", StringComparison.Ordinal)}' '{dest.Replace("'", "''", StringComparison.Ordinal)}'
                """);
            var extractedAz = Path.Combine(dest, "tools", "any", "linux-x64", "az", "bin", "az");
            var extractedHost = Path.Combine(dest, "tools", "any", "linux-x64", "azx");
            Assert.True(File.Exists(extractedAz), extractedAz);
            Assert.True(File.Exists(extractedHost), extractedHost);
            if (!OperatingSystem.IsWindows())
            {
                Assert.True(File.GetUnixFileMode(extractedAz).HasFlag(UnixFileMode.UserExecute), extractedAz);
                Assert.True(File.GetUnixFileMode(extractedHost).HasFlag(UnixFileMode.UserExecute), extractedHost);
            }
        }
        finally
        {
            if (Directory.Exists(scratch))
                Directory.Delete(scratch, recursive: true);
        }
    }

    [Fact]
    public void Azure_cli_version_pin_is_semver()
    {
        var pin = File.ReadAllText(Path.Combine(FindRepoRoot(), "azure-cli.version")).Trim();
        Assert.Matches(@"^\d+\.\d+\.\d+$", pin);
    }

    [Fact]
    public void Payload_sources_use_github_tarball_not_zip_via_tar()
    {
        var repo = FindRepoRoot();
        var functions = File.ReadAllText(Path.Combine(repo, "src", "Azure.Cli", "payload.functions.ps1"));
        Assert.Contains("azure-cli-$Version.tar.gz", functions);
        Assert.Contains("archive/refs/tags/azure-cli-$Version.tar.gz", functions);
        Assert.Contains("azure-cli-$Version-src.tar.gz", functions);
        Assert.DoesNotContain("archive/refs/tags/azure-cli-$Version.zip", functions);
        Assert.DoesNotContain("azure-cli-$Version-src.zip", functions);
        Assert.Contains("Expand-TarGz", functions);
        Assert.Contains("Expand-Zip", functions);
        Assert.Contains("Expand-Archive", functions);
        Assert.DoesNotContain("tar -xf $zip", functions);
        Assert.Contains("githubusercontent", functions);
        Assert.DoesNotContain("Invoke-WebRequest -Uri $Url -OutFile $Dest -Headers (Get-GitHubHeaders)", functions);

        var payload = File.ReadAllText(Path.Combine(repo, "src", "Azure.Cli", "payload.ps1"));
        Assert.Contains("Expand-Zip $zip $OutDir", payload);
        Assert.DoesNotContain("tar -xf $zip", payload);
        Assert.Contains("Remove-UnusedPythonShare", payload);
        Assert.Contains("Install-CliFromPyPI", payload);
    }

    [Fact]
    public void ExpandZip_extracts_zip_without_gnu_tar()
    {
        var repo = FindRepoRoot();
        var functions = Path.Combine(repo, "src", "Azure.Cli", "payload.functions.ps1");
        var scratch = Path.Combine(Path.GetTempPath(), "azx-expand-zip-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(scratch);
        try
        {
            var zip = Path.Combine(scratch, "payload.zip");
            var dest = Path.Combine(scratch, "out");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("bin/az.cmd");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("@echo off");
            }

            var start = new System.Diagnostics.ProcessStartInfo("pwsh")
            {
                WorkingDirectory = repo,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-Command");
            var functionsLit = functions.Replace("'", "''", StringComparison.Ordinal);
            var zipLit = zip.Replace("'", "''", StringComparison.Ordinal);
            var destLit = dest.Replace("'", "''", StringComparison.Ordinal);
            start.ArgumentList.Add($$"""
                $ErrorActionPreference = 'Stop'
                Set-StrictMode -Version Latest
                . '{{functionsLit}}'
                Expand-Zip '{{zipLit}}' '{{destLit}}'
                """);

            using var process = System.Diagnostics.Process.Start(start)
                ?? throw new InvalidOperationException("Failed to start pwsh.");
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            Assert.True(process.WaitForExit(60_000), stdout + Environment.NewLine + stderr);
            Assert.True(process.ExitCode == 0, stdout + Environment.NewLine + stderr);
            Assert.True(File.Exists(Path.Combine(dest, "bin", "az.cmd")), dest + Environment.NewLine + stdout + stderr);
        }
        finally
        {
            if (Directory.Exists(scratch))
                Directory.Delete(scratch, recursive: true);
        }
    }

    [Fact]
    public void RemoveUnusedPythonShare_deletes_terminfo_tree()
    {
        var repo = FindRepoRoot();
        var functions = Path.Combine(repo, "src", "Azure.Cli", "payload.functions.ps1");
        var root = Path.Combine(Path.GetTempPath(), "azx-share-" + Guid.NewGuid().ToString("n"));
        var share = Path.Combine(root, "python", "share", "terminfo", "n", "ncr260vt300wpp");
        Directory.CreateDirectory(Path.GetDirectoryName(share)!);
        File.WriteAllText(share, "x");
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo("pwsh")
            {
                WorkingDirectory = repo,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-Command");
            var functionsLit = functions.Replace("'", "''", StringComparison.Ordinal);
            var rootLit = root.Replace("'", "''", StringComparison.Ordinal);
            start.ArgumentList.Add($$"""
                $ErrorActionPreference = 'Stop'
                Set-StrictMode -Version Latest
                . '{{functionsLit}}'
                Remove-UnusedPythonShare '{{rootLit}}'
                """);

            using var process = System.Diagnostics.Process.Start(start)
                ?? throw new InvalidOperationException("Failed to start pwsh.");
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            Assert.True(process.WaitForExit(60_000), stdout + Environment.NewLine + stderr);
            Assert.True(process.ExitCode == 0, stdout + Environment.NewLine + stderr);
            Assert.False(Directory.Exists(Path.Combine(root, "python", "share")));
            Assert.True(Directory.Exists(Path.Combine(root, "python")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GetSourcesRoot_unpacks_azure_cli_tag_tarball()
    {
        var repo = FindRepoRoot();
        var version = File.ReadAllText(Path.Combine(repo, "azure-cli.version")).Trim();
        var functions = Path.Combine(repo, "src", "Azure.Cli", "payload.functions.ps1");
        var cache = Path.Combine(repo, "src", "Azure.Cli", "obj", "payload", "cache");
        Directory.CreateDirectory(cache);

        var start = new System.Diagnostics.ProcessStartInfo("pwsh")
        {
            WorkingDirectory = repo,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-Command");
        var functionsLit = functions.Replace("'", "''", StringComparison.Ordinal);
        var cacheLit = cache.Replace("'", "''", StringComparison.Ordinal);
        start.ArgumentList.Add($$"""
            $ErrorActionPreference = 'Stop'
            Set-StrictMode -Version Latest
            . '{{functionsLit}}'
            $root = Get-SourcesRoot -Version '{{version}}' -CacheDir '{{cacheLit}}'
            Write-Output $root
            """);

        using var process = System.Diagnostics.Process.Start(start)
            ?? throw new InvalidOperationException("Failed to start pwsh.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(180_000), "Get-SourcesRoot timed out." + Environment.NewLine + stdout + stderr);
        Assert.True(process.ExitCode == 0, stdout + Environment.NewLine + stderr);

        var inner = Path.Combine(cache, "src-" + version, "azure-cli-azure-cli-" + version);
        Assert.True(Directory.Exists(inner), inner + Environment.NewLine + stdout + stderr);
        Assert.True(File.Exists(Path.Combine(inner, "build_scripts", "windows", "scripts", "build.cmd")));
        Assert.True(File.Exists(Path.Combine(cache, "azure-cli-" + version + "-src.tar.gz")));
        Assert.False(File.Exists(Path.Combine(cache, "azure-cli-" + version + "-src.zip")));
        Assert.Contains("azure-cli-azure-cli-" + version, stdout.Replace('\\', '/'), StringComparison.Ordinal);
    }

    [Fact]
    public void GetCaseCollidingDuplicates_drops_terminfo_case_variants()
    {
        var repo = FindRepoRoot();
        var functions = Path.Combine(repo, "src", "Azure.Cli", "payload.functions.ps1");
        var start = new System.Diagnostics.ProcessStartInfo("pwsh")
        {
            WorkingDirectory = repo,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-Command");
        var functionsLit = functions.Replace("'", "''", StringComparison.Ordinal);
        start.ArgumentList.Add($$"""
            $ErrorActionPreference = 'Stop'
            Set-StrictMode -Version Latest
            . '{{functionsLit}}'
            $paths = @(
                'python/share/terminfo/2/2621a',
                'python/share/terminfo/2/2621A',
                'python/share/terminfo/h/hp2621a',
                'python/share/terminfo/h/hp2621A',
                'python/share/terminfo/h/hp70092a',
                'python/share/terminfo/h/hp70092A',
                'bin/az'
            )
            foreach ($d in @(Get-CaseCollidingDuplicates $paths)) {
                Write-Output $d
            }
            """);

        using var process = System.Diagnostics.Process.Start(start)
            ?? throw new InvalidOperationException("Failed to start pwsh.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), stdout + Environment.NewLine + stderr);
        Assert.True(process.ExitCode == 0, stdout + Environment.NewLine + stderr);

        var dropped = stdout.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, dropped.Length);
        Assert.Contains("python/share/terminfo/2/2621A", dropped);
        Assert.Contains("python/share/terminfo/h/hp2621A", dropped);
        Assert.Contains("python/share/terminfo/h/hp70092A", dropped);
        Assert.DoesNotContain("bin/az", dropped);
    }

    static HashSet<string> ZipNames(string nupkg)
    {
        using var zip = ZipFile.OpenRead(nupkg);
        return zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    static bool HasUnixExecute(int externalAttributes)
        => ((externalAttributes >> 16) & Convert.ToInt32("111", 8)) != 0;

    static void AssertUnixExecuteBits(string nupkg)
    {
        using var zip = ZipFile.OpenRead(nupkg);
        var az = zip.Entries.FirstOrDefault(e =>
        {
            var n = e.FullName.Replace('\\', '/');
            return n == "az/bin/az" || n.EndsWith("/az/bin/az", StringComparison.Ordinal);
        });
        var python = zip.Entries.FirstOrDefault(e =>
        {
            var n = e.FullName.Replace('\\', '/');
            return n.EndsWith("/python/bin/python3", StringComparison.Ordinal)
                || n.EndsWith("/python/bin/python", StringComparison.Ordinal)
                || n == "python/bin/python3"
                || n == "python/bin/python";
        });
        Assert.True(az is not null, nupkg + " missing az/bin/az");
        Assert.True(python is not null, nupkg + " missing python/bin/python3");
        Assert.True(HasUnixExecute(az!.ExternalAttributes), az.FullName + " in " + nupkg);
        Assert.True(HasUnixExecute(python!.ExternalAttributes), python.FullName + " in " + nupkg);
    }

    static void RunPwsh(string workingDirectory, string command)
    {
        var start = new System.Diagnostics.ProcessStartInfo("pwsh")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(command);
        using var process = System.Diagnostics.Process.Start(start)
            ?? throw new InvalidOperationException("Failed to start pwsh.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), stdout + Environment.NewLine + stderr);
        Assert.True(process.ExitCode == 0, stdout + Environment.NewLine + stderr);
    }

    static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "azx.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not find azx.slnx from " + AppContext.BaseDirectory);
    }
}
