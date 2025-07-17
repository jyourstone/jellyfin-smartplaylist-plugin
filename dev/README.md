# Development Environment

This folder contains all the files needed for local development and testing of the SmartPlaylist plugin.

## Prerequisites

Make sure you have the following installed on your system:

- [Docker](https://www.docker.com/)
- [.NET SDK](https://dotnet.microsoft.com/)

## Files in this Folder

- **`build-local.sh`** – Bash script to build the plugin and restart the Docker container (Linux/macOS/WSL)
- **`build-local.ps1`** – PowerShell script to build the plugin and restart the Docker container (Windows)
- **`docker-compose.yml`** – Docker Compose configuration for local Jellyfin testing  
- **`meta-dev.json`** – Development version of the plugin metadata  
- **`jellyfin-data/`** – Jellyfin data, gets created when built (persistent across restarts)  
- **`media/`** – Media directories for testing (place your test media files here)  

## How to Use

1. **Build and Run the Plugin Locally:**
   
   **Linux/macOS/WSL:**
   ```bash
   ./build-local.sh
   ```
   
   **Windows PowerShell:**
   ```powershell
   .\build-local.ps1
   ```

2. **Access Jellyfin:**
    - Open [http://localhost:8096](http://localhost:8096) in your browser  
    - Complete the initial setup if it's your first time  
    - The plugin will appear as **"SmartPlaylist"** in the plugin list  

3. **Add Test Media:**
    - Place movie files in `dev/media/movies/`  
    - Place TV show files in `dev/media/shows/`
    - Place music files in `dev/media/music/`  

## Notes

- The build scripts automatically build the plugin and restart the Jellyfin Docker container.  
- Logs can be accessed in `/dev/jellyfin-data/config/log`  

- **`jellyfin-data`**: This directory stores Jellyfin's configuration and data, including logs, playlists, and user information. It's mounted into the container so your data persists across restarts. The `smartplaylists` folder inside `jellyfin-data/config/data` is where your created playlists are stored.
- **`meta-dev.json`**: This is a development-specific plugin manifest. It overrides the main `meta.json` during local builds.