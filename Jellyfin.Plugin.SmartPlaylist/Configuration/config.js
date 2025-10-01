(function () {
    'use strict';
    
    // Constants
    const PLUGIN_ID = "A0A2A7B2-747A-4113-8B39-757A9D267C79";
    const ENDPOINTS = {
        fields: 'Plugins/SmartPlaylist/fields',
        base: 'Plugins/SmartPlaylist',
        users: 'Plugins/SmartPlaylist/users',
        refresh: 'Plugins/SmartPlaylist/refresh',
        refreshDirect: 'Plugins/SmartPlaylist/refresh-direct',
        export: 'Plugins/SmartPlaylist/export',
        import: 'Plugins/SmartPlaylist/import'
    };
    
    // Field type constants to avoid duplication
    const FIELD_TYPES = {
        LIST_FIELDS: ['Collections', 'People', 'Genres', 'Studios', 'Tags', 'Artists', 'AlbumArtists'],
        NUMERIC_FIELDS: ['ProductionYear', 'CommunityRating', 'CriticRating', 'RuntimeMinutes', 'PlayCount', 'Framerate'],
        DATE_FIELDS: ['DateCreated', 'DateLastRefreshed', 'DateLastSaved', 'DateModified', 'ReleaseDate', 'LastPlayedDate'],
        BOOLEAN_FIELDS: ['IsPlayed', 'IsFavorite', 'NextUnwatched'],
        SIMPLE_FIELDS: ['ItemType'],
        RESOLUTION_FIELDS: ['Resolution'],
        USER_DATA_FIELDS: ['IsPlayed', 'IsFavorite', 'PlayCount', 'NextUnwatched', 'LastPlayedDate']
    };
    
    // Helper functions to generate common option sets (DRY principle)
    function generateTimeOptions(defaultValue) {
        var options = [];
        for (var hour = 0; hour < 24; hour++) {
            for (var minute = 0; minute < 60; minute += 15) {
                var timeValue = (hour < 10 ? '0' : '') + hour + ':' + (minute < 10 ? '0' : '') + minute;
                var displayTime = formatTimeForUser(hour, minute);
                var selected = timeValue === defaultValue;
                options.push({ value: timeValue, label: displayTime, selected: selected });
            }
        }
        return options;
    }
    
    // Format time according to user's locale preferences
    function formatTimeForUser(hour, minute) {
        // Use browser locale for time formatting
        var locale = navigator.language || navigator.userLanguage || 'en-US';
        
        // Create a Date object for formatting
        var date = new Date();
        date.setHours(hour);
        date.setMinutes(minute);
        date.setSeconds(0);
        
        // Use Intl.DateTimeFormat for locale-aware time formatting
        try {
            return new Intl.DateTimeFormat(locale, {
                hour: 'numeric',
                minute: '2-digit',
                hour12: isLocale12Hour(locale)
            }).format(date);
        } catch (e) {
            // Fallback to manual formatting if Intl is not available
            return formatTimeFallback(hour, minute, isLocale12Hour(locale));
        }
    }
    
    
    // Determine if locale uses 12-hour format
    function isLocale12Hour(locale) {
        try {
            return new Intl.DateTimeFormat(locale, { hour: 'numeric' })
                .resolvedOptions().hour12 === true;
        } catch (_) {
            // Fallback heuristic: default to 24h if unsure
            return false;
        }
    }

    // Format relative time from ISO string (e.g., "2 minutes ago", "3 hours ago")
    function formatRelativeTimeFromIso(isoString, emptyText = 'Unknown') {
        if (!isoString) return emptyText;
        const ts = Date.parse(isoString);
        if (Number.isNaN(ts)) return emptyText;
        const diffMins = Math.floor((Date.now() - ts) / 60000);
        if (diffMins < 1) return 'Just now';
        if (diffMins < 60) return diffMins + ' minute' + (diffMins === 1 ? '' : 's') + ' ago';
        const diffHours = Math.floor(diffMins / 60);
        if (diffHours < 24) return diffHours + ' hour' + (diffHours === 1 ? '' : 's') + ' ago';
        const diffDays = Math.floor(diffHours / 24);
        return diffDays + ' day' + (diffDays === 1 ? '' : 's') + ' ago';
    }

    // Toggle schedule containers based on trigger value (DRY helper)
    function toggleScheduleContainers(page, prefix, triggerValue) {
        const timeContainer = page.querySelector(`#${prefix}scheduleTimeContainer`);
        const dayContainer = page.querySelector(`#${prefix}scheduleDayContainer`);
        const dayOfMonthContainer = page.querySelector(`#${prefix}scheduleDayOfMonthContainer`);
        const intervalContainer = page.querySelector(`#${prefix}scheduleIntervalContainer`);

        [timeContainer, dayContainer, dayOfMonthContainer, intervalContainer].forEach(el => { if (el) el.classList.add('hide'); });

        if (triggerValue === 'Daily') {
            if (timeContainer) timeContainer.classList.remove('hide');
        } else if (triggerValue === 'Weekly') {
            if (timeContainer) timeContainer.classList.remove('hide');
            if (dayContainer) dayContainer.classList.remove('hide');
        } else if (triggerValue === 'Monthly') {
            if (timeContainer) timeContainer.classList.remove('hide');
            if (dayOfMonthContainer) dayOfMonthContainer.classList.remove('hide');
        } else if (triggerValue === 'Interval') {
            if (intervalContainer) intervalContainer.classList.remove('hide');
        }
    }
    
    // Fallback time formatting for older browsers
    function formatTimeFallback(hour, minute, use12Hour) {
        var displayMinute = minute < 10 ? '0' + minute : minute;
        var displayHour; // Declare once to avoid redeclaration error
        
        if (use12Hour) {
            displayHour = hour === 0 ? 12 : (hour > 12 ? hour - 12 : hour);
            var ampm = hour < 12 ? 'AM' : 'PM';
            return displayHour + ':' + displayMinute + ' ' + ampm;
        } else {
            displayHour = hour < 10 ? '0' + hour : hour;
            return displayHour + ':' + displayMinute;
        }
    }
    
    function generateAutoRefreshOptions(defaultValue) {
        var options = [
            { value: 'Never', label: 'Never - Manual/scheduled refresh only' },
            { value: 'OnLibraryChanges', label: 'On library changes - When items are added/removed' },
            { value: 'OnAllChanges', label: 'On all changes - Including playback status changes' }
        ];
        // Mark the default option as selected
        for (var i = 0; i < options.length; i++) {
            options[i].selected = options[i].value === defaultValue;
        }
        return options;
    }
    
    function generateScheduleTriggerOptions(defaultValue, includeNoSchedule) {
        var options = [];
        if (includeNoSchedule) {
            options.push({ value: '', label: 'No schedule' });
        }
        options.push(
            { value: 'Daily', label: 'Daily' },
            { value: 'Weekly', label: 'Weekly' },
            { value: 'Monthly', label: 'Monthly' },
            { value: 'Interval', label: 'Interval' }
        );
        // Mark the default option as selected
        for (var i = 0; i < options.length; i++) {
            options[i].selected = options[i].value === defaultValue;
        }
        return options;
    }
    
    // Helper function to generate summary text with consistent styling
    function generateSummaryText(totalPlaylists, enabledPlaylists, disabledPlaylists, filteredCount = null, searchTerm = null) {
        const bulletStyle = 'margin: 0 0.25em;';
        const bullet = '<span style="' + bulletStyle + '">•</span>';
        
        let summaryText = '<strong>Summary:&nbsp;</strong> ';
        
        if (filteredCount !== null && filteredCount !== totalPlaylists) {
            // Filtered results
            summaryText += filteredCount + ' of ' + totalPlaylists + ' playlist' + (totalPlaylists !== 1 ? 's' : '');
            if (searchTerm) {
                summaryText += ' matching "' + escapeHtml(searchTerm) + '"';
            }
        } else {
            // All playlists
            summaryText += totalPlaylists + ' playlist' + (totalPlaylists !== 1 ? 's' : '');
        }
        
        summaryText += ' ' + bullet + ' ' + enabledPlaylists + ' enabled ' + bullet + ' ' + disabledPlaylists + ' disabled';
        
        return summaryText;
    }

    // Helper function to generate bulk actions HTML
    function generateBulkActionsHTML(summaryText) {
        let html = '';
        html += '<div class="inputContainer" id="bulkActionsContainer" style="margin-bottom: 1em; display: none;">';
        html += '<div class="paperList" style="padding: 1em; background-color: #202020; border-radius: 4px;">';
        
        // Summary row at top
        html += '<div id="playlist-summary" class="field-description" style="margin: 0 0 1em 0; padding: 0.5em; background: #2A2A2A; border-radius: 4px;">';
        html += summaryText;
        html += '</div>';
        
        // Layout: Left side (Select All, bulk actions) | Right side (Expand All, Reload List)
        html += '<div style="display: flex; align-items: center; justify-content: flex-start; flex-wrap: wrap; gap: 0.5em;">';
        
        // Left side: Select All checkbox and bulk action buttons
        html += '<div style="display: flex; align-items: center; gap: 0.25em; flex-wrap: wrap;">';
        
        // 1. Select All checkbox
        html += '<label class="emby-checkbox-label" style="width: auto; min-width: auto;">';
        html += '<input type="checkbox" id="selectAllCheckbox" data-embycheckbox="true" class="emby-checkbox">';
        html += '<span class="checkboxLabel">Select All</span>';
        html += '<span class="checkboxOutline">';
        html += '<span class="material-icons checkboxIcon checkboxIcon-checked check" aria-hidden="true"></span>';
        html += '<span class="material-icons checkboxIcon checkboxIcon-unchecked" aria-hidden="true"></span>';
        html += '</span>';
        html += '</label>';
        
        // Selected count display
        html += '<span id="selectedCountDisplay" class="fieldDescription" style="color: #999; margin-right: 0.75em; font-style: italic;">(0 selected)</span>';
        
        // 2. Enable button
        html += '<button type="button" id="bulkEnableBtn" class="emby-button raised" disabled>Enable</button>';
        
        // 3. Disable button
        html += '<button type="button" id="bulkDisableBtn" class="emby-button raised" disabled>Disable</button>';
        
        // 4. Delete button
        html += '<button type="button" id="bulkDeleteBtn" class="emby-button raised button-delete" disabled>Delete</button>';
        
        html += '</div>'; // End left side
        
        // Right side: View control buttons
        html += '<div style="display: flex; align-items: center; gap: 0.25em; flex-wrap: wrap; margin-left: auto;">';
        
        // 5. Expand All button
        html += '<button type="button" id="expandAllBtn" class="emby-button raised">Expand All</button>';
        
        // 6. Reload List button
        html += '<button type="button" id="refreshPlaylistListBtn" class="emby-button raised">Reload List</button>';
        
        html += '</div>'; // End right side
        html += '</div>'; // End flex container
        html += '</div>'; // End paperList
        html += '</div>'; // End inputContainer
        
        return html;
    }
    
    function generateDayOfWeekOptions(defaultValue) {
        var days = [
            { value: '0', label: 'Sunday' },
            { value: '1', label: 'Monday' },
            { value: '2', label: 'Tuesday' },
            { value: '3', label: 'Wednesday' },
            { value: '4', label: 'Thursday' },
            { value: '5', label: 'Friday' },
            { value: '6', label: 'Saturday' }
        ];
        // Mark the default option as selected
        for (var i = 0; i < days.length; i++) {
            days[i].selected = days[i].value === defaultValue;
        }
        return days;
    }
    
    function generateDayOfMonthOptions(defaultValue) {
        var days = [];
        for (var i = 1; i <= 31; i++) {
            var suffix = '';
            if (i === 1 || i === 21 || i === 31) suffix = 'st';
            else if (i === 2 || i === 22) suffix = 'nd';
            else if (i === 3 || i === 23) suffix = 'rd';
            else suffix = 'th';
            
            days.push({ value: i.toString(), label: i + suffix });
        }
        // Mark the default option as selected
        for (var j = 0; j < days.length; j++) {
            days[j].selected = days[j].value === defaultValue;
        }
        return days;
    }
    
    function convertDayOfWeekToValue(dayOfWeek) {
        if (dayOfWeek === undefined || dayOfWeek === null) {
            return '0'; // Default to Sunday
        }
        
        // Handle numeric values (0-6)
        if (typeof dayOfWeek === 'number') {
            return dayOfWeek.toString();
        }
        
        // Handle string values ("Sunday", etc.)
        if (typeof dayOfWeek === 'string') {
            const dayMap = {
                'Sunday': '0', 'Monday': '1', 'Tuesday': '2', 'Wednesday': '3',
                'Thursday': '4', 'Friday': '5', 'Saturday': '6'
            };
            return dayMap[dayOfWeek] || '0';
        }
        
        return '0'; // Fallback to Sunday
    }

    function generateIntervalOptions(defaultValue) {
        var intervals = [
            { value: '00:15:00', label: '15 minutes' },
            { value: '00:30:00', label: '30 minutes' },
            { value: '01:00:00', label: '1 hour' },
            { value: '02:00:00', label: '2 hours' },
            { value: '03:00:00', label: '3 hours' },
            { value: '04:00:00', label: '4 hours' },
            { value: '06:00:00', label: '6 hours' },
            { value: '08:00:00', label: '8 hours' },
            { value: '12:00:00', label: '12 hours' },
            { value: '1.00:00:00', label: '24 hours' }
        ];
        // Mark the default option as selected
        for (var i = 0; i < intervals.length; i++) {
            intervals[i].selected = intervals[i].value === defaultValue;
        }
        return intervals;
    }
    
    // Enhanced HTML escaping function to prevent XSS vulnerabilities
    function escapeHtml(text) {
        if (text == null) return ''; // Only treat null/undefined as empty
        
        // Convert to string and handle common cases efficiently
        const str = String(text);
        
        // Use standard HTML character escaping
        return str
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#x27;')
            .replace(/=/g, '&#x3D;');
    }
    
    // Standardized API error handling system
    async function handleApiResponse(response) {
        if (!response.ok) {
            let errorMessage = 'Unknown error occurred';
            
            try {
                const contentType = response.headers.get('content-type');
                if (contentType && contentType.includes('application/json')) {
                    const errorData = await response.json();
                    // Handle both direct error messages and wrapped error objects
                    if (typeof errorData === 'string') {
                        errorMessage = errorData;
                    } else if (errorData.message) {
                        errorMessage = errorData.message;
                    } else if (errorData.error) {
                        errorMessage = errorData.error;
                    } else {
                        errorMessage = JSON.stringify(errorData);
                    }
                } else {
                    // Try to get text content for non-JSON responses
                    const textContent = await response.text();
                    if (textContent) {
                        // Handle JSON-encoded strings from backend
                        try {
                            errorMessage = JSON.parse(textContent);
                        } catch {
                            errorMessage = textContent;
                        }
                    } else {
                        errorMessage = `HTTP ${response.status}: ${response.statusText}`;
                    }
                }
            } catch (parseError) {
                console.error('Error parsing API error response:', parseError);
                errorMessage = `HTTP ${response.status}: ${response.statusText}`;
            }
            
            throw new ApiError(errorMessage, response.status);
        }
        
        return response;
    }
    
    // Custom error class for API errors
    class ApiError extends Error {
        constructor(message, status) {
            super(message);
            this.name = 'ApiError';
            this.status = status;
        }
    }
    
    // Standardized API call wrapper with network error handling
    async function makeApiCall(apiClient, method, url, data = null, options = {}) {
        try {
            const config = {
                type: method.toUpperCase(),
                url: apiClient.getUrl(url),
                contentType: 'application/json',
                ...options
            };
            
            if (data && (method.toUpperCase() === 'POST' || method.toUpperCase() === 'PUT')) {
                config.data = JSON.stringify(data);
            }
            
            const response = await apiClient.ajax(config);
            return await handleApiResponse(response);
        } catch (error) {
            console.error(`API call failed: ${method} ${url}`, error);
            
            // Handle different types of network errors
            if (error instanceof ApiError) {
                // Already an API error, just re-throw
                throw error;
            } else if (error.name === 'NetworkError' || error.name === 'TypeError') {
                // Network connectivity issues
                throw new ApiError('Network connection failed. Please check your internet connection and try again.', 0);
            } else if (error.name === 'TimeoutError' || error.message?.includes('timeout')) {
                // Request timeout
                throw new ApiError('Request timed out. Please try again.', 408);
            } else if (error.name === 'AbortError') {
                // Request was cancelled
                throw new ApiError('Request was cancelled.', 0);
            } else {
                // Generic network/connection error
                const message = error.message || 'Network request failed';
                throw new ApiError(`Connection error: ${message}`, 0);
            }
        }
    }
    
    // Standardized error display function
    function displayApiError(error, context = '') {
        let message = 'An unexpected error occurred, check the logs for more details.';
        
        if (error instanceof ApiError) {
            message = error.message;
        } else if (error && error.message) {
            message = error.message;
        } else if (typeof error === 'string') {
            message = error;
        }
        
        const contextPrefix = context ? context + ': ' : '';
        const fullMessage = contextPrefix + message;
        
        console.error('API Error:', fullMessage, error);
        showNotification(fullMessage, 'error');
        
        return fullMessage;
    }
    
    // Safe HTML attribute escaping for use in HTML attributes
    function escapeHtmlAttribute(text) {
        if (text == null) return '';
        return String(text)
            .replace(/&/g, '&amp;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#x27;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;');
    }

    // Safe DOM manipulation helper to prevent XSS vulnerabilities
    // Accepts an array of {value, label, selected} objects
    function populateSelectElement(selectElement, optionsData) {
        // Clear existing options
        selectElement.innerHTML = '';
        
        if (Array.isArray(optionsData)) {
            // Create option elements programmatically
            optionsData.forEach(function(optionData) {
                var option = document.createElement('option');
                option.value = optionData.value || '';
                option.textContent = optionData.label || optionData.value || '';
                if (optionData.selected) {
                    option.selected = true;
                }
                selectElement.appendChild(option);
            });
        }
    }
    
    // DOM Helper Functions to reduce repetition and improve maintainability
    
    /**
     * Get element value safely with optional default
     */
    function getElementValue(page, selector, defaultValue = '') {
        const element = page.querySelector(selector);
        return element ? element.value : defaultValue;
    }
    
    /**
     * Get element checked state safely with optional default
     */
    function getElementChecked(page, selector, defaultValue = false) {
        const element = page.querySelector(selector);
        return element ? element.checked : defaultValue;
    }
    
    /**
     * Set element value safely (only if element exists)
     */
    function setElementValue(page, selector, value) {
        const element = page.querySelector(selector);
        if (element) {
            element.value = value;
            return true;
        }
        return false;
    }
    
    /**
     * Set element checked state safely (only if element exists)
     */
    function setElementChecked(page, selector, checked) {
        const element = page.querySelector(selector);
        if (element) {
            element.checked = checked;
            return true;
        }
        return false;
    }   
    
    // Centralized styling configuration
    const STYLES = {
        // Logic group styles
        logicGroup: {
            border: '1px solid #666',
            borderRadius: '2px',
            padding: '1.5em 1.5em 0.5em 1.5em',
            marginBottom: '1em',
            background: 'rgba(255, 255, 255, 0.05)',
            boxShadow: '0 2px 8px rgba(0, 0, 0, 0.3)',
            position: 'relative'
        },

        // Rule action buttons
        buttons: {
            action: {
                base: {
                    padding: '0.3em 0.8em',
                    fontSize: '0.8em',
                    border: '1px solid #666',
                    background: 'rgba(255, 255, 255, 0.1)',
                    color: '#aaa',
                    borderRadius: '4px',
                    cursor: 'pointer',
                    fontWeight: '500'
                }
            },
            delete: {
                base: {
                    padding: '0.3em 0.8em',
                    fontSize: '0.8em',
                    border: '1px solid #666',
                    background: 'rgba(255, 255, 255, 0.07)',
                    color: '#aaa',
                    borderRadius: '4px',
                    cursor: 'pointer',
                    fontWeight: '500',
                }
            }
        },

        // Separator styles
        separators: {
            and: {
                textAlign: 'center',
                margin: '0.8em 0',
                color: '#888',
                fontSize: '0.8em',
                fontWeight: 'bold',
                position: 'relative',
                padding: '0.3em 0'
            },
            or: {
                textAlign: 'center',
                margin: '1em 0',
                position: 'relative'
            },
            orText: {
                background: '#1a1a1a',
                color: '#bbb',
                padding: '0.4em',
                borderRadius: '4px',
                fontWeight: 'bold',
                fontSize: '0.9em',
                position: 'relative',
                zIndex: '2',
                display: 'inline-block',
                border: '1px solid #777',
                boxShadow: '0 2px 6px rgba(0, 0, 0, 0.4)'
            },
            orLine: {
                position: 'absolute',
                top: '50%',
                left: '0',
                right: '0',
                height: '2px',
                background: 'linear-gradient(to right, transparent, #777, transparent)',
                zIndex: '1'
            },
            andLine: {
                position: 'absolute',
                top: '50%',
                left: '20%',
                right: '20%',
                height: '1px',
                background: 'rgba(136, 136, 136, 0.3)',
                zIndex: '1'
            }
        },

        // Modal styles
        modal: {
            container: {
                position: 'fixed',
                top: '50%',
                left: '50%',
                transform: 'translate(-50%, -50%)',
                zIndex: '10001',
                backgroundColor: '#101010',
                padding: '1.5em',
                width: '90%',
                maxWidth: '400px'
            },
            backdrop: {
                position: 'fixed',
                top: '0',
                left: '0',
                width: '100%',
                height: '100%',
                backgroundColor: 'rgba(0,0,0,0.5)',
                zIndex: '10000'
            }
        },


        // Layout fixes
        layout: {
            tabContent: {
                maxWidth: '830px',
                boxSizing: 'border-box',
                paddingRight: '25px'
            }
            // Notification styles removed - now using floating notifications
        }
    };
    
    // Constants for operators
    const RELATIVE_DATE_OPERATORS = ['NewerThan', 'OlderThan'];
    const MULTI_VALUE_OPERATORS = ['IsIn', 'IsNotIn'];

    // Utility functions for applying styles
    function applyStyles(element, styles) {
        if (!element || !styles) return;
        
        Object.entries(styles).forEach(([property, value]) => {
            // Convert camelCase to kebab-case
            const cssProperty = property.replace(/([A-Z])/g, '-$1').toLowerCase();
            element.style.setProperty(cssProperty, value, 'important');
        });
    }

    function createStyledElement(tagName, className, styles) {
        const element = document.createElement(tagName);
        if (className) element.className = className;
        if (styles) applyStyles(element, styles);
        return element;
    }

    function styleRuleActionButton(button, buttonType) {
        // Map and/or buttons to shared 'action' styling
        const styleKey = (buttonType === 'and' || buttonType === 'or') ? 'action' : buttonType;
        const buttonStyles = STYLES.buttons[styleKey];
        if (!buttonStyles) return;
        
        const styles = buttonStyles.base;
        applyStyles(button, styles);
    }

    function createAndSeparator() {
        const separator = createStyledElement('div', 'rule-within-group-separator', STYLES.separators.and);
        separator.textContent = 'AND';
        
        const line = createStyledElement('div', '', STYLES.separators.andLine);
        separator.appendChild(line);
        
        return separator;
    }

    function createOrSeparator() {
        const separator = createStyledElement('div', 'logic-group-separator', STYLES.separators.or);
        
        const orText = createStyledElement('span', '', STYLES.separators.orText);
        orText.textContent = 'OR';
        separator.appendChild(orText);
        
        const gradientLine = createStyledElement('div', '', STYLES.separators.orLine);
        separator.appendChild(gradientLine);
        
        return separator;
    }

    function getPluginId() {
        return PLUGIN_ID;
    }

    let availableFields = {};
    let notificationTimeout;
    // Remove global flags - these will be stored per page
    // let pageInitialized = false;
    // let tabListenersInitialized = false;
    // REMOVED: Global edit state - now stored per page
    // let editMode = false;
    // let editingPlaylistId = null;
    // Remove global modal handler - this will be stored per modal
    // let currentModalBackdropHandler = null;
    const mediaTypes = [ 
        { Value: "Movie", Label: "Movie" }, 
        { Value: "Episode", Label: "Episode" }, 
        { Value: "Audio", Label: "Audio (Music)" },
        { Value: "MusicVideo", Label: "Music Video" },
        { Value: "Video", Label: "Video (Home Video)" },
        { Value: "Photo", Label: "Photo (Home Photo)" },
        { Value: "Book", Label: "Book" },
        { Value: "AudioBook", Label: "Audiobook" }
    ];

    // Generate media type checkboxes from the mediaTypes array
    const generateMediaTypeCheckboxes = (page) => {
        const container = page.querySelector('#media-types-container');
        if (!container) return;
        
        // Clear existing content
        container.innerHTML = '';
        
        // Create one big checkboxList paperList container
        const mainContainer = document.createElement('div');
        mainContainer.className = 'checkboxList paperList';
        mainContainer.style.cssText = 'padding: 0.5em 1em; margin: 0; display: block;';
        
        // Generate checkboxes for each media type
        mediaTypes.forEach(mediaType => {
            const sectionCheckbox = document.createElement('div');
            sectionCheckbox.className = 'sectioncheckbox';
            
            const label = document.createElement('label');
            label.className = 'emby-checkbox-label';
            
            const checkbox = document.createElement('input');
            checkbox.type = 'checkbox';
            checkbox.setAttribute('is', 'emby-checkbox');
            checkbox.setAttribute('data-embycheckbox', 'true');
            checkbox.id = `mediaType${mediaType.Value}`;
            checkbox.className = 'emby-checkbox media-type-checkbox';
            checkbox.value = mediaType.Value;
            
            const span = document.createElement('span');
            span.className = 'checkboxLabel';
            span.textContent = mediaType.Label;
            
            const checkboxOutline = document.createElement('span');
            checkboxOutline.className = 'checkboxOutline';
            
            const checkedIcon = document.createElement('span');
            checkedIcon.className = 'material-icons checkboxIcon checkboxIcon-checked check';
            checkedIcon.setAttribute('aria-hidden', 'true');
            
            const uncheckedIcon = document.createElement('span');
            uncheckedIcon.className = 'material-icons checkboxIcon checkboxIcon-unchecked';
            uncheckedIcon.setAttribute('aria-hidden', 'true');
            
            checkboxOutline.appendChild(checkedIcon);
            checkboxOutline.appendChild(uncheckedIcon);
            
            label.appendChild(checkbox);
            label.appendChild(span);
            label.appendChild(checkboxOutline);
            sectionCheckbox.appendChild(label);
            mainContainer.appendChild(sectionCheckbox);
        });
        
        container.appendChild(mainContainer);
    };

    // Helper function to manage search input state with better error handling
    const setSearchInputState = (page, disabled, placeholder = 'Search playlists...') => {
        try {
            const searchInput = page.querySelector('#playlistSearchInput');
            const clearSearchBtn = page.querySelector('#clearSearchBtn');
            
            if (searchInput) {
                searchInput.disabled = disabled;
                searchInput.placeholder = placeholder;
                
                // Store original state to restore later if needed
                if (!page._originalSearchState) {
                    page._originalSearchState = {
                        disabled: false,
                        placeholder: 'Search playlists...'
                    };
                }
            }
            
            // Only hide clear button when disabling, let updateClearButtonVisibility handle showing it
            if (clearSearchBtn && disabled) {
                clearSearchBtn.style.display = 'none';
            }
        } catch (err) {
            console.warn('Failed to update search input state:', err);
            // Ensure we don't leave search permanently disabled
            try {
                const searchInput = page.querySelector('#playlistSearchInput');
                if (searchInput && disabled) {
                    // Fallback: re-enable search after a short delay
                    setTimeout(() => {
                        searchInput.disabled = false;
                        searchInput.placeholder = 'Search playlists...';
                    }, 1000);
                }
            } catch (fallbackErr) {
                console.error('Failed to apply search input fallback:', fallbackErr);
            }
        }
    };
    
    // Helper function to restore search input to original state
    const restoreSearchInputState = (page) => {
        try {
            const searchInput = page.querySelector('#playlistSearchInput');
            if (searchInput && page._originalSearchState) {
                searchInput.disabled = page._originalSearchState.disabled;
                searchInput.placeholder = page._originalSearchState.placeholder;
            }
        } catch (err) {
            console.warn('Failed to restore search input state:', err);
        }
    };

    // Helper functions for page-specific state
    function getPageEditState(page) {
        return {
            editMode: page._editMode || false,
            editingPlaylistId: page._editingPlaylistId || null
        };
    }

    function setPageEditState(page, editMode, editingPlaylistId = null) {
        page._editMode = editMode;
        page._editingPlaylistId = editingPlaylistId;
    }

    // AbortController for event listener cleanup
    function createAbortController() {
        return new AbortController();
    }

    function getEventListenerOptions(signal) {
        return { signal };
    }

    function showNotification(message, type = 'error') {
        // Create or get the floating notification container
        let floatingNotification = document.querySelector('#floating-notification');
        if (!floatingNotification) {
            floatingNotification = document.createElement('div');
            floatingNotification.id = 'floating-notification';
            document.body.appendChild(floatingNotification);
        }

        // Add type prefix for better clarity
        let prefixedMessage = message;
        if (type === 'warning') {
            prefixedMessage = '⚠ ' + message;
        } else if (type === 'error') {
            prefixedMessage = '✗ ' + message;
        }

        // Set the message
        floatingNotification.textContent = prefixedMessage;

        // Apply floating notification styles - positioned like default Jellyfin notifications
        const notificationStyles = {
            position: 'fixed',
            bottom: '20px',
            left: '20px',
            maxWidth: '400px',
            minWidth: '300px',
            padding: '16px 20px',
            color: type === 'success' ? 'rgba(255, 255, 255, 0.95)' : 'rgba(255, 255, 255, 0.95)',
            backgroundColor: type === 'success' ? 'rgba(40, 40, 40, 0.95)' : 
                            type === 'warning' ? '#ff9800' : '#f44336',
            boxShadow: '0 4px 12px rgba(0, 0, 0, 0.4)',
            fontSize: '16px',
            fontWeight: 'normal',
            textAlign: 'left',
            zIndex: '10000',
            transform: 'translateY(100%)',
            opacity: '0',
            transition: 'all 0.3s cubic-bezier(0.4, 0, 0.2, 1)',
            boxSizing: 'border-box',
            pointerEvents: 'none' // Prevents interference with clicks
        };

        // Use applyStyles helper for consistent styling with !important rules
        applyStyles(floatingNotification, notificationStyles);

        // Animate in from bottom (like default Jellyfin notifications)
        setTimeout(() => {
            applyStyles(floatingNotification, {
                transform: 'translateY(0)',
                opacity: '1'
            });
        }, 10);

        // Clear any existing timeout
        clearTimeout(notificationTimeout);
        
        // Animate out to bottom and remove after delay
        notificationTimeout = setTimeout(() => {
            applyStyles(floatingNotification, {
                transform: 'translateY(100%)',
                opacity: '0'
            });
            
            // Remove from DOM after animation completes
            setTimeout(() => {
                if (floatingNotification && floatingNotification.parentNode) {
                    floatingNotification.parentNode.removeChild(floatingNotification);
                }
            }, 300);
        }, 8000); // 8 second display time
    }


    function handleApiError(err, defaultMessage) {
        // Try to extract meaningful error message from server response
        if (err && typeof err.text === 'function') {
            return err.text().then(serverMessage => {
                let friendlyMessage = defaultMessage;
                try {
                    const parsedMessage = JSON.parse(serverMessage);
                    if (parsedMessage && parsedMessage.message) {
                        // Remove quotes and Unicode escapes, then add context
                        const cleanMessage = parsedMessage.message
                            .replace(/"/g, '')
                            .replace(/\\u0027/g, "'")
                            .replace(/\\u0022/g, '"');
                        friendlyMessage = defaultMessage.replace(/\.$/, '') + ': ' + cleanMessage;
                    } else if (parsedMessage && parsedMessage.title) {
                        // Handle structured error responses (like ASP.NET Core error objects)
                        const cleanMessage = parsedMessage.title
                            .replace(/"/g, '')
                            .replace(/\\u0027/g, "'")
                            .replace(/\\u0022/g, '"');
                        friendlyMessage = defaultMessage.replace(/\.$/, '') + ': ' + cleanMessage;
                    } else if (serverMessage && serverMessage.trim()) {
                        // Remove quotes and Unicode escapes, then add context
                        const cleanMessage = serverMessage
                            .replace(/"/g, '')
                            .replace(/\\u0027/g, "'")
                            .replace(/\\u0022/g, '"');
                        friendlyMessage = defaultMessage.replace(/\.$/, '') + ': ' + cleanMessage;
                    }
                } catch (e) {
                    if (serverMessage && serverMessage.trim()) {
                        // Remove quotes and Unicode escapes, then add context
                        const cleanMessage = serverMessage
                            .replace(/"/g, '')
                            .replace(/\\u0027/g, "'")
                            .replace(/\\u0022/g, '"');
                        friendlyMessage = defaultMessage.replace(/\.$/, '') + ': ' + cleanMessage;
                    }
                }
                showNotification(friendlyMessage);
            }).catch(() => {
                showNotification(defaultMessage + ' HTTP ' + (err.status || 'Unknown'));
            });
        } else {
            showNotification(defaultMessage + ' ' + ((err && err.message) ? err.message : 'Unknown error'));
        }
    }

    function getApiClient() {
        return window.ApiClient;
    }

    function loadAndPopulateFields() {
        const apiClient = getApiClient();
        const url = apiClient.getUrl(ENDPOINTS.fields);
        
        return apiClient.get(url).then(response => {
            if (!response.ok) { throw new Error(`Network response was not ok: ${response.statusText}`); }
            return response.json();
        }).then(fields => {
            availableFields = fields;
            return fields;
        }).catch(err => {
            console.error('Error loading or parsing fields:', err);
            throw err;
        });
    }

    function populateSelect(selectElement, options, defaultValue = null, forceSelection = true) {
        if (!selectElement) return;
        options.forEach((opt, index) => {
            const option = document.createElement('option');
            option.value = opt.Value;
            option.textContent = opt.Label;
            selectElement.appendChild(option);
            
            if ((defaultValue && opt.Value === defaultValue) || (!defaultValue && forceSelection && index === 0)) {
                option.selected = true;
            }
        });
    }

    function populateFieldSelect(selectElement, fieldGroups, defaultValue = null) {
        if (!selectElement || !fieldGroups) return;
        
        // Clear existing options
        selectElement.innerHTML = '<option value="">-- Select Field --</option>';
        
        // Define field group display names and order
        const groupConfig = [
            { key: 'ContentFields', label: 'Content' },
            { key: 'RatingsPlaybackFields', label: 'Ratings & Playback' },
            { key: 'LibraryFields', label: 'Library' },
            { key: 'FileFields', label: 'File Info' },
            { key: 'CollectionFields', label: 'Collections' }
        ];
        
        groupConfig.forEach(group => {
            const fields = fieldGroups[group.key];
            if (fields && fields.length > 0) {
                const optgroup = document.createElement('optgroup');
                optgroup.label = group.label;
                
                fields.forEach(field => {
                    const option = document.createElement('option');
                    option.value = field.Value;
                    option.textContent = field.Label;
                    
                    if (defaultValue && field.Value === defaultValue) {
                        option.selected = true;
                    }
                    
                    optgroup.appendChild(option);
                });
                
                selectElement.appendChild(optgroup);
            }
        });
    }

    // Initialize page elements including media type checkboxes
    const initializePageElements = (page) => {
        // Generate media type checkboxes from the mediaTypes array
        generateMediaTypeCheckboxes(page);
    };

    // Helper function to show/hide schedule containers based on selected trigger
    function updateScheduleContainers(page, triggerValue) {
        toggleScheduleContainers(page, '', triggerValue);
    }
    
    // Helper function to format schedule display text
    function formatScheduleDisplay(playlist) {
        if (!playlist.ScheduleTrigger) {
            // undefined = legacy tasks (property doesn't exist), null = legacy tasks  
            return 'Legacy Jellyfin tasks';
        }
        
        if (playlist.ScheduleTrigger === 'None') {
            return 'No schedule';
        }
        
        if (playlist.ScheduleTrigger === 'Daily') {
            const raw = playlist.ScheduleTime ? playlist.ScheduleTime.substring(0, 5) : '03:00';
            const parts = raw.split(':'); 
            const h = parts[0] !== undefined ? parseInt(parts[0], 10) : 3; // Don't use || operator with 0
            const m = parts[1] !== undefined ? parseInt(parts[1], 10) : 0;
            return 'Daily at ' + formatTimeForUser(h, m);
        } else if (playlist.ScheduleTrigger === 'Weekly') {
            const raw = playlist.ScheduleTime ? playlist.ScheduleTime.substring(0, 5) : '03:00';
            const parts = raw.split(':'); 
            const h = parts[0] !== undefined ? parseInt(parts[0], 10) : 3; // Don't use || operator with 0
            const m = parts[1] !== undefined ? parseInt(parts[1], 10) : 0;
            const days = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
            
            // Handle different DayOfWeek value types (numeric, string name, or undefined)
            let dayIndex = 0; // Default to Sunday
            if (playlist.ScheduleDayOfWeek !== undefined && playlist.ScheduleDayOfWeek !== null) {
                if (typeof playlist.ScheduleDayOfWeek === 'number') {
                    // Numeric value (0-6)
                    dayIndex = Math.max(0, Math.min(6, playlist.ScheduleDayOfWeek));
                } else if (typeof playlist.ScheduleDayOfWeek === 'string') {
                    // String value - could be numeric string or day name
                    const numericValue = parseInt(playlist.ScheduleDayOfWeek, 10);
                    if (!isNaN(numericValue) && numericValue >= 0 && numericValue <= 6) {
                        dayIndex = numericValue;
                    } else {
                        // Try to match day name (case-insensitive)
                        const dayNames = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
                        const foundIndex = dayNames.findIndex(day => day.toLowerCase() === playlist.ScheduleDayOfWeek.toLowerCase());
                        if (foundIndex !== -1) {
                            dayIndex = foundIndex;
                        }
                    }
                }
            }
            
            const day = days[dayIndex];
            return 'Weekly on ' + day + ' at ' + formatTimeForUser(h, m);
        } else if (playlist.ScheduleTrigger === 'Monthly') {
            const raw = playlist.ScheduleTime ? playlist.ScheduleTime.substring(0, 5) : '03:00';
            const parts = raw.split(':'); 
            const h = parts[0] !== undefined ? parseInt(parts[0], 10) : 3; // Don't use || operator with 0
            const m = parts[1] !== undefined ? parseInt(parts[1], 10) : 0;
            const dayOfMonth = Math.min(31, Math.max(1, parseInt(playlist.ScheduleDayOfMonth, 10) || 1));
            const suffix = (dayOfMonth === 1 || dayOfMonth === 21 || dayOfMonth === 31) ? 'st' :
                          (dayOfMonth === 2 || dayOfMonth === 22) ? 'nd' :
                          (dayOfMonth === 3 || dayOfMonth === 23) ? 'rd' : 'th';
            return 'Monthly on the ' + dayOfMonth + suffix + ' at ' + formatTimeForUser(h, m);
        } else if (playlist.ScheduleTrigger === 'Interval') {
            const interval = playlist.ScheduleInterval || '1.00:00:00';
            if (interval === '00:15:00') return 'Every 15 minutes';
            if (interval === '00:30:00') return 'Every 30 minutes';
            if (interval === '01:00:00') return 'Every hour';
            if (interval === '02:00:00') return 'Every 2 hours';
            if (interval === '03:00:00') return 'Every 3 hours';
            if (interval === '04:00:00') return 'Every 4 hours';
            if (interval === '06:00:00') return 'Every 6 hours';
            if (interval === '08:00:00') return 'Every 8 hours';
            if (interval === '12:00:00') return 'Every 12 hours';
            if (interval === '1.00:00:00') return 'Every 24 hours';
            return 'Every ' + interval;
        }
        
        return playlist.ScheduleTrigger;
    }
    
    // Helper function for default schedule containers
    function updateDefaultScheduleContainers(page, triggerValue) {
        toggleScheduleContainers(page, 'default', triggerValue);
    }

    // Helper function to toggle Sort Order visibility based on Sort By value
    function toggleSortOrderVisibility(sortOrderContainer, sortByValue) {
        const container = sortOrderContainer ? sortOrderContainer.closest('.inputContainer') : null;
        if (!container) return;
        container.style.display = (sortByValue === 'Random' || sortByValue === 'NoOrder') ? 'none' : '';
    }

    function populateStaticSelects(page) {
        // Initialize page elements
        initializePageElements(page);
        
        // Populate all common selectors dynamically (DRY principle) - using safe DOM manipulation
        const scheduleTimeElement = page.querySelector('#scheduleTime');
        if (scheduleTimeElement) {
            populateSelectElement(scheduleTimeElement, generateTimeOptions('00:00')); // Default to midnight
        }
        
        const defaultScheduleTimeElement = page.querySelector('#defaultScheduleTime');
        if (defaultScheduleTimeElement) {
            populateSelectElement(defaultScheduleTimeElement, generateTimeOptions('00:00')); // Default to midnight
        }
        
        const autoRefreshElement = page.querySelector('#autoRefreshMode');
        if (autoRefreshElement) {
            populateSelectElement(autoRefreshElement, generateAutoRefreshOptions('OnLibraryChanges'));
        }
        
        const defaultAutoRefreshElement = page.querySelector('#defaultAutoRefresh');
        if (defaultAutoRefreshElement) {
            populateSelectElement(defaultAutoRefreshElement, generateAutoRefreshOptions('OnLibraryChanges'));
        }
        
        const scheduleTriggerElement = page.querySelector('#scheduleTrigger');
        if (scheduleTriggerElement) {
            populateSelectElement(scheduleTriggerElement, generateScheduleTriggerOptions('', true)); // Include "No schedule"
        }
        
        const defaultScheduleTriggerElement = page.querySelector('#defaultScheduleTrigger');
        if (defaultScheduleTriggerElement) {
            populateSelectElement(defaultScheduleTriggerElement, generateScheduleTriggerOptions('', true)); // Include "No schedule"
        }
        
        const scheduleDayElement = page.querySelector('#scheduleDayOfWeek');
        if (scheduleDayElement) {
            populateSelectElement(scheduleDayElement, generateDayOfWeekOptions('0')); // Default Sunday
        }
        
        const scheduleDayOfMonthElement = page.querySelector('#scheduleDayOfMonth');
        if (scheduleDayOfMonthElement) {
            populateSelectElement(scheduleDayOfMonthElement, generateDayOfMonthOptions('1')); // Default 1st
        }
        
        const defaultScheduleDayElement = page.querySelector('#defaultScheduleDayOfWeek');
        if (defaultScheduleDayElement) {
            populateSelectElement(defaultScheduleDayElement, generateDayOfWeekOptions('0')); // Default Sunday
        }
        
        const defaultScheduleDayOfMonthElement = page.querySelector('#defaultScheduleDayOfMonth');
        if (defaultScheduleDayOfMonthElement) {
            populateSelectElement(defaultScheduleDayOfMonthElement, generateDayOfMonthOptions('1')); // Default 1st
        }
        
        const scheduleIntervalElement = page.querySelector('#scheduleInterval');
        if (scheduleIntervalElement) {
            populateSelectElement(scheduleIntervalElement, generateIntervalOptions('1.00:00:00')); // Default 24 hours
        }
        
        const defaultScheduleIntervalElement = page.querySelector('#defaultScheduleInterval');
        if (defaultScheduleIntervalElement) {
            populateSelectElement(defaultScheduleIntervalElement, generateIntervalOptions('1.00:00:00')); // Default 24 hours
        }
        
         const sortOptions = [
            { Value: 'Name', Label: 'Name' },
            { Value: 'Name (Ignore Articles)', Label: 'Name (Ignore Article \'The\')' },
            { Value: 'ProductionYear', Label: 'Production Year' },
            { Value: 'CommunityRating', Label: 'Community Rating' },
            { Value: 'DateCreated', Label: 'Date Created' },
            { Value: 'ReleaseDate', Label: 'Release Date' },
            { Value: 'PlayCount (owner)', Label: 'Play Count (owner)' },
            { Value: 'Resolution', Label: 'Resolution' },
            { Value: 'Random', Label: 'Random' },
            { Value: 'NoOrder', Label: 'No Order' }
        ];
        const orderOptions = [
            { Value: 'Ascending', Label: 'Ascending' },
            { Value: 'Descending', Label: 'Descending' }
        ];

        const sortByContainer = page.querySelector('#sortBy-container');
        const sortOrderContainer = page.querySelector('#sortOrder-container');
        
        let sortBySelect = page.querySelector('#sortBy');
        if (!sortBySelect) {
            sortBySelect = document.createElement('select');
            sortBySelect.setAttribute('is', 'emby-select');
            sortBySelect.className = 'emby-select';
            sortBySelect.id = 'sortBy';
            sortByContainer.appendChild(sortBySelect);
        }
        
        // Add event listener to hide/show Sort Order based on Sort By selection
        sortBySelect.addEventListener('change', function() {
            toggleSortOrderVisibility(sortOrderContainer, this.value);
        });
        
        let sortOrderSelect = page.querySelector('#sortOrder');
        if (!sortOrderSelect) {
            sortOrderSelect = document.createElement('select');
            sortOrderSelect.setAttribute('is', 'emby-select');
            sortOrderSelect.className = 'emby-select';
            sortOrderSelect.id = 'sortOrder';
            sortOrderContainer.appendChild(sortOrderSelect);
        }

        const apiClient = getApiClient();
        return apiClient.getPluginConfiguration(getPluginId()).then(config => {
            const defaultSortBy = config.DefaultSortBy || 'Name';
            const defaultSortOrder = config.DefaultSortOrder || 'Ascending';
            const defaultMakePublic = config.DefaultMakePublic || false;
            const defaultMaxItems = config.DefaultMaxItems !== undefined && config.DefaultMaxItems !== null ? config.DefaultMaxItems : 500;
            const defaultMaxPlayTimeMinutes = config.DefaultMaxPlayTimeMinutes !== undefined && config.DefaultMaxPlayTimeMinutes !== null ? config.DefaultMaxPlayTimeMinutes : 0;
            const defaultPlaylistNamePrefix = config.PlaylistNamePrefix || '';
            const defaultPlaylistNameSuffix = (config.PlaylistNameSuffix !== undefined && config.PlaylistNameSuffix !== null) ? config.PlaylistNameSuffix : '[Smart]';
            const defaultAutoRefresh = config.DefaultAutoRefresh || 'OnLibraryChanges';
            
            if (sortBySelect.children.length === 0) { populateSelect(sortBySelect, sortOptions, defaultSortBy); }
            if (sortOrderSelect.children.length === 0) { populateSelect(sortOrderSelect, orderOptions, defaultSortOrder); }
            
            // Initial hide/show of Sort Order based on default Sort By value
            toggleSortOrderVisibility(sortOrderContainer, defaultSortBy);
            page.querySelector('#playlistIsPublic').checked = defaultMakePublic;
            const maxItemsElement = page.querySelector('#playlistMaxItems');
            if (maxItemsElement) {
                maxItemsElement.value = defaultMaxItems;
            }
            const maxPlayTimeMinutesElement = page.querySelector('#playlistMaxPlayTimeMinutes');
            if (maxPlayTimeMinutesElement) {
                maxPlayTimeMinutesElement.value = defaultMaxPlayTimeMinutes;
            }
            const autoRefreshElement = page.querySelector('#autoRefreshMode');
            if (autoRefreshElement) {
                autoRefreshElement.value = defaultAutoRefresh;
            }
            
            // Set up schedule trigger selector and event handlers
            const scheduleTriggerElement = page.querySelector('#scheduleTrigger');
            if (scheduleTriggerElement) {
                scheduleTriggerElement.value = config.DefaultScheduleTrigger === 'None' ? '' : (config.DefaultScheduleTrigger || '');
                if (!scheduleTriggerElement._spListenerAdded) {
                    scheduleTriggerElement.addEventListener('change', function() {
                        updateScheduleContainers(page, this.value);
                    });
                    scheduleTriggerElement._spListenerAdded = true;
                }
                // Initialize containers based on current value
                updateScheduleContainers(page, scheduleTriggerElement.value);
            }
            
            // Set up default schedule values
            const scheduleTimeElement = page.querySelector('#scheduleTime');
            if (scheduleTimeElement && config.DefaultScheduleTime) {
                const timeString = config.DefaultScheduleTime.substring(0, 5); // Extract HH:MM from HH:MM:SS
                scheduleTimeElement.value = timeString;
            }
            
            const scheduleDayElement = page.querySelector('#scheduleDayOfWeek');
            if (scheduleDayElement) {
                scheduleDayElement.value = convertDayOfWeekToValue(config.DefaultScheduleDayOfWeek);
            }
            
            const scheduleIntervalElement = page.querySelector('#scheduleInterval');
            if (scheduleIntervalElement && config.DefaultScheduleInterval) {
                scheduleIntervalElement.value = config.DefaultScheduleInterval;
            }
            
            // Set up global default schedule settings and event handlers
            const defaultScheduleTriggerElement = page.querySelector('#defaultScheduleTrigger');
            if (defaultScheduleTriggerElement) {
                defaultScheduleTriggerElement.value = config.DefaultScheduleTrigger === 'None' ? '' : (config.DefaultScheduleTrigger || '');
                defaultScheduleTriggerElement.addEventListener('change', function() {
                    updateDefaultScheduleContainers(page, this.value);
                });
                updateDefaultScheduleContainers(page, defaultScheduleTriggerElement.value);
            }
            
            const defaultScheduleTimeElement = page.querySelector('#defaultScheduleTime');
            if (defaultScheduleTimeElement && config.DefaultScheduleTime) {
                const timeString = config.DefaultScheduleTime.substring(0, 5); // Extract HH:MM from HH:MM:SS
                defaultScheduleTimeElement.value = timeString;
            }
            
            const defaultScheduleDayElement = page.querySelector('#defaultScheduleDayOfWeek');
            if (defaultScheduleDayElement) {
                defaultScheduleDayElement.value = config.DefaultScheduleDayOfWeek !== undefined ? config.DefaultScheduleDayOfWeek.toString() : '0';
            }
            
            const defaultScheduleDayOfMonthElement = page.querySelector('#defaultScheduleDayOfMonth');
            if (defaultScheduleDayOfMonthElement) {
                defaultScheduleDayOfMonthElement.value = config.DefaultScheduleDayOfMonth !== undefined ? config.DefaultScheduleDayOfMonth.toString() : '1';
            }
            
            const defaultScheduleIntervalElement = page.querySelector('#defaultScheduleInterval');
            if (defaultScheduleIntervalElement && config.DefaultScheduleInterval) {
                defaultScheduleIntervalElement.value = config.DefaultScheduleInterval;
            }
            
            
            // Populate settings tab dropdowns with current configuration values
            const defaultSortBySetting = page.querySelector('#defaultSortBy');
            const defaultSortOrderSetting = page.querySelector('#defaultSortOrder');
            if (defaultSortBySetting && defaultSortBySetting.children.length === 0) { 
                populateSelect(defaultSortBySetting, sortOptions, defaultSortBy); 
                
                // Add event listener for default settings Sort By dropdown
                defaultSortBySetting.addEventListener('change', function() {
                    toggleSortOrderVisibility(defaultSortOrderSetting, this.value);
                });
                
                // Initial hide/show for default settings
                toggleSortOrderVisibility(defaultSortOrderSetting, defaultSortBy);
            }
            if (defaultSortOrderSetting && defaultSortOrderSetting.children.length === 0) { 
                populateSelect(defaultSortOrderSetting, orderOptions, defaultSortOrder); 
            }
            
            // Populate playlist naming configuration fields
            const playlistNamePrefix = page.querySelector('#playlistNamePrefix');
            const playlistNameSuffix = page.querySelector('#playlistNameSuffix');
            if (playlistNamePrefix) {
                playlistNamePrefix.value = defaultPlaylistNamePrefix;
            }
            if (playlistNameSuffix) {
                playlistNameSuffix.value = defaultPlaylistNameSuffix;
            }
            
            // Update preview if both elements exist
            if (playlistNamePrefix && playlistNameSuffix) {
                updatePlaylistNamePreview(page);
            }
        }).catch(() => {
            if (sortBySelect.children.length === 0) { populateSelect(sortBySelect, sortOptions, 'Name'); }
            if (sortOrderSelect.children.length === 0) { populateSelect(sortOrderSelect, orderOptions, 'Ascending'); }
            page.querySelector('#playlistIsPublic').checked = false;
            const maxItemsElement = page.querySelector('#playlistMaxItems');
            if (maxItemsElement) {
                maxItemsElement.value = 500;
            }
            const maxPlayTimeMinutesElement = page.querySelector('#playlistMaxPlayTimeMinutes');
            if (maxPlayTimeMinutesElement) {
                maxPlayTimeMinutesElement.value = 0;
            }
            
            // Populate settings tab dropdowns with defaults even if config fails
            const defaultSortBySetting = page.querySelector('#defaultSortBy');
            const defaultSortOrderSetting = page.querySelector('#defaultSortOrder');
            if (defaultSortBySetting && defaultSortBySetting.children.length === 0) { 
                populateSelect(defaultSortBySetting, sortOptions, 'Name'); 
                
                // Add event listener for default settings Sort By dropdown (fallback case)
                defaultSortBySetting.addEventListener('change', function() {
                    toggleSortOrderVisibility(defaultSortOrderSetting, this.value);
                });
                
                // Initial state for fallback (Name doesn't need hiding)
                toggleSortOrderVisibility(defaultSortOrderSetting, 'Name');
            }
            if (defaultSortOrderSetting && defaultSortOrderSetting.children.length === 0) { 
                populateSelect(defaultSortOrderSetting, orderOptions, 'Ascending'); 
            }
            
            // Populate playlist naming configuration fields with defaults even if config fails
            const playlistNamePrefix = page.querySelector('#playlistNamePrefix');
            const playlistNameSuffix = page.querySelector('#playlistNameSuffix');
            if (playlistNamePrefix) {
                playlistNamePrefix.value = '';
            }
            if (playlistNameSuffix) {
                playlistNameSuffix.value = '[Smart]';
            }
            
            // Update preview if both elements exist
            if (playlistNamePrefix && playlistNameSuffix) {
                updatePlaylistNamePreview(page);
            }
        });
    }

    function updateOperatorOptions(fieldValue, operatorSelect) {
        operatorSelect.innerHTML = '<option value="">-- Select Operator --</option>';
        let allowedOperators = [];
        
        // Use the new field-specific operator mappings from the API
        if (availableFields.FieldOperators?.[fieldValue]) {
            const allowedOperatorValues = availableFields.FieldOperators[fieldValue];
            allowedOperators = availableFields.Operators.filter(op => allowedOperatorValues.includes(op.Value));
        } else {
            // Fallback to the old logic if FieldOperators is not available
            // Define common operator sets to avoid duplication
            const stringListOperators = ['Contains', 'NotContains', 'IsIn', 'IsNotIn', 'MatchRegex'];
            const stringOperators = ['Equal', 'NotEqual', 'Contains', 'NotContains', 'IsIn', 'IsNotIn', 'MatchRegex'];
            const numericOperators = ['Equal', 'NotEqual', 'GreaterThan', 'LessThan', 'GreaterThanOrEqual', 'LessThanOrEqual'];
            const booleanOperators = ['Equal', 'NotEqual'];
            
            if (FIELD_TYPES.LIST_FIELDS.includes(fieldValue)) {
                allowedOperators = availableFields.Operators.filter(op => stringListOperators.includes(op.Value));
            } else if (FIELD_TYPES.NUMERIC_FIELDS.includes(fieldValue)) {
                // Numeric fields should NOT include date-specific operators
                allowedOperators = availableFields.Operators.filter(op => numericOperators.includes(op.Value));
            } else if (FIELD_TYPES.DATE_FIELDS.includes(fieldValue)) {
                // Date fields: exclude string operators and numeric-specific operators, include date-specific operators
                allowedOperators = availableFields.Operators.filter(op => 
                    !stringListOperators.includes(op.Value) && 
                    !numericOperators.includes(op.Value)
                );
            } else if (FIELD_TYPES.BOOLEAN_FIELDS.includes(fieldValue) || FIELD_TYPES.SIMPLE_FIELDS.includes(fieldValue)) {
                allowedOperators = availableFields.Operators.filter(op => booleanOperators.includes(op.Value));
            } else { // Default to string fields
                allowedOperators = availableFields.Operators.filter(op => stringOperators.includes(op.Value));
            }
        }

        allowedOperators.forEach(opt => {
            const option = document.createElement('option');
            option.value = opt.Value;
            option.textContent = opt.Label;
            operatorSelect.appendChild(option);
        });
        if (fieldValue === 'ItemType' || FIELD_TYPES.BOOLEAN_FIELDS.includes(fieldValue)) { operatorSelect.value = 'Equal'; }
    }

    /**
     * Main dispatcher function for setting value inputs based on field type and operator
     */
    function setValueInput(fieldValue, valueContainer, operatorValue, explicitCurrentValue) {
        // Store the current value before clearing the container
        // For relative date operators, we need to capture both number and unit
        let currentValue = explicitCurrentValue;
        
        if (!currentValue) {
            // Check if this is a multi-value operator
            if (MULTI_VALUE_OPERATORS.includes(operatorValue)) {
                // For multi-value fields, get the value from the hidden input directly
                const hiddenInput = valueContainer.querySelector('input[type="hidden"].rule-value-input');
                if (hiddenInput) {
                    currentValue = hiddenInput.value;
                }
            } else {
                const currentValueInput = valueContainer.querySelector('.rule-value-input');
                const currentUnitSelect = valueContainer.querySelector('.rule-value-unit');
                
                if (currentValueInput) {
                    if (currentUnitSelect && currentUnitSelect.value) {
                        // This is a relative date input, combine number:unit format
                        currentValue = `${currentValueInput.value}:${currentUnitSelect.value}`;
                    } else {
                        // Regular input, just use the value
                        currentValue = currentValueInput.value;
                    }
                }
            }
        }
        
        valueContainer.innerHTML = '';

        // Check if this is an IsIn/IsNotIn operator to use tag-based input
        const ruleRow = valueContainer.closest('.rule-row');
        const operatorSelect = ruleRow ? ruleRow.querySelector('.rule-operator-select') : null;
        const currentOperator = operatorValue || (operatorSelect ? operatorSelect.value : '');
        const isMultiValueOperator = MULTI_VALUE_OPERATORS.includes(currentOperator);

        if (isMultiValueOperator) {
            // Create tag-based input for IsIn/IsNotIn operators
            createTagBasedInput(valueContainer, currentValue);
        } else if (FIELD_TYPES.SIMPLE_FIELDS.includes(fieldValue)) {
            handleSimpleFieldInput(valueContainer, currentValue);
        } else if (FIELD_TYPES.BOOLEAN_FIELDS.includes(fieldValue)) {
            handleBooleanFieldInput(valueContainer, fieldValue, currentValue);
        } else if (FIELD_TYPES.NUMERIC_FIELDS.includes(fieldValue)) {
            handleNumericFieldInput(valueContainer, fieldValue, currentValue);
        } else if (FIELD_TYPES.DATE_FIELDS.includes(fieldValue)) {
            handleDateFieldInput(valueContainer, currentOperator, currentValue);
        } else if (FIELD_TYPES.RESOLUTION_FIELDS.includes(fieldValue)) {
            handleResolutionFieldInput(valueContainer, currentValue);
        } else {
            handleTextFieldInput(valueContainer, currentValue);
        }
        
        // Restore the current value if it exists and is valid for the new field type
        restoreFieldValue(valueContainer, fieldValue, currentOperator, currentValue, isMultiValueOperator);
    }

    /**
     * Handles simple field inputs (media type selects)
     */
    function handleSimpleFieldInput(valueContainer, currentValue) {
        const select = document.createElement('select');
        select.className = 'emby-select rule-value-input';
        select.setAttribute('is', 'emby-select');
        select.style.width = '100%';
        mediaTypes.forEach(opt => {
            const option = document.createElement('option');
            option.value = opt.Value;
            option.textContent = opt.Label;
            if (currentValue && opt.Value === currentValue) {
                option.selected = true;
            }
            select.appendChild(option);
        });
        valueContainer.appendChild(select);
    }

    /**
     * Handles boolean field inputs with appropriate labels
     */
    function handleBooleanFieldInput(valueContainer, fieldValue, currentValue) {
        const select = document.createElement('select');
        select.className = 'emby-select rule-value-input';
        select.setAttribute('is', 'emby-select');
        select.style.width = '100%';
        let boolOptions;
        if (fieldValue === 'IsPlayed') {
            boolOptions = [ { Value: "true", Label: "Yes (Played)" }, { Value: "false", Label: "No (Unplayed)" } ];
        } else if (fieldValue === 'IsFavorite') {
            boolOptions = [ { Value: "true", Label: "Yes (Favorite)" }, { Value: "false", Label: "No (Not Favorite)" } ];
        } else if (fieldValue === 'NextUnwatched') {
            boolOptions = [ { Value: "true", Label: "Yes (Next to Watch)" }, { Value: "false", Label: "No (Not Next)" } ];
        } else {
            boolOptions = [ { Value: "true", Label: "Yes" }, { Value: "false", Label: "No" } ];
        }
        boolOptions.forEach(opt => {
            const option = document.createElement('option');
            option.value = opt.Value;
            option.textContent = opt.Label;
            if (currentValue && opt.Value === currentValue) {
                option.selected = true;
            }
            select.appendChild(option);
        });
        valueContainer.appendChild(select);
    }

    /**
     * Handles numeric field inputs
     */
    function handleNumericFieldInput(valueContainer, fieldValue, currentValue) {
        const input = document.createElement('input');
        input.type = 'number';
        input.className = 'emby-input rule-value-input';
        input.placeholder = 'Value';
        input.style.width = '100%';
        
        // Set appropriate step for decimal fields like Framerate
        if (fieldValue === 'Framerate' || fieldValue === 'CommunityRating' || fieldValue === 'CriticRating') {
            input.step = 'any'; // Allow any decimal precision
        } else {
            input.step = '1'; // Integer fields like ProductionYear, RuntimeMinutes, PlayCount
        }
        
        if (currentValue) {
            input.value = currentValue;
        }
        valueContainer.appendChild(input);
    }

    /**
     * Handles date field inputs (both relative and absolute)
     */
    function handleDateFieldInput(valueContainer, currentOperator, currentValue) {
        const isRelativeDateOperator = RELATIVE_DATE_OPERATORS.includes(currentOperator);
        
        if (isRelativeDateOperator) {
            handleRelativeDateInput(valueContainer, currentValue);
        } else {
            handleAbsoluteDateInput(valueContainer, currentValue);
        }
    }

    /**
     * Handles relative date inputs (number + unit dropdown)
     */
    function handleRelativeDateInput(valueContainer) {
        const inputContainer = document.createElement('div');
        inputContainer.style.display = 'flex';
        inputContainer.style.gap = '0.5em';
        inputContainer.style.alignItems = 'center';
        valueContainer.appendChild(inputContainer);

        const input = document.createElement('input');
        input.type = 'number';
        input.className = 'emby-input rule-value-input';
        input.placeholder = 'Number';
        input.min = '0';
        input.style.flex = '0 0 43%';
        inputContainer.appendChild(input);

        const unitSelect = document.createElement('select');
        unitSelect.className = 'emby-select rule-value-unit';
        unitSelect.setAttribute('is', 'emby-select');
        unitSelect.style.flex = '0 0 55%';
        
        // Add placeholder option
        const placeholderOption = document.createElement('option');
        placeholderOption.value = '';
        placeholderOption.textContent = '-- Select Unit --';
        placeholderOption.disabled = true;
        placeholderOption.selected = true;
        unitSelect.appendChild(placeholderOption);
        
        [
            { value: 'hours', label: 'Hour(s)' },
            { value: 'days', label: 'Day(s)' },
            { value: 'weeks', label: 'Week(s)' },
            { value: 'months', label: 'Month(s)' },
            { value: 'years', label: 'Year(s)' }
        ].forEach(opt => {
            const option = document.createElement('option');
            option.value = opt.value;
            option.textContent = opt.label;
            unitSelect.appendChild(option);
        });
        inputContainer.appendChild(unitSelect);
    }

    /**
     * Handles absolute date inputs
     */
    function handleAbsoluteDateInput(valueContainer) {
        const input = document.createElement('input');
        input.type = 'date';
        input.className = 'emby-input rule-value-input';
        input.style.width = '100%';
        valueContainer.appendChild(input);
    }

    /**
     * Handles resolution field inputs with predefined resolution options
     */
    function handleResolutionFieldInput(valueContainer, currentValue) {
        const select = document.createElement('select');
        select.className = 'emby-select rule-value-input';
        select.setAttribute('is', 'emby-select');
        select.style.width = '100%';
        
        // Add placeholder option
        const placeholderOption = document.createElement('option');
        placeholderOption.value = '';
        placeholderOption.textContent = '-- Select Resolution --';
        placeholderOption.disabled = true;
        // Only select placeholder if no currentValue
        if (!currentValue) {
            placeholderOption.selected = true;
        }
        select.appendChild(placeholderOption);
        
        // Resolution options with display names
        const resolutionOptions = [
            { Value: '480p', Label: '480p (854x480)' },
            { Value: '720p', Label: '720p (1280x720)' },
            { Value: '1080p', Label: '1080p (1920x1080)' },
            { Value: '1440p', Label: '1440p (2560x1440)' },
            { Value: '4K', Label: '4K (3840x2160)' },
            { Value: '8K', Label: '8K (7680x4320)' }
        ];
        
        // Add resolution options and select if matches currentValue
        resolutionOptions.forEach(opt => {
            const option = document.createElement('option');
            option.value = opt.Value;
            option.textContent = opt.Label;
            if (currentValue && opt.Value === currentValue) {
                option.selected = true;
            }
            select.appendChild(option);
        });
        
        valueContainer.appendChild(select);
    }

    /**
     * Handles text field inputs (default fallback)
     */
    function handleTextFieldInput(valueContainer, currentValue) {
        const input = document.createElement('input');
        input.type = 'text';
        input.className = 'emby-input rule-value-input';
        input.placeholder = 'Value';
        input.style.width = '100%';
        if (currentValue) {
            input.value = currentValue;
        }
        valueContainer.appendChild(input);
    }

    /**
     * Restores field values based on field type and operator
     */
    function restoreFieldValue(valueContainer, fieldValue, currentOperator, currentValue, isMultiValueOperator) {
        const newValueInput = valueContainer.querySelector('.rule-value-input');
        if (newValueInput && currentValue) {
            // Store the original value as a data attribute for potential restoration
            newValueInput.setAttribute('data-original-value', currentValue);
            
            // Try to restore the value if it's appropriate for the new field type
            if (FIELD_TYPES.SIMPLE_FIELDS.includes(fieldValue) || FIELD_TYPES.BOOLEAN_FIELDS.includes(fieldValue)) {
                restoreSelectValue(newValueInput, currentValue);
            } else if (FIELD_TYPES.DATE_FIELDS.includes(fieldValue)) {
                restoreDateValue(valueContainer, currentOperator, currentValue, newValueInput);
            } else if (isMultiValueOperator) {
                restoreMultiValueInput(valueContainer, currentValue);
            } else {
                // For inputs, restore the value directly
                // If switching from multi-value operators, use first tag as fallback
                if (currentValue && currentValue.includes(';')) {
                    const tags = currentValue.split(';').map(tag => tag.trim()).filter(tag => tag.length > 0);
                    newValueInput.value = tags[0] || '';
                } else {
                    newValueInput.value = currentValue;
                }
            }
        }
    }

    /**
     * Restores select field values
     */
    function restoreSelectValue(selectElement, currentValue) {
        if (selectElement.tagName === 'SELECT') {
            const option = Array.from(selectElement.options).find(opt => opt.value === currentValue);
            if (option) {
                selectElement.value = currentValue;
            }
        }
    }

    /**
     * Restores date field values (both relative and absolute)
     */
    function restoreDateValue(valueContainer, currentOperator, currentValue, newValueInput) {
        const isRelativeDateOperator = RELATIVE_DATE_OPERATORS.includes(currentOperator);
        
        if (isRelativeDateOperator) {
            restoreRelativeDateValue(valueContainer, currentValue, newValueInput);
        } else {
            // For regular date operators, restore the date value directly
            if (newValueInput.tagName === 'INPUT') {
                newValueInput.value = currentValue;
            }
        }
    }

    /**
     * Restores relative date values (number:unit format)
     */
    function restoreRelativeDateValue(valueContainer, currentValue, newValueInput) {
        // Parse number:unit format for relative date operators
        const parts = currentValue.split(':');
        const validUnits = ['hours', 'days', 'weeks', 'months', 'years'];
        const num = parts[0];
        const unit = parts[1];
        const isValidNum = /^\d+$/.test(num) && parseInt(num, 10) >= 0;
        const isValidUnit = validUnits.includes(unit);
        
        if (parts.length === 2 && isValidNum && isValidUnit) {
            // Set the number input
            if (newValueInput.tagName === 'INPUT') {
                newValueInput.value = num;
            }
            // Set the unit dropdown
            const unitSelect = valueContainer.querySelector('.rule-value-unit');
            if (unitSelect) {
                unitSelect.value = unit;
            }
        } else {
            // Log a warning if the value is malformed
            console.warn(`Malformed relative date value: '${currentValue}'. Expected format: <number>:<unit> (e.g., '3:months'). Parts:`, parts, `isValidNum: ${isValidNum}`, `isValidUnit: ${isValidUnit}`);
        }
    }

    /**
     * Restores multi-value input (tag-based) values
     */
    function restoreMultiValueInput(valueContainer, currentValue) {
        // For tag-based inputs, restore the semicolon-separated values as individual tags
        if (currentValue) {
            // Clear existing tags first to prevent duplicates and ensure UI consistency
            const existingTags = valueContainer.querySelectorAll('.tag-item');
            existingTags.forEach(tag => tag.remove());
            
            const tags = currentValue.split(';').map(tag => tag.trim()).filter(tag => tag.length > 0);
            tags.forEach(tag => addTagToContainer(valueContainer, tag));
        }
    }

    function updateRegexHelp(ruleGroup) {
        const operatorSelect = ruleGroup.querySelector('.rule-operator-select');
        const existingHelp = ruleGroup.querySelector('.regex-help');
        if (existingHelp) existingHelp.remove();
        
        if (operatorSelect && operatorSelect.value === 'MatchRegex') {
            const helpDiv = document.createElement('div');
            helpDiv.className = 'regex-help field-description';
            helpDiv.style.cssText = 'margin-top: 0.5em; margin-bottom: 0.5em; font-size: 0.85em; color: #aaa; background: rgba(255,255,255,0.05); padding: 0.5em; border-radius: 1px;';
            // Use safe HTML creation instead of innerHTML for security
            helpDiv.innerHTML = '';
            
            // Create help content safely
            const strongRegexHelp = document.createElement('strong');
            strongRegexHelp.textContent = 'Regex Help:';
            helpDiv.appendChild(strongRegexHelp);
            
            helpDiv.appendChild(document.createTextNode(' Use .NET syntax. Examples: '));
            
            const code1 = document.createElement('code');
            code1.textContent = '(?i)swe';
            helpDiv.appendChild(code1);
            helpDiv.appendChild(document.createTextNode(' (case-insensitive), '));
            
            const code2 = document.createElement('code');
            code2.textContent = '(?i)(eng|en)';
            helpDiv.appendChild(code2);
            helpDiv.appendChild(document.createTextNode(' (multiple options), '));
            
            const code3 = document.createElement('code');
            code3.textContent = '^Action';
            helpDiv.appendChild(code3);
            helpDiv.appendChild(document.createTextNode(' (starts with). Do not use JavaScript-style /pattern/flags.'));
            
            helpDiv.appendChild(document.createElement('br'));
            
            const strongTestPatterns = document.createElement('strong');
            strongTestPatterns.textContent = 'Test patterns:';
            helpDiv.appendChild(strongTestPatterns);
            helpDiv.appendChild(document.createTextNode(' '));
            
            const regexLink = document.createElement('a');
            regexLink.href = 'https://regex101.com/?flavor=dotnet';
            regexLink.target = '_blank';
            regexLink.style.color = '#00a4dc';
            regexLink.textContent = 'Regex101.com (.NET flavor)';
            helpDiv.appendChild(regexLink);
            ruleGroup.appendChild(helpDiv);
        }
    }

    function createInitialLogicGroup(page) {
        const rulesContainer = page.querySelector('#rules-container');
        const logicGroupId = 'logic-group-' + Date.now();
        
        const logicGroupDiv = createStyledElement('div', 'logic-group', STYLES.logicGroup);
        logicGroupDiv.setAttribute('data-group-id', logicGroupId);
        
        rulesContainer.appendChild(logicGroupDiv);
        
        // Add the first rule to this group
        addRuleToGroup(page, logicGroupDiv);
        
        return logicGroupDiv;
    }

    function addRuleToGroup(page, logicGroup) {
        const existingRules = logicGroup.querySelectorAll('.rule-row');
        
        // Add AND separator if this isn't the first rule in the group
        if (existingRules.length > 0) {
            const andSeparator = createAndSeparator();
            logicGroup.appendChild(andSeparator);
        }
        
        const ruleDiv = document.createElement('div');
        ruleDiv.className = 'rule-row';
        ruleDiv.setAttribute('data-rule-id', 'rule-' + Date.now());

        // Create AbortController for this rule's event listeners
        const abortController = createAbortController();
        const signal = abortController.signal;
        
        // Store the controller on the element for cleanup
        ruleDiv._abortController = abortController;

        const fieldsHtml = `
            <div class="input-group" style="display: flex; gap: 0.5em; align-items: center; margin-bottom: 1em;">
                <select is="emby-select" class="emby-select rule-field-select" style="flex: 0 0 25%;">
                    <option value="">-- Select Field --</option>
                </select>
                <select is="emby-select" class="emby-select rule-operator-select" style="flex: 0 0 20%;">
                    <option value="">-- Select Operator --</option>
                </select>
                <span class="rule-value-container" style="flex: 1;">
                    <input type="text" class="emby-input rule-value-input" placeholder="Value" style="width: 100%;">
                </span>
                <div class="rule-actions">
                    <button type="button" class="rule-action-btn and-btn" title="Add AND rule">And</button>
                    <button type="button" class="rule-action-btn or-btn" title="Add OR group">Or</button>
                    <button type="button" class="rule-action-btn delete-btn" title="Remove rule">×</button>
                </div>
            </div>
            <div class="rule-user-selector" style="display: none; margin-bottom: 0.75em; padding: 0.5em; background: rgba(255,255,255,0.05); border-radius: 4px;">
                <label style="display: block; margin-bottom: 0.25em; font-size: 0.85em; color: #ccc; font-weight: 500;">
                    Check for specific user (optional):
                </label>
                <select is="emby-select" class="emby-select rule-user-select" style="width: 100%;">
                    <option value="">Default (playlist owner)</option>
                </select>
            </div>
            <div class="rule-nextunwatched-options" style="display: none; margin-bottom: 0.75em; padding: 0.5em; background: rgba(255,255,255,0.05); border-radius: 4px;">
                <label style="display: block; margin-bottom: 0.25em; font-size: 0.85em; color: #ccc; font-weight: 500;">
                    Include unwatched series:
                </label>
                <select is="emby-select" class="emby-select rule-nextunwatched-select" style="width: 100%;">
                    <option value="true">Yes - Include first episodes of unwatched series</option>
                    <option value="false">No - Only show next episodes from started series</option>
                </select>
            </div>
            <div class="rule-collections-options" style="display: none; margin-bottom: 0.75em; padding: 0.5em; background: rgba(255,255,255,0.05); border-radius: 4px;">
                <label style="display: block; margin-bottom: 0.25em; font-size: 0.85em; color: #ccc; font-weight: 500;">
                    Include episodes within series:
                </label>
                <select is="emby-select" class="emby-select rule-collections-select" style="width: 100%;">
                    <option value="false">No - Only include the series themselves</option>
                    <option value="true">Yes - Include individual episodes from series in collections</option>
                </select>
            </div>`;
        
        ruleDiv.innerHTML = fieldsHtml;
        logicGroup.appendChild(ruleDiv);
        
        const newRuleRow = logicGroup.lastElementChild;
        const fieldSelect = newRuleRow.querySelector('.rule-field-select');
        const operatorSelect = newRuleRow.querySelector('.rule-operator-select');
        const valueContainer = newRuleRow.querySelector('.rule-value-container');

        if (availableFields.ContentFields) {
            populateFieldSelect(fieldSelect, availableFields, null);
        }
        if (availableFields.Operators) {
            populateSelect(operatorSelect, availableFields.Operators, null, false);
        }

        setValueInput(fieldSelect.value, valueContainer, operatorSelect.value);
        updateOperatorOptions(fieldSelect.value, operatorSelect);
        
        // Initialize user selector visibility and load users
        updateUserSelectorVisibility(newRuleRow, fieldSelect.value);
        const userSelect = newRuleRow.querySelector('.rule-user-select');
        if (userSelect) {
            loadUsersForRule(userSelect, true);
        }
        
        // Initialize NextUnwatched options visibility
        updateNextUnwatchedOptionsVisibility(newRuleRow, fieldSelect.value);
        
        // Initialize Collections options visibility
        updateCollectionsOptionsVisibility(newRuleRow, fieldSelect.value);
        
        // Add event listeners with AbortController signal (if supported)
        const listenerOptions = getEventListenerOptions(signal);
        fieldSelect.addEventListener('change', function() {
            setValueInput(fieldSelect.value, valueContainer, operatorSelect.value);
            updateOperatorOptions(fieldSelect.value, operatorSelect);
            updateUserSelectorVisibility(newRuleRow, fieldSelect.value);
            updateNextUnwatchedOptionsVisibility(newRuleRow, fieldSelect.value);
            updateCollectionsOptionsVisibility(newRuleRow, fieldSelect.value);
            updateRegexHelp(newRuleRow);
        }, listenerOptions);
        
                        operatorSelect.addEventListener('change', function() {
                    updateRegexHelp(newRuleRow);
                    // Always re-render the value input on operator change for consistency
                    // setValueInput is idempotent and cheap, so this simplifies maintenance
                    const fieldValue = fieldSelect.value;
                    setValueInput(fieldValue, valueContainer, this.value);
                }, listenerOptions);

        // Style the action buttons
        const actionButtons = newRuleRow.querySelectorAll('.rule-action-btn');
        actionButtons.forEach(button => {
            let buttonType;
            if (button.classList.contains('and-btn')) buttonType = 'and';
            else if (button.classList.contains('or-btn')) buttonType = 'or';
            else if (button.classList.contains('delete-btn')) buttonType = 'delete';
            
            if (buttonType) {
                // Apply base styles
                styleRuleActionButton(button, buttonType);
                
                // Add hover effects
                button.addEventListener('mouseenter', function() {
                    styleRuleActionButton(this, buttonType);
                }, listenerOptions);
                
                button.addEventListener('mouseleave', function() {
                    styleRuleActionButton(this, buttonType);
                }, listenerOptions);
            }
        });

        // Update button visibility for all rules in all groups
        updateRuleButtonVisibility(page);
    }

    function addNewLogicGroup(page) {
        const rulesContainer = page.querySelector('#rules-container');
        
        // Add OR separator between groups
        const orSeparator = createOrSeparator();
        rulesContainer.appendChild(orSeparator);
        
        // Create new logic group
        const logicGroupId = 'logic-group-' + Date.now();
        const logicGroupDiv = createStyledElement('div', 'logic-group', STYLES.logicGroup);
        logicGroupDiv.setAttribute('data-group-id', logicGroupId);
        
        rulesContainer.appendChild(logicGroupDiv);
        
        // Add the first rule to this group
        addRuleToGroup(page, logicGroupDiv);
        
        return logicGroupDiv;
    }

    function removeRule(page, ruleElement) {
        const logicGroup = ruleElement.closest('.logic-group');
        const rulesInGroup = logicGroup.querySelectorAll('.rule-row');
        
        // Clean up event listeners before removing
        cleanupRuleEventListeners(ruleElement);
        
        if (rulesInGroup.length === 1) {
            // This is the last rule in the group, remove the entire group
            removeLogicGroup(page, logicGroup);
        } else {
            // Remove the rule and any adjacent separator
            const nextSibling = ruleElement.nextElementSibling;
            const prevSibling = ruleElement.previousElementSibling;
            
            if (nextSibling && nextSibling.classList.contains('rule-within-group-separator')) {
                nextSibling.remove();
            } else if (prevSibling && prevSibling.classList.contains('rule-within-group-separator')) {
                prevSibling.remove();
            }
            
            ruleElement.remove();
            updateRuleButtonVisibility(page);
        }
    }

    function cleanupRuleEventListeners(ruleElement) {
        // Abort all event listeners for this rule
        if (ruleElement._abortController) {
            ruleElement._abortController.abort();
            ruleElement._abortController = null;
        }
    }

    function removeLogicGroup(page, logicGroup) {
        const rulesContainer = page.querySelector('#rules-container');
        const allGroups = rulesContainer.querySelectorAll('.logic-group');
        
        // Clean up all event listeners in this group
        const rulesInGroup = logicGroup.querySelectorAll('.rule-row');
        rulesInGroup.forEach(rule => cleanupRuleEventListeners(rule));
        
        if (allGroups.length === 1) {
            // This is the last group, clear it and add a new rule
            logicGroup.innerHTML = '';
            addRuleToGroup(page, logicGroup);
        } else {
            // Remove the group and any adjacent separator
            const nextSibling = logicGroup.nextElementSibling;
            const prevSibling = logicGroup.previousElementSibling;
            
            if (prevSibling && prevSibling.classList.contains('logic-group-separator')) {
                prevSibling.remove();
            } else if (nextSibling && nextSibling.classList.contains('logic-group-separator')) {
                nextSibling.remove();
            }
            
            logicGroup.remove();
            updateRuleButtonVisibility(page);
        }
    }

    function updateRuleButtonVisibility(page) {
        const rulesContainer = page.querySelector('#rules-container');
        const allLogicGroups = rulesContainer.querySelectorAll('.logic-group');
        
        allLogicGroups.forEach(group => {
            const rulesInGroup = group.querySelectorAll('.rule-row');
            
            rulesInGroup.forEach((rule, index) => {
                const andBtn = rule.querySelector('.and-btn');
                const orBtn = rule.querySelector('.or-btn');
                const deleteBtn = rule.querySelector('.delete-btn');
                
                // Hide AND and OR buttons if this is not the last rule in the group
                if (index < rulesInGroup.length - 1) {
                    andBtn.style.display = 'none';
                    orBtn.style.display = 'none';
                } else {
                    andBtn.style.display = 'inline-flex';
                    orBtn.style.display = 'inline-flex';
                }
                
                // Always show DELETE button
                deleteBtn.style.display = 'inline-flex';
            });
        });
    }

    function reinitializeExistingRules(page) {
        // Clean up existing event listeners for all rules
        const allRules = page.querySelectorAll('.rule-row');
        allRules.forEach(rule => cleanupRuleEventListeners(rule));
        
        // Re-initialize each rule with proper event listeners
        allRules.forEach(ruleRow => {
            const fieldSelect = ruleRow.querySelector('.rule-field-select');
            const operatorSelect = ruleRow.querySelector('.rule-operator-select');
            const valueContainer = ruleRow.querySelector('.rule-value-container');
            
            if (fieldSelect && operatorSelect && valueContainer) {
                // Create new AbortController for this rule
                const abortController = createAbortController();
                const signal = abortController.signal;
                
                // Store the controller on the element for cleanup
                ruleRow._abortController = abortController;
                
                // Re-populate field options if needed
                if (availableFields.ContentFields && fieldSelect.children.length <= 1) {
                    populateFieldSelect(fieldSelect, availableFields, fieldSelect.value);
                }
                
                // Re-populate operator options if needed
                if (availableFields.Operators && operatorSelect.children.length <= 1) {
                    populateSelect(operatorSelect, availableFields.Operators, operatorSelect.value, false);
                }
                
                // Re-set value input based on current field value
                const currentFieldValue = fieldSelect.value;
                if (currentFieldValue) {
                    setValueInput(currentFieldValue, valueContainer, operatorSelect.value);
                    updateOperatorOptions(currentFieldValue, operatorSelect);
                    updateUserSelectorVisibility(ruleRow, currentFieldValue);
                    updateNextUnwatchedOptionsVisibility(ruleRow, currentFieldValue);
                    updateCollectionsOptionsVisibility(ruleRow, currentFieldValue);
                }
                
                // Re-add event listeners
                const listenerOptions = getEventListenerOptions(signal);
                fieldSelect.addEventListener('change', function() {
                    setValueInput(fieldSelect.value, valueContainer, operatorSelect.value);
                    updateOperatorOptions(fieldSelect.value, operatorSelect);
                    updateUserSelectorVisibility(ruleRow, fieldSelect.value);
                    updateNextUnwatchedOptionsVisibility(ruleRow, fieldSelect.value);
                    updateCollectionsOptionsVisibility(ruleRow, fieldSelect.value);
                    updateRegexHelp(ruleRow);
                }, listenerOptions);
                
                operatorSelect.addEventListener('change', function() {
                    updateRegexHelp(ruleRow);
                    // Always re-render the value input on operator change for consistency
                    // setValueInput is idempotent and cheap, so this simplifies maintenance
                    const fieldValue = fieldSelect.value;
                    setValueInput(fieldValue, valueContainer, this.value);
                }, listenerOptions);
                
                // Re-style action buttons
                const actionButtons = ruleRow.querySelectorAll('.rule-action-btn');
                actionButtons.forEach(button => {
                    let buttonType;
                    if (button.classList.contains('and-btn')) buttonType = 'and';
                    else if (button.classList.contains('or-btn')) buttonType = 'or';
                    else if (button.classList.contains('delete-btn')) buttonType = 'delete';
                    
                    if (buttonType) {
                        // Apply base styles
                        styleRuleActionButton(button, buttonType);
                        
                        // Add hover effects
                        button.addEventListener('mouseenter', function() {
                            styleRuleActionButton(this, buttonType);
                        }, listenerOptions);
                        
                        button.addEventListener('mouseleave', function() {
                            styleRuleActionButton(this, buttonType);
                        }, listenerOptions);
                    }
                });
                
                // Re-initialize user selector if needed
                const userSelect = ruleRow.querySelector('.rule-user-select');
                if (userSelect && userSelect.children.length <= 1) {
                    loadUsersForRule(userSelect, true);
                }
                
                // Update regex help if needed
                updateRegexHelp(ruleRow);
            }
        });
        
        // Update button visibility
        updateRuleButtonVisibility(page);
    }
    
    async function createPlaylist(page) {
        // Get edit state to determine if we're creating or updating
        const editState = getPageEditState(page);
        
        // Only scroll to top when creating new playlist (not when updating existing)
        if (!editState.editMode) {
            window.scrollTo({ top: 0, behavior: 'smooth' });
        }
        
        try {
            const apiClient = getApiClient();
            const playlistName = getElementValue(page, '#playlistName');

            if (!playlistName) {
                showNotification('Playlist name is required.');
                return;
            }

            const expressionSets = [];
            page.querySelectorAll('.logic-group').forEach(logicGroup => {
                const expressions = [];
                logicGroup.querySelectorAll('.rule-row').forEach(rule => {
                    const memberName = rule.querySelector('.rule-field-select').value;
                    const operator = rule.querySelector('.rule-operator-select').value;
                    let targetValue;
                    if ((operator === 'NewerThan' || operator === 'OlderThan') && rule.querySelector('.rule-value-unit')) {
                        // Serialize as number:unit
                        const num = rule.querySelector('.rule-value-input').value;
                        const unit = rule.querySelector('.rule-value-unit').value;
                        targetValue = num && unit ? `${num}:${unit}` : '';
                    } else {
                        targetValue = rule.querySelector('.rule-value-input').value;
                    }
                    
                    if (memberName && operator && targetValue) {
                        const expression = { MemberName: memberName, Operator: operator, TargetValue: targetValue };
                        
                        // Check if a specific user is selected for user data fields
                        const userSelect = rule.querySelector('.rule-user-select');
                        if (userSelect && userSelect.value) {
                            // Only add UserId if a specific user is selected (not default)
                            expression.UserId = userSelect.value;
                        }
                        // If no user is selected or default is selected, the expression works as before
                        // (for the playlist owner - backwards compatibility)
                        
                        // Check for NextUnwatched specific options
                        const nextUnwatchedSelect = rule.querySelector('.rule-nextunwatched-select');
                        if (nextUnwatchedSelect && memberName === 'NextUnwatched') {
                            // Convert string to boolean and only include if it's explicitly false
                            const includeUnwatchedSeries = nextUnwatchedSelect.value === 'true';
                            if (!includeUnwatchedSeries) {
                                expression.IncludeUnwatchedSeries = false;
                            }
                            // If true (default), don't include the parameter to save space
                        }
                        
                        // Check for Collections specific options
                        const collectionsSelect = rule.querySelector('.rule-collections-select');
                        if (collectionsSelect && memberName === 'Collections') {
                            // Convert string to boolean and only include if it's explicitly true
                            const includeEpisodesWithinSeries = collectionsSelect.value === 'true';
                            if (includeEpisodesWithinSeries) {
                                expression.IncludeEpisodesWithinSeries = true;
                            }
                            // If false (default), don't include the parameter to save space
                        }
                        
                        expressions.push(expression);
                    }
                });
                if (expressions.length > 0) {
                    expressionSets.push({ Expressions: expressions });
                }
            });

            const selectedMediaTypes = [];
            const mediaTypesSelect = page.querySelectorAll('.media-type-checkbox');
            mediaTypesSelect.forEach(checkbox => {
                if (checkbox.checked) {
                    selectedMediaTypes.push(checkbox.value);
                }
            });
            if (selectedMediaTypes.length === 0) {
                showNotification('At least one media type must be selected.');
                return;
            }

            // Use helper functions for cleaner form data collection
            const sortByValue = getElementValue(page, '#sortBy', 'Name');
            const sortOrderValue = getElementValue(page, '#sortOrder', 'Ascending');
            
            // Special handling for Random and NoOrder - they don't need Ascending/Descending
            const orderName = (sortByValue === 'Random' || sortByValue === 'NoOrder') 
                ? sortByValue 
                : sortByValue + ' ' + sortOrderValue;
            const isPublic = getElementChecked(page, '#playlistIsPublic', false);
            const isEnabled = getElementChecked(page, '#playlistIsEnabled', true); // Default to true
            const autoRefreshMode = getElementValue(page, '#autoRefreshMode', 'Never');
            // Capture schedule settings with helper functions
            const scheduleTriggerValue = getElementValue(page, '#scheduleTrigger');
            const scheduleTrigger = scheduleTriggerValue === '' ? 'None' : (scheduleTriggerValue || null);
            const scheduleTimeValue = getElementValue(page, '#scheduleTime');
            // Only set scheduleTime for Daily/Weekly/Monthly, not for Interval
            const scheduleTime = (scheduleTrigger === 'Daily' || scheduleTrigger === 'Weekly' || scheduleTrigger === 'Monthly') && scheduleTimeValue 
                ? scheduleTimeValue + ':00' : null;
            
            // Only set scheduleDayOfWeek for Weekly
            const scheduleDayOfWeekValue = getElementValue(page, '#scheduleDayOfWeek');
            const scheduleDayOfWeek = scheduleTrigger === 'Weekly' && scheduleDayOfWeekValue 
                ? parseInt(scheduleDayOfWeekValue) : null;
                
            // Only set scheduleDayOfMonth for Monthly
            const scheduleDayOfMonthValue = getElementValue(page, '#scheduleDayOfMonth');
            const scheduleDayOfMonth = scheduleTrigger === 'Monthly' && scheduleDayOfMonthValue 
                ? parseInt(scheduleDayOfMonthValue) : null;
                
            // Only set scheduleInterval for Interval
            const scheduleIntervalValue = getElementValue(page, '#scheduleInterval');
            const scheduleInterval = scheduleTrigger === 'Interval' && scheduleIntervalValue 
                ? scheduleIntervalValue : null;
            // Handle maxItems with validation using helper function
            const maxItemsInput = getElementValue(page, '#playlistMaxItems');
            let maxItems;
            if (maxItemsInput === '') {
                maxItems = 500;
            } else {
                const parsedValue = parseInt(maxItemsInput);
                maxItems = isNaN(parsedValue) ? 500 : parsedValue;
            }

            // Handle maxPlayTimeMinutes with helper function
            const maxPlayTimeMinutesInput = getElementValue(page, '#playlistMaxPlayTimeMinutes');
            let maxPlayTimeMinutes;
            if (maxPlayTimeMinutesInput === '') {
                maxPlayTimeMinutes = 0;
            } else {
                const parsedValue = parseInt(maxPlayTimeMinutesInput);
                maxPlayTimeMinutes = isNaN(parsedValue) ? 0 : parsedValue;
            }

            // Get selected user ID from dropdown using helper function
            const userId = getElementValue(page, '#playlistUser');
            
            if (!userId) {
                showNotification('Please select a playlist owner.');
                return;
            }

            const playlistDto = {
                Name: playlistName,
                ExpressionSets: expressionSets,
                Order: { Name: orderName },
                Public: isPublic,
                Enabled: isEnabled,
                UserId: userId,
                MediaTypes: selectedMediaTypes,
                MaxItems: maxItems,
                MaxPlayTimeMinutes: maxPlayTimeMinutes,
                AutoRefresh: autoRefreshMode,
                ScheduleTrigger: scheduleTrigger,
                ScheduleTime: scheduleTime,
                ScheduleDayOfWeek: scheduleDayOfWeek,
                ScheduleDayOfMonth: scheduleDayOfMonth,
                ScheduleInterval: scheduleInterval
            };

            // Add ID if in edit mode
            const editState = getPageEditState(page);
            if (editState.editMode && editState.editingPlaylistId) {
                playlistDto.Id = editState.editingPlaylistId;
            }

            Dashboard.showLoadingMsg();
            
            const requestType = editState.editMode ? "PUT" : "POST";
            const url = editState.editMode ? 
                apiClient.getUrl(ENDPOINTS.base + '/' + editState.editingPlaylistId) : 
                apiClient.getUrl(ENDPOINTS.base);
            
            apiClient.ajax({
                type: requestType,
                url: url,
                data: JSON.stringify(playlistDto),
                contentType: 'application/json'
            }).then(() => {
                Dashboard.hideLoadingMsg();
                const message = editState.editMode ? 
                    'Playlist "' + playlistName + '" updated successfully.' : 
                    'Playlist "' + playlistName + '" created. The playlist has been generated.';
                showNotification(message, 'success');
                
                // Exit edit mode and clear form
                if (editState.editMode) {
                    // Exit edit mode silently without showing cancellation message
                    setPageEditState(page, false, null);
                    const editIndicator = page.querySelector('#edit-mode-indicator');
                    editIndicator.style.display = 'none';
                    const submitBtn = page.querySelector('#submitBtn');
                    if (submitBtn) submitBtn.textContent = 'Create Playlist';
                    
                    // Restore tab button text
                    const createTabButton = page.querySelector('a[data-tab="create"]');
                    if (createTabButton) {
                        createTabButton.textContent = 'Create Playlist';
                    }
                    
                    // Switch to Manage tab and scroll to top after successful update (auto for instant behavior)
                    switchToTab(page, 'manage');
                    window.scrollTo({ top: 0, behavior: 'auto' });
                }
                clearForm(page);
            }).catch(err => {
                Dashboard.hideLoadingMsg();
                console.error('Error creating playlist:', err);
                const action = editState.editMode ? 'update' : 'create';
                handleApiError(err, 'Failed to ' + action + ' playlist ' + playlistName);
            });
        } catch (e) {
            Dashboard.hideLoadingMsg();
            console.error('A synchronous error occurred in createPlaylist:', e);
            showNotification('A critical client-side error occurred: ' + e.message);
        }
    }
    
    function clearForm(page) {
        // Only handle form clearing - edit mode management should be done by caller
        
        setElementValue(page, '#playlistName', '');
        
        // Clean up all existing event listeners before clearing rules
        const rulesContainer = page.querySelector('#rules-container');
        const allRules = rulesContainer.querySelectorAll('.rule-row');
        allRules.forEach(rule => cleanupRuleEventListeners(rule));
        
        rulesContainer.innerHTML = '';
        
        // Clear media type selections
        const mediaTypesSelect = page.querySelectorAll('.media-type-checkbox');
        mediaTypesSelect.forEach(checkbox => {
            checkbox.checked = false;
        });
        
        const apiClient = getApiClient();
        apiClient.getPluginConfiguration(getPluginId()).then(config => {
            setElementValue(page, '#sortBy', config.DefaultSortBy || 'Name');
            setElementValue(page, '#sortOrder', config.DefaultSortOrder || 'Ascending');
            
            // Ensure Sort Order visibility is synced after setting defaults
            toggleSortOrderVisibility(page.querySelector('#sortOrder-container'), config.DefaultSortBy || 'Name');
            setElementChecked(page, '#playlistIsPublic', config.DefaultMakePublic || false);
            setElementChecked(page, '#playlistIsEnabled', true); // Default to enabled
            const defaultMaxItems = config.DefaultMaxItems !== undefined && config.DefaultMaxItems !== null ? config.DefaultMaxItems : 500;
            setElementValue(page, '#playlistMaxItems', defaultMaxItems);
            const defaultMaxPlayTimeMinutes = config.DefaultMaxPlayTimeMinutes !== undefined && config.DefaultMaxPlayTimeMinutes !== null ? config.DefaultMaxPlayTimeMinutes : 0;
            setElementValue(page, '#playlistMaxPlayTimeMinutes', defaultMaxPlayTimeMinutes);
            setElementValue(page, '#autoRefreshMode', config.DefaultAutoRefresh || 'OnLibraryChanges');
            // Set default schedule values from config
            const defaultScheduleTriggerForForm = page.querySelector('#scheduleTrigger');
            if (defaultScheduleTriggerForForm) {
                defaultScheduleTriggerForForm.value = config.DefaultScheduleTrigger === 'None' ? '' : (config.DefaultScheduleTrigger || '');
                updateScheduleContainers(page, defaultScheduleTriggerForForm.value);
            }
            
            const defaultScheduleTimeForForm = page.querySelector('#scheduleTime');
            if (defaultScheduleTimeForForm && config.DefaultScheduleTime) {
                const timeString = config.DefaultScheduleTime.substring(0, 5);
                defaultScheduleTimeForForm.value = timeString;
            }
            
            const defaultScheduleDayForForm = page.querySelector('#scheduleDayOfWeek');
            if (defaultScheduleDayForForm) {
                defaultScheduleDayForForm.value = convertDayOfWeekToValue(config.DefaultScheduleDayOfWeek);
            }
            
            const defaultScheduleDayOfMonthForForm = page.querySelector('#scheduleDayOfMonth');
            if (defaultScheduleDayOfMonthForForm) {
                defaultScheduleDayOfMonthForForm.value = config.DefaultScheduleDayOfMonth !== undefined ? config.DefaultScheduleDayOfMonth.toString() : '1';
            }
            
            const defaultScheduleIntervalForForm = page.querySelector('#scheduleInterval');
            if (defaultScheduleIntervalForForm && config.DefaultScheduleInterval) {
                defaultScheduleIntervalForForm.value = config.DefaultScheduleInterval;
            }
        }).catch(() => {
            setElementValue(page, '#sortBy', 'Name');
            setElementValue(page, '#sortOrder', 'Ascending');
            setElementChecked(page, '#playlistIsPublic', false);
            setElementChecked(page, '#playlistIsEnabled', true); // Default to enabled
            setElementValue(page, '#playlistMaxItems', 500);
            setElementValue(page, '#playlistMaxPlayTimeMinutes', 0);
            setElementValue(page, '#autoRefreshMode', 'OnLibraryChanges');
            // Set fallback schedule defaults
            const fallbackScheduleTrigger = page.querySelector('#scheduleTrigger');
            if (fallbackScheduleTrigger) {
                fallbackScheduleTrigger.value = ''; // No schedule by default
                updateScheduleContainers(page, '');
            }
        });
        
        // Create initial logic group with one rule
        createInitialLogicGroup(page);
        
        // Update button visibility after initial group is created
        updateRuleButtonVisibility(page);
    }

    function updateUserSelectorVisibility(ruleRow, fieldValue) {
        const isUserDataField = FIELD_TYPES.USER_DATA_FIELDS.includes(fieldValue);
        const userSelectorDiv = ruleRow.querySelector('.rule-user-selector');
        
        if (userSelectorDiv) {
            if (isUserDataField) {
                userSelectorDiv.style.display = 'block';
            } else {
                userSelectorDiv.style.display = 'none';
                // Reset to default when hiding
                const userSelect = userSelectorDiv.querySelector('.rule-user-select');
                if (userSelect) {
                    userSelect.value = '';
                }
            }
        }
    }
    
    function updateNextUnwatchedOptionsVisibility(ruleRow, fieldValue) {
        const isNextUnwatchedField = fieldValue === 'NextUnwatched';
        const nextUnwatchedOptionsDiv = ruleRow.querySelector('.rule-nextunwatched-options');
        
        if (nextUnwatchedOptionsDiv) {
            if (isNextUnwatchedField) {
                nextUnwatchedOptionsDiv.style.display = 'block';
            } else {
                nextUnwatchedOptionsDiv.style.display = 'none';
                // Reset to default when hiding
                const nextUnwatchedSelect = nextUnwatchedOptionsDiv.querySelector('.rule-nextunwatched-select');
                if (nextUnwatchedSelect) {
                    nextUnwatchedSelect.value = 'true'; // Default to including unwatched series
                }
            }
        }
    }
    
    function updateCollectionsOptionsVisibility(ruleRow, fieldValue) {
        const isCollectionsField = fieldValue === 'Collections';
        const collectionsOptionsDiv = ruleRow.querySelector('.rule-collections-options');
        
        if (collectionsOptionsDiv) {
            if (isCollectionsField) {
                collectionsOptionsDiv.style.display = 'block';
            } else {
                collectionsOptionsDiv.style.display = 'none';
                // Reset to default when hiding
                const collectionsSelect = collectionsOptionsDiv.querySelector('.rule-collections-select');
                if (collectionsSelect) {
                    collectionsSelect.value = 'false'; // Default to not including episodes within series
                }
            }
        }
    }

    async function loadUsersForRule(userSelect, isOptional = false) {
        const apiClient = getApiClient();
        
        try {
            const response = await apiClient.ajax({
                type: "GET",
                url: apiClient.getUrl(ENDPOINTS.users),
                contentType: 'application/json'
            });
            
            const users = await response.json();
            
            if (!isOptional) {
                userSelect.innerHTML = '';
            } else {
                // Remove all options except the first (default) if present
                while (userSelect.options.length > 1) {
                    userSelect.remove(1);
                }
            }
            
            // Add user options
            users.forEach(user => {
                const option = document.createElement('option');
                option.value = user.Id;
                option.textContent = user.Name;
                userSelect.appendChild(option);
            });
            
        } catch (err) {
            console.error('Error loading users for rule:', err);
            if (!isOptional) {
                userSelect.innerHTML = '<option value="">Error loading users</option>';
            }
        }
    }

    async function loadUsers(page) {
        const apiClient = getApiClient();
        const userSelect = page.querySelector('#playlistUser');
        
        try {
            const response = await apiClient.ajax({
                type: "GET",
                url: apiClient.getUrl(ENDPOINTS.users),
                contentType: 'application/json'
            });
            
            const users = await response.json();
            
            // Clear existing options
            userSelect.innerHTML = '';
            
            // Add user options
            users.forEach(user => {
                const option = document.createElement('option');
                option.value = user.Id;
                option.textContent = user.Name;
                userSelect.appendChild(option);
            });
            
            // Set current user as default
            await setCurrentUserAsDefault(page);
            
        } catch (err) {
            console.error('Error loading users:', err);
            userSelect.innerHTML = '<option value="">Error loading users</option>';
            showNotification('Failed to load users. Using fallback.');
            throw err; // Re-throw to be caught by Promise.all
        }
    }
    
    async function setCurrentUserAsDefault(page) {
        const apiClient = getApiClient();
        const userSelect = page.querySelector('#playlistUser');
        
        try {
            // Use client-side method to get current user
            let userId = apiClient.getCurrentUserId();
            if (!userId) {
                const user = await apiClient.getCurrentUser();
                userId = user?.Id;
            }
            if (userId) {
                userSelect.value = userId;
            }
        } catch (err) {
            console.error('Error setting current user as default:', err);
        }
    }

    async function resolveUsername(apiClient, playlist) {
        if (playlist.UserId && playlist.UserId !== '00000000-0000-0000-0000-000000000000') {
            const name = await resolveUserIdToName(apiClient, playlist.UserId);
            return name || 'Unknown User';
        }
        return 'Unknown User';
    }

    // Cache for user ID to name lookups
    const userNameCache = new Map();
    
    async function resolveUserIdToName(apiClient, userId) {
        if (!userId || userId === '00000000-0000-0000-0000-000000000000') {
            return null;
        }
        
        // Check cache first
        if (userNameCache.has(userId)) {
            return userNameCache.get(userId);
        }
        
        try {
            const user = await apiClient.getUser(userId);
            const userName = user?.Name || 'Unknown User';
            
            // Cache the result
            userNameCache.set(userId, userName);
            return userName;
        } catch (err) {
            console.error('Error resolving user ID ' + userId + ':', err);
            const fallback = 'Unknown User';
            
            // Cache the fallback too to avoid repeated failed lookups
            userNameCache.set(userId, fallback);
            return fallback;
        }
    }

    // Helper function to format playlist display values
    function formatPlaylistDisplayValues(playlist) {
        const maxItemsDisplay = (playlist.MaxItems === undefined || playlist.MaxItems === null || playlist.MaxItems === 0) ? 'Unlimited' : playlist.MaxItems.toString();
        const maxPlayTimeDisplay = (playlist.MaxPlayTimeMinutes === undefined || playlist.MaxPlayTimeMinutes === null || playlist.MaxPlayTimeMinutes === 0) ? 'Unlimited' : playlist.MaxPlayTimeMinutes.toString() + ' minutes';
        return { maxItemsDisplay, maxPlayTimeDisplay };
    }

    async function loadPlaylistList(page) {
        const apiClient = getApiClient();
        const container = page.querySelector('#playlist-list-container');
        
        // Prevent multiple simultaneous requests
        if (page._loadingPlaylists) {
            return;
        }
        
        // Set loading state BEFORE any async operations
        page._loadingPlaylists = true;
        
        // Disable search input while loading
        setSearchInputState(page, true, 'Loading playlists...');
        
        container.innerHTML = '<p>Loading playlists...</p>';
        
        try {
            const response = await apiClient.ajax({
                type: "GET",
                url: apiClient.getUrl(ENDPOINTS.base),
                contentType: 'application/json'
            });
            
            if (!response.ok) { 
                throw new Error('HTTP ' + response.status + ': ' + response.statusText); 
            }
            
            const playlists = await response.json();
            let processedPlaylists = playlists;
            // Ensure playlists is an array
            if (!Array.isArray(processedPlaylists)) {
                console.warn('API returned non-array playlists data, converting to empty array');
                processedPlaylists = [];
            }
            
            // Check if any playlists were skipped due to corruption
            // This is a simple heuristic - if there are JSON files but fewer playlists loaded
            // Note: This won't be 100% accurate but gives users a heads up
            if (processedPlaylists.length > 0) {
                console.log(`SmartPlaylist: Loaded ${processedPlaylists.length} playlist(s) successfully`);
            }
            
            // Store playlists data for filtering
            page._allPlaylists = processedPlaylists;
            
            try {
                // Populate user filter dropdown
                await populateUserFilter(page, processedPlaylists);
            } catch (err) {
                console.error('Error populating user filter:', err);
                // Continue even if user filter fails
            }
            
            if (processedPlaylists && processedPlaylists.length > 0) {
                // Apply all filters and sorting and display results
                const filteredPlaylists = applyAllFiltersAndSort(page, processedPlaylists);
                
                // Use the existing playlist display logic instead of the async function for now
                const totalPlaylists = processedPlaylists.length;
                const filteredCount = filteredPlaylists.length;
                const enabledPlaylists = filteredPlaylists.filter(p => p.Enabled !== false).length;
                const disabledPlaylists = filteredCount - enabledPlaylists;
                
                let html = '';
                
                // Add bulk actions container after summary
                const summaryText = generateSummaryText(totalPlaylists, enabledPlaylists, disabledPlaylists, filteredCount, null);
                html += generateBulkActionsHTML(summaryText);
                
                // Process filtered playlists sequentially to resolve usernames
                for (const playlist of filteredPlaylists) {
                    // Resolve username first
                    const resolvedUserName = await resolveUsername(apiClient, playlist);
                    
                    // Generate detailed rules display using helper function
                    const rulesHtml = await generateRulesHtml(playlist, apiClient);
                    
                    // Use helper function to generate playlist HTML (DRY)
                    html += generatePlaylistCardHtml(playlist, rulesHtml, resolvedUserName);
                }
                container.innerHTML = html;
                
                // Restore expand states from localStorage
                restorePlaylistExpandStates(page);
                
                // Update expand all button text based on current states
                updateExpandAllButtonText(page);
                
                // Update bulk actions visibility and state
                updateBulkActionsVisibility(page);
            } else {
                container.innerHTML = '<div class="inputContainer"><p>No smart playlists found.</p></div>';
            }
            
        } catch (err) {
            const errorMessage = displayApiError(err, 'Failed to load playlists');
            container.innerHTML = '<div class="inputContainer"><p style="color: #ff6b6b;">' + escapeHtml(errorMessage) + '</p></div>';
        } finally {
            // Always re-enable search input and clear loading flag
            setSearchInputState(page, false);
            page._loadingPlaylists = false;
        }
    }

    function filterPlaylists(playlists, searchTerm, page) {
        if (!searchTerm) return playlists;
        
        return playlists.filter(playlist => {
            // Search in playlist name
            if (playlist.Name && playlist.Name.toLowerCase().includes(searchTerm)) {
                return true;
            }
            
            // Search in filename
            if (playlist.FileName && playlist.FileName.toLowerCase().includes(searchTerm)) {
                return true;
            }
            
            // Search in media types
            if (playlist.MediaTypes && playlist.MediaTypes.some(type => type.toLowerCase().includes(searchTerm))) {
                return true;
            }
            
            // Search in rules (field names, operators, and values)
            if (playlist.ExpressionSets) {
                for (const expressionSet of playlist.ExpressionSets) {
                    if (expressionSet.Expressions) {
                        for (const expression of expressionSet.Expressions) {
                            // Search in field name
                            if (expression.MemberName && expression.MemberName.toLowerCase().includes(searchTerm)) {
                                return true;
                            }
                            
                            // Search in operator
                            if (expression.Operator && expression.Operator.toLowerCase().includes(searchTerm)) {
                                return true;
                            }
                            
                            // Search in target value
                            if (expression.TargetValue && expression.TargetValue.toLowerCase().includes(searchTerm)) {
                                return true;
                            }
                        }
                    }
                }
            }
            
            // Search in sort order
            if (playlist.Order && playlist.Order.Name && playlist.Order.Name.toLowerCase().includes(searchTerm)) {
                return true;
            }
            
            // Search in public/private status
            if (searchTerm === 'public' && playlist.Public) {
                return true;
            }
            if (searchTerm === 'private' && !playlist.Public) {
                return true;
            }
            
            // Search in enabled/disabled status
            if (searchTerm === 'enabled' && playlist.Enabled !== false) {
                return true;
            }
            if (searchTerm === 'disabled' && playlist.Enabled === false) {
                return true;
            }
            
            // Search in username (resolved from UserId)
            if (page && page._usernameCache && playlist.UserId) {
                const username = page._usernameCache.get(playlist.UserId);
                if (username && username.toLowerCase().includes(searchTerm)) {
                    return true;
                }
            }
            
            return false;
        });
    }


    // ========================================
    // PLAYLIST FILTERING & SORTING SYSTEM
    // ========================================
    
    // Centralized filter configuration - eliminates DRY violations
    const PLAYLIST_FILTER_CONFIGS = {
        search: {
            selector: '#playlistSearchInput',
            defaultValue: '',
            getValue: (element) => element ? element.value.trim().toLowerCase() : '',
            filterFn: (playlists, searchTerm, page) => {
        if (!searchTerm) return playlists;
                return filterPlaylists(playlists, searchTerm, page); // Use existing comprehensive search
            }
        },
        mediaType: {
            selector: '#mediaTypeFilter',
            defaultValue: 'all',
            getValue: (element) => element ? element.value : 'all',
            filterFn: (playlists, mediaTypeFilter, page) => {
                if (!mediaTypeFilter || mediaTypeFilter === 'all') return playlists;
                
                return playlists.filter(playlist => {
                    const mediaTypes = playlist.MediaTypes || [];
                    
                    // Show playlists that contain the selected media type (inclusive filtering)
                    return mediaTypes.includes(mediaTypeFilter);
                });
            }
        },
        visibility: {
            selector: '#visibilityFilter',
            defaultValue: 'all',
            getValue: (element) => element ? element.value : 'all',
            filterFn: (playlists, visibilityFilter, page) => {
                if (!visibilityFilter || visibilityFilter === 'all') return playlists;
                
                return playlists.filter(playlist => {
                    const isPublic = playlist.Public === true;
                    
                    switch (visibilityFilter) {
                        case 'public':
                            return isPublic;
                        case 'private':
                            return !isPublic;
                        default:
                            return true;
                    }
                });
            }
        },
        user: {
            selector: '#userFilter',
            defaultValue: 'all',
            getValue: (element) => element ? element.value : 'all',
            filterFn: (playlists, userFilter, page) => {
                if (!userFilter || userFilter === 'all') return playlists;
                
                return playlists.filter(playlist => {
                    return playlist.UserId === userFilter;
                });
            }
        },
        sort: {
            selector: '#playlistSortSelect',
            defaultValue: 'name-asc',
            getValue: (element) => element ? element.value : 'name-asc'
            // Note: sorting is handled by sortPlaylists function, not as a filter
        }
    };

    // Generic DOM query helper - eliminates repetitive querySelector patterns
    function getFilterValue(page, filterKey) {
        const config = PLAYLIST_FILTER_CONFIGS[filterKey];
        if (!config) return config?.defaultValue || '';
        
        const element = page.querySelector(config.selector);
        return config.getValue(element);
    }

    // Generic filter application function - replaces all individual filter functions
    function applyFilter(playlists, filterKey, filterValue, page) {
        const config = PLAYLIST_FILTER_CONFIGS[filterKey];
        if (!config) return playlists;
        
        return config.filterFn(playlists, filterValue, page);
    }

    // Initialize page-level AbortController for better event listener management
    function initializePageEventListeners(page) {
        // Create page-level AbortController if it doesn't exist
        if (!page._pageAbortController) {
            page._pageAbortController = createAbortController();
        }
        return page._pageAbortController.signal;
    }

    // Generic event listener setup - eliminates repetitive filter change handlers
    function setupFilterEventListeners(page, pageSignal = null) {
        const signal = pageSignal || initializePageEventListeners(page);
        const filterKeys = ['sort', 'mediaType', 'visibility', 'user'];
        
        filterKeys.forEach(filterKey => {
            const config = PLAYLIST_FILTER_CONFIGS[filterKey];
            if (!config) return;
            
            const element = page.querySelector(config.selector);
            if (element) {
                element.addEventListener('change', function() {
                    savePlaylistFilterPreferences(page);
                    applySearchFilter(page).catch(err => {
                        console.error(`Error during ${filterKey} filter:`, err);
                        showNotification(`Filter error: ${err.message}`);
                    });
                }, getEventListenerOptions(signal));
            }
        });
    }

    // Helper function to generate rules HTML (DRY principle)
    async function generateRulesHtml(playlist, apiClient) {
        let rulesHtml = '';
        if (playlist.ExpressionSets && playlist.ExpressionSets.length > 0) {
            for (let groupIndex = 0; groupIndex < playlist.ExpressionSets.length; groupIndex++) {
                const expressionSet = playlist.ExpressionSets[groupIndex];
                if (groupIndex > 0) {
                    rulesHtml += '<strong style="color: #888;">OR</strong><br>';
                }
                
                if (expressionSet.Expressions && expressionSet.Expressions.length > 0) {
                    rulesHtml += '<div style="padding: 0.6em; background: rgba(255,255,255,0.02); border-radius: 4px; margin: 0.3em 0;">';
                    
                    for (let ruleIndex = 0; ruleIndex < expressionSet.Expressions.length; ruleIndex++) {
                        const rule = expressionSet.Expressions[ruleIndex];
                        if (ruleIndex > 0) {
                            rulesHtml += '<br><em style="color: #888; font-size: 0.9em;">AND</em><br>';
                        }
                        
                        let fieldName = rule.MemberName;
                        if (fieldName === 'ItemType') fieldName = 'Media Type';
                        let operator = rule.Operator;
                        switch(operator) {
                            case 'Equal': operator = 'equals'; break;
                            case 'NotEqual': operator = 'not equals'; break;
                            case 'Contains': operator = 'contains'; break;
                            case 'NotContains': operator = "not contains"; break;
                            case 'IsIn': operator = 'is in'; break;
                            case 'IsNotIn': operator = 'is not in'; break;
                            case 'GreaterThan': operator = '>'; break;
                            case 'LessThan': operator = '<'; break;
                            case 'After': operator = 'after'; break;
                            case 'Before': operator = 'before'; break;
                            case 'GreaterThanOrEqual': operator = '>='; break;
                            case 'LessThanOrEqual': operator = '<='; break;
                            case 'MatchRegex': operator = 'matches regex'; break;
                        }
                        let value = rule.TargetValue;
                        if (rule.MemberName === 'IsPlayed') { value = value === 'true' ? 'Yes (Played)' : 'No (Unplayed)'; }
                        if (rule.MemberName === 'NextUnwatched') { value = value === 'true' ? 'Yes (Next to Watch)' : 'No (Not Next)'; }
                        
                        // Check if this rule has a specific user and resolve username
                        let userInfo = '';
                        if (rule.UserId && rule.UserId !== '00000000-0000-0000-0000-000000000000') {
                            try {
                                const userName = await resolveUserIdToName(apiClient, rule.UserId);
                                userInfo = ' for ' + (userName || 'Unknown User');
                            } catch (err) {
                                console.error('Error resolving username for rule:', err);
                                userInfo = ' for specific user';
                            }
                        }
                        
                        // Add NextUnwatched configuration info
                        let nextUnwatchedInfo = '';
                        if (rule.MemberName === 'NextUnwatched' && rule.IncludeUnwatchedSeries !== undefined) {
                            nextUnwatchedInfo = rule.IncludeUnwatchedSeries ? ' (including unwatched series)' : ' (excluding unwatched series)';
                        }
                        
                        // Add Collections configuration info
                        let collectionsInfo = '';
                        if (rule.MemberName === 'Collections' && rule.IncludeEpisodesWithinSeries === true) {
                            collectionsInfo = ' (including episodes within series)';
                        }
                        
                        rulesHtml += '<span style="font-family: monospace; background: #232323; padding: 4px 4px; border-radius: 3px;">';
                        rulesHtml += escapeHtml(fieldName) + ' ' + escapeHtml(operator) + ' "' + escapeHtml(value) + '"' + escapeHtml(userInfo) + escapeHtml(nextUnwatchedInfo) + escapeHtml(collectionsInfo);
                        rulesHtml += '</span>';
                    }
                    rulesHtml += '</div>';
                }
            }
        } else {
            rulesHtml = 'No rules defined';
        }
        return rulesHtml;
    }

    // Helper function to generate playlist HTML (DRY principle)
    function generatePlaylistCardHtml(playlist, rulesHtml, resolvedUserName) {
        const isPublic = playlist.Public ? 'Public' : 'Private';
        const isEnabled = playlist.Enabled !== false; // Default to true for backward compatibility
        const enabledStatus = isEnabled ? '' : 'Disabled';
        const enabledStatusColor = isEnabled ? '#4CAF50' : '#f44336';
        const statusDisplayText = isEnabled ? 'Enabled' : 'Disabled';
        const autoRefreshMode = playlist.AutoRefresh || 'Never';
        const autoRefreshDisplay = autoRefreshMode === 'Never' ? 'Manual/scheduled only' :
                                 autoRefreshMode === 'OnLibraryChanges' ? 'On library changes' :
                                 autoRefreshMode === 'OnAllChanges' ? 'On all changes' : autoRefreshMode;
        const scheduleDisplay = formatScheduleDisplay(playlist);
        
        // Format last scheduled refresh display
        const lastRefreshDisplay = formatRelativeTimeFromIso(playlist.LastRefreshed, 'Unknown');
        const dateCreatedDisplay = formatRelativeTimeFromIso(playlist.DateCreated, 'Unknown');
        const sortName = playlist.Order ? playlist.Order.Name : 'Default';
        
        // Use the resolved username passed as parameter
        const userName = resolvedUserName || 'Unknown User';
        const playlistId = playlist.Id || 'NO_ID';
        // Create individual media type labels - filter out deprecated Series type
        let mediaTypesArray = [];
        if (playlist.MediaTypes && playlist.MediaTypes.length > 0) {
            const validTypes = playlist.MediaTypes.filter(type => type !== 'Series');
            mediaTypesArray = validTypes.length > 0 ? validTypes : ['Unknown'];
        } else {
            mediaTypesArray = ['Unknown'];
        }
        
        const { maxItemsDisplay, maxPlayTimeDisplay } = formatPlaylistDisplayValues(playlist);
        
        // Format media types for display in Properties table
        const mediaTypesDisplayText = mediaTypesArray.join(', ');
        
        // Escape all dynamic content to prevent XSS
        const eName = escapeHtml(playlist.Name || '');
        const eFileName = escapeHtml(playlist.FileName || '');
        const eUserName = escapeHtml(userName || '');
        const eSortName = escapeHtml(sortName);
        const eMaxItems = escapeHtml(maxItemsDisplay);
        const eMaxPlayTime = escapeHtml(maxPlayTimeDisplay);
        const eAutoRefreshDisplay = escapeHtml(autoRefreshDisplay);
        const eScheduleDisplay = escapeHtml(scheduleDisplay);
        const eLastRefreshDisplay = escapeHtml(lastRefreshDisplay);
        const eDateCreatedDisplay = escapeHtml(dateCreatedDisplay);
        const eStatusDisplayText = escapeHtml(statusDisplayText);
        const eMediaTypesDisplayText = escapeHtml(mediaTypesDisplayText);
        
        // Generate collapsible playlist card with improved styling
        return '<div class="inputContainer playlist-card" data-playlist-id="' + escapeHtmlAttribute(playlistId) + '" style="border: none; border-radius: 2px; margin-bottom: 1em; background: #202020;">' +
            // Compact header (always visible)
            '<div class="playlist-header" style="padding: 0.75em; cursor: pointer; display: flex; align-items: center; justify-content: space-between;">' +
                '<div class="playlist-header-left" style="display: flex; align-items: center; flex: 1; min-width: 0;">' +
                    '<label class="emby-checkbox-label" style="width: auto; min-width: auto; margin-right: 0.3em; margin-left: 0.3em; flex-shrink: 0;">' +
                        '<input type="checkbox" is="emby-checkbox" data-embycheckbox="true" class="emby-checkbox playlist-checkbox" data-playlist-id="' + escapeHtmlAttribute(playlistId) + '">' +
                        '<span class="checkboxLabel" style="display: none;"></span>' +
                        '<span class="checkboxOutline">' +
                            '<span class="material-icons checkboxIcon checkboxIcon-checked check" aria-hidden="true"></span>' +
                            '<span class="material-icons checkboxIcon checkboxIcon-unchecked" aria-hidden="true"></span>' +
                        '</span>' +
                    '</label>' +
                    '<span class="playlist-expand-icon" style="margin-right: 0.5em; font-family: monospace; font-size: 1.2em; color: #999; flex-shrink: 0;">▶</span>' +
                    '<h3 style="margin: 0; flex: 1.5; min-width: 0; word-wrap: break-word; padding-right: 0.5em;">' + eName + '</h3>' +
                    (enabledStatus ? '<span class="playlist-status" style="color: ' + enabledStatusColor + '; font-weight: bold; margin-right: 0.5em; flex-shrink: 0;">' + enabledStatus + '</span>' : '') +
                '</div>' +
                '<div class="playlist-header-right" style="display: flex; align-items: center; margin-left: 1em; margin-right: 0.5em;">' +
                    '<div class="playlist-media-types-container" style="display: flex; flex-wrap: wrap; gap: 0.25em; flex-shrink: 0; max-width: 160px; justify-content: flex-end;">' +
                        mediaTypesArray.map(type => '<span class="playlist-media-type-label" style="padding: 0.2em 0.5em; background: #333; border-radius: 3px; font-size: 0.8em; color: #ccc; white-space: nowrap;">' + escapeHtml(type) + '</span>').join('') +
                    '</div>' +
                '</div>' +
            '</div>' +
            
            // Detailed content (initially hidden)
            '<div class="playlist-details" style="display: none; padding: 0 0.75em 0.75em 0.75em; background: #202020;">' +
                // Rules section
                '<div class="rules-section" style="margin-bottom: 1em; margin-left: 0.5em;">' +
                    '<h4 style="margin: 0 0 0.5em 0; color: #fff; font-size: 1em;">Rules</h4>' +
                        rulesHtml +
                '</div>' +
                
                // Properties table
                '<div class="properties-section" style="margin-left: 0.5em;">' +
                    '<h4 style="margin: 0 0 0.5em 0; color: #fff; font-size: 1em;">Properties</h4>' +
                    '<table style="width: 100%; border-collapse: collapse; background: rgba(255,255,255,0.02); border-radius: 4px; overflow: hidden;">' +
                        '<tr style="border-bottom: 1px solid rgba(255,255,255,0.1);">' +
                            '<td style="padding: 0.5em 0.75em; font-weight: bold; color: #ccc; width: 40%; border-right: 1px solid rgba(255,255,255,0.1);">File</td>' +
                            '<td style="padding: 0.5em 0.75em; color: #fff;">' + eFileName + '</td>' +
                        '</tr>' +
                        '<tr style="border-bottom: 1px solid rgba(255,255,255,0.1);">' +
                            '<td style="padding: 0.5em 0.75em; font-weight: bold; color: #ccc; width: 40%; border-right: 1px solid rgba(255,255,255,0.1);">User</td>' +
                            '<td style="padding: 0.5em 0.75em; color: #fff;">' + eUserName + '</td>' +
                        '</tr>' +
                        '<tr style="border-bottom: 1px solid rgba(255,255,255,0.1);">' +
                            '<td style="padding: 0.5em 0.75em; font-weight: bold; color: #ccc; width: 40%; border-right: 1px solid rgba(255,255,255,0.1);">Status</td>' +
                            '<td style="padding: 0.5em 0.75em; color: #fff;">' + eStatusDisplayText + '</td>' +
                        '</tr>' +
                        '<tr style="border-bottom: 1px solid rgba(255,255,255,0.1);">' +
                            '<td style="padding: 0.5em 0.75em; font-weight: bold; color: #ccc; width: 40%; border-right: 1px solid rgba(255,255,255,0.1);">Visibility</td>' +
                            '<td style="padding: 0.5em 0.75em; color: #fff;">' + isPublic + '</td>' +
                        '</tr>' +
                        '<tr style="border-bottom: 1px solid rgba(255,255,255,0.1);">' +
                            '<td style="padding: 0.5em 0.75em; font-weight: bold; color: #ccc; width: 40%; border-right: 1px solid rgba(255,255,255,0.1);">Media Type</td>' +
                            '<td style="padding: 0.5em 0.75em; color: #fff;">' + eMediaTypesDisplayText + '</td>' +
                        '</tr>' +
                        '<tr style="border-bottom: 1px solid rgba(255,255,255,0.1);">' +
                            '<td style="padding: 0.5em 0.75em; font-weight: bold; color: #ccc; width: 40%; border-right: 1px solid rgba(255,255,255,0.1);">Sort</td>' +
                            '<td style="padding: 0.5em 0.75em; color: #fff;">' + eSortName + '</td>' +
                        '</tr>' +
                        '<tr style="border-bottom: 1px solid rgba(255,255,255,0.1);">' +
                            '<td style="padding: 0.5em 0.75em; font-weight: bold; color: #ccc; width: 40%; border-right: 1px solid rgba(255,255,255,0.1);">Max Items</td>' +
                            '<td style="padding: 0.5em 0.75em; color: #fff;">' + eMaxItems + '</td>' +
                        '</tr>' +
                        '<tr style="border-bottom: 1px solid rgba(255,255,255,0.1);">' +
                            '<td style="padding: 0.5em 0.75em; font-weight: bold; color: #ccc; width: 40%; border-right: 1px solid rgba(255,255,255,0.1);">Max Play Time</td>' +
                            '<td style="padding: 0.5em 0.75em; color: #fff;">' + eMaxPlayTime + '</td>' +
                        '</tr>' +
                        '<tr style="border-bottom: 1px solid rgba(255,255,255,0.1);">' +
                            '<td style="padding: 0.5em 0.75em; font-weight: bold; color: #ccc; width: 40%; border-right: 1px solid rgba(255,255,255,0.1);">Auto-refresh</td>' +
                            '<td style="padding: 0.5em 0.75em; color: #fff;">' + eAutoRefreshDisplay + '</td>' +
                        '</tr>' +
                        '<tr style="border-bottom: 1px solid rgba(255,255,255,0.1);">' +
                            '<td style="padding: 0.5em 0.75em; font-weight: bold; color: #ccc; width: 40%; border-right: 1px solid rgba(255,255,255,0.1);">Scheduled refresh</td>' +
                            '<td style="padding: 0.5em 0.75em; color: #fff;">' + eScheduleDisplay + '</td>' +
                        '</tr>' +
                        '<tr style="border-bottom: 1px solid rgba(255,255,255,0.1);">' +
                            '<td style="padding: 0.5em 0.75em; font-weight: bold; color: #ccc; width: 40%; border-right: 1px solid rgba(255,255,255,0.1);">Date created</td>' +
                            '<td style="padding: 0.5em 0.75em; color: #fff;">' + eDateCreatedDisplay + '</td>' +
                        '</tr>' +
                        '<tr>' +
                            '<td style="padding: 0.5em 0.75em; font-weight: bold; color: #ccc; width: 40%; border-right: 1px solid rgba(255,255,255,0.1);">Last refreshed</td>' +
                            '<td style="padding: 0.5em 0.75em; color: #fff;">' + eLastRefreshDisplay + '</td>' +
                        '</tr>' +
                    '</table>' +
                '</div>' +
                
                // Action buttons (moved to bottom, after properties)
                '<div class="playlist-actions" style="margin-top: 1em; margin-bottom: 0.5em; padding-top: 0.5em;">' +
                    '<button type="button" is="emby-button" class="emby-button raised edit-playlist-btn" data-playlist-id="' + escapeHtmlAttribute(playlistId) + '" style="margin-right: 0.5em;">Edit</button>' +
                    '<button type="button" is="emby-button" class="emby-button raised clone-playlist-btn" data-playlist-id="' + escapeHtmlAttribute(playlistId) + '" data-playlist-name="' + escapeHtmlAttribute(playlist.Name || '') + '" style="margin-right: 0.5em;">Clone</button>' +
                    '<button type="button" is="emby-button" class="emby-button raised refresh-playlist-btn" data-playlist-id="' + escapeHtmlAttribute(playlistId) + '" data-playlist-name="' + escapeHtmlAttribute(playlist.Name || '') + '" style="margin-right: 0.5em;">Refresh</button>' +
                    (isEnabled ? 
                        '<button type="button" is="emby-button" class="emby-button raised disable-playlist-btn" data-playlist-id="' + escapeHtmlAttribute(playlistId) + '" data-playlist-name="' + escapeHtmlAttribute(playlist.Name || '') + '" style="margin-right: 0.5em;">Disable</button>' :
                        '<button type="button" is="emby-button" class="emby-button raised enable-playlist-btn" data-playlist-id="' + escapeHtmlAttribute(playlistId) + '" data-playlist-name="' + escapeHtmlAttribute(playlist.Name || '') + '" style="margin-right: 0.5em;">Enable</button>'
                    ) +
                    '<button type="button" is="emby-button" class="emby-button raised button-delete delete-playlist-btn" data-playlist-id="' + escapeHtmlAttribute(playlistId) + '" data-playlist-name="' + escapeHtmlAttribute(playlist.Name || '') + '">Delete</button>' +
                '</div>' +
            '</div>' +
        '</div>';
    }

    // Cached DOM elements for bulk operations (performance optimization)
    function getBulkActionElements(page, forceRefresh = false) {
        if (!page._bulkActionElements || forceRefresh) {
            page._bulkActionElements = {
                bulkContainer: page.querySelector('#bulkActionsContainer'),
                countDisplay: page.querySelector('#selectedCountDisplay'),
                bulkEnableBtn: page.querySelector('#bulkEnableBtn'),
                bulkDisableBtn: page.querySelector('#bulkDisableBtn'),
                bulkDeleteBtn: page.querySelector('#bulkDeleteBtn'),
                selectAllCheckbox: page.querySelector('#selectAllCheckbox')
            };
        }
        return page._bulkActionElements;
    }

    // Bulk operations functionality
    function updateBulkActionsVisibility(page) {
        const elements = getBulkActionElements(page, true); // Force refresh after HTML changes
        const checkboxes = page.querySelectorAll('.playlist-checkbox');
        
        // Show bulk actions if any playlists exist
        if (elements.bulkContainer) {
            elements.bulkContainer.style.display = checkboxes.length > 0 ? 'block' : 'none';
        }
        
        // Update selected count and button states
        updateSelectedCount(page);
    }
    
    function updateSelectedCount(page) {
        const selectedCheckboxes = page.querySelectorAll('.playlist-checkbox:checked');
        const selectedCount = selectedCheckboxes.length;
        const elements = getBulkActionElements(page);
        
        // Update count display
        if (elements.countDisplay) {
            elements.countDisplay.textContent = '(' + selectedCount + ' selected)';
        }
        
        // Update button states
        const hasSelection = selectedCount > 0;
        if (elements.bulkEnableBtn) elements.bulkEnableBtn.disabled = !hasSelection;
        if (elements.bulkDisableBtn) elements.bulkDisableBtn.disabled = !hasSelection;
        if (elements.bulkDeleteBtn) elements.bulkDeleteBtn.disabled = !hasSelection;
        
        // Update Select All checkbox state
        if (elements.selectAllCheckbox) {
            const totalCheckboxes = page.querySelectorAll('.playlist-checkbox').length;
            if (totalCheckboxes > 0) {
                elements.selectAllCheckbox.checked = selectedCount === totalCheckboxes;
                elements.selectAllCheckbox.indeterminate = selectedCount > 0 && selectedCount < totalCheckboxes;
            }
        }
    }
    
    function toggleSelectAll(page) {
        const elements = getBulkActionElements(page);
        const playlistCheckboxes = page.querySelectorAll('.playlist-checkbox');
        
        const shouldSelect = elements.selectAllCheckbox ? elements.selectAllCheckbox.checked : false;
        
        playlistCheckboxes.forEach(checkbox => {
            checkbox.checked = shouldSelect;
        });
        
        updateSelectedCount(page);
    }
    
    async function bulkEnablePlaylists(page) {
        const selectedCheckboxes = page.querySelectorAll('.playlist-checkbox:checked');
        const playlistIds = Array.from(selectedCheckboxes).map(cb => cb.getAttribute('data-playlist-id'));
        
        if (playlistIds.length === 0) {
            showNotification('No playlists selected', 'error');
            return;
        }
        
        // Filter to only playlists that are currently disabled
        const playlistsToEnable = [];
        const alreadyEnabled = [];
        
        for (const checkbox of selectedCheckboxes) {
            const playlistId = checkbox.getAttribute('data-playlist-id');
            const playlistCard = checkbox.closest('.playlist-card');
            const statusElement = playlistCard.querySelector('.playlist-status');
            const isCurrentlyEnabled = !statusElement || !statusElement.textContent.includes('Disabled');
            
            if (isCurrentlyEnabled) {
                alreadyEnabled.push(playlistId);
            } else {
                playlistsToEnable.push(playlistId);
            }
        }
        
        if (playlistsToEnable.length === 0) {
            showNotification('All selected playlists are already enabled', 'info');
            return;
        }
        
        const apiClient = getApiClient();
        let successCount = 0;
        let errorCount = 0;
        
        Dashboard.showLoadingMsg();
        
        for (const playlistId of playlistsToEnable) {
            try {
                await apiClient.ajax({
                    type: "POST",
                    url: apiClient.getUrl(ENDPOINTS.base + '/' + playlistId + '/enable'),
                    contentType: 'application/json'
                });
                successCount++;
            } catch (err) {
                console.error('Error enabling playlist:', playlistId, err);
                errorCount++;
            }
        }
        
        Dashboard.hideLoadingMsg();
        
        if (errorCount === 0) {
            showNotification(successCount + ' playlist(s) enabled successfully', 'success');
        } else {
            showNotification(successCount + ' enabled, ' + errorCount + ' failed', 'error');
        }
        
        // Refresh the list and clear selections
        loadPlaylistList(page);
    }
    
    async function bulkDisablePlaylists(page) {
        const selectedCheckboxes = page.querySelectorAll('.playlist-checkbox:checked');
        const playlistIds = Array.from(selectedCheckboxes).map(cb => cb.getAttribute('data-playlist-id'));
        
        if (playlistIds.length === 0) {
            showNotification('No playlists selected', 'error');
            return;
        }
        
        // Filter to only playlists that are currently enabled
        const playlistsToDisable = [];
        const alreadyDisabled = [];
        
        for (const checkbox of selectedCheckboxes) {
            const playlistId = checkbox.getAttribute('data-playlist-id');
            const playlistCard = checkbox.closest('.playlist-card');
            const statusElement = playlistCard.querySelector('.playlist-status');
            const isCurrentlyEnabled = !statusElement || !statusElement.textContent.includes('Disabled');
            
            if (isCurrentlyEnabled) {
                playlistsToDisable.push(playlistId);
            } else {
                alreadyDisabled.push(playlistId);
            }
        }
        
        if (playlistsToDisable.length === 0) {
            showNotification('All selected playlists are already disabled', 'info');
            return;
        }
        
        const apiClient = getApiClient();
        let successCount = 0;
        let errorCount = 0;
        
        Dashboard.showLoadingMsg();
        
        for (const playlistId of playlistsToDisable) {
            try {
                await apiClient.ajax({
                    type: "POST",
                    url: apiClient.getUrl(ENDPOINTS.base + '/' + playlistId + '/disable'),
                    contentType: 'application/json'
                });
                successCount++;
            } catch (err) {
                console.error('Error disabling playlist:', playlistId, err);
                errorCount++;
            }
        }
        
        Dashboard.hideLoadingMsg();
        
        if (errorCount === 0) {
            showNotification(successCount + ' playlist(s) disabled successfully', 'success');
        } else {
            showNotification(successCount + ' disabled, ' + errorCount + ' failed', 'error');
        }
        
        // Refresh the list and clear selections
        loadPlaylistList(page);
    }
    
    // Refresh confirmation modal function
    function showRefreshConfirmModal(page, onConfirm) {
        const modal = page.querySelector('#refresh-confirm-modal');
        if (!modal) return;
        
        // Clean up any existing modal listeners
        cleanupModalListeners(modal);

        // Apply modal styles using centralized configuration
        const modalContainer = modal.querySelector('.custom-modal-container');
        applyStyles(modalContainer, STYLES.modal.container);
        applyStyles(modal, STYLES.modal.backdrop);
        
        // Show the modal
        modal.classList.remove('hide');
        
        // Create AbortController for modal event listeners
        const modalAbortController = createAbortController();
        const modalSignal = modalAbortController.signal;

        // Clean up function to close modal and remove all listeners
        const cleanupAndClose = () => {
            modal.classList.add('hide');
            cleanupModalListeners(modal);
        };

        // Handle confirm button
        const confirmBtn = modal.querySelector('.modal-confirm-btn');
        confirmBtn.addEventListener('click', function() {
            cleanupAndClose();
            onConfirm();
        }, getEventListenerOptions(modalSignal));

        // Handle cancel button
        const cancelBtn = modal.querySelector('.modal-cancel-btn');
        cancelBtn.addEventListener('click', function() {
            cleanupAndClose();
        }, getEventListenerOptions(modalSignal));

        // Handle backdrop click
        modal.addEventListener('click', function(e) {
            if (e.target === modal) {
                cleanupAndClose();
            }
        }, getEventListenerOptions(modalSignal));

        // Store abort controller for cleanup
        modal._modalAbortController = modalAbortController;
    }

    // Generic delete modal function to reduce duplication
    function showDeleteModal(page, confirmText, onConfirm) {
        const modal = page.querySelector('#delete-confirm-modal');
        if (!modal) return;
        
        // Clean up any existing modal listeners
        cleanupModalListeners(modal);

        // Apply modal styles using centralized configuration
        const modalContainer = modal.querySelector('.custom-modal-container');
        applyStyles(modalContainer, STYLES.modal.container);
        applyStyles(modal, STYLES.modal.backdrop);

        // Set the confirmation text with proper line break handling
        const confirmTextElement = modal.querySelector('#delete-confirm-text');
        confirmTextElement.textContent = confirmText;
        confirmTextElement.style.whiteSpace = 'pre-line';
        
        // Reset checkbox to checked by default
        const checkbox = modal.querySelector('#delete-jellyfin-playlist-checkbox');
        if (checkbox) {
            checkbox.checked = true;
        }
        
        // Show the modal
        modal.classList.remove('hide');
        
        // Create AbortController for modal event listeners
        const modalAbortController = createAbortController();
        const modalSignal = modalAbortController.signal;

        // Clean up function to close modal and remove all listeners
        const cleanupAndClose = () => {
            modal.classList.add('hide');
            cleanupModalListeners(modal);
        };

        // Handle confirm button
        const confirmBtn = modal.querySelector('#delete-confirm-btn');
        confirmBtn.addEventListener('click', function() {
            cleanupAndClose();
            onConfirm();
        }, getEventListenerOptions(modalSignal));

        // Handle cancel button
        const cancelBtn = modal.querySelector('#delete-cancel-btn');
        cancelBtn.addEventListener('click', function() {
            cleanupAndClose();
        }, getEventListenerOptions(modalSignal));

        // Handle backdrop click
        modal.addEventListener('click', function(e) {
            if (e.target === modal) {
                cleanupAndClose();
            }
        }, getEventListenerOptions(modalSignal));

        // Store abort controller for cleanup
        modal._modalAbortController = modalAbortController;
    }

    function showBulkDeleteConfirm(page, playlistIds, playlistNames) {
        const playlistList = playlistNames.length > 5 
            ? playlistNames.slice(0, 5).join('\n') + `\n... and ${playlistNames.length - 5} more`
            : playlistNames.join('\n');
        
        const isPlural = playlistNames.length !== 1;
        const confirmText = `Are you sure you want to delete the following ${isPlural ? 'playlists' : 'playlist'}?\n\n${playlistList}\n\nThis action cannot be undone.`;
        
        showDeleteModal(page, confirmText, () => {
            performBulkDelete(page, playlistIds);
        });
    }

    async function performBulkDelete(page, playlistIds) {
        const apiClient = getApiClient();
        const deleteJellyfinPlaylist = page.querySelector('#delete-jellyfin-playlist-checkbox').checked;
        let successCount = 0;
        let errorCount = 0;
        
        Dashboard.showLoadingMsg();
        
        for (const playlistId of playlistIds) {
            try {
                await apiClient.ajax({
                    type: "DELETE",
                    url: apiClient.getUrl(ENDPOINTS.base + '/' + playlistId + '?deleteJellyfinPlaylist=' + deleteJellyfinPlaylist),
                    contentType: 'application/json'
                });
                successCount++;
            } catch (err) {
                console.error('Error deleting playlist:', playlistId, err);
                errorCount++;
            }
        }
        
        Dashboard.hideLoadingMsg();
        
        if (successCount > 0) {
            const action = deleteJellyfinPlaylist ? 'deleted' : 'suffix/prefix removed (if any) and configuration deleted';
            showNotification(`Successfully ${action} ${successCount} playlist(s).`, 'success');
        }
        if (errorCount > 0) {
            showNotification(`Failed to delete ${errorCount} playlist(s).`, 'error');
        }
        
        // Clear selections and reload
        const selectAllCheckbox = page.querySelector('#selectAllCheckbox');
        if (selectAllCheckbox) {
            selectAllCheckbox.checked = false;
        }
        
        loadPlaylistList(page);
    }

    async function bulkDeletePlaylists(page) {
        const selectedCheckboxes = page.querySelectorAll('.playlist-checkbox:checked');
        const playlistIds = Array.from(selectedCheckboxes).map(cb => cb.getAttribute('data-playlist-id'));
        
        if (playlistIds.length === 0) {
            showNotification('No playlists selected', 'error');
            return;
        }
        
        const playlistNames = Array.from(selectedCheckboxes).map(cb => {
            const playlistCard = cb.closest('.playlist-card');
            const nameElement = playlistCard ? playlistCard.querySelector('.playlist-header-left h3') : null;
            return nameElement ? nameElement.textContent : 'Unknown';
        });
        
        // Show the custom modal instead of browser confirm
        showBulkDeleteConfirm(page, playlistIds, playlistNames);
    }

    // Collapsible playlist functionality
    function togglePlaylistCard(playlistCard) {
        const details = playlistCard.querySelector('.playlist-details');
        const actions = playlistCard.querySelector('.playlist-actions');
        const icon = playlistCard.querySelector('.playlist-expand-icon');
        
        if (details.style.display === 'none' || details.style.display === '') {
            // Expand
            details.style.display = 'block';
            actions.style.display = 'block';
            icon.textContent = '▼';
            playlistCard.setAttribute('data-expanded', 'true');
        } else {
            // Collapse
            details.style.display = 'none';
            actions.style.display = 'none';
            icon.textContent = '▶';
            playlistCard.setAttribute('data-expanded', 'false');
        }
        
        // Save state to localStorage
        savePlaylistExpandStates();
    }

    function toggleAllPlaylists(page) {
        const expandAllBtn = page.querySelector('#expandAllBtn');
        const playlistCards = page.querySelectorAll('.playlist-card');
        
        if (!playlistCards.length) return;
        
        // Base action on current button text, not on current state
        const shouldExpand = expandAllBtn.textContent.trim() === 'Expand All';
        
        // Preserve scroll position when expanding to prevent unwanted scrolling
        const currentScrollTop = window.pageYOffset || document.documentElement.scrollTop;
        
        if (shouldExpand) {
            // Expand all
            playlistCards.forEach(card => {
                const details = card.querySelector('.playlist-details');
                const actions = card.querySelector('.playlist-actions');
                const icon = card.querySelector('.playlist-expand-icon');
                details.style.display = 'block';
                actions.style.display = 'block';
                icon.textContent = '▼';
                card.setAttribute('data-expanded', 'true');
            });
            expandAllBtn.textContent = 'Collapse All';
            
            // Restore scroll position after DOM changes to prevent unwanted scrolling
            requestAnimationFrame(() => {
                window.scrollTo(0, currentScrollTop);
            });
        } else {
            // Collapse all
            playlistCards.forEach(card => {
                const details = card.querySelector('.playlist-details');
                const actions = card.querySelector('.playlist-actions');
                const icon = card.querySelector('.playlist-expand-icon');
                details.style.display = 'none';
                actions.style.display = 'none';
                icon.textContent = '▶';
                card.setAttribute('data-expanded', 'false');
            });
            expandAllBtn.textContent = 'Expand All';
        }
        
        // Save state to localStorage
        savePlaylistExpandStates();
    }

    function savePlaylistExpandStates() {
        try {
            const playlistCards = document.querySelectorAll('.playlist-card');
            const states = {};
            
            playlistCards.forEach(card => {
                const playlistId = card.getAttribute('data-playlist-id');
                const isExpanded = card.getAttribute('data-expanded') === 'true';
                if (playlistId) {
                    states[playlistId] = isExpanded;
                }
            });
            
            localStorage.setItem('smartPlaylistExpandStates', JSON.stringify(states));
        } catch (err) {
            console.warn('Failed to save playlist expand states:', err);
        }
    }

    function loadPlaylistExpandStates() {
        try {
            const saved = localStorage.getItem('smartPlaylistExpandStates');
            if (!saved) return {};
            
            return JSON.parse(saved);
        } catch (err) {
            console.warn('Failed to load playlist expand states:', err);
            return {};
        }
    }

    function restorePlaylistExpandStates(page) {
        const savedStates = loadPlaylistExpandStates();
        const playlistCards = page.querySelectorAll('.playlist-card');
        
        playlistCards.forEach(card => {
            const playlistId = card.getAttribute('data-playlist-id');
            const shouldExpand = savedStates[playlistId] === true;
            
            if (shouldExpand) {
                const details = card.querySelector('.playlist-details');
                const actions = card.querySelector('.playlist-actions');
                const icon = card.querySelector('.playlist-expand-icon');
                details.style.display = 'block';
                actions.style.display = 'block';
                icon.textContent = '▼';
                card.setAttribute('data-expanded', 'true');
            } else {
                // Ensure collapsed state (default)
                card.setAttribute('data-expanded', 'false');
            }
        });
    }

    function updateExpandAllButtonText(page) {
        const expandAllBtn = page.querySelector('#expandAllBtn');
        const playlistCards = page.querySelectorAll('.playlist-card');
        
        if (!expandAllBtn || !playlistCards.length) return;
        
        // Count how many playlists are currently expanded
        const expandedCount = Array.from(playlistCards).filter(card => 
            card.getAttribute('data-expanded') === 'true'
        ).length;
        const totalCount = playlistCards.length;
        
        // Update button text based on current state
        if (expandedCount === totalCount) {
            expandAllBtn.textContent = 'Collapse All';
        } else {
            expandAllBtn.textContent = 'Expand All';
        }
    }

    function sortPlaylists(playlists, sortBy) {
        if (!sortBy || !playlists) return playlists || [];
        
        // Ensure playlists is an array
        if (!Array.isArray(playlists)) {
            console.error('sortPlaylists: playlists is not an array:', typeof playlists, playlists);
            return [];
        }
        
        if (playlists.length === 0) return playlists;
        
        const sortedPlaylists = [...playlists]; // Create a copy to avoid mutating original
        
        switch (sortBy) {
            case 'name-asc':
                return sortedPlaylists.sort((a, b) => {
                    const nameA = (a.Name || '').toLowerCase();
                    const nameB = (b.Name || '').toLowerCase();
                    return nameA.localeCompare(nameB);
                });
                
            case 'name-desc':
                return sortedPlaylists.sort((a, b) => {
                    const nameA = (a.Name || '').toLowerCase();
                    const nameB = (b.Name || '').toLowerCase();
                    return nameB.localeCompare(nameA);
                });
                
            case 'created-desc':
                return sortedPlaylists.sort((a, b) => {
                    const dateA = a.DateCreated ? new Date(a.DateCreated) : new Date(0);
                    const dateB = b.DateCreated ? new Date(b.DateCreated) : new Date(0);
                    return dateB - dateA;
                });
                
            case 'created-asc':
                return sortedPlaylists.sort((a, b) => {
                    const dateA = a.DateCreated ? new Date(a.DateCreated) : new Date(0);
                    const dateB = b.DateCreated ? new Date(b.DateCreated) : new Date(0);
                    return dateA - dateB;
                });
                
            case 'refreshed-desc':
                return sortedPlaylists.sort((a, b) => {
                    const dateA = a.LastRefreshed ? new Date(a.LastRefreshed) : new Date(0);
                    const dateB = b.LastRefreshed ? new Date(b.LastRefreshed) : new Date(0);
                    return dateB - dateA;
                });
                
            case 'refreshed-asc':
                return sortedPlaylists.sort((a, b) => {
                    const dateA = a.LastRefreshed ? new Date(a.LastRefreshed) : new Date(0);
                    const dateB = b.LastRefreshed ? new Date(b.LastRefreshed) : new Date(0);
                    return dateA - dateB;
                });
                
            case 'enabled-first':
                return sortedPlaylists.sort((a, b) => {
                    const enabledA = a.Enabled !== false ? 1 : 0;
                    const enabledB = b.Enabled !== false ? 1 : 0;
                    if (enabledA !== enabledB) return enabledB - enabledA;
                    // Secondary sort by name
                    return (a.Name || '').toLowerCase().localeCompare((b.Name || '').toLowerCase());
                });
                
            case 'disabled-first':
                return sortedPlaylists.sort((a, b) => {
                    const enabledA = a.Enabled !== false ? 1 : 0;
                    const enabledB = b.Enabled !== false ? 1 : 0;
                    if (enabledA !== enabledB) return enabledA - enabledB;
                    // Secondary sort by name
                    return (a.Name || '').toLowerCase().localeCompare((b.Name || '').toLowerCase());
                });
                
            default:
                return sortedPlaylists;
        }
    }

    function applyAllFiltersAndSort(page, playlists) {
        if (!playlists) return [];
        
        // Ensure playlists is an array
        if (!Array.isArray(playlists)) {
            console.error('applyAllFiltersAndSort: playlists is not an array:', typeof playlists, playlists);
            return [];
        }
        
        let filteredPlaylists = [...playlists];
        
        // Apply all filters using the generic system - much cleaner!
        const filterOrder = ['search', 'mediaType', 'visibility', 'user'];
        
        for (const filterKey of filterOrder) {
            const filterValue = getFilterValue(page, filterKey);
            filteredPlaylists = applyFilter(filteredPlaylists, filterKey, filterValue, page);
        }
        
        // Apply sorting
        const sortValue = getFilterValue(page, 'sort') || 'name-asc';
        filteredPlaylists = sortPlaylists(filteredPlaylists, sortValue);
        
        return filteredPlaylists;
    }


    function clearAllFilters(page) {
        // Clear search
        const searchInput = page.querySelector('#playlistSearchInput');
        if (searchInput) {
            searchInput.value = '';
        }
        
        // Reset filters to default
        const mediaTypeFilter = page.querySelector('#mediaTypeFilter');
        if (mediaTypeFilter) {
            mediaTypeFilter.value = 'all';
        }
        
        const visibilityFilter = page.querySelector('#visibilityFilter');
        if (visibilityFilter) {
            visibilityFilter.value = 'all';
        }
        
        const userFilter = page.querySelector('#userFilter');
        if (userFilter) {
            userFilter.value = 'all';
        }
        
        // Reset sort to default
        const sortSelect = page.querySelector('#playlistSortSelect');
        if (sortSelect) {
            sortSelect.value = 'name-asc';
        }
        
        // Save preferences
        savePlaylistFilterPreferences(page);
        
        // Apply filters
        applySearchFilter(page).catch(err => {
            console.error('Error during clear filters:', err);
            showNotification('Filter error: ' + err.message);
        });
        
        // Update clear button visibility
        const clearSearchBtn = page.querySelector('#clearSearchBtn');
        if (clearSearchBtn) {
            clearSearchBtn.style.display = 'none';
        }
    }

    // Enhanced preferences system with validation and error recovery
    function savePlaylistFilterPreferences(page) {
        try {
            const preferences = {};
            
            // Get preferences for all filters except search (session-specific)
            const persistentFilters = ['sort', 'mediaType', 'visibility', 'user'];
            
            for (const filterKey of persistentFilters) {
                const config = PLAYLIST_FILTER_CONFIGS[filterKey];
                if (config) {
                    const element = page.querySelector(config.selector);
                    if (element) {
                        const value = config.getValue(element);
                        // Only save non-default values to reduce storage
                        if (value !== config.defaultValue) {
                            preferences[filterKey] = value;
                        }
                    }
                }
            }
            
            localStorage.setItem('smartPlaylistFilterPreferences', JSON.stringify(preferences));
            console.debug('Saved filter preferences:', preferences);
        } catch (err) {
            console.warn('Failed to save playlist filter preferences:', err);
        }
    }

    function loadPlaylistFilterPreferences(page) {
        try {
            const saved = localStorage.getItem('smartPlaylistFilterPreferences');
            if (!saved) {
                console.debug('No saved filter preferences found, using defaults');
                return;
            }
            
            const preferences = JSON.parse(saved);
            console.debug('Loading filter preferences:', preferences);
            
            // Apply saved preferences using the generic system with validation
            Object.entries(preferences).forEach(([filterKey, value]) => {
                const config = PLAYLIST_FILTER_CONFIGS[filterKey];
                if (config && value !== undefined) {
                    const element = page.querySelector(config.selector);
                    if (element) {
                        // Validate that the saved value is still valid for this element
                        const options = Array.from(element.options || []);
                        const isValidOption = options.length === 0 || options.some(opt => opt.value === value);
                        
                        if (isValidOption) {
                            element.value = value;
                            console.debug(`Restored ${filterKey} filter to:`, value);
                        } else {
                            console.warn(`Invalid saved value for ${filterKey}:`, value, 'Available options:', options.map(o => o.value));
                            // Fall back to default value
                            element.value = config.defaultValue;
                        }
                    } else {
                        console.warn(`Filter element not found for ${filterKey}:`, config.selector);
                    }
                } else {
                    console.warn(`Invalid filter configuration for ${filterKey}`);
                }
            });
            
            // Ensure all filters have valid values, even if not in saved preferences
            const persistentFilters = ['sort', 'mediaType', 'visibility', 'user'];
            persistentFilters.forEach(filterKey => {
                if (!preferences.hasOwnProperty(filterKey)) {
                    const config = PLAYLIST_FILTER_CONFIGS[filterKey];
                    if (config) {
                        const element = page.querySelector(config.selector);
                        if (element && !element.value) {
                            element.value = config.defaultValue;
                            console.debug(`Set default value for ${filterKey}:`, config.defaultValue);
                        }
                    }
                }
            });
            
        } catch (err) {
            console.warn('Failed to load playlist filter preferences:', err);
            // Reset to defaults on error
            resetFiltersToDefaults(page);
        }
    }
    
    function resetFiltersToDefaults(page) {
        try {
            const persistentFilters = ['sort', 'mediaType', 'visibility', 'user'];
            persistentFilters.forEach(filterKey => {
                const config = PLAYLIST_FILTER_CONFIGS[filterKey];
                if (config) {
                    const element = page.querySelector(config.selector);
                    if (element) {
                        element.value = config.defaultValue;
                    }
                }
            });
            console.debug('Reset all filters to defaults');
        } catch (err) {
            console.error('Failed to reset filters to defaults:', err);
        }
    }

    async function populateUserFilter(page, playlists) {
        const userFilter = page.querySelector('#userFilter');
        if (!userFilter || !playlists) return;
        
        try {
            // Ensure playlists is an array
            if (!Array.isArray(playlists)) {
                console.warn('Playlists is not an array:', typeof playlists, playlists);
                return;
            }
            
            // Initialize username cache with size limit if it doesn't exist
            if (!page._usernameCache) {
                page._usernameCache = new Map();
                page._usernameCacheMaxSize = 100; // Limit cache size
            }
            
            // Clear cache if it gets too large (simple LRU-like behavior)
            if (page._usernameCache.size > page._usernameCacheMaxSize) {
                console.log('Username cache size exceeded, clearing old entries');
                const entries = Array.from(page._usernameCache.entries());
                // Keep only the last 50% of entries (rough LRU approximation)
                page._usernameCache.clear();
                const keepCount = Math.floor(page._usernameCacheMaxSize / 2);
                entries.slice(-keepCount).forEach(([key, value]) => {
                    page._usernameCache.set(key, value);
                });
            }
            
            // Get unique user IDs from playlists
            const userIds = [...new Set(playlists.map(p => p.UserId).filter(id => id))];
            
            // Clear existing options except "All Users"
            const allUsersOption = userFilter.querySelector('option[value="all"]');
            userFilter.innerHTML = '';
            if (allUsersOption) {
                userFilter.appendChild(allUsersOption);
            } else {
                const defaultOption = document.createElement('option');
                defaultOption.value = 'all';
                defaultOption.textContent = 'All Users';
                userFilter.appendChild(defaultOption);
            }
            
            // Fetch user names and populate dropdown
            const apiClient = getApiClient();
            for (const userId of userIds) {
                try {
                    // Try to resolve username
                    const userName = await resolveUsername(apiClient, { UserId: userId });
                    
                    // Cache the resolved username for search functionality
                    page._usernameCache.set(userId, userName || 'Unknown User');
                    
                    const option = document.createElement('option');
                    option.value = userId;
                    option.textContent = userName || 'Unknown User';
                    userFilter.appendChild(option);
                } catch (err) {
                    console.warn('Failed to resolve username for user ID:', userId, err);
                    // Still add the option with the user ID and cache it
                    const fallbackName = 'User ' + userId.substring(0, 8) + '...';
                    page._usernameCache.set(userId, fallbackName);
                    
                    const option = document.createElement('option');
                    option.value = userId;
                    option.textContent = fallbackName;
                    userFilter.appendChild(option);
                }
            }
        } catch (err) {
            console.warn('Failed to populate user filter:', err);
        }
    }

    async function applySearchFilter(page) {
        const searchInput = page.querySelector('#playlistSearchInput');
        if (!searchInput || !page._allPlaylists) {
            return;
        }
        
        // Don't search while loading playlists
        if (page._loadingPlaylists) {
            return;
        }
        
        // Apply all filters and sorting
        const filteredPlaylists = applyAllFiltersAndSort(page, page._allPlaylists);
        
        // Display the filtered results
        await displayFilteredPlaylists(page, filteredPlaylists, '');
    }

    async function displayFilteredPlaylists(page, filteredPlaylists, searchTerm) {
        const container = page.querySelector('#playlist-list-container');
        const apiClient = getApiClient();
        
        // Calculate summary statistics for filtered results
        const totalPlaylists = page._allPlaylists.length;
        const filteredCount = filteredPlaylists.length;
        const enabledPlaylists = filteredPlaylists.filter(p => p.Enabled !== false).length;
        const disabledPlaylists = filteredCount - enabledPlaylists;
        
        let html = '';
        
        // Add bulk actions container after summary
        let summaryText;
        summaryText = generateSummaryText(totalPlaylists, enabledPlaylists, disabledPlaylists, filteredCount, searchTerm);
        html += generateBulkActionsHTML(summaryText);
        
        // Process filtered playlists using the helper function
        for (const playlist of filteredPlaylists) {
            // Resolve username first
            const resolvedUserName = await resolveUsername(apiClient, playlist);
            
            // Generate detailed rules display using helper function
            const rulesHtml = await generateRulesHtml(playlist, apiClient);
            
            // Use helper function to generate playlist HTML (DRY)
            html += generatePlaylistCardHtml(playlist, rulesHtml, resolvedUserName);
        }
        
        container.innerHTML = html;
        
        // Restore expand states from localStorage after regenerating HTML
        restorePlaylistExpandStates(page);
        
        // Update expand all button text based on current states
        updateExpandAllButtonText(page);
        
        // Update bulk actions visibility and state
        updateBulkActionsVisibility(page);
    }

    function refreshPlaylist(playlistId, playlistName) {
        const apiClient = getApiClient();
        
        Dashboard.showLoadingMsg();
        
        apiClient.ajax({
            type: 'POST',
            url: apiClient.getUrl(ENDPOINTS.base + '/' + playlistId + '/refresh'),
            contentType: 'application/json'
        }).then(() => {
            Dashboard.hideLoadingMsg();
            showNotification('Playlist "' + playlistName + '" has been refreshed successfully.', 'success');
            
            // Auto-refresh the playlist list to show updated LastRefreshed timestamp
            const page = document.querySelector('.SmartPlaylistConfigurationPage');
            if (page) {
                loadPlaylistList(page);
            }
        }).catch(async (err) => {
            Dashboard.hideLoadingMsg();
            
            // Enhanced error handling for API responses
            let errorMessage = 'An unexpected error occurred, check the logs for more details.';
            
            try {
                // Check if this is a Response object (from fetch API)
                if (err && typeof err.json === 'function') {
                    try {
                        const errorData = await err.json();
                        if (errorData.message) {
                            errorMessage = errorData.message;
                        } else if (typeof errorData === 'string') {
                            errorMessage = errorData;
                        }
                    } catch (parseError) {
                        // If JSON parsing fails, try to get text
                        try {
                            const textContent = await err.text();
                            if (textContent) {
                                errorMessage = textContent;
                            }
                        } catch (textError) {
                            console.log('Could not extract error text:', textError);
                        }
                    }
                }
                // Check if the error has response text (legacy error format)
                else if (err.responseText) {
                    try {
                        const errorData = JSON.parse(err.responseText);
                        if (errorData.message) {
                            errorMessage = errorData.message;
                        } else if (typeof errorData === 'string') {
                            errorMessage = errorData;
                        }
                    } catch (parseError) {
                        // If JSON parsing fails, use the raw response text
                        errorMessage = err.responseText;
                    }
                } else if (err.message) {
                    errorMessage = err.message;
                }
            } catch (processingError) {
                console.error('Error processing API error response:', processingError);
            }
            
            const fullMessage = 'Failed to refresh playlist "' + playlistName + '": ' + errorMessage;
            console.error('Playlist refresh error:', fullMessage, err);
            showNotification(fullMessage, 'error');
        });
    }

    function deletePlaylist(page, playlistId, playlistName) {
        const apiClient = getApiClient();
        const deleteJellyfinPlaylist = page.querySelector('#delete-jellyfin-playlist-checkbox').checked;
        
        Dashboard.showLoadingMsg();
        apiClient.ajax({
            type: "DELETE",
            url: apiClient.getUrl(ENDPOINTS.base + '/' + playlistId + '?deleteJellyfinPlaylist=' + deleteJellyfinPlaylist),
            contentType: 'application/json'
        }).then(() => {
            Dashboard.hideLoadingMsg();
            const action = deleteJellyfinPlaylist ? 'deleted' : 'suffix/prefix removed (if any) and configuration deleted';
            showNotification('Playlist "' + playlistName + '" ' + action + ' successfully.', 'success');
            loadPlaylistList(page);
        }).catch(err => {
            Dashboard.hideLoadingMsg();
            displayApiError(err, 'Failed to delete playlist "' + playlistName + '"');
        });
    }

    function enablePlaylist(page, playlistId, playlistName) {
        const apiClient = getApiClient();
        
        Dashboard.showLoadingMsg();
        apiClient.ajax({
            type: "POST",
            url: apiClient.getUrl(ENDPOINTS.base + '/' + playlistId + '/enable'),
            contentType: 'application/json'
        }).then(response => {
            if (!response.ok) { throw new Error('HTTP ' + response.status + ': ' + response.statusText); }
            return response.json();
        }).then(result => {
            Dashboard.hideLoadingMsg();
            showNotification(result.message || 'Playlist "' + playlistName + '" has been enabled.', 'success');
            loadPlaylistList(page);
        }).catch(err => {
            Dashboard.hideLoadingMsg();
            displayApiError(err, 'Failed to enable playlist "' + playlistName + '"');
        });
    }

    function disablePlaylist(page, playlistId, playlistName) {
        const apiClient = getApiClient();
        
        Dashboard.showLoadingMsg();
        apiClient.ajax({
            type: "POST",
            url: apiClient.getUrl(ENDPOINTS.base + '/' + playlistId + '/disable'),
            contentType: 'application/json'
        }).then(response => {
            if (!response.ok) { throw new Error('HTTP ' + response.status + ': ' + response.statusText); }
            return response.json();
        }).then(result => {
            Dashboard.hideLoadingMsg();
            showNotification(result.message || 'Playlist "' + playlistName + '" has been disabled.', 'success');
            loadPlaylistList(page);
        }).catch(err => {
            Dashboard.hideLoadingMsg();
            displayApiError(err, 'Failed to disable playlist "' + playlistName + '"');
        });
    }

    function showDeleteConfirm(page, playlistId, playlistName) {
        const confirmText = 'Are you sure you want to delete the smart playlist "' + playlistName + '"? This cannot be undone.';
        
        showDeleteModal(page, confirmText, () => {
            deletePlaylist(page, playlistId, playlistName);
        });
    }

    function cleanupModalListeners(modal) {
        // Remove any existing backdrop listener to prevent accumulation
        // Use modal-specific handler instead of global
        if (modal._modalBackdropHandler) {
            modal.removeEventListener('click', modal._modalBackdropHandler);
            modal._modalBackdropHandler = null;
        }
        
        // Abort any AbortController-managed listeners
        if (modal._modalAbortController) {
            try {
                modal._modalAbortController.abort();
            } catch (err) {
                console.warn('Error aborting modal listeners:', err);
            } finally {
                modal._modalAbortController = null;
            }
        }
    }
    
    // Clean up all page-level event listeners and cached elements
    function cleanupPageEventListeners(page) {
        try {
            // Clean up bulk action element cache
            if (page._bulkActionElements) {
                page._bulkActionElements = null;
            }
            
            // Clean up username cache
            if (page._usernameCache) {
                page._usernameCache.clear();
                page._usernameCache = null;
            }
            
            // Clean up any page-level abort controllers
            if (page._pageAbortController) {
                page._pageAbortController.abort();
                page._pageAbortController = null;
            }
            
            // Clean up all rule abort controllers
            const ruleRows = page.querySelectorAll('.rule-row');
            ruleRows.forEach(ruleRow => {
                if (ruleRow._abortController) {
                    try {
                        ruleRow._abortController.abort();
                    } catch (err) {
                        console.warn('Error aborting rule listeners:', err);
                    } finally {
                        ruleRow._abortController = null;
                    }
                }
            });
            
            console.debug('Cleaned up page event listeners and cached elements');
        } catch (err) {
            console.error('Error during page cleanup:', err);
        }
    }

    // Helper function to parse sort order from playlist data
    function parseSortOrder(playlist) {
        const orderName = playlist.Order ? playlist.Order.Name : 'Name Ascending';
        
        let sortBy, sortOrder;
        if (orderName === 'Random' || orderName === 'NoOrder' || orderName === 'No Order') {
            // Special handling for Random/NoOrder - no Asc/Desc
            sortBy = (orderName === 'No Order') ? 'NoOrder' : orderName;
            sortOrder = 'Ascending'; // Default sort order (though it won't be used)
        } else {
            // Normal parsing for other orders like "Name Ascending"
            const parts = orderName.split(' ');
            sortBy = parts.slice(0, -1).join(' ') || 'Name';
            sortOrder = parts[parts.length - 1] || 'Ascending';
        }
        
        return { sortBy, sortOrder };
    }

    // Helper function to apply sort order to form elements
    function applySortOrderToForm(page, playlist) {
        const { sortBy, sortOrder } = parseSortOrder(playlist);
        
        const sortByElem = page.querySelector('#sortBy');
        if (sortByElem) {
            sortByElem.value = sortBy;
        }
        
        const sortOrderElem = page.querySelector('#sortOrder');
        if (sortOrderElem) {
            sortOrderElem.value = sortOrder;
        }
        
        // Hide/show Sort Order based on loaded Sort By value
        const sortOrderContainer = page.querySelector('#sortOrder-container');
        if (sortOrderContainer) {
            toggleSortOrderVisibility(sortOrderContainer, sortBy);
        }
    }

    async function editPlaylist(page, playlistId) {
        const apiClient = getApiClient();
        Dashboard.showLoadingMsg();
        
        // Always scroll to top when entering edit mode (auto for instant behavior)
        window.scrollTo({ top: 0, behavior: 'auto' });
                
        apiClient.ajax({
            type: "GET",
            url: apiClient.getUrl(ENDPOINTS.base + '/' + playlistId),
            contentType: 'application/json'
        }).then(response => {
            if (!response.ok) { throw new Error('HTTP ' + response.status + ': ' + response.statusText); }
            return response.json();
        }).then(playlist => {
            Dashboard.hideLoadingMsg();
            
            if (!playlist) {
                showNotification('No playlist data received from server.');
                return;
            }
            
            try {
                // Populate form with playlist data using helper functions
                setElementValue(page, '#playlistName', playlist.Name || '');
                setElementChecked(page, '#playlistIsPublic', playlist.Public || false);
                setElementChecked(page, '#playlistIsEnabled', playlist.Enabled !== false); // Default to true for backward compatibility
                
                // Handle AutoRefresh with backward compatibility
                const autoRefreshValue = playlist.AutoRefresh !== undefined ? playlist.AutoRefresh : 'Never';
                const autoRefreshElement = page.querySelector('#autoRefreshMode');
                if (autoRefreshElement) {
                    autoRefreshElement.value = autoRefreshValue;
                }
                
                // Handle schedule settings with backward compatibility
                const scheduleTriggerElement = page.querySelector('#scheduleTrigger');
                if (scheduleTriggerElement) {
                    // Convert "None" back to empty string for form display
                    const triggerValue = playlist.ScheduleTrigger === 'None' ? '' : (playlist.ScheduleTrigger || '');
                    scheduleTriggerElement.value = triggerValue;
                    if (!scheduleTriggerElement._spListenerAdded) {
                        scheduleTriggerElement.addEventListener('change', function() {
                            updateScheduleContainers(page, this.value);
                        });
                        scheduleTriggerElement._spListenerAdded = true;
                    }
                    updateScheduleContainers(page, triggerValue);
                }
                
                const scheduleTimeElement = page.querySelector('#scheduleTime');
                if (scheduleTimeElement && playlist.ScheduleTime) {
                    const timeString = playlist.ScheduleTime.substring(0, 5);
                    scheduleTimeElement.value = timeString;
                }
                
                const scheduleDayElement = page.querySelector('#scheduleDayOfWeek');
                if (scheduleDayElement && playlist.ScheduleDayOfWeek !== undefined) {
                    scheduleDayElement.value = convertDayOfWeekToValue(playlist.ScheduleDayOfWeek);
                }
                
                const scheduleDayOfMonthElement = page.querySelector('#scheduleDayOfMonth');
                if (scheduleDayOfMonthElement && playlist.ScheduleDayOfMonth !== undefined) {
                    scheduleDayOfMonthElement.value = playlist.ScheduleDayOfMonth.toString();
                }
                
                const scheduleIntervalElement = page.querySelector('#scheduleInterval');
                if (scheduleIntervalElement && playlist.ScheduleInterval) {
                    scheduleIntervalElement.value = playlist.ScheduleInterval;
                }
                
                // Handle MaxItems with backward compatibility for existing playlists
                // Default to 0 (unlimited) for old playlists that didn't have this setting
                const maxItemsValue = (playlist.MaxItems !== undefined && playlist.MaxItems !== null) ? playlist.MaxItems : 0;
                const maxItemsElement = page.querySelector('#playlistMaxItems');
                if (maxItemsElement) {
                    maxItemsElement.value = maxItemsValue;
                } else {
                    console.warn('Max Items element not found when trying to populate edit form');
                }
                
                // Handle MaxPlayTimeMinutes with backward compatibility for existing playlists
                // Default to 0 (unlimited) for old playlists that didn't have this setting
                const maxPlayTimeMinutesValue = (playlist.MaxPlayTimeMinutes !== undefined && playlist.MaxPlayTimeMinutes !== null) ? playlist.MaxPlayTimeMinutes : 0;
                const maxPlayTimeMinutesElement = page.querySelector('#playlistMaxPlayTimeMinutes');
                if (maxPlayTimeMinutesElement) {
                    maxPlayTimeMinutesElement.value = maxPlayTimeMinutesValue;
                } else {
                    console.warn('Max Play Time Minutes element not found when trying to populate edit form');
                }
                
                // Set media types
                const mediaTypesSelect = Array.from(page.querySelectorAll('.media-type-checkbox'));
                if (playlist.MediaTypes && playlist.MediaTypes.length > 0) {
                    playlist.MediaTypes.forEach(type => {
                        const checkbox = mediaTypesSelect.find(checkbox => checkbox.value === type);
                        if (checkbox) {
                            checkbox.checked = true;
                        }
                    });
                }
                
                // Set the playlist owner
                if (playlist.UserId && playlist.UserId !== '00000000-0000-0000-0000-000000000000') {
                    setElementValue(page, '#playlistUser', playlist.UserId);
                } else if (playlist.User) {
                    // Legacy support: try to find user by username (simplified)
                    // Since this is legacy, just warn the user and use current user as fallback
                    console.warn('Legacy playlist detected with username:', playlist.User);
                    showNotification('Legacy playlist detected. Please verify the owner is correct.', 'warning');
                    setCurrentUserAsDefault(page);
                }
                
                // Set sort options using helper function
                applySortOrderToForm(page, playlist);
                
                // Clear existing rules
                const rulesContainer = page.querySelector('#rules-container');
                rulesContainer.innerHTML = '';
                
                // Populate logic groups and rules
                if (playlist.ExpressionSets && playlist.ExpressionSets.length > 0) {
                    playlist.ExpressionSets.forEach((expressionSet, groupIndex) => {
                        let logicGroup;
                        
                        if (groupIndex === 0) {
                            // Create first logic group
                            logicGroup = createInitialLogicGroup(page);
                            // Remove only the rules, preserve the label
                            const rulesToRemove = logicGroup.querySelectorAll('.rule-row, .rule-within-group-separator');
                            rulesToRemove.forEach(rule => rule.remove());
                        } else {
                            // Add subsequent logic groups
                            logicGroup = addNewLogicGroup(page);
                            // Remove only the rules, preserve the label
                            const rulesToRemove = logicGroup.querySelectorAll('.rule-row, .rule-within-group-separator');
                            rulesToRemove.forEach(rule => rule.remove());
                        }
                        
                        // Add rules to this logic group
                        if (expressionSet.Expressions && expressionSet.Expressions.length > 0) {
                            expressionSet.Expressions.forEach(expression => {
                                addRuleToGroup(page, logicGroup);
                                const ruleRows = logicGroup.querySelectorAll('.rule-row');
                                const currentRule = ruleRows[ruleRows.length - 1];
                                
                                const fieldSelect = currentRule.querySelector('.rule-field-select');
                                const operatorSelect = currentRule.querySelector('.rule-operator-select');
                                const valueContainer = currentRule.querySelector('.rule-value-container');
                                
                                fieldSelect.value = expression.MemberName;
                                
                                // Update operator options first
                                updateOperatorOptions(expression.MemberName, operatorSelect);
                                
                                // Set operator so setValueInput knows what type of input to create
                                operatorSelect.value = expression.Operator;
                                
                                // Update UI elements based on the loaded rule data
                                // Pass the operator and current value to ensure correct input type is created
                                setValueInput(expression.MemberName, valueContainer, expression.Operator, expression.TargetValue);
                                updateUserSelectorVisibility(currentRule, expression.MemberName);
                                updateNextUnwatchedOptionsVisibility(currentRule, expression.MemberName);
                                updateCollectionsOptionsVisibility(currentRule, expression.MemberName);
                                
                                // Set value AFTER the correct input type is created
                                const valueInput = currentRule.querySelector('.rule-value-input');
                                if (valueInput) {
                                    // For tag-based inputs (IsIn/IsNotIn), the tags are already created by setValueInput
                                    // For relative date operators, we need to parse the "number:unit" format
                                    const isRelativeDateOperator = expression.Operator === 'NewerThan' || expression.Operator === 'OlderThan';
                                    if (isRelativeDateOperator && expression.TargetValue) {
                                        const parts = expression.TargetValue.split(':');
                                        if (parts.length === 2) {
                                            const num = parts[0];
                                            const unit = parts[1];
                                            
                                            // Set the number input
                                            valueInput.value = num;
                                            
                                            // Set the unit dropdown
                                            const unitSelect = currentRule.querySelector('.rule-value-unit');
                                            if (unitSelect) {
                                                unitSelect.value = unit;
                                            }
                                        }
                                                    } else if (expression.Operator === 'IsIn' || expression.Operator === 'IsNotIn') {
                                    // For tag-based inputs, the tags are already created by setValueInput
                                    // and the hidden input is already synced - no additional assignment needed
                                    } else {
                                        // For regular inputs, set the value directly
                                        valueInput.value = expression.TargetValue;
                                    }
                                }
                                
                                // Set user selector if this is a user-specific rule
                                if (expression.UserId) {
                                    const userSelect = currentRule.querySelector('.rule-user-select');
                                    if (userSelect) {
                                        // Load users for this rule selector and then set the value
                                        loadUsersForRule(userSelect, true).then(() => {
                                            userSelect.value = expression.UserId;
                                        });
                                    }
                                }
                                
                                // Set NextUnwatched options if this is a NextUnwatched rule
                                if (expression.MemberName === 'NextUnwatched') {
                                    const nextUnwatchedSelect = currentRule.querySelector('.rule-nextunwatched-select');
                                    if (nextUnwatchedSelect) {
                                        // Set the value based on the IncludeUnwatchedSeries parameter
                                        // Default to true if not specified (backwards compatibility)
                                        const includeValue = expression.IncludeUnwatchedSeries !== false ? 'true' : 'false';
                                        nextUnwatchedSelect.value = includeValue;
                                    }
                                }
                                
                                // Set Collections options if this is a Collections rule
                                if (expression.MemberName === 'Collections') {
                                    const collectionsSelect = currentRule.querySelector('.rule-collections-select');
                                    if (collectionsSelect) {
                                        // Set the value based on the IncludeEpisodesWithinSeries parameter
                                        // Default to false if not specified (backwards compatibility)
                                        const includeValue = expression.IncludeEpisodesWithinSeries === true ? 'true' : 'false';
                                        collectionsSelect.value = includeValue;
                                    }
                                }
                                
                                updateRegexHelp(currentRule);
                            });
                        } else {
                            // Add one empty rule if no expressions exist in this group
                            addRuleToGroup(page, logicGroup);
                        }
                    });
                } else {
                    // Add one empty logic group with one rule if no expression sets exist
                    createInitialLogicGroup(page);
                }
                
                // If we get here, form population was successful - now enter edit mode
                setPageEditState(page, true, playlistId);
                
                // Update UI to show edit mode
                const editIndicator = page.querySelector('#edit-mode-indicator');
                editIndicator.style.display = 'block';
                editIndicator.querySelector('span').textContent = '✏️ Editing Mode - Modifying existing playlist "' + playlist.Name + '"';
                page.querySelector('#submitBtn').textContent = 'Update Playlist';
                
                // Update tab button text
                const createTabButton = page.querySelector('a[data-tab="create"]');
                if (createTabButton) {
                    createTabButton.textContent = 'Edit Playlist';
                }
                
                // Switch to create tab (which becomes edit tab) using shared helper
                switchToTab(page, 'create');
                
                // Update button visibility after editing form is populated
                updateRuleButtonVisibility(page);
            
            showNotification('Playlist "' + playlist.Name + '" loaded for editing.', 'success');
                
            } catch (formError) {
                console.error('Error populating form:', formError);
                showNotification('Error loading playlist data: ' + formError.message);
            }
        }).catch(err => {
            Dashboard.hideLoadingMsg();
            console.error('Error loading playlist for edit:', err);
            handleApiError(err, 'Failed to load playlist for editing');
        });
    }

    async function clonePlaylist(page, playlistId, playlistName) {
        const apiClient = getApiClient();
        Dashboard.showLoadingMsg();
        
        // Always scroll to top when entering clone mode (auto for instant behavior)
        window.scrollTo({ top: 0, behavior: 'auto' });
                
        apiClient.ajax({
            type: "GET",
            url: apiClient.getUrl(ENDPOINTS.base + '/' + playlistId),
            contentType: 'application/json'
        }).then(response => {
            if (!response.ok) { throw new Error('HTTP ' + response.status + ': ' + response.statusText); }
            return response.json();
        }).then(playlist => {
            Dashboard.hideLoadingMsg();
            
            if (!playlist) {
                showNotification('No playlist data received from server.');
                return;
            }
            
            try {
                // Switch to Create tab
                switchToTab(page, 'create');
                
                // Clear any existing edit state
                setPageEditState(page, false, null);
                
                // Populate form with cloned playlist data (similar to edit, but for creating new)
                setElementValue(page, '#playlistName', (playlist.Name || '') + ' (Copy)');
                setElementChecked(page, '#playlistIsPublic', playlist.Public || false);
                setElementChecked(page, '#playlistIsEnabled', playlist.Enabled !== false);
                
                // Handle AutoRefresh
                const autoRefreshValue = playlist.AutoRefresh !== undefined ? playlist.AutoRefresh : 'Never';
                const autoRefreshElement = page.querySelector('#autoRefreshMode');
                if (autoRefreshElement) {
                    autoRefreshElement.value = autoRefreshValue;
                }
                
                // Handle schedule settings
                const scheduleTriggerElement = page.querySelector('#scheduleTrigger');
                if (scheduleTriggerElement) {
                    const triggerValue = playlist.ScheduleTrigger === 'None' ? '' : (playlist.ScheduleTrigger || '');
                    scheduleTriggerElement.value = triggerValue;
                    updateScheduleContainers(page, triggerValue);
                }
                
                const scheduleTimeElement = page.querySelector('#scheduleTime');
                if (scheduleTimeElement && playlist.ScheduleTime) {
                    const timeString = playlist.ScheduleTime.substring(0, 5);
                    scheduleTimeElement.value = timeString;
                }
                
                const scheduleDayElement = page.querySelector('#scheduleDayOfWeek');
                if (scheduleDayElement && playlist.ScheduleDayOfWeek !== undefined) {
                    scheduleDayElement.value = convertDayOfWeekToValue(playlist.ScheduleDayOfWeek);
                }
                
                const scheduleDayOfMonthElement = page.querySelector('#scheduleDayOfMonth');
                if (scheduleDayOfMonthElement && playlist.ScheduleDayOfMonth !== undefined) {
                    scheduleDayOfMonthElement.value = playlist.ScheduleDayOfMonth.toString();
                }
                
                const scheduleIntervalElement = page.querySelector('#scheduleInterval');
                if (scheduleIntervalElement && playlist.ScheduleInterval) {
                    scheduleIntervalElement.value = playlist.ScheduleInterval;
                }
                
                // Handle MaxItems
                const maxItemsValue = (playlist.MaxItems !== undefined && playlist.MaxItems !== null) ? playlist.MaxItems : 0;
                const maxItemsElement = page.querySelector('#playlistMaxItems');
                if (maxItemsElement) {
                    maxItemsElement.value = maxItemsValue;
                }
                
                // Handle MaxPlayTimeMinutes
                const maxPlayTimeMinutesValue = (playlist.MaxPlayTimeMinutes !== undefined && playlist.MaxPlayTimeMinutes !== null) ? playlist.MaxPlayTimeMinutes : 0;
                const maxPlayTimeMinutesElement = page.querySelector('#playlistMaxPlayTimeMinutes');
                if (maxPlayTimeMinutesElement) {
                    maxPlayTimeMinutesElement.value = maxPlayTimeMinutesValue;
                }
                
                // Set media types
                const mediaTypesSelect = Array.from(page.querySelectorAll('.media-type-checkbox'));
                // First clear all checkboxes
                mediaTypesSelect.forEach(checkbox => checkbox.checked = false);
                // Then set the ones from the cloned playlist
                if (playlist.MediaTypes && playlist.MediaTypes.length > 0) {
                    playlist.MediaTypes.forEach(type => {
                        const checkbox = mediaTypesSelect.find(checkbox => checkbox.value === type);
                        if (checkbox) {
                            checkbox.checked = true;
                        }
                    });
                }
                
                // Set sort order using helper function
                applySortOrderToForm(page, playlist);
                
                // Clear existing rules and populate with cloned rules
                const rulesContainer = page.querySelector('#rules-container');
                if (rulesContainer) {
                    rulesContainer.innerHTML = '';
                }
                
                // Populate rules from cloned playlist
                if (playlist.ExpressionSets && playlist.ExpressionSets.length > 0) {
                    playlist.ExpressionSets.forEach((expressionSet, setIndex) => {
                        const logicGroup = setIndex === 0 ? createInitialLogicGroup(page) : addNewLogicGroup(page);
                        
                        if (expressionSet.Expressions && expressionSet.Expressions.length > 0) {
                            expressionSet.Expressions.forEach((expression, expIndex) => {
                                if (expIndex === 0) {
                                    // Use the first rule row that's already in the group
                                    const firstRuleRow = logicGroup.querySelector('.rule-row');
                                    if (firstRuleRow) {
                                        populateRuleRow(firstRuleRow, expression);
                                    }
                                } else {
                                    // Add additional rule rows
                                    addRuleToGroup(page, logicGroup);
                                    const newRuleRow = logicGroup.querySelector('.rule-row:last-child');
                                    if (newRuleRow) {
                                        populateRuleRow(newRuleRow, expression);
                                    }
                                }
                            });
                        }
                    });
                } else {
                    // If no rules, create initial empty group
                    createInitialLogicGroup(page);
                }
                
                // Update button visibility
                updateRuleButtonVisibility(page);
                
                // Show success message
                showNotification(`Playlist "${playlistName}" cloned successfully! You can now modify and create the new playlist.`, 'success');
                
            } catch (formError) {
                console.error('Error populating form for clone:', formError);
                showNotification('Error loading playlist data for cloning: ' + formError.message);
            }
        }).catch(err => {
            Dashboard.hideLoadingMsg();
            console.error('Error loading playlist for clone:', err);
            handleApiError(err, 'Failed to load playlist for cloning');
        });
    }

    // Helper function to populate a rule row with expression data
    function populateRuleRow(ruleRow, expression) {
        try {
            const fieldSelect = ruleRow.querySelector('.rule-field-select');
            const operatorSelect = ruleRow.querySelector('.rule-operator-select');
            const valueContainer = ruleRow.querySelector('.rule-value-container');
            
            if (fieldSelect && expression.MemberName) {
                fieldSelect.value = expression.MemberName;
                updateOperatorOptions(expression.MemberName, operatorSelect);
                updateUserSelectorVisibility(ruleRow, expression.MemberName);
                updateNextUnwatchedOptionsVisibility(ruleRow, expression.MemberName);
                updateCollectionsOptionsVisibility(ruleRow, expression.MemberName);
            }
            
            if (operatorSelect && expression.Operator) {
                operatorSelect.value = expression.Operator;
            }
            
            if (valueContainer && expression.TargetValue !== undefined) {
                setValueInput(expression.MemberName, valueContainer, expression.Operator, expression.TargetValue);
            }
            
            // Handle user-specific rules
            if (expression.UserId) {
                const userSelect = ruleRow.querySelector('.rule-user-select');
                if (userSelect) {
                    // Ensure options are loaded before setting the value
                    loadUsersForRule(userSelect, true).then(() => {
                        userSelect.value = expression.UserId;
                    }).catch(() => {
                        // Fallback: set value anyway in case of error
                        userSelect.value = expression.UserId;
                    });
                }
            }
        } catch (error) {
            console.error('Error populating rule row:', error);
        }
    }

    function cancelEdit(page) {
        setPageEditState(page, false, null);
        
        // Update UI to show create mode
        const editIndicator = page.querySelector('#edit-mode-indicator');
        editIndicator.style.display = 'none';
        page.querySelector('#submitBtn').textContent = 'Create Playlist';
        
        // Restore tab button text
        const createTabButton = page.querySelector('a[data-tab="create"]');
        if (createTabButton) {
            createTabButton.textContent = 'Create Playlist';
        }
        
        // Clear form
        clearForm(page);
        
        // Switch to Manage tab after canceling edit
        switchToTab(page, 'manage');
        window.scrollTo({ top: 0, behavior: 'auto' });
        
        showNotification('Edit mode cancelled.', 'success');
    }

    function loadConfiguration(page) {
        Dashboard.showLoadingMsg();
        getApiClient().getPluginConfiguration(getPluginId()).then(config => {
            const defaultSortByEl = page.querySelector('#defaultSortBy');
            const defaultSortOrderEl = page.querySelector('#defaultSortOrder');
            const defaultMakePublicEl = page.querySelector('#defaultMakePublic');
            const defaultMaxItemsEl = page.querySelector('#defaultMaxItems');
            const defaultMaxPlayTimeMinutesEl = page.querySelector('#defaultMaxPlayTimeMinutes');
            const defaultAutoRefreshEl = page.querySelector('#defaultAutoRefresh');
            const playlistNamePrefixEl = page.querySelector('#playlistNamePrefix');
            const playlistNameSuffixEl = page.querySelector('#playlistNameSuffix');
            
            if (defaultSortByEl) defaultSortByEl.value = config.DefaultSortBy || 'Name';
            if (defaultSortOrderEl) defaultSortOrderEl.value = config.DefaultSortOrder || 'Ascending';
            if (defaultMakePublicEl) defaultMakePublicEl.checked = config.DefaultMakePublic || false;
            if (defaultMaxItemsEl) defaultMaxItemsEl.value = config.DefaultMaxItems !== undefined && config.DefaultMaxItems !== null ? config.DefaultMaxItems : 500;
            if (defaultMaxPlayTimeMinutesEl) defaultMaxPlayTimeMinutesEl.value = config.DefaultMaxPlayTimeMinutes !== undefined && config.DefaultMaxPlayTimeMinutes !== null ? config.DefaultMaxPlayTimeMinutes : 0;
            if (defaultAutoRefreshEl) defaultAutoRefreshEl.value = config.DefaultAutoRefresh || 'OnLibraryChanges';
            
            if (playlistNamePrefixEl) playlistNamePrefixEl.value = config.PlaylistNamePrefix || '';
            if (playlistNameSuffixEl) playlistNameSuffixEl.value = config.PlaylistNameSuffix !== undefined && config.PlaylistNameSuffix !== null ? config.PlaylistNameSuffix : '[Smart]';
            
            // Load schedule configuration values
            const defaultScheduleTriggerElement = page.querySelector('#defaultScheduleTrigger');
            if (defaultScheduleTriggerElement) {
                defaultScheduleTriggerElement.value = config.DefaultScheduleTrigger === 'None' ? '' : (config.DefaultScheduleTrigger || '');
                updateDefaultScheduleContainers(page, defaultScheduleTriggerElement.value);
            }
            
            const defaultScheduleTimeElement = page.querySelector('#defaultScheduleTime');
            if (defaultScheduleTimeElement && config.DefaultScheduleTime) {
                const timeString = config.DefaultScheduleTime.substring(0, 5); // Extract HH:MM from HH:MM:SS
                defaultScheduleTimeElement.value = timeString;
            }
            
            const defaultScheduleDayElement = page.querySelector('#defaultScheduleDayOfWeek');
            if (defaultScheduleDayElement) {
                defaultScheduleDayElement.value = convertDayOfWeekToValue(config.DefaultScheduleDayOfWeek);
            }
            
            const defaultScheduleDayOfMonthElement = page.querySelector('#defaultScheduleDayOfMonth');
            if (defaultScheduleDayOfMonthElement) {
                defaultScheduleDayOfMonthElement.value = config.DefaultScheduleDayOfMonth !== undefined ? config.DefaultScheduleDayOfMonth.toString() : '1';
            }
            
            const defaultScheduleIntervalElement = page.querySelector('#defaultScheduleInterval');
            if (defaultScheduleIntervalElement && config.DefaultScheduleInterval) {
                defaultScheduleIntervalElement.value = config.DefaultScheduleInterval;
            }
            
            // Update playlist name preview
            updatePlaylistNamePreview(page);
            
            Dashboard.hideLoadingMsg();
        }).catch(() => {
            Dashboard.hideLoadingMsg();
            showNotification('Could not load settings from server.');
        });
    }

    function saveConfiguration(page) {
        Dashboard.showLoadingMsg();
        const apiClient = getApiClient();
        apiClient.getPluginConfiguration(getPluginId()).then(config => {
            config.DefaultSortBy = page.querySelector('#defaultSortBy').value;
            config.DefaultSortOrder = page.querySelector('#defaultSortOrder').value;
            config.DefaultMakePublic = page.querySelector('#defaultMakePublic').checked;
            const defaultMaxItemsInput = page.querySelector('#defaultMaxItems').value;
            if (defaultMaxItemsInput === '') {
                config.DefaultMaxItems = 500;
            } else {
                const parsedValue = parseInt(defaultMaxItemsInput);
                config.DefaultMaxItems = isNaN(parsedValue) ? 500 : parsedValue;
            }
            
            const defaultMaxPlayTimeMinutesInput = page.querySelector('#defaultMaxPlayTimeMinutes').value;
            if (defaultMaxPlayTimeMinutesInput === '') {
                config.DefaultMaxPlayTimeMinutes = 0;
            } else {
                const parsedValue = parseInt(defaultMaxPlayTimeMinutesInput);
                config.DefaultMaxPlayTimeMinutes = isNaN(parsedValue) ? 0 : parsedValue;
            }
            
            config.DefaultAutoRefresh = page.querySelector('#defaultAutoRefresh').value || 'OnLibraryChanges';
            
            // Save default schedule settings
            const defaultScheduleTriggerValue = page.querySelector('#defaultScheduleTrigger').value;
            config.DefaultScheduleTrigger = defaultScheduleTriggerValue === '' ? 'None' : (defaultScheduleTriggerValue || null);
            
            const defaultScheduleTimeValue = page.querySelector('#defaultScheduleTime').value;
            config.DefaultScheduleTime = defaultScheduleTimeValue ? defaultScheduleTimeValue + ':00' : '00:00:00';
            
            config.DefaultScheduleDayOfWeek = parseInt(page.querySelector('#defaultScheduleDayOfWeek').value) || 0;
            config.DefaultScheduleDayOfMonth = parseInt(page.querySelector('#defaultScheduleDayOfMonth').value) || 1;
            config.DefaultScheduleInterval = page.querySelector('#defaultScheduleInterval').value || '1.00:00:00';
            
            // Save playlist naming configuration
            config.PlaylistNamePrefix = page.querySelector('#playlistNamePrefix').value;
            config.PlaylistNameSuffix = page.querySelector('#playlistNameSuffix').value;
            
            apiClient.updatePluginConfiguration(getPluginId(), config).then(() => {
                Dashboard.hideLoadingMsg();
                showNotification('Settings saved.', 'success');
                // Scroll to top of page after saving
                window.scrollTo({ top: 0, behavior: 'smooth' });
            }).catch(err => {
                Dashboard.hideLoadingMsg();
                console.error('Error saving configuration:', err);
                handleApiError(err, 'Failed to save settings');
            });
        }).catch(() => {
            Dashboard.hideLoadingMsg();
            showNotification('Could not load configuration from server.');
        });
    }

    function refreshAllPlaylists() {
        Dashboard.showLoadingMsg();
        
        getApiClient().ajax({
            type: "POST",
            url: getApiClient().getUrl(ENDPOINTS.refreshDirect),
            contentType: 'application/json'
        }).then(() => {
            Dashboard.hideLoadingMsg();
            showNotification('All smart playlists have been refreshed successfully.', 'success');
            
            // Auto-refresh the playlist list to show updated LastRefreshed timestamps
            const page = document.querySelector('.SmartPlaylistConfigurationPage');
            if (page) {
                loadPlaylistList(page);
            }
        }).catch((err) => {
            Dashboard.hideLoadingMsg();
            console.error('Error refreshing playlists:', err);
            
            // Handle the case where a refresh is already in progress (409 Conflict)
            if (err.status === 409) {
                showNotification('A playlist refresh is already in progress. Please wait for it to complete.', 'warning');
            } else {
                handleApiError(err, 'Failed to refresh playlists');
            }
        });
    }
    
    function applyCustomStyles() {
        // Check if styles are already added
        if (document.getElementById('smartplaylist-custom-styles')) {
            return;
        }

        const style = document.createElement('style');
        style.id = 'smartplaylist-custom-styles';
        style.textContent = `
            select.emby-select, select[is="emby-select"] {
                -webkit-appearance: none;
                -moz-appearance: none;
                appearance: none;
                background-image: url("data:image/svg+xml,%3csvg xmlns='http://www.w3.org/2000/svg' fill='%23e0e0e0' viewBox='0 0 24 24'%3e%3cpath d='M7 10l5 5 5-5z'/%3e%3c/svg%3e");
                background-repeat: no-repeat;
                background-position: right 0.7em top 50%;
                background-size: 1.2em auto;
                padding-right: 1em !important;
            }
            
            /* Field group styling */
            optgroup {
                font-weight: bold;
                font-size: 0.9em;
                color: #00a4dc;
                background: rgba(255, 255, 255, 0.05);
                padding: 0.2em 0;
                margin-top: 0.3em;
            }
            
            optgroup option {
                font-weight: normal;
                font-size: 1em;
                color: #e0e0e0;
                background: inherit;
                padding-left: 1em;
            }
            
            /* Hide native search input clear button to avoid double X with our custom clear button */
            #playlistSearchInput::-webkit-search-cancel-button,
            #playlistSearchInput::-webkit-search-decoration,
            #playlistSearchInput::-webkit-search-results-button,
            #playlistSearchInput::-webkit-search-results-decoration {
                -webkit-appearance: none !important;
                appearance: none !important;
                display: none !important;
            }
        `;
        document.head.appendChild(style);
    }



    function initPage(page) {
        // Check if this specific page is already initialized
        if (page._pageInitialized) {
            return;
        }
        page._pageInitialized = true;
        
        applyCustomStyles();

        // Show loading state
        const userSelect = page.querySelector('#playlistUser');
        if (userSelect) {
            userSelect.innerHTML = '<option value="">Loading users...</option>';
        }
        
        // Disable form submission until initialization is complete
        const submitBtn = page.querySelector('#submitBtn');
        if (submitBtn) {
            submitBtn.disabled = true;
            submitBtn.textContent = 'Loading...';
        }
        
        // Coordinate all async initialization
        Promise.all([
            populateStaticSelects(page), // Make synchronous function async
            loadUsers(page),
            loadAndPopulateFields()
        ]).then(() => {
            // All async operations completed successfully
            const rulesContainer = page.querySelector('#rules-container');
            if (rulesContainer.children.length === 0) {
                createInitialLogicGroup(page);
            } else {
                // Re-initialize existing rules to ensure event listeners are properly attached
                reinitializeExistingRules(page);
            }
            
            // Enable form submission
            const editState = getPageEditState(page);
            const submitBtn = page.querySelector('#submitBtn');
            if (submitBtn) {
                submitBtn.disabled = false;
                submitBtn.textContent = editState.editMode ? 'Update Playlist' : 'Create Playlist';
            }
        }).catch((error) => {
            console.error('Error during page initialization:', error);
            showNotification('Some configuration options failed to load. Please refresh the page.');
            
            // Still enable form submission even if some things failed
            const editState = getPageEditState(page);
            const submitBtn = page.querySelector('#submitBtn');
            if (submitBtn) {
                submitBtn.disabled = false;
                submitBtn.textContent = editState.editMode ? 'Update Playlist' : 'Create Playlist';
            }
        });

        // Set up event listeners (these don't depend on async operations)
        setupEventListeners(page);
        
        // Set up navigation functionality
        setupNavigation(page);
        
        // Load configuration (this can run independently)
        loadConfiguration(page);

        applyMainContainerLayoutFix(page);
    }

    // Shared navigation helper functions (moved to global scope)
    function getCurrentTab() {
        const hash = window.location.hash;
        const match = hash.match(/[?&]tab=([^&]*)/);
        return match ? decodeURIComponent(match[1]) : 'create';
    }
    
    function updateUrl(tabId) {
        let hash = window.location.hash;
        let newHash;

        // Ensure hash starts with # for proper parsing by getCurrentTab
        if (!hash) {
            hash = '#';
        }

        if (hash.includes('tab=')) {
            // Replace existing tab parameter
            newHash = hash.replace(/([?&])tab=[^&]*/, '$1tab=' + encodeURIComponent(tabId));
        } else {
            // Add tab parameter
            const separator = hash.includes('?') ? '&' : '?';
            newHash = hash + separator + 'tab=' + encodeURIComponent(tabId);
        }
        
        window.history.replaceState({}, '', window.location.pathname + window.location.search + newHash);
    }
    

    // Shared tab switching helper function
    function switchToTab(page, tabId) {
        var navContainer = page.querySelector('.localnav');
        var navButtons = navContainer ? navContainer.querySelectorAll('a[data-tab]') : [];
        var tabContents = page.querySelectorAll('[data-tab-content]');

        // Update navigation buttons
        navButtons.forEach(function(btn) {
            if (btn.getAttribute('data-tab') === tabId) {
                btn.classList.add('ui-btn-active');
            } else {
                btn.classList.remove('ui-btn-active');
            }
        });

        // Update tab content visibility
        tabContents.forEach(function(content) {
            var contentTabId = content.getAttribute('data-tab-content');
            if (contentTabId === tabId) {
                content.classList.remove('hide');
            } else {
                content.classList.add('hide');
            }
        });

        // Load playlist list when switching to manage tab
        if (tabId === 'manage') {
            // Load saved filter preferences first
            loadPlaylistFilterPreferences(page);
            loadPlaylistList(page);
        }

        // Update URL
        updateUrl(tabId);
    }

    function setupNavigation(page) {
        var navContainer = page.querySelector('.localnav');
        if (!navContainer) {
            return;
        }
        
        // Prevent multiple setups on the same navigation
        if (navContainer._navInitialized) return;
        navContainer._navInitialized = true;

        // Apply Jellyfin's native styling to the navigation container
        applyStyles(navContainer, {
            marginBottom: '2.2em'
        });

        // Set initial active tab immediately to prevent flash
        var initialTab = getCurrentTab();
        switchToTab(page, initialTab);

        // Use shared tab switching helper
        function setActiveTab(tabId) {
            switchToTab(page, tabId);
        }

        // Create AbortController for navigation click listeners
        var navAbortController = createAbortController();
        var navSignal = navAbortController.signal;
        
        // Store controller for cleanup
        navContainer._navAbortController = navAbortController;

        // Handle navigation clicks
        var navButtons = navContainer.querySelectorAll('a[data-tab]');
        navButtons.forEach(function(button) {
            button.addEventListener('click', function(e) {
                e.preventDefault();
                var tabId = button.getAttribute('data-tab');
                
                // Hide any open modals when switching tabs
                var deleteModal = page.querySelector('#delete-confirm-modal');
                if (deleteModal && !deleteModal.classList.contains('hide')) {
                    deleteModal.classList.add('hide');
                    cleanupModalListeners(deleteModal);
                }
                var refreshModal = page.querySelector('#refresh-confirm-modal');
                if (refreshModal && !refreshModal.classList.contains('hide')) {
                    refreshModal.classList.add('hide');
                    cleanupModalListeners(refreshModal);
                }
                
                // Use shared tab switching helper (includes URL update)
                setActiveTab(tabId);
            }, getEventListenerOptions(navSignal));
        });

        // Note: No popstate handler - tab navigation uses replaceState for URL bookmarking only
        
        // Initial tab already set above to prevent flash
    }

    // Helper function to update the live preview of playlist names
    function updatePlaylistNamePreview(page) {
        const prefix = page.querySelector('#playlistNamePrefix').value;
        const suffix = page.querySelector('#playlistNameSuffix').value;
        const previewText = page.querySelector('#previewText');
        
        const exampleName = 'My Awesome Playlist';
        let finalName = '';
        
        if (prefix) {
            finalName += prefix + ' ';
        }
        finalName += exampleName;
        if (suffix) {
            finalName += ' ' + suffix;
        }
        
        previewText.textContent = finalName;
    }

    // Helper function to setup playlist naming event listeners
    function setupPlaylistNamingListeners(page, signal) {
        const prefixInput = page.querySelector('#playlistNamePrefix');
        const suffixInput = page.querySelector('#playlistNameSuffix');
        
        if (prefixInput) {
            prefixInput.addEventListener('input', () => {
                updatePlaylistNamePreview(page);
            }, getEventListenerOptions(signal));
        }
        
        if (suffixInput) {
            suffixInput.addEventListener('input', () => {
                updatePlaylistNamePreview(page);
            }, getEventListenerOptions(signal));
        }
    }

    function setupEventListeners(page) {
        // Create AbortController for page event listeners
        const pageAbortController = createAbortController();
        const pageSignal = pageAbortController.signal;
        
        // Store controller on the page for cleanup
        page._pageAbortController = pageAbortController;
        
        // Setup playlist naming event listeners
        setupPlaylistNamingListeners(page, pageSignal);
        
        page.addEventListener('click', function (e) {
            const target = e.target;
            
            // Handle rule action buttons
            if (target.classList.contains('and-btn')) {
                const ruleRow = target.closest('.rule-row');
                const logicGroup = ruleRow.closest('.logic-group');
                addRuleToGroup(page, logicGroup);
            }
            if (target.classList.contains('or-btn')) {
                addNewLogicGroup(page);
            }
            if (target.classList.contains('delete-btn')) {
                const ruleRow = target.closest('.rule-row');
                removeRule(page, ruleRow);
            }
            
            // Handle other buttons
            if (target.closest('#clearFormBtn')) { clearForm(page); }
            if (target.closest('#saveSettingsBtn')) { saveConfiguration(page); }
            if (target.closest('#refreshPlaylistsBtn')) { 
                showRefreshConfirmModal(page, refreshAllPlaylists);
            }
            if (target.closest('#refreshPlaylistListBtn')) { loadPlaylistList(page); }
            if (target.closest('#exportPlaylistsBtn')) { exportPlaylists(); }
            if (target.closest('#importPlaylistsBtn')) { importPlaylists(page); }
            if (target.closest('.delete-playlist-btn')) {
                const button = target.closest('.delete-playlist-btn');
                showDeleteConfirm(page, button.getAttribute('data-playlist-id'), button.getAttribute('data-playlist-name'));
            }
            if (target.closest('.edit-playlist-btn')) {
                const button = target.closest('.edit-playlist-btn');
                editPlaylist(page, button.getAttribute('data-playlist-id'));
            }
            if (target.closest('.clone-playlist-btn')) {
                const button = target.closest('.clone-playlist-btn');
                clonePlaylist(page, button.getAttribute('data-playlist-id'), button.getAttribute('data-playlist-name'));
            }
            if (target.closest('.refresh-playlist-btn')) {
                const button = target.closest('.refresh-playlist-btn');
                refreshPlaylist(button.getAttribute('data-playlist-id'), button.getAttribute('data-playlist-name'));
            }
            if (target.closest('.enable-playlist-btn')) {
                const button = target.closest('.enable-playlist-btn');
                enablePlaylist(page, button.getAttribute('data-playlist-id'), button.getAttribute('data-playlist-name'));
            }
            if (target.closest('.disable-playlist-btn')) {
                const button = target.closest('.disable-playlist-btn');
                disablePlaylist(page, button.getAttribute('data-playlist-id'), button.getAttribute('data-playlist-name'));
            }
            if (target.closest('#cancelEditBtn')) {
                cancelEdit(page);
            }
            if (target.closest('#expandAllBtn')) {
                toggleAllPlaylists(page);
            }
            if (target.closest('.playlist-header')) {
                const playlistCard = target.closest('.playlist-card');
                if (playlistCard) {
                    togglePlaylistCard(playlistCard);
                }
            }
            
            // Bulk operations
            if (target.closest('#selectAllCheckbox')) {
                toggleSelectAll(page);
            }
            if (target.closest('#bulkEnableBtn')) {
                bulkEnablePlaylists(page);
            }
            if (target.closest('#bulkDisableBtn')) {
                bulkDisablePlaylists(page);
            }
            if (target.closest('#bulkDeleteBtn')) {
                bulkDeletePlaylists(page);
            }
            if (target.classList.contains('playlist-checkbox')) {
                e.stopPropagation(); // Prevent triggering playlist header click
                updateSelectedCount(page);
            }
            if (target.closest('.emby-checkbox-label') && target.closest('.playlist-header')) {
                const label = target.closest('.emby-checkbox-label');
                const checkbox = label.querySelector('.playlist-checkbox');
                if (checkbox && target !== checkbox) {
                    e.stopPropagation(); // Prevent triggering playlist header click
                    // Let the label's default behavior handle the checkbox toggle
                }
            }
        }, getEventListenerOptions(pageSignal));
        
        const playlistForm = page.querySelector('#playlistForm');
        if (playlistForm) {
            playlistForm.addEventListener('submit', function (e) {
                e.preventDefault();
                createPlaylist(page);
            }, getEventListenerOptions(pageSignal));
        }
        
        // Add search input event listener
        const searchInput = page.querySelector('#playlistSearchInput');
        const clearSearchBtn = page.querySelector('#clearSearchBtn');
        if (searchInput) {
            // Store search timeout on the page for cleanup
            page._searchTimeout = null;
            
            // Function to update clear button visibility
            const updateClearButtonVisibility = () => {
                if (clearSearchBtn) {
                    clearSearchBtn.style.display = searchInput.value.trim() ? 'flex' : 'none';
                }
            };
            
            // Use debounced search to avoid too many re-renders
            searchInput.addEventListener('input', function() {
                updateClearButtonVisibility();
                clearTimeout(page._searchTimeout);
                page._searchTimeout = setTimeout(async () => {
                    try {
                        await applySearchFilter(page);
                    } catch (err) {
                        console.error('Error during search:', err);
                        showNotification('Search error: ' + err.message);
                    }
                }, 300); // 300ms delay
            }, getEventListenerOptions(pageSignal));
            
            // Also search on Enter key
            searchInput.addEventListener('keydown', function(e) {
                if (e.key === 'Enter') {
                    clearTimeout(page._searchTimeout);
                    applySearchFilter(page).catch(err => {
                        console.error('Error during search:', err);
                        showNotification('Search error: ' + err.message);
                    });
                }
            }, getEventListenerOptions(pageSignal));
            
            // Handle clear button click
            if (clearSearchBtn) {
                clearSearchBtn.addEventListener('click', function() {
                    searchInput.value = '';
                    updateClearButtonVisibility();
                    clearTimeout(page._searchTimeout);
                    applySearchFilter(page).catch(err => {
                        console.error('Error during search:', err);
                        showNotification('Search error: ' + err.message);
                    });
                    searchInput.focus(); // Return focus to search input
                }, getEventListenerOptions(pageSignal));
            }
            
            // Initialize clear button visibility
            updateClearButtonVisibility();
        }
        
        // Generic event listener setup - eliminates DRY violations
        setupFilterEventListeners(page, pageSignal);
        
        const clearFiltersBtn = page.querySelector('#clearFiltersBtn');
        if (clearFiltersBtn) {
            clearFiltersBtn.addEventListener('click', function() {
                clearAllFilters(page);
            }, getEventListenerOptions(pageSignal));
        }
        
        // Add import file input event listener
        const importFileInput = page.querySelector('#importPlaylistsFile');
        const importBtn = page.querySelector('#importPlaylistsBtn');
        const selectedFileName = page.querySelector('#selectedFileName');
        if (importFileInput && importBtn) {
            importFileInput.addEventListener('change', function() {
                const hasFile = this.files && this.files.length > 0;
                
                // Show/hide and enable/disable import button based on file selection
                if (hasFile) {
                    importBtn.style.display = 'inline-block';
                    importBtn.disabled = false;
                } else {
                    importBtn.style.display = 'none';
                    importBtn.disabled = true;
                }
                
                // Update filename display
                if (selectedFileName) {
                    if (hasFile) {
                        selectedFileName.textContent = 'Selected: ' + this.files[0].name;
                        selectedFileName.style.fontStyle = 'italic';
                    } else {
                        selectedFileName.textContent = '';
                    }
                }
            }, getEventListenerOptions(pageSignal));
        }
    }

    document.addEventListener('pageshow', function (e) {
        const page = e.target;
        if (page.classList.contains('SmartPlaylistConfigurationPage')) {
            initPage(page);
        }
    });
    // Clean up all event listeners when page is hidden/unloaded
    document.addEventListener('pagehide', function (e) {
        const page = e.target;
        if (page.classList.contains('SmartPlaylistConfigurationPage')) {
            cleanupAllEventListeners(page);
        }
    });

    function cleanupAllEventListeners(page) {
        // Clean up rule event listeners
        const allRules = page.querySelectorAll('.rule-row');
        allRules.forEach(rule => cleanupRuleEventListeners(rule));
        
        // Clean up modal listeners
        const deleteModal = page.querySelector('#delete-confirm-modal');
        if (deleteModal) {
            cleanupModalListeners(deleteModal);
        }
        const refreshModal = page.querySelector('#refresh-confirm-modal');
        if (refreshModal) {
            cleanupModalListeners(refreshModal);
        }
        
        // Clean up page event listeners
        if (page._pageAbortController) {
            page._pageAbortController.abort();
            page._pageAbortController = null;
        }
        
        // Clean up tab listeners
        if (page._tabAbortController) {
            page._tabAbortController.abort();
            page._tabAbortController = null;
        }
        
        // Clean up search timeout
        if (page._searchTimeout) {
            clearTimeout(page._searchTimeout);
            page._searchTimeout = null;
        }
        
        // Clean up notification timer
        if (typeof notificationTimeout !== 'undefined' && notificationTimeout) {
            clearTimeout(notificationTimeout);
            notificationTimeout = null;
        }
        
        // Clean up navigation listeners
        const navContainer = page.querySelector('.localnav');
        if (navContainer) {
            // Clean up navigation click listeners via AbortController
            if (navContainer._navAbortController) {
                try {
                    navContainer._navAbortController.abort();
                } catch (e) {
                    console.warn('Failed to abort navigation listeners:', e);
                }
                navContainer._navAbortController = null;
            }
            
            // Note: No popstate listener to clean up
            
            navContainer._navInitialized = false;
        }
        
        // Reset page-specific initialization flags and edit state
        page._pageInitialized = false;
        page._tabListenersInitialized = false;
        page._editMode = false;
        page._editingPlaylistId = null;
        page._loadingPlaylists = false;
        page._allPlaylists = null; // Clear stored playlist data
    }

    function applyMainContainerLayoutFix(page) {
        // Apply to all tab content containers
        var tabContents = page.querySelectorAll('.page-content');
        for (var i = 0; i < tabContents.length; i++) {
            applyStyles(tabContents[i], STYLES.layout.tabContent);
        }
    }

    // (removed) applyNotificationLayoutFix – legacy in-page notification area no longer exists

    /**
     * Creates a tag-based input interface for IsIn/IsNotIn operators
     */
    function createTagBasedInput(valueContainer, currentValue) {
        // Create the main container with EXACT same styling as standard Jellyfin inputs
        const tagContainer = document.createElement('div');
        tagContainer.className = 'tag-input-container';
        tagContainer.style.cssText = 'width: 100%; min-height: 38px; border: none; border-radius: 0; background: #292929; padding: 0.5em; display: flex; flex-wrap: wrap; gap: 0.5em; align-items: flex-start; box-sizing: border-box; align-content: flex-start;';
        
        // Create the input field with standard Jellyfin styling
        const input = document.createElement('input');
        input.type = 'text';
        input.className = 'emby-input tag-input-field';
        input.placeholder = 'Type a value and press Enter';
        input.style.cssText = 'border: none; background: transparent; color: #fff; flex: 1; min-width: 200px; outline: none; font-size: 0.9em; font-family: inherit;';
        input.setAttribute('data-input-type', 'tag-input');
        
        // Use page-level ::placeholder styling (see config.html)
        input.style.setProperty('color-scheme', 'dark');
        
        // Create the hidden input that will store the semicolon-separated values for the backend
        const hiddenInput = document.createElement('input');
        hiddenInput.type = 'hidden';
        hiddenInput.className = 'rule-value-input';
        hiddenInput.setAttribute('data-input-type', 'hidden-tag-input');
        
        // Add elements to container
        tagContainer.appendChild(input);
        valueContainer.appendChild(tagContainer);
        valueContainer.appendChild(hiddenInput);
        
        // Add event listeners
        input.addEventListener('keydown', function(e) {
            if (e.key === 'Enter') {
                e.preventDefault();
                const value = input.value.trim();
                if (value) {
                    addTagToContainer(valueContainer, value);
                    input.value = '';
                    updateHiddenInput(valueContainer);
                    hideAddOptionDropdown(valueContainer);
                }
            } else if (e.key === 'Tab') {
                // Only handle Tab when dropdown is visible (for tag commit)
                const dropdown = valueContainer.querySelector('.add-option-dropdown');
                if (dropdown && dropdown.style.display !== 'none') {
                    e.preventDefault();
                    const value = input.value.trim();
                    if (value) {
                        addTagToContainer(valueContainer, value);
                        input.value = '';
                        updateHiddenInput(valueContainer);
                        hideAddOptionDropdown(valueContainer);
                    }
                }
                // If no dropdown visible, let Tab work normally for keyboard navigation
            } else if (e.key === 'Backspace' && input.value === '') {
                // Remove last tag when backspace is pressed on empty input
                e.preventDefault();
                removeLastTag(valueContainer);
            }
        });
        
        input.addEventListener('input', function() {
            const value = input.value.trim();
            
            if (value) {
                // Check if value contains semicolon
                if (value.includes(';')) {
                    const parts = value.split(';');
                    parts.forEach(part => {
                        const trimmedPart = part.trim();
                        if (trimmedPart) {
                            addTagToContainer(valueContainer, trimmedPart);
                        }
                    });
                    input.value = '';
                    updateHiddenInput(valueContainer);
                    hideAddOptionDropdown(valueContainer);
                } else {
                    showAddOptionDropdown(valueContainer, value);
                }
            } else {
                hideAddOptionDropdown(valueContainer);
            }
        });
        
        input.addEventListener('blur', function() {
            // Small delay to allow clicking on the dropdown
            setTimeout(() => hideAddOptionDropdown(valueContainer), 150);
        });
        
        // Restore existing tags if any
        if (currentValue) {
            const tags = currentValue.split(';').map(tag => tag.trim()).filter(tag => tag.length > 0);
            tags.forEach(tag => addTagToContainer(valueContainer, tag));
        }
        
        // Initial update of hidden input
        updateHiddenInput(valueContainer);
    }
    
    /**
     * Adds a tag to the container
     */
    function addTagToContainer(valueContainer, tagText) {
        const tagContainer = valueContainer.querySelector('.tag-input-container');
        if (!tagContainer) return;
        
        // Check if tag already exists to prevent duplicates (case-insensitive)
        const existingTags = Array.from(tagContainer.querySelectorAll('.tag-item span'))
            .map(span => span.textContent.toLowerCase());
        if (existingTags.includes(tagText.toLowerCase())) {
            return; // Tag already exists, don't add duplicate
        }
        
        // Create tag element with subtle Jellyfin styling
        const tag = document.createElement('div');
        tag.className = 'tag-item';
        tag.style.cssText = 'background: #292929; color: #ccc; padding: 0.3em 0.6em; border-radius: 2px; font-size: 0.85em; display: inline-flex; align-items: center; gap: 0.5em; max-width: none; flex: 0 0 auto; border: 1px solid #444; white-space: nowrap; overflow: hidden;';
        
        // Tag text
        const tagTextSpan = document.createElement('span');
        tagTextSpan.textContent = tagText;
        tagTextSpan.style.cssText = 'overflow: hidden; text-overflow: ellipsis; white-space: nowrap;';
        
        // Remove button
        const removeBtn = document.createElement('button');
        removeBtn.type = 'button';
        removeBtn.innerHTML = '×';
        removeBtn.style.cssText = 'background: none; border: none; color: #ccc; cursor: pointer; font-size: 1.2em; font-weight: bold; padding: 0; line-height: 1; width: 1.2em; height: 1.2em; display: flex; align-items: center; justify-content: center; border-radius: 50%; transition: background-color 0.2s ease;';
        
        removeBtn.addEventListener('mouseenter', function() {
            this.style.backgroundColor = 'rgba(255, 255, 255, 0.1)';
        });
        
        removeBtn.addEventListener('mouseleave', function() {
            this.style.backgroundColor = 'transparent';
        });
        
        removeBtn.addEventListener('click', function() {
            tag.remove();
            updateHiddenInput(valueContainer);
        });
        
        // Assemble tag
        tag.appendChild(tagTextSpan);
        tag.appendChild(removeBtn);
        
        // Insert before the input field
        const input = tagContainer.querySelector('.tag-input-field');
        tagContainer.insertBefore(tag, input);
        
        // Update hidden input
        updateHiddenInput(valueContainer);
    }
    
    /**
     * Shows the "Add option" dropdown
     */
    function showAddOptionDropdown(valueContainer, value) {
        // Remove existing dropdown
        hideAddOptionDropdown(valueContainer);
        
        const tagContainer = valueContainer.querySelector('.tag-input-container');
        if (!tagContainer) return;
        
        // Create dropdown
        const dropdown = document.createElement('div');
        dropdown.className = 'add-option-dropdown';
        dropdown.style.cssText = 'position: absolute; background: #2a2a2a; border: 1px solid #444; border-radius: 2px; padding: 0.5em; margin-top: 0.25em; z-index: 1000; box-shadow: 0 4px 12px rgba(0, 0, 0, 0.5); min-width: 200px;';
        
        const dropdownText = document.createElement('div');
        dropdownText.style.cssText = 'color: #ccc; font-size: 0.9em; margin-bottom: 0.5em;';
        dropdownText.textContent = 'Add option:';
        
        const optionText = document.createElement('div');
        optionText.style.cssText = 'background: #292929; color: #ccc; padding: 0.5em; border-radius: 2px; font-weight: 500; cursor: pointer; transition: background-color 0.2s ease; border: 1px solid #444;';
        optionText.textContent = value;
        
        optionText.addEventListener('mouseenter', function() {
            this.style.backgroundColor = '#3a3a3a';
        });
        
        optionText.addEventListener('mouseleave', function() {
            this.style.backgroundColor = '#292929';
        });
        
        optionText.addEventListener('click', function() {
            addTagToContainer(valueContainer, value);
            const input = tagContainer.querySelector('.tag-input-field');
            if (input) input.value = '';
            hideAddOptionDropdown(valueContainer);
        });
        
        dropdown.appendChild(dropdownText);
        dropdown.appendChild(optionText);
        
        // Position the dropdown
        tagContainer.style.position = 'relative';
        tagContainer.appendChild(dropdown);
    }
    
    /**
     * Hides the "Add option" dropdown
     */
    function hideAddOptionDropdown(valueContainer) {
        const dropdown = valueContainer.querySelector('.add-option-dropdown');
        if (dropdown) {
            dropdown.remove();
        }
        
        // Also remove any positioning styles that might affect layout
        const tagContainer = valueContainer.querySelector('.tag-input-container');
        if (tagContainer) {
            tagContainer.style.position = 'static';
        }
    }
    
    /**
     * Updates the hidden input with semicolon-separated values
     */
    function updateHiddenInput(valueContainer) {
        const hiddenInput = valueContainer.querySelector('.rule-value-input[data-input-type="hidden-tag-input"]');
        if (!hiddenInput) return;
        
        const tags = Array.from(valueContainer.querySelectorAll('.tag-item span'))
            .map(span => span.textContent.trim())
            .filter(Boolean);
        hiddenInput.value = tags.join(';');    
    }
    
    /**
     * Removes the last tag from the container
     */
    function removeLastTag(valueContainer) {
        const tagContainer = valueContainer.querySelector('.tag-input-container');
        if (!tagContainer) return;
        
        const tags = tagContainer.querySelectorAll('.tag-item');
        if (tags.length > 0) {
            const lastTag = tags[tags.length - 1];
            lastTag.remove();
            updateHiddenInput(valueContainer);
        }
    }

    /**
     * Export all playlists as a ZIP file
     */
    function exportPlaylists() {
        try {
            const apiClient = getApiClient();
            const url = apiClient.getUrl(ENDPOINTS.base + '/export');
            
            fetch(url, {
                method: 'POST',
                headers: {
                    'Authorization': `MediaBrowser Token="${apiClient.accessToken()}"`,
                    'Content-Type': 'application/json'
                }
            })
            .then(async response => {
                if (!response.ok) {
                    const errorData = await response.json();
                    throw new Error(errorData.message || 'Export failed');
                }
                
                // Get the blob from response
                const blob = await response.blob();
                
                // Create download link
                const url = window.URL.createObjectURL(blob);
                const a = document.createElement('a');
                a.href = url;
                
                // Get filename from Content-Disposition header or use default
                const contentDisposition = response.headers.get('Content-Disposition');
                let filename = 'smartplaylists_export.zip';
                if (contentDisposition) {
                    const matches = contentDisposition.match(/filename[^;=\n]*=((['"]).*?\2|[^;\n]*)/);
                    if (matches && matches[1]) {
                        filename = matches[1].replace(/['"]/g, '');
                    }
                }
                
                a.download = filename;
                document.body.appendChild(a);
                a.click();
                window.URL.revokeObjectURL(url);
                document.body.removeChild(a);
                
                showNotification('Export completed successfully!', 'success');
            })
            .catch(error => {
                console.error('Export error:', error);
                showNotification('Export failed: ' + error.message, 'error');
            });
        } catch (error) {
            console.error('Export error:', error);
            showNotification('Export failed: ' + error.message, 'error');
        }
    }

    /**
     * Import playlists from selected ZIP file
     */
    function importPlaylists(page) {
        const fileInput = page.querySelector('#importPlaylistsFile');
        const file = fileInput.files[0];
        
        if (!file) {
            showNotification('Please select a file to import', 'error');
            return;
        }
        
        // File size limit: 10MB
        const MAX_FILE_SIZE = 10 * 1024 * 1024;
        if (file.size > MAX_FILE_SIZE) {
            showNotification('File is too large (max 10MB)', 'error');
            return;
        }
        
        // Extension check as safety net (accept attribute already filters in dialog)
        if (!file.name.toLowerCase().endsWith('.zip')) {
            showNotification('Please select a ZIP file', 'error');
            return;
        }
        
        try {
            const apiClient = getApiClient();
            const url = apiClient.getUrl(ENDPOINTS.base + '/import');
            
            const formData = new FormData();
            formData.append('file', file);
            
            fetch(url, {
                method: 'POST',
                headers: {
                    'Authorization': `MediaBrowser Token="${apiClient.accessToken()}"`
                },
                body: formData
            })
            .then(async response => {
                if (!response.ok) {
                    const errorData = await response.json();
                    throw new Error(errorData.message || 'Import failed');
                }
                
                const result = await response.json();
                
                // Clear the file input and filename display
                fileInput.value = '';
                const selectedFileName = page.querySelector('#selectedFileName');
                if (selectedFileName) {
                    selectedFileName.textContent = '';
                }
                const importBtn = page.querySelector('#importPlaylistsBtn');
                if (importBtn) {
                    importBtn.disabled = true;
                    importBtn.style.display = 'none';
                }
                
                // Show detailed notification
                const message = `Import completed: ${result.imported} imported, ${result.skipped} skipped, ${result.errors} errors`;
                showNotification(message, result.errors > 0 ? 'warning' : 'success');
            })
            .catch(error => {
                console.error('Import error:', error);
                showNotification('Import failed: ' + error.message, 'error');
            });
        } catch (error) {
            console.error('Import error:', error);
            showNotification('Import failed: ' + error.message, 'error');
        }
    }

})();