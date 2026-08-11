$sonarrProcess = Start-Process -FilePath 'dotnet' -ArgumentList 'run --project c:\Users\ito\Desktop\Anidarr\Sonarr-5-develop\src\Sonarr.Console\Sonarr.Console.csproj --no-browser' -PassThru -WindowStyle Hidden
Write-Output 'Waiting for Sonarr to start...'
Start-Sleep -Seconds 20

$configPath = 'C:\ProgramData\Sonarr\config.xml'
if (-not (Test-Path $configPath)) {
    Write-Output 'Config not found'
    Stop-Process -Id $sonarrProcess.Id -Force
    exit 1
}
[xml]$config = Get-Content $configPath
$apiKey = $config.Config.ApiKey
Write-Output 'API Key: ' $apiKey

$headers = @{ 'X-Api-Key' = $apiKey }
$lookupUrl = 'http://localhost:8989/api/v5/series/lookup?term=anidb:10445'
$lookupResult = Invoke-RestMethod -Uri $lookupUrl -Headers $headers
$seriesToAdd = $lookupResult[0]

Write-Output 'Found Series: ' $seriesToAdd.title

$seriesToAdd.qualityProfileId = 1
$seriesToAdd.rootFolderPath = 'C:\Test\TV'
$seriesToAdd.addOptions = @{ monitor = 'all'; searchForMissingEpisodes = $false }

$addUrl = 'http://localhost:8989/api/v5/series'
$addResult = Invoke-RestMethod -Uri $addUrl -Method Post -Headers $headers -Body ($seriesToAdd | ConvertTo-Json -Depth 10) -ContentType 'application/json'

Write-Output 'Added Series ID: ' $addResult.id
Start-Sleep -Seconds 5

$verifyLookupResult = Invoke-RestMethod -Uri $lookupUrl -Headers $headers
$seriesAfterAdd = $verifyLookupResult[0]

Write-Output 'Mapped AniDB IDs after adding:'
$seriesAfterAdd.mappedAniDbIds | ForEach-Object { Write-Output $_ }

Stop-Process -Id $sonarrProcess.Id -Force
