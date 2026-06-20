param(
    [Alias('t')]
    [string]$Type,

    [Alias('Types')]
    [string[]]$TypeList,

    [string]$Pattern,
    [int[]]$Context = @(0, 8),
    [int]$First = 0,

    [switch]$FoundOnly,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$IlspyArgs
)

$Dll = 'C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64\sts2.dll'
$env:PATH += ";$env:USERPROFILE\.dotnet\tools"

function Invoke-IlspyType {
    param([string]$TypeName)
    & ilspycmd $Dll -t $TypeName 2>&1
}

function Write-FilteredOutput {
    param(
        [object]$Output,
        [int]$ExitCode,
        [string]$TypeName,
        [bool]$ShowHeader
    )

    $filtered = if ($Pattern) {
        $matches = $Output | Select-String -Pattern $Pattern -Context $Context[0], $Context[1]
        if ($First -gt 0) { $matches | Select-Object -First $First } else { $matches }
    } elseif ($First -gt 0) {
        $Output | Select-Object -First $First
    } else {
        $Output
    }

    if ($FoundOnly) {
        if ($ExitCode -eq 0) {
            Write-Host "FOUND: $TypeName"
            $filtered
        }
        return
    }

    if ($ShowHeader) {
        Write-Host "=== $TypeName ==="
    }
    $filtered
}

if ($TypeList) {
    $typesToRun = @($TypeList)
} elseif ($Type) {
    $typesToRun = @($Type)
} elseif ($IlspyArgs.Count -gt 0) {
    & ilspycmd $Dll @IlspyArgs
    exit $LASTEXITCODE
} else {
    Write-Error 'Specify -Type, -Types, or pass ilspycmd arguments.'
    exit 1
}

$showHeader = ($typesToRun.Count -gt 1) -or $FoundOnly

foreach ($typeName in $typesToRun) {
    $output = Invoke-IlspyType $typeName
    $exitCode = $LASTEXITCODE
    Write-FilteredOutput -Output $output -ExitCode $exitCode -TypeName $typeName -ShowHeader $showHeader
}

exit $LASTEXITCODE
