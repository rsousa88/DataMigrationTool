param(
    [decimal]$SoftCoverageTarget = 75,
    [switch]$EnforceCoverage
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repoRoot 'Dataverse.XrmTools\Dataverse.XrmTools.sln'
$testAssembly = Join-Path $repoRoot 'Dataverse.XrmTools\Dataverse.XrmTools.DataMigrationTool.Tests\bin\Debug\Dataverse.XrmTools.DataMigrationTool.Tests.dll'
$runSettings = Join-Path $repoRoot 'coverage.runsettings'
$coverageRoot = Join-Path $repoRoot 'TestResults\Coverage'
$runId = Get-Date -Format 'yyyyMMdd-HHmmss'
$runDirectory = Join-Path $coverageRoot $runId

function Resolve-ToolPath {
    param(
        [string]$Name,
        [string[]]$Candidates
    )

    foreach ($candidate in $Candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }

    throw "$Name was not found. Install Visual Studio test tools or update tools/Verify-BuildTestsCoverage.ps1."
}

function Get-CoveragePercent {
    param([string]$CoverageXmlPath)

    [xml]$coverage = Get-Content -LiteralPath $CoverageXmlPath

    $summaryNode = $coverage.SelectSingleNode('//*[local-name()="summary"]')
    if ($summaryNode -and $summaryNode.Attributes['line_coverage']) {
        return [decimal]$summaryNode.Attributes['line_coverage'].Value
    }

    $coverageNode = $coverage.SelectSingleNode('//*[local-name()="coverage"]')
    if ($coverageNode -and $coverageNode.Attributes['line-rate']) {
        return [decimal]$coverageNode.Attributes['line-rate'].Value * 100
    }

    $moduleNode = $coverage.SelectSingleNode('//*[local-name()="module" and @line_coverage]')
    if ($moduleNode) {
        return [decimal]$moduleNode.Attributes['line_coverage'].Value
    }

    return $null
}

$programFiles = ${env:ProgramFiles}
$programFilesX86 = ${env:ProgramFiles(x86)}

$msbuild = Resolve-ToolPath 'MSBuild.exe' @(
    (Join-Path $programFiles 'Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'),
    (Join-Path $programFiles 'Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe'),
    (Join-Path $programFilesX86 'Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe'),
    (Join-Path $programFilesX86 'Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin\MSBuild.exe')
)

$vstest = Resolve-ToolPath 'vstest.console.exe' @(
    (Join-Path $programFiles 'Microsoft Visual Studio\2022\Enterprise\Common7\IDE\Extensions\TestPlatform\vstest.console.exe'),
    (Join-Path $programFiles 'Microsoft Visual Studio\2022\Community\Common7\IDE\Extensions\TestPlatform\vstest.console.exe'),
    (Join-Path $programFilesX86 'Microsoft Visual Studio\2019\Enterprise\Common7\IDE\Extensions\TestPlatform\vstest.console.exe'),
    (Join-Path $programFilesX86 'Microsoft Visual Studio\2019\Community\Common7\IDE\Extensions\TestPlatform\vstest.console.exe')
)

$codeCoverage = Resolve-ToolPath 'CodeCoverage.exe' @(
    (Join-Path $env:USERPROFILE '.nuget\packages\microsoft.codecoverage\17.7.1\build\netstandard2.0\CodeCoverage\CodeCoverage.exe'),
    (Join-Path $env:USERPROFILE '.nuget\packages\microsoft.codecoverage\17.7.1\build\netstandard2.0\CodeCoverage\amd64\CodeCoverage.exe'),
    (Join-Path $programFiles 'Microsoft Visual Studio\2022\Enterprise\Team Tools\Dynamic Code Coverage Tools\CodeCoverage.exe'),
    (Join-Path $programFiles 'Microsoft Visual Studio\2022\Enterprise\Team Tools\Dynamic Code Coverage Tools\amd64\CodeCoverage.exe'),
    (Join-Path $programFiles 'Microsoft Visual Studio\2022\Community\Team Tools\Dynamic Code Coverage Tools\CodeCoverage.exe'),
    (Join-Path $programFiles 'Microsoft Visual Studio\2022\Community\Team Tools\Dynamic Code Coverage Tools\amd64\CodeCoverage.exe')
)

New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null

Write-Host 'Building solution...'
& $msbuild $solutionPath /p:Configuration=Debug /verbosity:minimal
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $testAssembly)) {
    throw "Test assembly was not found: $testAssembly"
}

Write-Host 'Running tests with code coverage...'
& $vstest $testAssembly /Settings:$runSettings /EnableCodeCoverage "/ResultsDirectory:$runDirectory"
if ($LASTEXITCODE -ne 0) {
    throw "Tests failed with exit code $LASTEXITCODE."
}

$coverageFile = Get-ChildItem -LiteralPath $runDirectory -Recurse -Filter '*.coverage' |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $coverageFile) {
    throw "No .coverage attachment was produced in $runDirectory."
}

$coverageXml = Join-Path $runDirectory 'coverage.xml'
Write-Host 'Converting coverage report...'
& $codeCoverage analyze "/output:$coverageXml" $coverageFile.FullName
if ($LASTEXITCODE -ne 0) {
    throw "Coverage conversion failed with exit code $LASTEXITCODE."
}

$coveragePercent = Get-CoveragePercent -CoverageXmlPath $coverageXml
if ($null -eq $coveragePercent) {
    Write-Warning "Coverage XML was generated, but the line coverage percentage could not be parsed: $coverageXml"
}
else {
    $roundedCoverage = [Math]::Round($coveragePercent, 2)
    Write-Host "Non-UI line coverage: $roundedCoverage% (soft target: $SoftCoverageTarget%)."

    if ($roundedCoverage -lt $SoftCoverageTarget) {
        $message = "Coverage is below the soft target. Continue extracting and testing non-UI logic."
        if ($EnforceCoverage) {
            throw $message
        }

        Write-Warning $message
    }
}

Write-Host "Coverage artifacts: $runDirectory"
