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
    Write-Host "Kullanım: .\collect.ps1 -Id <ORCID, Google Scholar ID veya ResearcherID> veya .\collect.ps1 clean" -ForegroundColor Yellow
    exit 1
}

try {
    $identifiers = @(
        $Id |
            ForEach-Object { $_ -split '\s+' } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
    $request = @{}

    foreach ($identifier in $identifiers) {
        if ($identifier -match '^\d{4}-\d{4}-\d{4}-\d{3}[\dXx]$') {
            if ($request.ContainsKey("Orcid")) {
                throw "Birden fazla ORCID verildi."
            }

            $request.Orcid = $identifier.ToUpperInvariant()
        }
        elseif ($identifier -match '^[A-Za-z]{1,3}-\d{4}-\d{4}$') {
            if ($request.ContainsKey("WebOfScienceResearcherId")) {
                throw "Birden fazla Web of Science ResearcherID verildi."
            }

            $request.WebOfScienceResearcherId = $identifier.ToUpperInvariant()
        }
        elseif ($identifier -match '^[A-Za-z0-9_-]{12}$') {
            if ($request.ContainsKey("GoogleScholarId")) {
                throw "Birden fazla Google Scholar ID verildi."
            }

            $request.GoogleScholarId = $identifier
        }
        else {
            throw "Bilinmeyen kimlik biçimi: $identifier"
        }
    }

    $body = $request | ConvertTo-Json -Compress
    $uri = "$($BaseUrl.TrimEnd('/'))/Services/AcademicPerformance/V1/Collect"

    Write-Host "Toplama isteği gönderiliyor: $($Id -join ', ')"

    $response = Invoke-RestMethod `
        -Method Post `
        -Uri $uri `
        -ContentType "application/json; charset=utf-8" `
        -Body $body

    if ($response.Messages.Count -gt 0) {
        $response.Messages | ForEach-Object { Write-Host $_ }
    }

    $response | ConvertTo-Json -Depth 10
}
catch {
    $errorMessage = $_.ErrorDetails.Message

    if ([string]::IsNullOrWhiteSpace($errorMessage)) {
        $errorMessage = $_.Exception.Message
    }
    else {
        try {
            $errorResponse = $errorMessage | ConvertFrom-Json

            if (-not [string]::IsNullOrWhiteSpace($errorResponse.Error.Message)) {
                $errorMessage = $errorResponse.Error.Message
            }
        }
        catch {
            # Yanıt JSON değilse özgün hata metnini göster.
        }
    }

    Write-Host $errorMessage -ForegroundColor Red
    exit 1
}
