$ErrorActionPreference = "SilentlyContinue"

docker rm -f anidarr_test_perf 2>&1 | Out-Null
docker volume rm anidarr_config 2>&1 | Out-Null

Write-Host "Measuring Cold Start..."
$sw = [System.Diagnostics.Stopwatch]::StartNew()
docker run -d --name anidarr_test_perf -p 8989:8989 -e PUID=1000 -e PGID=1000 -v anidarr_config:/config anidarr:cache2 2>&1 | Out-Null

while ($true) {
    curl.exe -s -f http://localhost:8989/ping 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) {
        break
    }
    Start-Sleep -Milliseconds 500
}
$sw.Stop()
Write-Host "Cold Start Time: $($sw.ElapsedMilliseconds) ms"

Write-Host "Stopping container..."
docker stop anidarr_test_perf 2>&1 | Out-Null

Write-Host "Measuring Warm Start..."
$sw = [System.Diagnostics.Stopwatch]::StartNew()
docker start anidarr_test_perf 2>&1 | Out-Null

while ($true) {
    curl.exe -s -f http://localhost:8989/ping 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) {
        break
    }
    Start-Sleep -Milliseconds 500
}
$sw.Stop()
Write-Host "Warm Start Time: $($sw.ElapsedMilliseconds) ms"
