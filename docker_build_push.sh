#!/bin/bash
cd "$(dirname "$0")"

# Build the Docker image
echo "Building Docker image for jteaito/anidarr:latest..."
docker build -t jteaito/anidarr:latest .

# Push the Docker image
echo "Pushing Docker image to Docker Hub..."
docker push jteaito/anidarr:latest

echo "Done!"
