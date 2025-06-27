# Development Environment

This folder contains all the files needed for local development and testing of the SmartPlaylist plugin.

## Prerequisites

Make sure you have the following installed on your system:

- [Docker](https://www.docker.com/)
- [.NET SDK](https://dotnet.microsoft.com/)

For Windows users:

- [Windows Subsystem for Linux (WSL)](https://learn.microsoft.com/en-us/windows/wsl/install)

## Files in this Folder

- **`build-local.sh`** – Script to build the plugin and restart the Docker container  
- **`docker-compose.yml`** – Docker Compose configuration for local Jellyfin testing  
- **`meta-dev.json`** – Development version of the plugin metadata  
- **`jellyfin-data/`** – Jellyfin data, gets created when built (persistent across restarts)  
- **`movies/` & `tv/`** – Media directories for testing (place your test media files here)  

## How to Use

1. **Build and Run the Plugin Locally:**
    ```bash build-local.sh
    ```

2. **Access Jellyfin:**
    - Open [http://localhost:8096](http://localhost:8096) in your browser  
    - Complete the initial setup if it's your first time  
    - The plugin will appear as **"SmartPlaylist DEV"** in the plugin list  

3. **Add Test Media:**
    - Place movie files in `dev/movies/`  
    - Place TV show files in `dev/tv/`  
    - Jellyfin will automatically scan and add them to the library  

## Notes

- The `build-local.sh` script automatically builds the plugin and restarts the Jellyfin Docker container.  
- Plugin data persists in `jellyfin-data/config/data/smartplaylists/`  
- Logs can be accessed in `/dev/jellyfin-data/config/log`  

- **`jellyfin-data`**: This directory stores Jellyfin's configuration and data, including logs, playlists, and user information. It's mounted into the container so your data persists across restarts. The `smartplaylists` folder inside `jellyfin-data/config/data` is where your created playlists are stored.
- **`meta-dev.json`**: This is a development-specific plugin manifest. It overrides the main `meta.json` during local builds.