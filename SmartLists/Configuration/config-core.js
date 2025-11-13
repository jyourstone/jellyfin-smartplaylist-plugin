(function(SmartLists) {
    'use strict';
    
    // Initialize namespace if it doesn't exist
    if (!SmartLists) {
        window.SmartLists = {};
        SmartLists = window.SmartLists;
    }
    
    // Constants
    SmartLists.PLUGIN_ID = "A0A2A7B2-747A-4113-8B39-757A9D267C79";
    SmartLists.ENDPOINTS = {
        fields: 'Plugins/SmartLists/fields',
        base: 'Plugins/SmartLists',
        users: 'Plugins/SmartLists/users',
        refresh: 'Plugins/SmartLists/refresh',
        refreshDirect: 'Plugins/SmartLists/refresh-direct',
        export: 'Plugins/SmartLists/export',
        import: 'Plugins/SmartLists/import'
    };
    
    // Field type constants to avoid duplication
    SmartLists.FIELD_TYPES = {
        LIST_FIELDS: ['Collections', 'People', 'Actors', 'Directors', 'Writers', 'Producers', 'GuestStars', 'Genres', 'Studios', 'Tags', 'Artists', 'AlbumArtists', 'AudioLanguages'],
        NUMERIC_FIELDS: ['ProductionYear', 'CommunityRating', 'CriticRating', 'RuntimeMinutes', 'PlayCount', 'Framerate', 'AudioBitrate', 'AudioSampleRate', 'AudioBitDepth', 'AudioChannels'],
        DATE_FIELDS: ['DateCreated', 'DateLastRefreshed', 'DateLastSaved', 'DateModified', 'ReleaseDate', 'LastPlayedDate'],
        BOOLEAN_FIELDS: ['IsPlayed', 'IsFavorite', 'NextUnwatched'],
        SIMPLE_FIELDS: ['ItemType'],
        RESOLUTION_FIELDS: ['Resolution'],
        STRING_FIELDS: ['SimilarTo', 'Name', 'Album', 'SeriesName', 'OfficialRating', 'Overview', 'FileName', 'FolderPath', 'AudioCodec', 'AudioProfile', 'VideoCodec', 'VideoProfile', 'VideoRange', 'VideoRangeType'],
        USER_DATA_FIELDS: ['IsPlayed', 'IsFavorite', 'PlayCount', 'NextUnwatched', 'LastPlayedDate']
    };
    
    // Media type capabilities - which types support audio/video streams
    SmartLists.AUDIO_CAPABLE_TYPES = ['Movie', 'Episode', 'Audio', 'AudioBook', 'MusicVideo', 'Video'];
    SmartLists.VIDEO_CAPABLE_TYPES = ['Movie', 'Episode', 'MusicVideo', 'Video'];
    
    // Audio and video field lists for visibility gating
    SmartLists.AUDIO_FIELD_NAMES = ['AudioBitrate', 'AudioSampleRate', 'AudioBitDepth', 'AudioCodec', 'AudioProfile', 'AudioChannels', 'AudioLanguages'];
    SmartLists.VIDEO_FIELD_NAMES = ['Resolution', 'Framerate', 'VideoCodec', 'VideoProfile', 'VideoRange', 'VideoRangeType'];
    
    // Debounce delay for media type change updates (milliseconds)
    SmartLists.MEDIA_TYPE_UPDATE_DEBOUNCE_MS = 200;
    
    // Enhanced HTML escaping function to prevent XSS vulnerabilities
    SmartLists.escapeHtml = function(text) {
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
    };
    
    // Safe HTML attribute escaping for use in HTML attributes
    SmartLists.escapeHtmlAttribute = function(text) {
        if (text == null) return '';
        return String(text)
            .replace(/&/g, '&amp;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#x27;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;');
    };
    
    // Custom error class for API errors
    SmartLists.ApiError = function(message, status) {
        this.name = 'ApiError';
        this.message = message;
        this.status = status;
        this.stack = (new Error()).stack;
    };
    SmartLists.ApiError.prototype = Object.create(Error.prototype);
    SmartLists.ApiError.prototype.constructor = SmartLists.ApiError;
    
    // Standardized error display function
    SmartLists.displayApiError = function(error, context) {
        context = context || '';
        let message = 'An unexpected error occurred, check the logs for more details.';
        
        if (error instanceof SmartLists.ApiError) {
            message = error.message;
        } else if (error && error.message) {
            message = error.message;
        } else if (typeof error === 'string') {
            message = error;
        }
        
        const contextPrefix = context ? context + ': ' : '';
        const fullMessage = contextPrefix + message;
        
        console.error('API Error:', fullMessage, error);
        if (SmartLists.showNotification) {
            SmartLists.showNotification(fullMessage, 'error');
        }
        
        return fullMessage;
    };
    
    // Safe DOM manipulation helper to prevent XSS vulnerabilities
    // Accepts an array of {value, label, selected} objects
    SmartLists.populateSelectElement = function(selectElement, optionsData) {
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
    };
    
    // DOM Helper Functions to reduce repetition and improve maintainability
    
    /**
     * Get element value safely with optional default
     */
    SmartLists.getElementValue = function(page, selector, defaultValue) {
        defaultValue = defaultValue || '';
        const element = page.querySelector(selector);
        return element ? element.value : defaultValue;
    };
    
    /**
     * Get element checked state safely with optional default
     */
    SmartLists.getElementChecked = function(page, selector, defaultValue) {
        defaultValue = defaultValue !== undefined ? defaultValue : false;
        const element = page.querySelector(selector);
        return element ? element.checked : defaultValue;
    };
    
    /**
     * Set element value safely (only if element exists)
     */
    SmartLists.setElementValue = function(page, selector, value) {
        const element = page.querySelector(selector);
        if (element) {
            element.value = value;
            return true;
        }
        return false;
    };
    
    /**
     * Set element checked state safely (only if element exists)
     */
    SmartLists.setElementChecked = function(page, selector, checked) {
        const element = page.querySelector(selector);
        if (element) {
            element.checked = checked;
            return true;
        }
        return false;
    };
    
    SmartLists.getPluginId = function() {
        return SmartLists.PLUGIN_ID;
    };
    
    SmartLists.getApiClient = function() {
        return window.ApiClient;
    };
    
    // Page state management
    SmartLists.getPageEditState = function(page) {
        return {
            editMode: page._editMode || false,
            editingPlaylistId: page._editingPlaylistId || null
        };
    };
    
    SmartLists.setPageEditState = function(page, editMode, editingPlaylistId) {
        editingPlaylistId = editingPlaylistId || null;
        page._editMode = editMode;
        page._editingPlaylistId = editingPlaylistId;
    };
    
    SmartLists.createAbortController = function() {
        return typeof AbortController !== 'undefined' ? new AbortController() : null;
    };
    
    SmartLists.getEventListenerOptions = function(signal) {
        return signal ? { signal: signal } : {};
    };
    
    // Notification system
    var notificationTimeout;
    SmartLists.showNotification = function(message, type) {
        type = type || 'error';
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
        } else if (type === 'info') {
            prefixedMessage = 'ℹ ' + message;
        }

        // Set the message
        floatingNotification.textContent = prefixedMessage;

        // Apply floating notification styles
        const notificationStyles = {
            position: 'fixed',
            bottom: '20px',
            left: '20px',
            maxWidth: '400px',
            minWidth: '300px',
            padding: '16px 20px',
            color: 'rgba(255, 255, 255, 0.95)',
            backgroundColor: type === 'success' ? 'rgba(40, 40, 40, 0.95)' : 
                            type === 'warning' ? '#ff9800' :
                            type === 'info' ? '#2196f3' : '#f44336',
            boxShadow: '0 4px 12px rgba(0, 0, 0, 0.4)',
            fontSize: '16px',
            fontWeight: 'normal',
            textAlign: 'left',
            zIndex: '10000',
            transform: 'translateY(100%)',
            opacity: '0',
            transition: 'all 0.3s cubic-bezier(0.4, 0, 0.2, 1)',
            boxSizing: 'border-box',
            pointerEvents: 'none'
        };

        // Apply styles
        Object.entries(notificationStyles).forEach(function(entry) {
            const property = entry[0].replace(/([A-Z])/g, '-$1').toLowerCase();
            floatingNotification.style.setProperty(property, entry[1], 'important');
        });

        // Animate in
        setTimeout(function() {
            floatingNotification.style.setProperty('transform', 'translateY(0)', 'important');
            floatingNotification.style.setProperty('opacity', '1', 'important');
        }, 10);

        // Clear any existing timeout
        clearTimeout(notificationTimeout);
        
        // Animate out after delay
        notificationTimeout = setTimeout(function() {
            floatingNotification.style.setProperty('transform', 'translateY(100%)', 'important');
            floatingNotification.style.setProperty('opacity', '0', 'important');
            
            setTimeout(function() {
                if (floatingNotification && floatingNotification.parentNode) {
                    floatingNotification.parentNode.removeChild(floatingNotification);
                }
            }, 300);
        }, 8000);
    };
    
})(window.SmartLists = window.SmartLists || {});

