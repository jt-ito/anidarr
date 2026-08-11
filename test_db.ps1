$port = 8989
$url = "http://localhost:$port/api/v5/series"
$config = [xml](Get-Content 'C:\ProgramData\Sonarr\config.xml')
$apiKey = $config.Config.ApiKey

$headers = @{ 'X-Api-Key' = $apiKey }
$series = Invoke-RestMethod -Uri $url -Headers $headers
$target = $series | Select-Object -First 1

if ($target) {
    Write-Output "Found: $($target.Title)"
    Write-Output "AlternateTitles:"
    $target.alternateTitles | ConvertTo-Json -Depth 5
} else {
    Write-Output "No series found. Let's add one."
}
