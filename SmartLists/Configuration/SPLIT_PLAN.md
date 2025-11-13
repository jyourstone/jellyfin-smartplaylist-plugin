# Config.js Split Plan

## Overview
The `config.js` file is 7,752 lines with 187 functions. This document outlines the plan to split it into 10 manageable files.

## File Structure

### 1. `config-core.js` (~300 lines) ✅ CREATED
**Purpose:** Core constants, utilities, and shared functions
**Contains:**
- Constants (PLUGIN_ID, ENDPOINTS, FIELD_TYPES, etc.)
- Core utilities (escapeHtml, escapeHtmlAttribute, etc.)
- DOM helpers (getElementValue, setElementValue, etc.)
- Notification system (showNotification)
- Page state management (getPageEditState, setPageEditState)

### 2. `config-formatters.js` (~400 lines) ⏳ TODO
**Purpose:** Formatting and display functions
**Contains:**
- Time formatting (formatTimeForUser, formatTimeFallback, etc.)
- Runtime formatting (formatRuntime, formatRuntimeLong)
- Date formatting (formatRelativeTimeFromIso)
- Option generators (generateTimeOptions, generateAutoRefreshOptions, etc.)
- Display formatters (formatScheduleDisplay, formatSortDisplay, etc.)

### 3. `config-schedules.js` (~600 lines) ⏳ TODO
**Purpose:** Schedule management system
**Contains:**
- Schedule initialization (initializeScheduleSystem)
- Schedule UI (createScheduleBox, addScheduleBox, removeScheduleBox)
- Schedule collection (collectSchedulesFromForm)
- Schedule loading (loadSchedulesIntoUI)

### 4. `config-sorts.js` (~500 lines) ⏳ TODO
**Purpose:** Sort management system
**Contains:**
- Sort initialization (initializeSortSystem)
- Sort UI (createSortBox, addSortBox, removeSortBox)
- Sort collection (collectSortsFromForm)
- Sort loading (loadSortOptionsIntoUI)

### 5. `config-rules.js` (~1200 lines) ⏳ TODO
**Purpose:** Rule management and field handling
**Contains:**
- Rule creation (createInitialLogicGroup, addRuleToGroup)
- Rule removal (removeRule, removeLogicGroup)
- Field handling (handleTextFieldInput, handleNumericFieldInput, etc.)
- Field visibility (updateTagsOptionsVisibility, updateCollectionsOptionsVisibility, etc.)
- Field population (populateFieldSelect, updateAllFieldSelects)

### 6. `config-playlists.js` (~1500 lines) ⏳ TODO
**Purpose:** Playlist CRUD operations
**Contains:**
- Create (createPlaylist)
- Edit (editPlaylist)
- Clone (clonePlaylist)
- Delete (deletePlaylist)
- Refresh (refreshPlaylist)
- Enable/Disable (enablePlaylist, disablePlaylist)
- Playlist loading (loadPlaylistList, generatePlaylistCardHtml)

### 7. `config-filters.js` (~800 lines) ⏳ TODO
**Purpose:** Filtering and search
**Contains:**
- Filtering (filterPlaylists, applyFilter)
- Search (applySearchFilter, displayFilteredPlaylists)
- Filter setup (setupFilterEventListeners)
- Filter preferences (savePlaylistFilterPreferences, loadPlaylistFilterPreferences)

### 8. `config-bulk-actions.js` (~600 lines) ⏳ TODO
**Purpose:** Bulk operations
**Contains:**
- Bulk enable/disable (bulkEnablePlaylists, bulkDisablePlaylists)
- Bulk delete (bulkDeletePlaylists)
- Modals (showDeleteModal, showRefreshConfirmModal)
- Bulk action UI (updateBulkActionsVisibility, toggleSelectAll)

### 9. `config-api.js` (~400 lines) ⏳ TODO
**Purpose:** API calls and error handling
**Contains:**
- API error handling (handleApiError, displayApiError)
- User loading (loadUsers, loadUsersForRule)
- Field loading (loadAndPopulateFields)
- User resolution (resolveUsername, resolveUserIdToName)

### 10. `config-init.js` (~200 lines) ⏳ TODO
**Purpose:** Page initialization
**Contains:**
- Page initialization (initPage)
- Event listener setup (setupEventListeners, setupNavigation)
- Custom styles (applyCustomStyles)
- Tab management (switchToTab, setActiveTab)

## Implementation Pattern

Each file uses the IIFE pattern with a shared namespace:

```javascript
(function(SmartLists) {
    'use strict';
    
    // Initialize namespace if it doesn't exist
    if (!SmartLists) {
        window.SmartLists = {};
        SmartLists = window.SmartLists;
    }
    
    // Functions attached to namespace
    SmartLists.functionName = function() {
        // Implementation
    };
    
})(window.SmartLists = window.SmartLists || {});
```

## Load Order

Files must be loaded in this order:
1. `config-core.js` (foundation)
2. `config-formatters.js` (used by many)
3. `config-api.js` (used by many)
4. `config-schedules.js`
5. `config-sorts.js`
6. `config-rules.js` (depends on formatters, api)
7. `config-filters.js` (depends on formatters)
8. `config-bulk-actions.js` (depends on api, playlists)
9. `config-playlists.js` (depends on rules, schedules, sorts, api)
10. `config-init.js` (depends on all)

## Next Steps

1. ✅ Create config-core.js
2. ⏳ Create remaining files systematically
3. ⏳ Update Plugin.cs to register all files
4. ⏳ Update config.html to load scripts in order
5. ⏳ Update .csproj to include all files as embedded resources
6. ⏳ Test thoroughly

## Notes

- All functions must be attached to `SmartLists` namespace
- Use `SmartLists.functionName` when calling functions from other files
- Maintain ES5 compatibility (no arrow functions, template literals, etc.)
- Test each file as it's created to catch dependency issues early

