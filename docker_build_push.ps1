Set-Location $PSScriptRoot
Write-Host "Building Docker image for jteaito/anidarr:latest..."
docker build -t jteaito/anidarr:latest .

Write-Host "Pushing Docker image to Docker Hub..."
docker push jteaito/anidarr:latest

Write-Host "Done!"
