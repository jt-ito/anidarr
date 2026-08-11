 = Start-Process -FilePath 'dotnet' -ArgumentList 'run --project c:\Users\ito\Desktop\Anidarr\Sonarr-5-develop\src\Sonarr.Console\Sonarr.Console.csproj --no-browser' -PassThru -WindowStyle Hidden

Write-Output 'Waiting for Sonarr to start...'
Start-Sleep -Seconds 20

# Get API key from config.xml
 = 'C:\ProgramData\Sonarr\config.xml'
if (-not (Test-Path )) {
    Write-Output 'Config not found at C:\ProgramData\Sonarr\config.xml'
    Stop-Process -Id .Id -Force
    exit 1
}
[xml] = Get-Content 
 = .Config.ApiKey
Write-Output 'API Key: ' 

# Search for Season 2 of a hub (e.g., SAO II, anidb:10445)
 = @{ 'X-Api-Key' =  }
 = 'http://localhost:8989/api/v5/series/lookup?term=anidb:10445'
 = Invoke-RestMethod -Uri  -Headers 
 = [0]

Write-Output 'Found Series: ' .title

# Prepare the series to add
.qualityProfileId = 1
.rootFolderPath = 'C:\Test\TV'
.addOptions = @{ monitor = 'all'; searchForMissingEpisodes = $false }

# Add the series
 = 'http://localhost:8989/api/v5/series'
 = Invoke-RestMethod -Uri  -Method Post -Headers  -Body ( | ConvertTo-Json -Depth 10) -ContentType 'application/json'

Write-Output 'Added Series ID: ' .id
Start-Sleep -Seconds 5

# Re-run lookup to verify mappedAniDbIds
 = Invoke-RestMethod -Uri  -Headers 
 = [0]

Write-Output 'Mapped AniDB IDs after adding:'
.mappedAniDbIds | ForEach-Object { Write-Output $_ }

Stop-Process -Id .Id -Force
