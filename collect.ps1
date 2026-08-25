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

    Push-Location $projectRoot
    try {
        & dotnet clean

        if ($LASTEXITCODE -ne 0) {
            throw "dotnet clean hata koduyla tamamlandı: $LASTEXITCODE"
        }
    }
    finally {
        Pop-Location
    }

    Write-Host "Build çıktıları temizlendi. SQL Server veritabanına dokunulmadı." `
        -ForegroundColor Green
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
