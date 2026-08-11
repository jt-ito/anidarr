Set-Location $PSScriptRoot

$buildInfoPath = Join-Path $PSScriptRoot "src\NzbDrone.Common\EnvironmentInfo\BuildInfo.cs"
$buildInfoContent = Get-Content $buildInfoPath -Raw
if ($buildInfoContent -match 'new Version\((\d+),\s*(\d+),\s*(\d+),\s*(\d+)\)') {
    $version = "$($matches[1]).$($matches[2]).$($matches[3]).$($matches[4])"
} else {
    Write-Error "Could not parse version from BuildInfo.cs"
    exit 1
}

Write-Host "Building Docker image for jteaito/anidarr:$version and latest..."
docker build -t jteaito/anidarr:$version -t jteaito/anidarr:latest .

Write-Host "Pushing Docker images to Docker Hub..."
docker push jteaito/anidarr:$version
docker push jteaito/anidarr:latest

Write-Host "Done!"
