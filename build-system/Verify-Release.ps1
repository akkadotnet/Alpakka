param(
    [Parameter(Mandatory)][string]$Tag,
    [string]$PackageDirectory
)

$ErrorActionPreference = 'Stop'
if ($Tag -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z]+(\.[0-9A-Za-z-]+)*)?$') {
    throw "Invalid release tag '$Tag'. Use a NuGet version without a v prefix."
}
$header = Get-Content RELEASE_NOTES.md -TotalCount 1
if ($header -notmatch '^####\s+(\S+)\s+(.+?)\s+####\s*$' -or $Matches[1] -cne $Tag -or $Matches[2] -match 'Unreleased') {
    throw "The first release notes header must contain version '$Tag' and a release date."
}
$version = ([xml](Get-Content Directory.Build.props -Raw)).SelectSingleNode('//VersionPrefix').InnerText
if ($version -cne $Tag) { throw "Tag '$Tag' does not match VersionPrefix '$version'." }
if (!$PackageDirectory) { return }

$expected = @(
    'Akka.Streams.Amqp.V1', 'Akka.Streams.Amqp.RabbitMq', 'Akka.Streams.Azure.EventHub',
    'Akka.Streams.Azure.ServiceBus', 'Akka.Streams.Azure.StorageQueue', 'Akka.Streams.Kinesis',
    'Akka.Streams.Sns', 'Akka.Streams.SQS', 'Akka.Streams.Csv', 'Akka.Streams.File',
    'Akka.Streams.SignalR', 'Akka.Streams.Xml'
)
$packages = @(Get-ChildItem $PackageDirectory -Filter '*.nupkg')
if ($packages.Count -ne $expected.Count) { throw "Expected $($expected.Count) packages, found $($packages.Count)." }
Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($id in $expected) {
    $path = Join-Path $PackageDirectory "$id.$Tag.nupkg"
    if (!(Test-Path -LiteralPath $path)) { throw "Missing package $path." }
    $archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $path))
    try {
        $nuspec = @($archive.Entries | Where-Object FullName -eq "$id.nuspec")
        if ($nuspec.Count -ne 1) { throw "Missing nuspec in $path." }
        $reader = [IO.StreamReader]::new($nuspec[0].Open())
        try { [xml]$metadata = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($metadata.package.metadata.id -cne $id -or $metadata.package.metadata.version -cne $Tag) {
            throw "Unexpected package identity in $path."
        }
        $readme = [string]$metadata.package.metadata.readme
        if (!$readme -or @($archive.Entries | Where-Object FullName -eq $readme).Count -ne 1) {
            throw "Missing declared README in $path."
        }
    } finally { $archive.Dispose() }
}
Write-Host "Verified $($packages.Count) packages at version $Tag."
