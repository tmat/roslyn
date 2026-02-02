#!/usr/bin/env dotnet
#:package System.CommandLine

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.CommandLine;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

var repoRoot = GetRepoRoot();
var configurationOption = new Option<string?>("--configuration", "-c")
{
    Arity = ArgumentArity.ExactlyOne
};

var rootCommand = new RootCommand()
{
    Options = { configurationOption },
    TreatUnmatchedTokensAsErrors = true,
};

var parseResult = rootCommand.Parse(args);
if (parseResult.Errors.Any())
{
    return -1;
}

return Run(parseResult.GetValue(configurationOption) ?? "Debug");

int Run(string configuration)
{
    var packagesDir = Path.Join(repoRoot, "artifacts", "packages", configuration);
    var shippingDir = Path.Join(packagesDir, "Shipping");
    var nonShippingDir = Path.Join(packagesDir, "NonShipping");

    Console.WriteLine($"Cleaning {shippingDir}...");
    Directory.Delete(shippingDir, recursive: true);

    Console.WriteLine($"Cleaning {nonShippingDir}...");
    Directory.Delete(nonShippingDir, recursive: true);

    var info = new ProcessStartInfo
    {
        FileName = "dotnet",
        Arguments = $"pack -c {configuration}",
        WorkingDirectory = repoRoot
    };

    Console.WriteLine($"Running '{info.FileName} {info.Arguments}'");
    var process = Process.Start(info);

    if (process is null)
    {
        Console.WriteLine($"Failed to start '{info.FileName} {info.Arguments}'");
        return 1;
    }

    process.WaitForExit();

    if (process.ExitCode != 0)
    {
        Console.WriteLine($"'{info.FileName} {info.Arguments}' failed with exit code {process.ExitCode}");
        return process.ExitCode;
    }

    Console.WriteLine("Build completed successfully.");

    var nugetCache = GetNuGetGlobalPackagesPath();
    if (nugetCache is null)
    {
        Console.WriteLine("Failed to determine NuGet global packages path.");
        return 1;
    }
    Console.WriteLine($"NuGet cache location: {nugetCache}");

    var removedCount = 0;

    if (Directory.Exists(shippingDir))
    {
        removedCount += ProcessPackagesDirectory(shippingDir, nugetCache);
    }

    if (Directory.Exists(nonShippingDir))
    {
        removedCount += ProcessPackagesDirectory(nonShippingDir, nugetCache);
    }

    Console.WriteLine($"Done. Removed {removedCount} package(s) from NuGet cache.");
    return 0;
}

static string GetRepoRoot([CallerFilePath] string sourceFilePath = "")
    => Path.GetDirectoryName(Path.GetDirectoryName(sourceFilePath))!;

static string? GetNuGetGlobalPackagesPath()
{
    var info = new ProcessStartInfo
    {
        FileName = "dotnet",
        Arguments = "nuget locals global-packages --list",
        RedirectStandardOutput = true,
    };

    Console.WriteLine($"Running '{info.FileName} {info.Arguments}'");

    var process = Process.Start(info);
    if (process is null)
    {
        return null;
    }

    var output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();

    // Output format: "global-packages: C:\Users\...\.nuget\packages"
    const string prefix = "global-packages:";
    if (output.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
    {
        return output[prefix.Length..].Trim();
    }

    return null;
}

static int ProcessPackagesDirectory(string packagesDir, string nugetCache)
{
    var removed = 0;
    foreach (var packagePath in Directory.GetFiles(packagesDir, "*.nupkg"))
    {
        var fileName = Path.GetFileNameWithoutExtension(packagePath);
        if (TryParsePackageName(fileName, out var packageId, out var version))
        {
            var cacheDir = Path.Join(nugetCache, packageId.ToLowerInvariant(), version);
            if (Directory.Exists(cacheDir))
            {
                Directory.Delete(cacheDir, recursive: true);
                Console.WriteLine($"  Removed from cache: {packageId} {version}");
                removed++;
            }
            else
            {
                Console.WriteLine($"  Not in cache: {packageId} {version}");
            }
        }
        else
        {
            Console.WriteLine($"  Could not parse package name: {fileName}");
        }
    }
    return removed;
}

static bool TryParsePackageName(string fileName, out string packageId, out string version)
{
    // NuGet package names follow the pattern: {PackageId}.{Version}
    // Version starts with a digit and may contain digits, dots, and pre-release suffixes (e.g., 1.0.0-dev)
    // We find the last segment that starts with a digit preceded by a dot
    var match = Regex.Match(fileName, @"^(.+?)\.(\d+\..+)$");
    if (match.Success)
    {
        packageId = match.Groups[1].Value;
        version = match.Groups[2].Value;
        return true;
    }

    packageId = string.Empty;
    version = string.Empty;
    return false;
}
