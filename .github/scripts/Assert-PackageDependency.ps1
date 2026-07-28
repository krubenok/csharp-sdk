param(
    [Parameter(Mandatory)]
    [string] $PackagePath,

    [Parameter(Mandatory)]
    [string] $PackageId,

    [Parameter(Mandatory)]
    [string] $PackageVersion,

    [Parameter(Mandatory)]
    [string] $DependencyId,

    [Parameter(Mandatory)]
    [string] $DependencyVersion
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.IO.Compression.FileSystem

$resolvedPackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
$archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedPackagePath)

try {
    $nuspecEntries = @($archive.Entries | Where-Object { $_.FullName.EndsWith(".nuspec", [StringComparison]::OrdinalIgnoreCase) })
    if ($nuspecEntries.Count -ne 1) {
        throw "Expected exactly one nuspec in '$resolvedPackagePath', but found $($nuspecEntries.Count)."
    }

    $reader = [System.IO.StreamReader]::new($nuspecEntries[0].Open())
    try {
        [xml] $nuspec = $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }

    $actualPackageId = $nuspec.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']/*[local-name()='id']").InnerText
    $actualPackageVersion = $nuspec.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']/*[local-name()='version']").InnerText

    if ($actualPackageId -ne $PackageId) {
        throw "Expected package id '$PackageId', but found '$actualPackageId'."
    }

    if ($actualPackageVersion -ne $PackageVersion) {
        throw "Expected package version '$PackageVersion', but found '$actualPackageVersion'."
    }

    $dependencies = @($nuspec.SelectNodes("//*[local-name()='dependency' and @id='$DependencyId']"))
    if ($dependencies.Count -eq 0) {
        throw "Dependency '$DependencyId' was not found in '$resolvedPackagePath'."
    }

    $unexpectedVersions = @($dependencies | Where-Object { $_.version -ne $DependencyVersion } | ForEach-Object { $_.version })
    if ($unexpectedVersions.Count -ne 0) {
        throw "Expected every '$DependencyId' dependency to be exactly '$DependencyVersion', but found: $($unexpectedVersions -join ', ')."
    }

    Write-Host "Verified $PackageId $PackageVersion depends on $DependencyId $DependencyVersion in all $($dependencies.Count) target framework groups."
}
finally {
    $archive.Dispose()
}
