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
        libraries: 'Plugins/SmartLists/libraries',
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
    
    // Constants for sort options (used throughout the application)
    SmartLists.SORT_OPTIONS = [
        { value: 'Name', label: 'Name' },
        { value: 'ProductionYear', label: 'Production Year' },
        { value: 'CommunityRating', label: 'Community Rating' },
        { value: 'DateCreated', label: 'Date Created' },
        { value: 'ReleaseDate', label: 'Release Date' },
        { value: 'SeasonNumber', label: 'Season Number' },
        { value: 'EpisodeNumber', label: 'Episode Number' },
        { value: 'PlayCount (owner)', label: 'Play Count (owner)' },
        { value: 'LastPlayed (owner)', label: 'Last Played (owner)' },
        { value: 'Runtime', label: 'Runtime' },
        { value: 'SeriesName', label: 'Series Name' },
        { value: 'AlbumName', label: 'Album Name' },
        { value: 'Artist', label: 'Artist' },
        { value: 'Similarity', label: 'Similarity (requires Similar To rule)' },
        { value: 'TrackNumber', label: 'Track Number' },
        { value: 'Resolution', label: 'Resolution' },
        { value: 'Random', label: 'Random' },
        { value: 'NoOrder', label: 'No Order' }
    ];
    
    SmartLists.SORT_ORDER_OPTIONS = [
        { value: 'Ascending', label: 'Ascending' },
        { value: 'Descending', label: 'Descending' }
    ];
    
    // Constants for operators
    SmartLists.RELATIVE_DATE_OPERATORS = ['NewerThan', 'OlderThan'];
    SmartLists.MULTI_VALUE_OPERATORS = ['IsIn', 'IsNotIn'];
    
    // Global state - availableFields is populated by loadAndPopulateFields
    SmartLists.availableFields = {};
    
    // Media types constant
    SmartLists.mediaTypes = [
        { Value: "Movie", Label: "Movie" },
        { Value: "Episode", Label: "Episode (TV Show)" },
        { Value: "Series", Label: "Series (TV Show)", CollectionOnly: true }, // Series can only be added to Collections, not Playlists
        { Value: "Audio", Label: "Audio (Music)" },
        { Value: "MusicVideo", Label: "Music Video" },
        { Value: "Video", Label: "Video (Home Video)" },
        { Value: "Photo", Label: "Photo (Home Photo)" },
        { Value: "Book", Label: "Book" },
        { Value: "AudioBook", Label: "Audiobook" }
    ];
    
    // Utility function to get selected media types from page
    SmartLists.getSelectedMediaTypes = function(page) {
        const selectedMediaTypes = [];
        const mediaTypesSelect = page.querySelectorAll('.media-type-checkbox');
        mediaTypesSelect.forEach(function(checkbox) {
            if (checkbox.checked) {
                selectedMediaTypes.push(checkbox.value);
            }
        });
        return selectedMediaTypes;
    };
    
    // Check if any rule has "Similar To" field selected
    SmartLists.hasSimilarToRuleInForm = function(page) {
        const allRules = page.querySelectorAll('.rule-row');
        for (var i = 0; i < allRules.length; i++) {
            const ruleRow = allRules[i];
            const fieldSelect = ruleRow.querySelector('.rule-field-select');
            if (fieldSelect && fieldSelect.value === 'SimilarTo') {
                return true;
            }
        }
        return false;
    };
    
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
    
    SmartLists.loadAndPopulateFields = function() {
        const apiClient = SmartLists.getApiClient();
        const url = apiClient.getUrl(SmartLists.ENDPOINTS.fields);
        
        return apiClient.get(url).then(function(response) {
            if (!response.ok) {
                throw new Error('Network response was not ok: ' + response.statusText);
            }
            return response.json();
        }).then(function(fields) {
            SmartLists.availableFields = fields;
            return fields;
        }).catch(function(err) {
            console.error('Error loading or parsing fields:', err);
            throw err;
        });
    };
    
    SmartLists.populateSelect = function(selectElement, options, defaultValue, forceSelection) {
        defaultValue = defaultValue !== undefined ? defaultValue : null;
        forceSelection = forceSelection !== undefined ? forceSelection : true;
        if (!selectElement) return;
        options.forEach(function(opt, index) {
            const option = document.createElement('option');
            option.value = opt.Value;
            option.textContent = opt.Label;
            selectElement.appendChild(option);
            
            if ((defaultValue && opt.Value === defaultValue) || (!defaultValue && forceSelection && index === 0)) {
                option.selected = true;
            }
        });
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
    
    // Centralized styling configuration
    SmartLists.STYLES = {
        scheduleBox: {
            border: '1px solid #666',
            borderRadius: '2px',
            padding: '1em 1.5em',
            marginBottom: '1em',
            background: 'rgba(255, 255, 255, 0.05)',
            boxShadow: '0 2px 8px rgba(0, 0, 0, 0.3)',
            position: 'relative'
        },
        scheduleFields: {
            display: 'flex',
            gap: '0.75em',
            alignItems: 'flex-end',
            flexWrap: 'wrap',
            marginBottom: '0.5em',
            position: 'relative'
        },
        scheduleField: {
            display: 'flex',
            flexDirection: 'column',
            minWidth: '150px',
            flex: '0 1 auto'
        },
        scheduleFieldLabel: {
            marginBottom: '0.3em',
            fontSize: '0.85em',
            color: '#ccc',
            fontWeight: '500'
        },
        scheduleRemoveBtn: {
            padding: '0.3em 0.6em',
            fontSize: '1.3em',
            border: '1px solid #666',
            background: 'rgba(255, 255, 255, 0.07)',
            color: '#aaa',
            borderRadius: '4px',
            cursor: 'pointer',
            fontWeight: '500',
            lineHeight: '1',
            width: 'auto',
            minWidth: 'auto',
            alignSelf: 'center',
            marginLeft: 'auto'
        },
        sortBox: {
            border: '1px solid #666',
            borderRadius: '2px',
            padding: '1em 1.5em',
            marginBottom: '1em',
            background: 'rgba(255, 255, 255, 0.05)',
            boxShadow: '0 2px 8px rgba(0, 0, 0, 0.3)',
            position: 'relative'
        },
        sortFields: {
            display: 'flex',
            gap: '0.75em',
            alignItems: 'flex-end',
            flexWrap: 'wrap'
        },
        sortField: {
            display: 'flex',
            flexDirection: 'column',
            minWidth: '180px',
            flex: '0 1 auto'
        },
        sortFieldLabel: {
            marginBottom: '0.3em',
            fontSize: '0.85em',
            color: '#ccc',
            fontWeight: '500'
        },
        sortRemoveBtn: {
            padding: '0.3em 0.6em',
            fontSize: '1.3em',
            border: '1px solid #666',
            background: 'rgba(255, 255, 255, 0.07)',
            color: '#aaa',
            borderRadius: '4px',
            cursor: 'pointer',
            fontWeight: '500',
            lineHeight: '1',
            width: 'auto',
            minWidth: 'auto',
            alignSelf: 'center',
            marginLeft: 'auto'
        },
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
        logicGroup: {
            border: '1px solid #666',
            borderRadius: '2px',
            padding: '1.5em 1.5em 0.5em 1.5em',
            marginBottom: '1em',
            background: 'rgba(255, 255, 255, 0.05)',
            boxShadow: '0 2px 8px rgba(0, 0, 0, 0.3)',
            position: 'relative'
        },
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
                    fontWeight: '500'
                }
            }
        },
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
        }
    };
    
    SmartLists.styleRuleActionButton = function(button, buttonType) {
        // Map and/or buttons to shared 'action' styling
        const styleKey = (buttonType === 'and' || buttonType === 'or') ? 'action' : buttonType;
        const buttonStyles = SmartLists.STYLES.buttons[styleKey];
        if (!buttonStyles) return;
        
        const styles = buttonStyles.base;
        SmartLists.applyStyles(button, styles);
    };
    
    SmartLists.createAndSeparator = function() {
        const separator = SmartLists.createStyledElement('div', 'rule-within-group-separator', SmartLists.STYLES.separators.and);
        separator.textContent = 'AND';
        
        const line = SmartLists.createStyledElement('div', '', SmartLists.STYLES.separators.andLine);
        separator.appendChild(line);
        
        return separator;
    };
    
    SmartLists.createOrSeparator = function() {
        const separator = SmartLists.createStyledElement('div', 'logic-group-separator', SmartLists.STYLES.separators.or);
        const orText = SmartLists.createStyledElement('div', '', SmartLists.STYLES.separators.orText);
        orText.textContent = 'OR';
        separator.appendChild(orText);
        
        const line = SmartLists.createStyledElement('div', '', SmartLists.STYLES.separators.orLine);
        separator.appendChild(line);
        
        return separator;
    };
    
    // Utility functions for applying styles
    SmartLists.applyStyles = function(element, styles) {
        if (!element || !styles) return;
        
        Object.keys(styles).forEach(function(property) {
            const value = styles[property];
            // Convert camelCase to kebab-case
            const cssProperty = property.replace(/([A-Z])/g, '-$1').toLowerCase();
            element.style.setProperty(cssProperty, value, 'important');
        });
    };
    
    SmartLists.createStyledElement = function(tagName, className, styles) {
        const element = document.createElement(tagName);
        if (className) element.className = className;
        if (styles) SmartLists.applyStyles(element, styles);
        return element;
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
    
    SmartLists.cleanupModalListeners = function(modal) {
        // Remove any existing backdrop listener to prevent accumulation
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
    };
    
})(window.SmartLists = window.SmartLists || {});

