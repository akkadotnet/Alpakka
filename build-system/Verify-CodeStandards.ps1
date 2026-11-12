$ErrorActionPreference = 'Stop'
$directory = Join-Path $PSScriptRoot ('.standards-probe-' + [guid]::NewGuid().ToString('N'))
$project = Join-Path $directory 'StandardsProbe.csproj'
$source = Join-Path $directory 'Probe.cs'

$cases = @(
    @{
        Rule = 'CS8602'
        Code = @'
#nullable enable
internal static class Probe
{
    public static int Length(string? value) => value.Length;
}
'@
    },
    @{
        Rule = 'CS4014'
        Code = @'
using System.Threading.Tasks;
internal static class Probe
{
    private static async Task WorkAsync() { await Task.Yield(); }
    public static async Task RunAsync() { WorkAsync(); await Task.Yield(); }
}
'@
    },
    @{
        Rule = 'CA2012'
        Code = @'
using System.Threading.Tasks;
internal interface IValueSource { ValueTask<int> NextAsync(); }
internal static class Probe
{
    public static async Task<int> TwiceAsync(IValueSource source)
    {
        var pending = source.NextAsync();
        var first = await pending;
        return first + await pending;
    }
}
'@
    },
    @{
        Rule = 'CA2014'
        Code = @'
using System;
internal static class Probe
{
    public static int Sum()
    {
        var sum = 0;
        for (var i = 0; i < 2; i++)
        {
            Span<int> buffer = stackalloc int[1];
            buffer[0] = i;
            sum += buffer[0];
        }
        return sum;
    }
}
'@
    },
    @{
        Rule = 'CA2200'
        Code = @'
using System;
internal static class Probe
{
    public static void Run()
    {
        try { throw new InvalidOperationException("probe"); }
        catch (Exception error) { throw error; }
    }
}
'@
    }
)

New-Item $directory -ItemType Directory | Out-Null
try {
    Set-Content $project @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
</Project>
'@
    Set-Content $source @'
#nullable enable
internal static class Probe
{
    public static int Length(string? value) => value?.Length ?? 0;
}
'@
    $output = (& dotnet build $project -c Release --no-incremental 2>&1) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw "Clean standards probe failed to build.`n$output" }
    Write-Host 'Clean standards probe passed.'

    foreach ($case in $cases) {
        Set-Content $source $case.Code
        $output = (& dotnet build $project -c Release --no-restore --no-incremental 2>&1) -join "`n"
        $exitCode = $LASTEXITCODE
        if ($exitCode -eq 0 -or $output -notmatch "\berror $($case.Rule)\b") {
            throw "Standards probe did not reject $($case.Rule) as an error (exit $exitCode).`n$output"
        }
        Write-Host "Standards probe rejected $($case.Rule)."
    }
}
finally {
    Remove-Item $directory -Recurse -Force
}

# Each expected compiler failure was checked before reporting a successful gate.
$global:LASTEXITCODE = 0
