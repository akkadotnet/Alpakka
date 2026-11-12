param(
    [Parameter(Mandatory)][string]$OriginalBinary,
    [Parameter(Mandatory)][string]$CandidateOutput,
    [Parameter(Mandatory)][string]$Feed,
    [Parameter(Mandatory)][string]$PackageId,
    [Parameter(Mandatory)][string]$PackageVersion,
    [Parameter(Mandatory)][string]$BindingAssembly,
    [Parameter(Mandatory)][string]$ArtifactRoot,
    [switch]$BuildOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
New-Item $ArtifactRoot -ItemType Directory -Force | Out-Null
$ArtifactRoot = (Resolve-Path $ArtifactRoot).Path
$failure = $null
$binary = $null
$config = $null
$hadConfig = $false
$configCaptured = $false
$savedConfig = Join-Path $ArtifactRoot 'FailedCandidate.config'
$originalHash = $null
$dlls = @{}
$identity = [ordered]@{ PackageId = $PackageId; PackageVersion = $PackageVersion; BuildOnly = [bool]$BuildOnly }

function Get-DllHashes([string]$Directory) {
    $result = @{}
    foreach ($file in Get-ChildItem $Directory -Filter '*.dll') { $result[$file.Name] = (Get-FileHash $file.FullName).Hash }
    return $result
}

function Invoke-Frozen([string]$Name, [bool]$ExpectBindingFailure) {
    $process = [Diagnostics.Process]::new()
    $started = $false
    $process.StartInfo = [Diagnostics.ProcessStartInfo]::new($binary)
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    try {
        if (!$process.Start()) { throw 'Frozen consumer did not start.' }
        $started = $true
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $process.WaitForExitAsync().WaitAsync([TimeSpan]::FromSeconds(45)).GetAwaiter().GetResult()
        $output = $stdout.WaitAsync([TimeSpan]::FromSeconds(5)).GetAwaiter().GetResult() + $stderr.WaitAsync([TimeSpan]::FromSeconds(5)).GetAwaiter().GetResult()
        Set-Content (Join-Path $ArtifactRoot "$Name.log") $output
        $identity[$Name] = $process.ExitCode
        if ($ExpectBindingFailure) {
            $bindingPattern = '(?is)\bFileLoadException\s*:\s*Could not load file or assembly\s*[''"]{0},\s*Version=' -f [regex]::Escape($BindingAssembly)
            if ($process.ExitCode -eq 0 -or $output -notmatch $bindingPattern -or $output -notmatch '0x80131040') {
                throw "Unchanged-policy control did not fail on the expected binding: $output"
            }
        } elseif ($process.ExitCode -ne 0) { throw "Frozen consumer failed under generated policy: $output" }
        Write-Host "$Name exit=$($process.ExitCode)"
    } finally {
        if ($started -and !$process.HasExited) {
            $process.Kill($true)
            if (!$process.WaitForExit(15000)) { throw 'Frozen consumer did not exit after termination.' }
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
    $config = "$binary.config"
    if ((Get-FileHash $binary).Hash -ne $originalHash) { throw 'Candidate output does not contain the frozen executable.' }
    $dlls = Get-DllHashes $CandidateOutput
    if ($dlls.Count -eq 0) { throw 'Candidate output has no runtime DLLs.' }
    $hadConfig = Test-Path $config
    if ($hadConfig) { Copy-Item $config $savedConfig }
    $configCaptured = $true
    if ($hadConfig) { $identity.FailedConfigurationHash = (Get-FileHash $savedConfig).Hash }
    if (!(Test-Path "$OriginalBinary.config")) { throw 'Original host configuration was not saved.' }
    if (!$BuildOnly) { Invoke-Frozen 'UnchangedPolicyBefore' $true }

    $directory = Join-Path $ArtifactRoot 'Resolver'
    New-Item $directory -ItemType Directory | Out-Null
    Copy-Item "$OriginalBinary.config" (Join-Path $directory 'Original.config')
    $template = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net48</TargetFramework>
    <AssemblyName>FrozenConsumerBindingResolver</AssemblyName>
    <AutoGenerateBindingRedirects>true</AutoGenerateBindingRedirects>
    <GenerateBindingRedirectsOutputType>true</GenerateBindingRedirectsOutputType>
    <AppConfig>Original.config</AppConfig>
    <RestoreSources>_SOURCES_</RestoreSources><RestorePackagesPath>_CACHE_</RestorePackagesPath>
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
    $project = $template.Replace('_SOURCES_', [Security.SecurityElement]::Escape("$Feed;https://api.nuget.org/v3/index.json"))
    $project = $project.Replace('_CACHE_', [Security.SecurityElement]::Escape((Join-Path $directory 'packages')))
    $project = $project.Replace('_LEGACY_', [Security.SecurityElement]::Escape($OriginalBinary))
    $project = $project.Replace('_PACKAGE_', [Security.SecurityElement]::Escape($PackageId)).Replace('_VERSION_', [Security.SecurityElement]::Escape($PackageVersion))
    Set-Content (Join-Path $directory 'Resolver.csproj') $project
    Set-Content (Join-Path $directory 'Program.cs') 'internal static class Resolver { private static void Main() { } }'
    & dotnet build (Join-Path $directory 'Resolver.csproj') -c Release *> (Join-Path $directory 'build.log')
    if ($LASTEXITCODE -ne 0) { Get-Content (Join-Path $directory 'build.log'); throw 'Binding resolver build failed.' }
    $id = $PackageId.ToLowerInvariant()
    $version = $PackageVersion.ToLowerInvariant()
    $resolved = Join-Path $directory "packages/$id/$version/$id.$version.nupkg"
    $packed = Join-Path $Feed "$PackageId.$PackageVersion.nupkg"
    if ((Get-FileHash $resolved).Hash -ne (Get-FileHash $packed).Hash) { throw 'Resolver did not use the exact candidate archive.' }
    $identity.CandidateArchiveHash = (Get-FileHash $packed).Hash
    $generated = Join-Path $directory 'bin/Release/net48/FrozenConsumerBindingResolver.exe.config'
    if (!(Test-Path $generated)) { throw 'MSBuild did not generate host binding policy.' }
    Copy-Item $generated (Join-Path $directory 'Generated.config')
    [xml]$before = Get-Content "$OriginalBinary.config" -Raw
    [xml]$after = Get-Content $generated -Raw
    foreach ($section in $before.DocumentElement.ChildNodes | Where-Object NodeType -eq Element | Where-Object Name -ne 'runtime') {
        if ($after.DocumentElement.SelectSingleNode($section.Name).OuterXml -ne $section.OuterXml) { throw "Host section changed: $($section.Name)." }
    }
    $runtime = $before.DocumentElement.SelectSingleNode('runtime')
    if ($runtime) {
        $newRuntime = $after.DocumentElement.SelectSingleNode('runtime')
        foreach ($attribute in $runtime.Attributes) {
            if ($newRuntime.GetAttribute($attribute.Name) -ne $attribute.Value) { throw "Runtime attribute changed: $($attribute.Name)." }
        }
        foreach ($setting in $runtime.ChildNodes | Where-Object NodeType -eq Element | Where-Object LocalName -ne 'assemblyBinding') {
            if ($newRuntime.SelectSingleNode($setting.Name).OuterXml -ne $setting.OuterXml) { throw "Runtime setting changed: $($setting.Name)." }
        }
    }
    $identity.OriginalConfigurationHash = (Get-FileHash "$OriginalBinary.config").Hash
    $identity.GeneratedConfigurationHash = (Get-FileHash $generated).Hash
    if (!$BuildOnly) {
        Copy-Item $generated $config -Force
        Invoke-Frozen 'GeneratedPolicy' $false
        if ($hadConfig) { Copy-Item $savedConfig $config -Force } else { Remove-Item $config }
        Invoke-Frozen 'UnchangedPolicyAfter' $true
    }
} catch {
    $failure = $_
} finally {
    try {
        if ($configCaptured -and (Test-Path $savedConfig)) { Copy-Item $savedConfig $config -Force }
        elseif ($configCaptured -and !$hadConfig -and (Test-Path $config)) { Remove-Item $config }
        if ($configCaptured -and $hadConfig -and (Get-FileHash $config).Hash -ne $identity.FailedConfigurationHash) { throw 'Original candidate host configuration was not restored.' }
        if ($originalHash) {
            if ((Get-FileHash $OriginalBinary).Hash -ne $originalHash -or (Get-FileHash $binary).Hash -ne $originalHash) { throw 'Frozen executable changed.' }
            $current = Get-DllHashes $CandidateOutput
            if ($current.Count -ne $dlls.Count) { throw 'Candidate DLL set changed.' }
            foreach ($name in $dlls.Keys) { if ($current[$name] -ne $dlls[$name]) { throw "Candidate DLL changed: $name" } }
            $identity.OriginalExecutableHash = $originalHash
            $identity.CandidateDllHashes = $dlls
        }
    } catch {
        if ($failure) { Write-Warning "Cleanup/identity verification also failed: $($_.Exception.Message)" }
        else { $failure = $_ }
    }
    try {
        if ($failure) { Set-Content (Join-Path $ArtifactRoot 'failure.txt') $failure.Exception.ToString() }
        $identity | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $ArtifactRoot 'identity.json')
        $report = Join-Path $ArtifactRoot 'report'
        New-Item $report -ItemType Directory -Force | Out-Null
        Get-ChildItem $ArtifactRoot -File | Copy-Item -Destination $report
        $resolver = Join-Path $ArtifactRoot 'Resolver'
        if (Test-Path $resolver) {
            $destination = Join-Path $report 'Resolver'
            New-Item $destination -ItemType Directory -Force | Out-Null
            Get-ChildItem $resolver -File | Copy-Item -Destination $destination
        }
    } catch {
        if ($failure) { Write-Warning "Could not collect partial diagnostic evidence: $($_.Exception.Message)" }
        else { $failure = $_ }
    }
}
if ($failure) { throw $failure }
Write-Host 'AZURE_BINDING_DIAGNOSTIC_COMPLETE'
