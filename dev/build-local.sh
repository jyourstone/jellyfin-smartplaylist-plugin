#!/bin/bash

# This script builds the SmartLists plugin and prepares it for local Docker-based testing.
# It will also restart the Jellyfin Docker container to apply the changes.

set -e # Exit immediately if a command exits with a non-zero status.

# Set the version for the build. For local testing, this can be a static string.
VERSION="10.11.0.0"
OUTPUT_DIR="../build_output"

echo "Building SmartLists plugin version for local development..."

# Clean the previous build output
rm -rf $OUTPUT_DIR
mkdir -p $OUTPUT_DIR

# Build the project
dotnet build ../Jellyfin.Plugin.SmartLists/Jellyfin.Plugin.SmartLists.csproj --configuration Release -o $OUTPUT_DIR /p:Version=$VERSION /p:AssemblyVersion=$VERSION

# Copy the dev meta.json file, as it's required by Jellyfin to load the plugin
cp meta-dev.json $OUTPUT_DIR/meta.json

# Copy the logo image for local plugin display
cp ../images/logo.jpg $OUTPUT_DIR/logo.jpg

# Create the Configuration directory and copy the logging file for debug logs
mkdir -p $OUTPUT_DIR/Configuration
mkdir -p jellyfin-data/config/config
cp logging.json jellyfin-data/config/config/logging.json

echo ""
echo "Build complete."
echo "Restarting Jellyfin container to apply changes..."

# Stop the existing container (if any) and start a new one with the updated plugin files.
docker compose down
docker container prune -f
docker compose up --detach

echo ""
echo "Jellyfin container is up and running."
echo "You can access it at: http://localhost:8096" 