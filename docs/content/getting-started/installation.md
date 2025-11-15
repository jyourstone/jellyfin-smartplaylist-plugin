# Installation

## From Repository

1. **Add the Repository**:
   - Go to **Dashboard** → **Plugins** → **Repositories** (or **Plugins** → **Manage Repositories** → **New Repository**)
   - Click **New Repository** (or the **+** button)
   - Enter the repository URL:
     ```
     https://raw.githubusercontent.com/jyourstone/jellyfin-plugin-manifest/main/manifest.json
     ```
   - Click **Save**

2. **Install the Plugin**:
   - Go to **Dashboard** → **Plugins** → **All/Available**
   - Click **SmartLists** in the list of available plugins
   - Click **Install**
   - Restart Jellyfin

## Manual Installation

Download the latest release from the [Releases page](https://github.com/jyourstone/jellyfin-smartplaylist-plugin/releases) and extract it to a subfolder in your Jellyfin plugins directory (for example `/config/plugins/SmartLists`) and restart Jellyfin.

## Try RC Releases (Unstable)

Want to test the latest features before they're officially released? You can try release candidate (RC) versions using the unstable manifest:

```
https://raw.githubusercontent.com/jyourstone/jellyfin-plugin-manifest/unstable/manifest.json
```

!!! warning "RC Releases"
    RC releases are pre-release versions that may contain bugs or incomplete features. Use at your own risk and consider backing up your smart list configurations before upgrading.

!!! tip "Dashboard Theme Recommendation"
    This plugin is best used with the **Dark** dashboard theme in Jellyfin. The plugin's custom styling is designed to match the dark theme, providing the best visual experience and consistency with the Jellyfin interface.