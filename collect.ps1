param(
    [Parameter(Position = 0)]
    [string[]] $Id,

    [ValidateNotNullOrEmpty()]
    [string] $BaseUrl = "http://localhost:5000",

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
    $storagePath = Join-Path $projectRoot "Storage"

    foreach ($path in @($databasePaths) + $storagePath) {
        $fullPath = [System.IO.Path]::GetFullPath($path)

        if (-not $fullPath.StartsWith(
            $projectPrefix,
            [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Temizleme hedefi proje dışında: $fullPath"
        }
    }

    $storageItem = $null

    if (Test-Path -LiteralPath $storagePath) {
        $storageItem = Get-Item -LiteralPath $storagePath -Force
        $linkTypeProperty = $storageItem.PSObject.Properties["LinkType"]
        $targetProperty = $storageItem.PSObject.Properties["Target"]
        $hasLinkType = $null -ne $linkTypeProperty -and
            -not [string]::IsNullOrWhiteSpace([string] $linkTypeProperty.Value)
        $hasLinkTarget = $null -ne $targetProperty -and
            $null -ne $targetProperty.Value -and
            @($targetProperty.Value).Count -gt 0

        if (($storageItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 -and
            ($hasLinkType -or $hasLinkTarget)) {
            throw "Storage bir bağlantı olduğu için otomatik silinmedi: $storagePath"
        }

        if (-not $storageItem.PSIsContainer) {
            throw "Storage hedefi bir klasör değil: $storagePath"
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

    if ($null -ne $storageItem) {
        Remove-Item -LiteralPath $storagePath -Recurse -Force
    }

    Write-Host "Yerel SQLite veritabanı ve Storage klasörü silindi." -ForegroundColor Green
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
    Write-Host "Kullanım: .\collect.ps1 -Id <ORCID> veya .\collect.ps1 clean" -ForegroundColor Yellow
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
