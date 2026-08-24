param(
    [Parameter(Position = 0)]
    [string[]] $Id,

    [ValidateNotNullOrEmpty()]
    [string] $BaseUrl = "http://localhost:5001",

    [switch] $Clean
)

$isCleanCommand = $Id.Count -eq 1 -and $Id[0] -ieq "clean"

function Invoke-ProjectClean {
    $projectRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
    $projectPrefix = $projectRoot.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $databasePaths = @(
        (Join-Path $projectRoot "academic.db"),
        (Join-Path $projectRoot "academic.db-shm"),
        (Join-Path $projectRoot "academic.db-wal")
    )

    foreach ($path in $databasePaths) {
        $fullPath = [System.IO.Path]::GetFullPath($path)

        if (-not $fullPath.StartsWith(
            $projectPrefix,
            [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Temizleme hedefi proje dışında: $fullPath"
        }
    }

    foreach ($databasePath in $databasePaths) {
        if (-not (Test-Path -LiteralPath $databasePath -PathType Leaf)) {
            continue
        }

        $stream = $null

        try {
            $stream = [System.IO.File]::Open(
                $databasePath,
                [System.IO.FileMode]::Open,
                [System.IO.FileAccess]::Read,
                [System.IO.FileShare]::None)
        }
        catch {
            throw "Veritabanı kullanımda veya erişilemiyor. Sunucuyu kapatıp tekrar dene: $databasePath"
        }
        finally {
            if ($null -ne $stream) {
                $stream.Dispose()
            }
        }
    }

    foreach ($databasePath in $databasePaths) {
        if (Test-Path -LiteralPath $databasePath -PathType Leaf) {
            Remove-Item -LiteralPath $databasePath -Force
        }
    }

    Write-Host "Yerel SQLite veritabanı silindi." -ForegroundColor Green
}

if ($Clean -or $isCleanCommand) {
    try {
        Invoke-ProjectClean
        exit 0
    }
    catch {
        Write-Host $_.Exception.Message -ForegroundColor Red
        exit 1
    }
}

if ($null -eq $Id -or $Id.Count -eq 0) {
    Write-Host "Kullanım: .\collect.ps1 -Id <ORCID veya ResearcherID> veya .\collect.ps1 clean" -ForegroundColor Yellow
    exit 1
}

$body = @{
    Identifiers = $Id
} | ConvertTo-Json -Compress

$uri = "$($BaseUrl.TrimEnd('/'))/Services/AcademicPerformance/Researcher/CollectText"

Write-Host "Toplama isteği gönderiliyor: $($Id -join ', ')"

try {
    Invoke-RestMethod `
        -Method Post `
        -Uri $uri `
        -ContentType "application/json; charset=utf-8" `
        -Body $body
}
catch {
    $errorMessage = $_.ErrorDetails.Message

    if ([string]::IsNullOrWhiteSpace($errorMessage)) {
        $errorMessage = $_.Exception.Message
    }

    Write-Host $errorMessage -ForegroundColor Red
    exit 1
}
