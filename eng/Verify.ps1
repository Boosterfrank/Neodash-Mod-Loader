#Requires -Version 7.2
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $SkipPublish
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifacts = Join-Path $repository 'artifacts'
$logs = Join-Path $artifacts 'logs'
$publish = Join-Path $artifacts 'publish'
$coverageDirectory = Join-Path $artifacts 'coverage\latest'
$coverageReport = Join-Path $coverageDirectory 'coverage.cobertura.xml'
$coverageMapping = Join-Path $coverageDirectory 'source-mapping.txt'
$resultsDirectory = Join-Path $artifacts "test-results/$Configuration"
$testReport = Join-Path $resultsDirectory 'integration.trx'
New-Item -ItemType Directory -Path $logs -Force | Out-Null
New-Item -ItemType Directory -Path $coverageDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $resultsDirectory -Force | Out-Null

function Invoke-DotnetCheck {
    param([string] $Name, [string[]] $Arguments)
    if ($Arguments[0] -in @('restore', 'build', 'publish')) {
        $Arguments += '-bl:' + (Join-Path $logs ($Name + '-{}.binlog'))
    }
    Write-Host "Running $Name..."
    & dotnet @Arguments 2>&1 | Tee-Object -FilePath (Join-Path $logs "$Name.log")
    if ($LASTEXITCODE -ne 0) { throw "$Name failed with exit code $LASTEXITCODE. See artifacts/logs/$Name.log." }
}

Push-Location $repository
try {
    Write-Host 'Running installer cleanup validation...'
    & (Join-Path $PSScriptRoot 'VerifyInstaller.ps1')
    Invoke-DotnetCheck 'restore' @('restore', 'Neodash.ModLoader.slnx', '--locked-mode', '-p:Platform=x64')
    Invoke-DotnetCheck 'coverage-tool-restore' @('tool', 'restore')
    Invoke-DotnetCheck 'build' @('build', 'Neodash.ModLoader.slnx', '--no-restore', '-c', $Configuration,
        '-p:Platform=x64', '-p:TreatWarningsAsErrors=true', '-p:ContinuousIntegrationBuild=true',
        '-p:PublishReleaseAfterBuild=false', '-t:Rebuild')
    foreach ($oldReport in @($testReport, $coverageReport, (Join-Path $coverageDirectory 'coverage.json'), (Join-Path $coverageDirectory 'summary.json'))) {
        if (Test-Path -LiteralPath $oldReport) { Remove-Item -LiteralPath $oldReport }
    }
    # Instrument before starting MTP so fresh child hosts execute the same instrumented production sources.
    # Resolve deterministic CI PDB paths back to this checkout without excluding missing virtual source paths.
    ('|' + $repository + [IO.Path]::DirectorySeparatorChar + '=/_/') | Set-Content -LiteralPath $coverageMapping
    $testAssembly = Join-Path $repository "tests/Neodash.ModLoader.Integration/bin/$Configuration/net10.0-windows/Neodash.ModLoader.Integration.dll"
    # xUnit v3 supplies its own TRX reporter; no VSTest bridge or extra reporting package is needed.
    # Its filename must be a basename, with the directory passed separately to MTP.
    $testArguments = "test --project tests/Neodash.ModLoader.Integration/Neodash.ModLoader.Integration.csproj --no-build --no-restore -c $Configuration -p:Platform=AnyCPU --fail-skips on --no-ansi --report-xunit-trx --report-xunit-trx-filename integration.trx --results-directory ""$resultsDirectory"" -bl:""$(Join-Path $logs 'tests-{}.binlog')"""
    Invoke-DotnetCheck 'tests' @('tool', 'run', 'coverlet', '--', $testAssembly,
        '--target', 'dotnet', '--targetargs', $testArguments,
        '--include', '[Neodash.ModLoader.Integration]*', '--include-test-assembly',
        '--exclude-by-file', '**/tests/**', '--exclude-by-file', '**/obj/**',
        '--exclude-by-attribute', 'GeneratedCodeAttribute', '--exclude-assemblies-without-sources', 'None',
        '--source-mapping-file', $coverageMapping, '--format', 'cobertura', '--format', 'json',
        '--output', ($coverageDirectory + [IO.Path]::DirectorySeparatorChar))

    [xml] $testResults = Get-Content -LiteralPath $testReport -Raw
    $counters = $testResults.TestRun.ResultSummary.Counters
    $testsTotal = [long] $counters.total
    $testsPassed = [long] $counters.passed
    if ($testsTotal -le 0 -or $testsPassed -ne $testsTotal -or [long] $counters.notExecuted -ne 0) {
        throw 'The test report must contain a nonempty, completely passing run with no skipped tests.'
    }

    [xml] $coverage = Get-Content -LiteralPath $coverageReport -Raw
    $linesValid = [long] $coverage.coverage.'lines-valid'
    $linesCovered = [long] $coverage.coverage.'lines-covered'
    if ($linesValid -le 0 -or $linesCovered -gt $linesValid) { throw 'Coverage collection returned invalid or empty core-source data.' }
    $summary = [ordered]@{
        scope = 'Linked core production sources; excludes tests, substitutes, generated code, and UI/runtime services not compiled into the harness.'
        linesCovered = $linesCovered
        linesValid = $linesValid
        linePercent = [Math]::Round(100.0 * $linesCovered / $linesValid, 2)
        branchesCovered = [long] $coverage.coverage.'branches-covered'
        branchesValid = [long] $coverage.coverage.'branches-valid'
        branchPercent = [Math]::Round(100.0 * [long] $coverage.coverage.'branches-covered' / [Math]::Max(1, [long] $coverage.coverage.'branches-valid'), 2)
    }
    $summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $coverageDirectory 'summary.json')
    Write-Host ("Core line coverage: {0}% ({1}/{2})." -f $summary.linePercent, $linesCovered, $linesValid)
    if ($env:GITHUB_STEP_SUMMARY) {
        @(
            "### $Configuration verification",
            '',
            "- Tests: $testsPassed / $testsTotal passed; skipped tests fail validation.",
            "- Linked core line coverage: $($summary.linePercent)% ($linesCovered / $linesValid).",
            '- Coverage excludes WinUI, test substitutes, and sources outside the isolated harness.'
        ) | Out-File -LiteralPath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
    }

    if ($SkipPublish) {
        Write-Host 'Build and regression tests passed; publish was intentionally skipped.'
        return
    }

    $propertyJson = & dotnet msbuild 'src/Neodash.ModLoader/Neodash.ModLoader.csproj' "-p:Configuration=$Configuration" '-p:Platform=x64' `
        '-getProperty:RuntimeIdentifier,SelfContained,WindowsAppSDKSelfContained,PublishSingleFile,Version,AssemblyVersion,FileVersion' `
        ('-bl:' + (Join-Path $logs 'publish-properties-{}.binlog')) '-v:quiet'
    if ($LASTEXITCODE -ne 0) { throw 'Could not evaluate publish settings.' }
    $properties = (($propertyJson -join [Environment]::NewLine) | ConvertFrom-Json).Properties
    if ($properties.RuntimeIdentifier -ne 'win-x64' -or $properties.SelfContained -ne 'true' -or
        $properties.WindowsAppSDKSelfContained -ne 'true' -or $properties.PublishSingleFile -ne 'true') {
        throw 'Publishing must include the .NET and Windows App SDK runtimes in a self-contained win-x64 single file.'
    }
    [xml] $packageManifest = Get-Content -LiteralPath (Join-Path $repository 'src/Neodash.ModLoader/Package.appxmanifest') -Raw
    $installerVersion = [regex]::Match((Get-Content -LiteralPath (Join-Path $repository 'installer.iss') -Raw), '(?m)^AppVersion=(.+)$').Groups[1].Value.Trim()
    if ($properties.Version -ne $properties.AssemblyVersion -or $properties.Version -ne $properties.FileVersion -or
        $properties.Version -ne $packageManifest.Package.Identity.Version -or $properties.Version -ne $installerVersion) {
        throw 'App, assembly, file, package manifest and installer versions must match.'
    }

    # Clear only the verified build output, and refuse to recurse through junctions or symbolic links.
    if (Test-Path -LiteralPath $publish) {
        $resolved = (Resolve-Path -LiteralPath $publish).ProviderPath
        if ($resolved -ne $publish -or !$resolved.StartsWith($repository + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Publish output must remain inside this repository.'
        }
        $entries = @((Get-Item -LiteralPath $artifacts), (Get-Item -LiteralPath $publish)) + @(Get-ChildItem -LiteralPath $publish -Recurse -Force)
        foreach ($entry in $entries) {
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Publish output contains a reparse point.' }
        }
        Remove-Item -LiteralPath $publish -Recurse -Force
    }
    Invoke-DotnetCheck 'publish' @('publish', 'src/Neodash.ModLoader/Neodash.ModLoader.csproj', '--no-build', '--no-restore', '-c', $Configuration,
        '-p:Platform=x64', '-p:ContinuousIntegrationBuild=true', '-p:TreatWarningsAsErrors=true', '-p:PublishReleaseAfterBuild=false', '-o', $publish)

    $files = @(Get-ChildItem -LiteralPath $publish -File -Recurse -Force)
    if ($files.Count -ne 1 -or $files[0].Name -ne 'Neodash Mod Loader.exe') {
        throw "Expected exactly Neodash Mod Loader.exe in publish output; found $($files.Count) files."
    }
    $publishedVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($files[0].FullName)
    if ($publishedVersion.FileVersion -ne $properties.Version -or $publishedVersion.ProductVersion.Split('+')[0] -ne $properties.Version) {
        throw 'Published EXE file and product versions must match the app version.'
    }
    $commit = & git rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Could not read the source commit for the published manifest.' }
    $commit = $commit.Trim()
    if (!$publishedVersion.ProductVersion.EndsWith('+' + $commit, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Published EXE must embed the source Git commit.'
    }
    $manifest = [ordered]@{
        file = $files[0].Name
        version = $publishedVersion.FileVersion
        bytes = $files[0].Length
        sha256 = (Get-FileHash -LiteralPath $files[0].FullName -Algorithm SHA256).Hash
        runtime = 'win-x64'
        selfContained = $true
        singleFile = $true
        commit = $commit
    }
    $manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $logs 'publish-manifest.json')
    if ($env:GITHUB_STEP_SUMMARY) {
        @('', "- Single EXE: $($manifest.file), version $($manifest.version).", "- Source commit: $commit.", "- SHA-256: $($manifest.sha256).") |
            Out-File -LiteralPath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
    }
    Write-Host 'Build, regression tests, and single-EXE publish passed.'
}
finally { Pop-Location }
