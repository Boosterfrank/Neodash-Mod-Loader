#Requires -Version 7.2
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$errors = [Collections.Generic.List[string]]::new()
$count = 0

foreach ($folder in @('eng', 'scripts')) {
    foreach ($script in Get-ChildItem -LiteralPath (Join-Path $repository $folder) -Filter '*.ps1' -File -Recurse) {
        $tokens = $null
        $parseErrors = $null
        $null = [Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref] $tokens, [ref] $parseErrors)
        $count++
        foreach ($parseError in $parseErrors) {
            $relative = [IO.Path]::GetRelativePath($repository, $script.FullName)
            $errors.Add("${relative}:$($parseError.Extent.StartLineNumber): $($parseError.Message)")
        }
    }
}

if ($count -eq 0) { throw 'No PowerShell verification or build scripts were found.' }
if ($errors.Count -gt 0) { throw ($errors -join [Environment]::NewLine) }
Write-Host "PowerShell syntax passed for $count scripts."
