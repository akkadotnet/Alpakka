param(
    [Parameter(Mandatory)][string]$OriginalBinary,
    [Parameter(Mandatory)][string]$CandidateOutput,
    [Parameter(Mandatory)][string]$Feed,
    [Parameter(Mandatory)][string]$ArtifactRoot,
    [switch]$BuildOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
New-Item $ArtifactRoot -ItemType Directory -Force | Out-Null
$ArtifactRoot = (Resolve-Path $ArtifactRoot).Path
$diagnosticFailure = $null
$binary = $null
$config = $null
$hadConfig = $false
$configCaptured = $false
$savedConfig = Join-Path $ArtifactRoot 'FailedCandidate.config'
$originalHash = $null
$dlls = @{}
$identity = [ordered]@{ BuildOnly = [bool]$BuildOnly }

function Build-Resolver([string]$Name, [string]$LegacyBinary, [string]$Config, [string]$Package, [string]$Version) {
    $directory = Join-Path $ArtifactRoot $Name
    New-Item $directory -ItemType Directory -Force | Out-Null
    $legacy = [Security.SecurityElement]::Escape($LegacyBinary)
    $sources = [Security.SecurityElement]::Escape("$Feed;https://api.nuget.org/v3/index.json")
    $cache = [Security.SecurityElement]::Escape((Join-Path $directory 'packages'))
    $configProperty = ''
    if (Test-Path $Config) {
        Copy-Item $Config (Join-Path $directory 'Original.config')
        $configProperty = '<AppConfig>Original.config</AppConfig>'
    }
    $template = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net48</TargetFramework>
    <AssemblyName>BindingResolver_NAME_</AssemblyName>
    <AutoGenerateBindingRedirects>true</AutoGenerateBindingRedirects>
    <GenerateBindingRedirectsOutputType>true</GenerateBindingRedirectsOutputType>
    <RestoreSources>_SOURCES_</RestoreSources><RestorePackagesPath>_CACHE_</RestorePackagesPath>
    _CONFIG_
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="FrozenLegacyConsumer"><HintPath>_LEGACY_</HintPath><Private>false</Private></Reference>
    <PackageReference Include="_PACKAGE_" Version="_VERSION_" />
  </ItemGroup>
  <Target Name="RecordBindingInputs" AfterTargets="ResolveAssemblyReferences">
    <WriteLinesToFile File="$(MSBuildProjectDirectory)/references.txt" Lines="@(ReferencePath->'%(FusionName)|%(ResolvedFrom)')" Overwrite="true" />
    <WriteLinesToFile File="$(MSBuildProjectDirectory)/suggested-bindings.txt" Lines="@(SuggestedBindingRedirects->'%(Identity)|%(MaxVersion)')" Overwrite="true" />
  </Target>
</Project>
'@
    $project = $template.Replace('_NAME_', $Name).Replace('_SOURCES_', $sources).Replace('_CACHE_', $cache)
    $project = $project.Replace('_CONFIG_', $configProperty).Replace('_LEGACY_', $legacy).Replace('_PACKAGE_', $Package).Replace('_VERSION_', $Version)
    Set-Content (Join-Path $directory 'Resolver.csproj') $project
    Set-Content (Join-Path $directory 'Program.cs') 'internal static class Resolver { private static void Main() { } }'
    $buildLog = Join-Path $directory 'build.log'
    & dotnet build (Join-Path $directory 'Resolver.csproj') -c Release *> $buildLog
    if ($LASTEXITCODE -ne 0) { Get-Content $buildLog; throw "Resolver build failed: $Name" }
    if ($Package -eq 'Akka.Streams.Amqp.V1') {
        $resolved = Join-Path (Join-Path $directory 'packages') "akka.streams.amqp.v1/$Version/akka.streams.amqp.v1.$Version.nupkg"
        $packed = Join-Path $Feed "Akka.Streams.Amqp.V1.$Version.nupkg"
        if ((Get-FileHash $resolved).Hash -ne (Get-FileHash $packed).Hash) { throw 'Resolver did not use the exact candidate archive.' }
    }
    $output = Join-Path $directory 'bin/Release/net48'
    $generated = Join-Path $output "BindingResolver$Name.exe.config"
    if (!(Test-Path $generated)) { throw "MSBuild did not generate a binding configuration: $Name" }
    if (Test-Path $Config) {
        [xml]$beforeConfig = Get-Content $Config -Raw
        [xml]$afterConfig = Get-Content $generated -Raw
        foreach ($section in $beforeConfig.DocumentElement.ChildNodes | Where-Object NodeType -eq Element | Where-Object Name -ne 'runtime') {
            if ($afterConfig.DocumentElement.SelectSingleNode($section.Name).OuterXml -ne $section.OuterXml) {
                throw "MSBuild changed the original host section: $($section.Name)"
            }
        }
        $oldRuntime = $beforeConfig.DocumentElement.SelectSingleNode('runtime')
        if ($oldRuntime) {
            $newRuntime = $afterConfig.DocumentElement.SelectSingleNode('runtime')
            foreach ($attribute in $oldRuntime.Attributes) {
                if ($newRuntime.GetAttribute($attribute.Name) -ne $attribute.Value) { throw "MSBuild changed runtime attribute $($attribute.Name)." }
            }
            foreach ($setting in $oldRuntime.ChildNodes | Where-Object NodeType -eq Element | Where-Object LocalName -ne 'assemblyBinding') {
                if ($newRuntime.SelectSingleNode($setting.Name).OuterXml -ne $setting.OuterXml) {
                    throw "MSBuild changed runtime setting $($setting.Name)."
                }
            }
        }
    }
    Copy-Item $generated (Join-Path $directory 'Generated.config')
    return [pscustomobject]@{ Directory = $directory; Output = $output; Config = $generated }
}

function Get-DllHashes([string]$Directory) {
    $result = @{}
    foreach ($file in Get-ChildItem $Directory -Filter '*.dll') { $result[$file.Name] = (Get-FileHash $file.FullName).Hash }
    return $result
}

function Invoke-Frozen([string]$Executable, [string]$Name, [ValidateSet('None', 'Binding', 'Interface')][string]$ExpectedFailure) {
    $process = [Diagnostics.Process]::new()
    $started = $false
    $process.StartInfo = [Diagnostics.ProcessStartInfo]::new($Executable)
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    try {
        if (!$process.Start()) { throw 'Consumer did not start.' }
        $started = $true
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $process.WaitForExitAsync().WaitAsync([TimeSpan]::FromSeconds(45)).GetAwaiter().GetResult()
        $text = $stdout.WaitAsync([TimeSpan]::FromSeconds(5)).GetAwaiter().GetResult() + $stderr.WaitAsync([TimeSpan]::FromSeconds(5)).GetAwaiter().GetResult()
        Set-Content (Join-Path $ArtifactRoot "$Name.log") $text
        $identity[$Name] = $process.ExitCode
        if ($ExpectedFailure -eq 'Interface') {
            if ($process.ExitCode -eq 0 -or $text -notmatch 'TypeLoadException' -or $text -notmatch 'get_Message') {
                throw "The incompatible-interface control failed for the wrong reason: $text"
            }
        } elseif ($ExpectedFailure -eq 'Binding') {
            if ($process.ExitCode -eq 0 -or $text -notmatch '(?is)\bFileLoadException\s*:\s*Could not load file or assembly\s*[''"]Amqp\.Net,\s*Version=' -or $text -notmatch '0x80131040') {
                throw "Unchanged-policy control did not fail on the expected SDK binding: $text"
            }
        } elseif ($process.ExitCode -ne 0) { throw "Frozen consumer failed under generated binding policy: $text" }
        Write-Host "$Name exit=$($process.ExitCode)"
    } finally {
        if ($started -and !$process.HasExited) {
            $process.Kill($true)
            if (!$process.WaitForExit(15000)) { throw 'Consumer did not exit after termination.' }
        }
        $process.Dispose()
    }
}

try {
$OriginalBinary = (Resolve-Path $OriginalBinary).Path
$CandidateOutput = (Resolve-Path $CandidateOutput).Path
$Feed = (Resolve-Path $Feed).Path
$originalHash = (Get-FileHash $OriginalBinary).Hash
$binary = Join-Path $CandidateOutput ([IO.Path]::GetFileName($OriginalBinary))
if ((Get-FileHash $binary).Hash -ne $originalHash) { throw 'Candidate output does not contain the frozen executable.' }
$dlls = Get-DllHashes $CandidateOutput
if ($dlls.Count -eq 0) { throw 'Candidate output has no runtime DLLs.' }
$config = "$binary.config"
$hadConfig = Test-Path $config
if ($hadConfig) { Copy-Item $config $savedConfig }
$configCaptured = $true
if ($hadConfig) { $identity.FailedConfigurationHash = (Get-FileHash $savedConfig).Hash }
if (!(Test-Path "$OriginalBinary.config")) { throw 'Original host configuration was not saved.' }
if (!$BuildOnly) { Invoke-Frozen $binary 'UnchangedPolicyBefore' 'Binding' }
$resolver = Build-Resolver 'Candidate' $OriginalBinary "$OriginalBinary.config" 'Akka.Streams.Amqp.V1' '1.5.59-amqp-v1-validation'
if (!$BuildOnly) {
    Copy-Item $resolver.Config $config -Force
    Invoke-Frozen $binary 'FrozenConsumerWithGeneratedPolicy' 'None'
    if ($hadConfig) { Copy-Item $savedConfig $config -Force } else { Remove-Item $config }
    Invoke-Frozen $binary 'UnchangedPolicyAfter' 'Binding'
}

$control = Join-Path $ArtifactRoot 'OldInterface'
New-Item $control -ItemType Directory -Force | Out-Null
Set-Content (Join-Path $control 'Probe.csproj') '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net48</TargetFramework><AssemblyName>FrozenDelivery</AssemblyName></PropertyGroup><ItemGroup><PackageReference Include="AMQPNetLite.Core" Version="2.4.11" /></ItemGroup></Project>'
Set-Content (Join-Path $control 'Program.cs') @'
using System;
using Amqp.Handler;
using Amqp.Framing;
internal static class Program { private static void Main() { if ((string)new LegacyDelivery().UserToken != "retained") throw new Exception("Payload changed."); Console.WriteLine("LEGACY_DELIVERY_PASS"); } }
public sealed class LegacyDelivery : IDelivery {
 public byte[] Tag { get; set; } = new byte[] { 1 };
 public DeliveryState State { get; set; } = new Accepted();
 public bool Batchable { get; set; }
 public object UserToken { get { return "retained"; } }
 public bool Settled { get; set; }
}
'@
& dotnet build (Join-Path $control 'Probe.csproj') -c Release *> (Join-Path $control 'build.log')
if ($LASTEXITCODE -ne 0) { Get-Content (Join-Path $control 'build.log'); throw 'Old interface control compilation failed.' }
$oldProbe = Join-Path $control 'bin/Release/net48/FrozenDelivery.exe'
$probeHash = (Get-FileHash $oldProbe).Hash
if (!$BuildOnly) { Invoke-Frozen $oldProbe 'OldInterfaceBaseline' 'None' }
$bad = Build-Resolver 'Incompatible' $oldProbe "$oldProbe.config" 'AMQPNetLite.Core' '2.5.5'
$badProbe = Join-Path $bad.Output 'FrozenDelivery.exe'
Copy-Item $oldProbe $badProbe -Force
Copy-Item $bad.Config "$badProbe.config" -Force
if ((Get-FileHash $badProbe).Hash -ne $probeHash) { throw 'Old interface probe changed.' }
if (!$BuildOnly) { Invoke-Frozen $badProbe 'IncompatibleInterfaceWithGeneratedPolicy' 'Interface' }
$identity.InterfaceProbeHash = $probeHash
$identity.IncompatibleDllHashes = Get-DllHashes $bad.Output
} catch {
    $diagnosticFailure = $_
} finally {
    try {
        if ($configCaptured -and (Test-Path $savedConfig)) { Copy-Item $savedConfig $config -Force }
        elseif ($configCaptured -and !$hadConfig -and (Test-Path $config)) { Remove-Item $config }
        if ($configCaptured -and $hadConfig -and (Get-FileHash $config).Hash -ne $identity.FailedConfigurationHash) { throw 'Original candidate host configuration was not restored.' }
        if ($originalHash) {
            if ((Get-FileHash $OriginalBinary).Hash -ne $originalHash -or (Get-FileHash $binary).Hash -ne $originalHash) { throw 'Frozen executable changed.' }
            $after = Get-DllHashes $CandidateOutput
            if ($after.Count -ne $dlls.Count) { throw 'Candidate DLL set changed.' }
            foreach ($name in $dlls.Keys) { if ($after[$name] -ne $dlls[$name]) { throw "Candidate DLL changed: $name" } }
            $identity.OriginalExecutableHash = $originalHash
            $identity.CandidateDllHashes = $dlls
        }
    } catch {
        if ($diagnosticFailure) { Write-Warning "Cleanup/identity verification also failed: $($_.Exception.Message)" }
        else { $diagnosticFailure = $_ }
    }
    try {
        if ($diagnosticFailure) { Set-Content (Join-Path $ArtifactRoot 'failure.txt') $diagnosticFailure.Exception.ToString() }
        $identity | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $ArtifactRoot 'identity.json')
        $report = Join-Path $ArtifactRoot 'report'
        New-Item $report -ItemType Directory -Force | Out-Null
        Get-ChildItem $ArtifactRoot -File | Copy-Item -Destination $report
        foreach ($name in @('Candidate', 'Incompatible', 'OldInterface')) {
            $directory = Join-Path $ArtifactRoot $name
            if (!(Test-Path $directory)) { continue }
            $destination = Join-Path $report $name
            New-Item $destination -ItemType Directory -Force | Out-Null
            Get-ChildItem $directory -File | Copy-Item -Destination $destination
        }
    } catch {
        if ($diagnosticFailure) { Write-Warning "Could not collect partial diagnostic evidence: $($_.Exception.Message)" }
        else { throw }
    }
}
if ($diagnosticFailure) { throw $diagnosticFailure }
Write-Host 'BINDING_DIAGNOSTIC_COMPLETE'
