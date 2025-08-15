(function () {
    'use strict';
    
    // Constants
    const PLUGIN_ID = "A0A2A7B2-747A-4113-8B39-757A9D267C79";
    const ENDPOINTS = {
        fields: 'Plugins/SmartPlaylist/fields',
        base: 'Plugins/SmartPlaylist',
        users: 'Plugins/SmartPlaylist/users',
        refresh: 'Plugins/SmartPlaylist/refresh'
    };
    
    // Field type constants to avoid duplication
    const FIELD_TYPES = {
        LIST_FIELDS: ['People', 'Genres', 'Studios', 'Tags', 'Artists', 'AlbumArtists'],
        NUMERIC_FIELDS: ['ProductionYear', 'CommunityRating', 'CriticRating', 'RuntimeMinutes', 'PlayCount'],
        DATE_FIELDS: ['DateCreated', 'DateLastRefreshed', 'DateLastSaved', 'DateModified', 'ReleaseDate', 'LastPlayedDate'],
        BOOLEAN_FIELDS: ['IsPlayed', 'IsFavorite', 'NextUnwatched'],
        SIMPLE_FIELDS: ['ItemType'],
        USER_DATA_FIELDS: ['IsPlayed', 'IsFavorite', 'PlayCount', 'NextUnwatched', 'LastPlayedDate']
    };
    
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

        // Tab slider styles
        tabSlider: {
            container: {
                overflowX: 'auto',
                overflowY: 'hidden',
                whiteSpace: 'nowrap',
                scrollbarWidth: 'thin',
                msOverflowStyle: 'auto',
                marginBottom: '1em',
                paddingBottom: '0.5em',
                position: 'relative',
                width: '100%',
                minHeight: '44px',
                background: 'inherit'
            },
            button: {
                display: 'inline-block',
                whiteSpace: 'nowrap',
                flexShrink: '0',
                minWidth: 'auto',
                flex: 'none',
                minHeight: '40px',
                verticalAlign: 'middle'
            }
        },

        // Layout fixes
        layout: {
            tabContent: {
                maxWidth: '830px',
                boxSizing: 'border-box',
                paddingRight: '25px'
            },
            notification: {
                maxWidth: '805px',
                marginRight: '25px',
                boxSizing: 'border-box'
            }
        }
    };

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
        { Value: "Series", Label: "Series" }, 
        { Value: "Episode", Label: "Episode" }, 
        { Value: "Audio", Label: "Audio (Music)" },
        { Value: "MusicVideo", Label: "Music Video" } 
    ];

    // Generate media type checkboxes from the mediaTypes array
    const generateMediaTypeCheckboxes = (page) => {
        const container = page.querySelector('#media-types-container');
        if (!container) return;
        
        // Clear existing content
        container.innerHTML = '';
        
        // Generate checkboxes for each media type
        mediaTypes.forEach(mediaType => {
            const label = document.createElement('label');
            label.className = 'checkboxLabel';
            label.setAttribute('for', `mediaType${mediaType.Value}`);
            
            const checkbox = document.createElement('input');
            checkbox.type = 'checkbox';
            checkbox.setAttribute('is', 'emby-checkbox');
            checkbox.id = `mediaType${mediaType.Value}`;
            checkbox.className = 'emby-checkbox media-type-checkbox';
            checkbox.value = mediaType.Value;
            
            const span = document.createElement('span');
            span.textContent = mediaType.Label;
            
            label.appendChild(checkbox);
            label.appendChild(span);
            container.appendChild(label);
        });
    };

    // Helper function to manage search input state
    const setSearchInputState = (page, disabled, placeholder = 'Search playlists...') => {
        const searchInput = page.querySelector('#playlistSearchInput');
        const clearSearchBtn = page.querySelector('#clearSearchBtn');
        
        if (searchInput) {
            searchInput.disabled = disabled;
            searchInput.placeholder = placeholder;
        }
        
        if (clearSearchBtn) {
            clearSearchBtn.style.display = disabled ? 'none' : 'block';
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
        const notificationArea = document.querySelector('#plugin-notification-area');
        if (!notificationArea) return;

        window.scrollTo({ top: 0, behavior: 'smooth' });

        notificationArea.textContent = message;
        notificationArea.style.color = 'white';
        notificationArea.style.backgroundColor = 
            type === 'success' ? '#3e8e41' : 
            type === 'warning' ? '#ff9800' : '#d9534f';
        notificationArea.style.display = 'block';

        clearTimeout(notificationTimeout);
        notificationTimeout = setTimeout(() => {
            notificationArea.style.display = 'none';
        }, 10000);
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

    function populateStaticSelects(page) {
        // Initialize page elements
        initializePageElements(page);
        
         const sortOptions = [
            { Value: 'Name', Label: 'Name' },
            { Value: 'ProductionYear', Label: 'Production Year' },
            { Value: 'CommunityRating', Label: 'Community Rating' },
            { Value: 'DateCreated', Label: 'Date Created' },
            { Value: 'ReleaseDate', Label: 'Release Date' },
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
            
            if (sortBySelect.children.length === 0) { populateSelect(sortBySelect, sortOptions, defaultSortBy); }
            if (sortOrderSelect.children.length === 0) { populateSelect(sortOrderSelect, orderOptions, defaultSortOrder); }
            page.querySelector('#playlistIsPublic').checked = defaultMakePublic;
            const maxItemsElement = page.querySelector('#playlistMaxItems');
            if (maxItemsElement) {
                maxItemsElement.value = defaultMaxItems;
            }
            const maxPlayTimeMinutesElement = page.querySelector('#playlistMaxPlayTimeMinutes');
            if (maxPlayTimeMinutesElement) {
                maxPlayTimeMinutesElement.value = defaultMaxPlayTimeMinutes;
            }
            
            // Populate settings tab dropdowns with current configuration values
            const defaultSortBySetting = page.querySelector('#defaultSortBy');
            const defaultSortOrderSetting = page.querySelector('#defaultSortOrder');
            if (defaultSortBySetting && defaultSortBySetting.children.length === 0) { 
                populateSelect(defaultSortBySetting, sortOptions, defaultSortBy); 
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

    function setValueInput(fieldValue, valueContainer, operatorValue, explicitCurrentValue) {
        // Store the current value before clearing the container
        const currentValueInput = valueContainer.querySelector('.rule-value-input');
        const currentValue = explicitCurrentValue || (currentValueInput ? currentValueInput.value : '');
        
        valueContainer.innerHTML = '';

        // Check if this is an IsIn/IsNotIn operator to use tag-based input
        const ruleRow = valueContainer.closest('.rule-row');
        const operatorSelect = ruleRow ? ruleRow.querySelector('.rule-operator-select') : null;
        const currentOperator = operatorValue || (operatorSelect ? operatorSelect.value : '');
        const isMultiValueOperator = currentOperator === 'IsIn' || currentOperator === 'IsNotIn';

        if (isMultiValueOperator) {
            // Create tag-based input for IsIn/IsNotIn operators
            createTagBasedInput(valueContainer, currentValue);
        } else if (FIELD_TYPES.SIMPLE_FIELDS.includes(fieldValue)) {
            const select = document.createElement('select');
            select.className = 'emby-select rule-value-input';
            select.setAttribute('is', 'emby-select');
            select.style.width = '100%';
            mediaTypes.forEach(opt => {
                const option = document.createElement('option');
                option.value = opt.Value;
                option.textContent = opt.Label;
                select.appendChild(option);
            });
            valueContainer.appendChild(select);
        } else if (FIELD_TYPES.BOOLEAN_FIELDS.includes(fieldValue)) {
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
                select.appendChild(option);
            });
            valueContainer.appendChild(select);
        } else if (FIELD_TYPES.NUMERIC_FIELDS.includes(fieldValue)) {
            const input = document.createElement('input');
            input.type = 'number';
            input.className = 'emby-input rule-value-input';
            input.placeholder = 'Value';
            input.style.width = '100%';
            valueContainer.appendChild(input);
        } else if (FIELD_TYPES.DATE_FIELDS.includes(fieldValue)) {
            // Check if this is a relative date operator by looking at the current operator
            const isRelativeDateOperator = operatorSelect && (operatorSelect.value === 'NewerThan' || operatorSelect.value === 'OlderThan');
            
            if (isRelativeDateOperator) {
                // For relative date operators, use a number input and a unit dropdown
                const inputContainer = document.createElement('div');
                inputContainer.style.display = 'flex';
                inputContainer.style.gap = '0.5em';
                inputContainer.style.alignItems = 'center';
                valueContainer.appendChild(inputContainer);

                const input = document.createElement('input');
                input.type = 'number';
                input.className = 'emby-input rule-value-input';
                input.placeholder = 'Number';
                input.min = '1';
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
            } else {
                // For regular date operators, use a date input
                const input = document.createElement('input');
                input.type = 'date';
                input.className = 'emby-input rule-value-input';
                input.style.width = '100%';
                valueContainer.appendChild(input);
            }
        }
        else {
            const input = document.createElement('input');
            input.type = 'text';
            input.className = 'emby-input rule-value-input';
            input.placeholder = 'Value';
            input.style.width = '100%';
            valueContainer.appendChild(input);
        }
        
        // Restore the current value if it exists and is valid for the new field type
        const newValueInput = valueContainer.querySelector('.rule-value-input');
        if (newValueInput && currentValue) {
            // Store the original value as a data attribute for potential restoration
            newValueInput.setAttribute('data-original-value', currentValue);
            
            // Try to restore the value if it's appropriate for the new field type
            if (FIELD_TYPES.SIMPLE_FIELDS.includes(fieldValue) || FIELD_TYPES.BOOLEAN_FIELDS.includes(fieldValue)) {
                // For selects, check if the value exists as an option
                if (newValueInput.tagName === 'SELECT') {
                    const option = Array.from(newValueInput.options).find(opt => opt.value === currentValue);
                    if (option) {
                        newValueInput.value = currentValue;
                    }
                }
            } else if (FIELD_TYPES.DATE_FIELDS.includes(fieldValue)) {
                // Check if this is a relative date operator
                const isRelativeDateOperator = currentOperator && (currentOperator === 'NewerThan' || currentOperator === 'OlderThan');
                
                if (isRelativeDateOperator) {
                    // Parse number:unit format for relative date operators
                    const parts = currentValue.split(':');
                    const validUnits = ['days', 'weeks', 'months', 'years'];
                    const num = parts[0];
                    const unit = parts[1];
                    const isValidNum = /^\d+$/.test(num) && parseInt(num, 10) > 0;
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
                } else {
                    // For regular date operators, restore the date value directly
                    if (newValueInput.tagName === 'INPUT') {
                        newValueInput.value = currentValue;
                    }
                }
            } else if (isMultiValueOperator) {
                // For tag-based inputs, restore the semicolon-separated values as individual tags
                if (currentValue) {
                    const tags = currentValue.split(';').map(tag => tag.trim()).filter(tag => tag.length > 0);
                    tags.forEach(tag => addTagToContainer(valueContainer, tag));
                }
            } else {
                // For inputs, restore the value directly
                newValueInput.value = currentValue;
            }
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
            helpDiv.innerHTML = '<strong>Regex Help:</strong> Use .NET syntax. Examples: <code>(?i)swe</code> (case-insensitive), <code>(?i)(eng|en)</code> (multiple options), <code>^Action</code> (starts with). Do not use JavaScript-style /pattern/flags.<br><strong>Test patterns:</strong> <a href="https://regex101.com/?flavor=dotnet" target="_blank" style="color: #00a4dc;">Regex101.com (.NET flavor)</a>';
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
        
        // Add event listeners with AbortController signal (if supported)
        const listenerOptions = getEventListenerOptions(signal);
        fieldSelect.addEventListener('change', function() {
            setValueInput(fieldSelect.value, valueContainer, operatorSelect.value);
            updateOperatorOptions(fieldSelect.value, operatorSelect);
            updateUserSelectorVisibility(newRuleRow, fieldSelect.value);
            updateNextUnwatchedOptionsVisibility(newRuleRow, fieldSelect.value);
            updateRegexHelp(newRuleRow);
        }, listenerOptions);
        
                        operatorSelect.addEventListener('change', function() {
                    updateRegexHelp(newRuleRow);
                    // Update value input type if this is a date field and operator changed to/from relative date operators
                    // or if switching to/from IsIn/IsNotIn operators
                    const fieldValue = fieldSelect.value;
                    if (FIELD_TYPES.DATE_FIELDS.includes(fieldValue) || 
                        this.value === 'IsIn' || this.value === 'IsNotIn' ||
                        this.value === 'Contains' || this.value === 'NotContains' ||
                        this.value === 'Equal' || this.value === 'NotEqual' ||
                        this.value === 'MatchRegex') {
                        setValueInput(fieldValue, valueContainer, this.value);
                    }
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
                }
                
                // Re-add event listeners
                const listenerOptions = getEventListenerOptions(signal);
                fieldSelect.addEventListener('change', function() {
                    setValueInput(fieldSelect.value, valueContainer, operatorSelect.value);
                    updateOperatorOptions(fieldSelect.value, operatorSelect);
                    updateUserSelectorVisibility(ruleRow, fieldSelect.value);
                    updateRegexHelp(ruleRow);
                }, listenerOptions);
                
                operatorSelect.addEventListener('change', function() {
                    updateRegexHelp(ruleRow);
                    // Update value input type if this is a date field and operator changed to/from relative date operators
                    // or if switching to/from IsIn/IsNotIn operators
                    const fieldValue = fieldSelect.value;
                    if (FIELD_TYPES.DATE_FIELDS.includes(fieldValue) || 
                        this.value === 'IsIn' || this.value === 'IsNotIn' ||
                        this.value === 'Contains' || this.value === 'NotContains' ||
                        this.value === 'Equal' || this.value === 'NotEqual' ||
                        this.value === 'MatchRegex') {
                        setValueInput(fieldValue, valueContainer, this.value);
                    }
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
        try {
            const apiClient = getApiClient();
            const playlistName = page.querySelector('#playlistName').value;

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

            const sortByElement = page.querySelector('#sortBy');
            const sortOrderElement = page.querySelector('#sortOrder');
            const sortByValue = sortByElement?.value || 'Name';
            const sortOrderValue = sortOrderElement?.value || 'Ascending';
            
            // Special handling for Random - it doesn't need Ascending/Descending
            const orderName = sortByValue === 'Random' ? 'Random' : sortByValue + ' ' + sortOrderValue;
            const isPublic = page.querySelector('#playlistIsPublic').checked || false;
            const isEnabled = page.querySelector('#playlistIsEnabled').checked !== false; // Default to true if checkbox doesn't exist
            const maxItemsElement = page.querySelector('#playlistMaxItems');
            const maxItemsInput = maxItemsElement?.value || '';
            let maxItems;
            if (maxItemsInput === '') {
                maxItems = 500;
            } else {
                const parsedValue = parseInt(maxItemsInput);
                maxItems = isNaN(parsedValue) ? 500 : parsedValue;
            }

            const maxPlayTimeMinutesElement = page.querySelector('#playlistMaxPlayTimeMinutes');
            const maxPlayTimeMinutesInput = maxPlayTimeMinutesElement?.value || '';
            let maxPlayTimeMinutes;
            if (maxPlayTimeMinutesInput === '') {
                maxPlayTimeMinutes = 0;
            } else {
                const parsedValue = parseInt(maxPlayTimeMinutesInput);
                maxPlayTimeMinutes = isNaN(parsedValue) ? 0 : parsedValue;
            }

            // Get selected user ID from dropdown
            const userId = page.querySelector('#playlistUser').value;
            
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
                MaxPlayTimeMinutes: maxPlayTimeMinutes
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
                const actionPast = editState.editMode ? 'updated' : 'created';
                const actionFuture = editState.editMode ? 'updated' : 'generated';
                showNotification('Playlist "' + playlistName + '" ' + actionPast + '. The playlist has been ' + actionFuture + '.', 'success');
                
                // Exit edit mode and clear form
                if (editState.editMode) {
                    // Exit edit mode silently without showing cancellation message
                    setPageEditState(page, false, null);
                    const editIndicator = page.querySelector('#edit-mode-indicator');
                    editIndicator.style.display = 'none';
                    const submitBtn = page.querySelector('#submitBtn');
                    if (submitBtn) submitBtn.textContent = 'Create Playlist';
                    
                    // Restore tab button text
                    const createTabButton = page.querySelector('.emby-tab-button[data-tab="create"] .emby-button-foreground');
                    if (createTabButton) {
                        createTabButton.textContent = 'Create Playlist';
                    }
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
        
        page.querySelector('#playlistName').value = '';
        
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
            page.querySelector('#sortBy').value = config.DefaultSortBy || 'Name';
            page.querySelector('#sortOrder').value = config.DefaultSortOrder || 'Ascending';
            page.querySelector('#playlistIsPublic').checked = config.DefaultMakePublic || false;
            page.querySelector('#playlistIsEnabled').checked = true; // Default to enabled
            const maxItemsElement = page.querySelector('#playlistMaxItems');
            if (maxItemsElement) {
                maxItemsElement.value = config.DefaultMaxItems !== undefined && config.DefaultMaxItems !== null ? config.DefaultMaxItems : 500;
            }
            const maxPlayTimeMinutesElement = page.querySelector('#playlistMaxPlayTimeMinutes');
            if (maxPlayTimeMinutesElement) {
                maxPlayTimeMinutesElement.value = config.DefaultMaxPlayTimeMinutes !== undefined && config.DefaultMaxPlayTimeMinutes !== null ? config.DefaultMaxPlayTimeMinutes : 0;
            }
        }).catch(() => {
            page.querySelector('#sortBy').value = 'Name';
            page.querySelector('#sortOrder').value = 'Ascending';
            page.querySelector('#playlistIsPublic').checked = false;
            page.querySelector('#playlistIsEnabled').checked = true; // Default to enabled
            const maxItemsElement = page.querySelector('#playlistMaxItems');
            if (maxItemsElement) {
                maxItemsElement.value = 500;
            }
            const maxPlayTimeMinutesElement = page.querySelector('#playlistMaxPlayTimeMinutes');
            if (maxPlayTimeMinutesElement) {
                maxPlayTimeMinutesElement.value = 0;
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
        // Handle both old User field and new UserId field
        if (playlist.UserId && playlist.UserId !== '00000000-0000-0000-0000-000000000000') {
            try {
                const user = await apiClient.getUser(playlist.UserId);
                return user?.Name || 'Unknown User';
            } catch (err) {
                console.error('Error resolving user ID ' + playlist.UserId + ':', err);
                return 'Unknown User';
            }
        } else if (playlist.User) {
            // Legacy format - username is directly stored
            return playlist.User;
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

    // Helper function to generate rules HTML for playlist display
    async function generatePlaylistRulesHtml(playlist, apiClient) {
        let rulesHtml = '';
        if (playlist.ExpressionSets && playlist.ExpressionSets.length > 0) {
            for (let groupIndex = 0; groupIndex < playlist.ExpressionSets.length; groupIndex++) {
                const expressionSet = playlist.ExpressionSets[groupIndex];
                if (groupIndex > 0) {
                    rulesHtml += '<strong style="color: #888;">OR</strong><br>';
                }
                
                if (expressionSet.Expressions && expressionSet.Expressions.length > 0) {
                    rulesHtml += '<div style="border: 1px solid #555; padding: 0.5em; margin: 0.25em 0; border-radius: 2px; background: rgba(255,255,255,0.02);">';
                    
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
                        
                        // Check if this rule has a specific user
                        let userInfo = '';
                        if (rule.UserId && rule.UserId !== '00000000-0000-0000-0000-000000000000') {
                            const userName = await resolveUserIdToName(apiClient, rule.UserId);
                            if (userName) {
                                userInfo = ' for user "' + userName + '"';
                            }
                        }
                        
                        // Check for NextUnwatched specific options
                        let nextUnwatchedInfo = '';
                        if (rule.MemberName === 'NextUnwatched' && rule.IncludeUnwatchedSeries === false) {
                            nextUnwatchedInfo = ' (excluding unwatched series)';
                        }
                        
                        rulesHtml += '<span style="font-family: monospace; background: rgba(255,255,255,0.1); padding: 2px 2px; border-radius: 2px;">' + fieldName + ' ' + operator + ' "' + value + '"' + userInfo + nextUnwatchedInfo + '</span>';
                    }
                    
                    rulesHtml += '</div>';
                }
            }
        } else {
            rulesHtml = '<em>No rules defined</em><br>';
        }
        return rulesHtml;
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
        page._loadingPlaylists = true;
        
        // Disable search input while loading
        setSearchInputState(page, true, 'Loading playlists...');
        
        container.innerHTML = '<p>Loading playlists...</p>';
        
        apiClient.ajax({
            type: "GET",
            url: apiClient.getUrl(ENDPOINTS.base),
            contentType: 'application/json'
        }).then(response => {
            if (!response.ok) { throw new Error('HTTP ' + response.status + ': ' + response.statusText); }
            return response.json();
        }).then(async playlists => {
            // Store playlists data for filtering
            page._allPlaylists = playlists;
            
            if (playlists && playlists.length > 0) {
                // Apply current search filter if any
                const searchInput = page.querySelector('#playlistSearchInput');
                const searchTerm = searchInput ? searchInput.value.trim().toLowerCase() : '';
                const filteredPlaylists = searchTerm ? filterPlaylists(playlists, searchTerm) : playlists;
                
                // Calculate summary statistics for filtered results
                const totalPlaylists = playlists.length;
                const filteredCount = filteredPlaylists.length;
                const enabledPlaylists = filteredPlaylists.filter(p => p.Enabled !== false).length;
                const disabledPlaylists = filteredCount - enabledPlaylists;
                
                let html = '<div class="inputContainer">';
                html += '<div class="field-description" style="margin-bottom: 1em; padding: 0.5em; background: rgba(255,255,255,0.05); border-radius: 4px; border-left: 3px solid #00a4dc;">';
                html += '<strong>Summary:</strong> ' + filteredCount + ' of ' + totalPlaylists + ' playlist' + (totalPlaylists !== 1 ? 's' : '') + 
                        (searchTerm ? ' matching "' + searchTerm + '"' : '') +
                        ' • ' + enabledPlaylists + ' enabled • ' + disabledPlaylists + ' disabled';
                html += '</div></div>';
                
                // Process filtered playlists sequentially to resolve usernames
                for (const playlist of filteredPlaylists) {
                    const isPublic = playlist.Public ? 'Public' : 'Private';
                    const isEnabled = playlist.Enabled !== false; // Default to true for backward compatibility
                    const enabledStatus = isEnabled ? 'Enabled' : 'Disabled';
                    const enabledStatusColor = isEnabled ? '#4CAF50' : '#f44336';
                    const sortName = playlist.Order ? playlist.Order.Name : 'Default';
                    const userName = await resolveUsername(apiClient, playlist);
                    const playlistId = playlist.Id || 'NO_ID';
                    const mediaTypes = playlist.MediaTypes && playlist.MediaTypes.length > 0 ? 
                        playlist.MediaTypes.join(', ') : 'All Types';
                    
                    // Use helper functions to generate rules HTML and format display values
                    const rulesHtml = await generatePlaylistRulesHtml(playlist, apiClient);
                    const { maxItemsDisplay, maxPlayTimeDisplay } = formatPlaylistDisplayValues(playlist);
                    
                    html += '<div class="inputContainer" style="border: 1px solid #444; padding: 1em; border-radius: 2px; margin-bottom: 1.5em;">' +
                        '<h4 style="margin-top: 0;">' + playlist.Name + '</h4>' +
                        '<div class="field-description">' +
                        '<strong>File:</strong> ' + playlist.FileName + '<br>' +
                        '<strong>User:</strong> ' + userName + '<br>' +
                        '<strong>Media Types:</strong> ' + mediaTypes + '<br>' +
                        '<strong>Rules:</strong><br>' + rulesHtml + 
                        '<strong>Sort:</strong> ' + sortName + '<br>' +
                        '<strong>Max Items:</strong> ' + maxItemsDisplay + '<br>' +
                        '<strong>Max Play Time:</strong> ' + maxPlayTimeDisplay + '<br>' +
                        '<strong>Visibility:</strong> ' + isPublic + '<br>' +
                        '<strong>Status:</strong> <span style="color: ' + enabledStatusColor + '; font-weight: bold;">' + enabledStatus + '</span>' +
                        '</div>' +
                        '<div style="margin-top: 1em;">' +
                        '<button type="button" is="emby-button" class="emby-button raised edit-playlist-btn" data-playlist-id="' + playlistId + '" style="margin-right: 0.5em;">Edit</button>' +
                        '<button type="button" is="emby-button" class="emby-button raised refresh-playlist-btn" data-playlist-id="' + playlistId + '" data-playlist-name="' + playlist.Name + '" style="margin-right: 0.5em;">Refresh</button>' +
                        (isEnabled ? 
                            '<button type="button" is="emby-button" class="emby-button raised disable-playlist-btn" data-playlist-id="' + playlistId + '" data-playlist-name="' + playlist.Name + '" style="margin-right: 0.5em;">Disable</button>' :
                            '<button type="button" is="emby-button" class="emby-button raised enable-playlist-btn" data-playlist-id="' + playlistId + '" data-playlist-name="' + playlist.Name + '" style="margin-right: 0.5em;">Enable</button>'
                        ) +
                        '<button type="button" is="emby-button" class="emby-button raised button-delete delete-playlist-btn" data-playlist-id="' + playlistId + '" data-playlist-name="' + playlist.Name + '">Delete</button>' +
                        '</div>' +
                        '</div>';
                }
                container.innerHTML = html;
            } else {
                container.innerHTML = '<div class="inputContainer"><p>No smart playlists found.</p></div>';
            }
            
            // Re-enable search input after loading is complete
            setSearchInputState(page, false);
            page._loadingPlaylists = false;
        }).catch(err => {
            console.error('Error loading playlists:', err);
            let errorMessage = (err && err.message) ? err.message : 'Unknown error occurred.';
            container.innerHTML = '<div class="inputContainer"><p style="color: #ff6b6b;">' + errorMessage + '</p></div>';
            
            // Re-enable search input even on error
            setSearchInputState(page, false);
            page._loadingPlaylists = false;
        });
    }

    function filterPlaylists(playlists, searchTerm) {
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
            
            // Search in legacy username field (for backward compatibility)
            if (playlist.User && playlist.User.toLowerCase().includes(searchTerm)) {
                return true;
            }
            
            return false;
        });
    }

    async function filterPlaylistsByUser(playlists, searchTerm) {
        if (!searchTerm) return playlists;
        
        const apiClient = getApiClient();
        const matchingPlaylists = [];
        
        // Process playlists sequentially to resolve usernames and search
        for (const playlist of playlists) {
            try {
                const userName = await resolveUsername(apiClient, playlist);
                if (userName.toLowerCase().includes(searchTerm)) {
                    matchingPlaylists.push(playlist);
                }
            } catch (err) {
                console.error('Error resolving username for playlist search:', err);
                // Continue with other playlists even if one fails
            }
        }
        
        return matchingPlaylists;
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
        
        const searchTerm = searchInput.value.trim().toLowerCase();
        
        if (!searchTerm) {
            // No search term, show all playlists
            displayFilteredPlaylists(page, page._allPlaylists, '');
            return;
        }
        
        // Show loading state for user search
        const container = page.querySelector('#playlist-list-container');
        if (container) {
            container.innerHTML = '<div class="inputContainer"><p>Searching playlists...</p></div>';
        }
        
        try {
            // Do basic filtering (synchronous) first
            const basicFiltered = filterPlaylists(page._allPlaylists, searchTerm);
            
            // Also do user search (asynchronous) in parallel
            const userFiltered = await filterPlaylistsByUser(page._allPlaylists, searchTerm);
            
            // Combine results, removing duplicates by playlist ID
            const combinedResults = new Map();
            
            // Add basic filtered results
            basicFiltered.forEach(playlist => {
                combinedResults.set(playlist.Id, playlist);
            });
            
            // Add user filtered results
            userFiltered.forEach(playlist => {
                combinedResults.set(playlist.Id, playlist);
            });
            
            const filteredPlaylists = Array.from(combinedResults.values());
            
            if (filteredPlaylists.length === 0) {
                container.innerHTML = '<div class="inputContainer"><p>No playlists match your search criteria.</p></div>';
                return;
            }
            
            // Re-use the existing display logic but with filtered data
            displayFilteredPlaylists(page, filteredPlaylists, searchTerm);
            
        } catch (err) {
            console.error('Error during search:', err);
            container.innerHTML = '<div class="inputContainer"><p style="color: #ff6b6b;">Search error: ' + err.message + '</p></div>';
        }
    }

    async function displayFilteredPlaylists(page, filteredPlaylists, searchTerm) {
        const container = page.querySelector('#playlist-list-container');
        const apiClient = getApiClient();
        
        // Calculate summary statistics for filtered results
        const totalPlaylists = page._allPlaylists.length;
        const filteredCount = filteredPlaylists.length;
        const enabledPlaylists = filteredPlaylists.filter(p => p.Enabled !== false).length;
        const disabledPlaylists = filteredCount - enabledPlaylists;
        
        let html = '<div class="inputContainer">';
        html += '<div class="field-description" style="margin-bottom: 1em; padding: 0.5em; background: rgba(255,255,255,0.05); border-radius: 1px; border-left: 3px solid #00a4dc;">';
        html += '<strong>Summary:</strong> ' + filteredCount + ' of ' + totalPlaylists + ' playlist' + (totalPlaylists !== 1 ? 's' : '') + 
                (searchTerm ? ' matching "' + searchTerm + '"' : '') +
                ' • ' + enabledPlaylists + ' enabled • ' + disabledPlaylists + ' disabled';
        html += '</div></div>';
        
        // Process filtered playlists sequentially to resolve usernames
        for (const playlist of filteredPlaylists) {
            const isPublic = playlist.Public ? 'Public' : 'Private';
            const isEnabled = playlist.Enabled !== false; // Default to true for backward compatibility
            const enabledStatus = isEnabled ? 'Enabled' : 'Disabled';
            const enabledStatusColor = isEnabled ? '#4CAF50' : '#f44336';
            const sortName = playlist.Order ? playlist.Order.Name : 'Default';
            const userName = await resolveUsername(apiClient, playlist);
            const playlistId = playlist.Id || 'NO_ID';
            const mediaTypes = playlist.MediaTypes && playlist.MediaTypes.length > 0 ? 
                playlist.MediaTypes.join(', ') : 'All Types';
            
            // Use helper functions to generate rules HTML and format display values
            const rulesHtml = await generatePlaylistRulesHtml(playlist, apiClient);
            const { maxItemsDisplay, maxPlayTimeDisplay } = formatPlaylistDisplayValues(playlist);
            
            html += '<div class="inputContainer" style="border: 1px solid #444; padding: 1em; border-radius: 1px; margin-bottom: 1.5em;">' +
                '<h4 style="margin-top: 0;">' + playlist.Name + '</h4>' +
                '<div class="field-description">' +
                '<strong>File:</strong> ' + playlist.FileName + '<br>' +
                '<strong>User:</strong> ' + userName + '<br>' +
                '<strong>Media Types:</strong> ' + mediaTypes + '<br>' +
                '<strong>Rules:</strong><br>' + rulesHtml + '<br>' +
                '<strong>Sort:</strong> ' + sortName + '<br>' +
                '<strong>Max Items:</strong> ' + maxItemsDisplay + '<br>' +
                '<strong>Max Play Time:</strong> ' + maxPlayTimeDisplay + '<br>' +
                '<strong>Visibility:</strong> ' + isPublic + '<br>' +
                '<strong>Status:</strong> <span style="color: ' + enabledStatusColor + '; font-weight: bold;">' + enabledStatus + '</span>' +
                '</div>' +
                '<div style="margin-top: 1em;">' +
                '<button type="button" is="emby-button" class="emby-button raised edit-playlist-btn" data-playlist-id="' + playlistId + '" style="margin-right: 0.5em;">Edit</button>' +
                '<button type="button" is="emby-button" class="emby-button raised refresh-playlist-btn" data-playlist-id="' + playlistId + '" data-playlist-name="' + playlist.Name + '" style="margin-right: 0.5em;">Refresh</button>' +
                (isEnabled ? 
                    '<button type="button" is="emby-button" class="emby-button raised disable-playlist-btn" data-playlist-id="' + playlistId + '" data-playlist-name="' + playlist.Name + '" style="margin-right: 0.5em;">Disable</button>' :
                    '<button type="button" is="emby-button" class="emby-button raised enable-playlist-btn" data-playlist-id="' + playlistId + '" data-playlist-name="' + playlist.Name + '" style="margin-right: 0.5em;">Enable</button>'
                ) +
                '<button type="button" is="emby-button" class="emby-button raised button-delete delete-playlist-btn" data-playlist-id="' + playlistId + '" data-playlist-name="' + playlist.Name + '">Delete</button>' +
                '</div>' +
                '</div>';
        }
        
        container.innerHTML = html;
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
        }).catch((err) => {
            Dashboard.hideLoadingMsg();
            console.error('Error refreshing playlist:', err);
            handleApiError(err, 'Failed to refresh playlist');
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
            console.error('Error deleting playlist:', err);
            handleApiError(err, 'Failed to delete playlist "' + playlistName + '".');
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
            console.error('Error enabling playlist:', err);
            handleApiError(err, 'Failed to enable playlist "' + playlistName + '".');
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
            console.error('Error disabling playlist:', err);
            handleApiError(err, 'Failed to disable playlist "' + playlistName + '".');
        });
    }

    function showDeleteConfirm(page, playlistId, playlistName) {
        const modal = page.querySelector('#delete-confirm-modal');
        if (!modal) return;
        
        const modalContainer = modal.querySelector('.custom-modal-container');
        const confirmText = modal.querySelector('#delete-confirm-text');
        const confirmBtn = modal.querySelector('#delete-confirm-btn');
        const cancelBtn = modal.querySelector('#delete-cancel-btn');

        // Clean up any existing modal listeners
        cleanupModalListeners(modal);

        // Apply modal styles using centralized configuration
        applyStyles(modalContainer, STYLES.modal.container);
        applyStyles(modal, STYLES.modal.backdrop);

        confirmText.textContent = 'Are you sure you want to delete the smart playlist "' + playlistName + '"? This cannot be undone.';
        
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
        
        // Store the controller on the modal for cleanup
        modal._modalAbortController = modalAbortController;
        
        // Clean up function to close modal and remove all listeners
        const cleanupAndClose = () => {
            modal.classList.add('hide');
            cleanupModalListeners(modal);
        };
        
        const handleConfirm = () => {
            deletePlaylist(page, playlistId, playlistName);
            cleanupAndClose();
        };

        const handleCancel = () => {
            cleanupAndClose();
        };
        
        const handleBackdropClick = (e) => {
            if (e.target === modal) {
                cleanupAndClose();
            }
        };

        // Store the backdrop handler on the modal for cleanup
        modal._modalBackdropHandler = handleBackdropClick;
        
        // Add event listeners with AbortController signal
        confirmBtn.addEventListener('click', handleConfirm, getEventListenerOptions(modalSignal));
        cancelBtn.addEventListener('click', handleCancel, getEventListenerOptions(modalSignal));
        modal.addEventListener('click', handleBackdropClick, getEventListenerOptions(modalSignal));
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
            modal._modalAbortController.abort();
            modal._modalAbortController = null;
        }
    }

    async function editPlaylist(page, playlistId) {
        const apiClient = getApiClient();
        Dashboard.showLoadingMsg();
        
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
                // Populate form with playlist data
                page.querySelector('#playlistName').value = playlist.Name || '';
                page.querySelector('#playlistIsPublic').checked = playlist.Public || false;
                page.querySelector('#playlistIsEnabled').checked = playlist.Enabled !== false; // Default to true for backward compatibility
                
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
                    page.querySelector('#playlistUser').value = playlist.UserId;
                } else if (playlist.User) {
                    // Legacy support: try to find user by username (simplified)
                    // Since this is legacy, just warn the user and use current user as fallback
                    console.warn('Legacy playlist detected with username:', playlist.User);
                    showNotification('Legacy playlist detected. Please verify the owner is correct.', 'warning');
                    setCurrentUserAsDefault(page);
                }
                
                // Set sort options
                const orderName = playlist.Order ? playlist.Order.Name : 'Name Ascending';
                
                let sortBy, sortOrder;
                if (orderName === 'Random') {
                    // Special handling for Random - it doesn't have Ascending/Descending
                    sortBy = 'Random';
                    sortOrder = 'Ascending'; // Default sort order (though it won't be used)
                } else {
                    // Normal parsing for other orders like "Name Ascending"
                    const parts = orderName.split(' ');
                    sortBy = parts.slice(0, -1).join(' ') || 'Name';
                    sortOrder = parts[parts.length - 1] || 'Ascending';
                }
                
                page.querySelector('#sortBy').value = sortBy;
                page.querySelector('#sortOrder').value = sortOrder;
                
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
                                        // Just ensure the hidden input has the correct value
                                        valueInput.value = expression.TargetValue;
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
                const createTabButton = page.querySelector('.emby-tab-button[data-tab="create"] .emby-button-foreground');
                if (createTabButton) {
                    createTabButton.textContent = 'Edit Playlist';
                }
                
                // Switch to create tab (which becomes edit tab)
                const createTab = page.querySelector('.emby-tab-button[data-tab="create"]');
                createTab.click();
                
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

    function cancelEdit(page) {
        setPageEditState(page, false, null);
        
        // Update UI to show create mode
        const editIndicator = page.querySelector('#edit-mode-indicator');
        editIndicator.style.display = 'none';
        page.querySelector('#submitBtn').textContent = 'Create Playlist';
        
        // Restore tab button text
        const createTabButton = page.querySelector('.emby-tab-button[data-tab="create"] .emby-button-foreground');
        if (createTabButton) {
            createTabButton.textContent = 'Create Playlist';
        }
        
        // Clear form
        clearForm(page);
        
        showNotification('Edit mode cancelled.', 'success');
    }

    function loadConfiguration(page) {
        Dashboard.showLoadingMsg();
        getApiClient().getPluginConfiguration(getPluginId()).then(config => {
            page.querySelector('#defaultSortBy').value = config.DefaultSortBy || 'Name';
            page.querySelector('#defaultSortOrder').value = config.DefaultSortOrder || 'Ascending';
            
            page.querySelector('#defaultMakePublic').checked = config.DefaultMakePublic || false;
            page.querySelector('#defaultMaxItems').value = config.DefaultMaxItems !== undefined && config.DefaultMaxItems !== null ? config.DefaultMaxItems : 500;
            page.querySelector('#defaultMaxPlayTimeMinutes').value = config.DefaultMaxPlayTimeMinutes !== undefined && config.DefaultMaxPlayTimeMinutes !== null ? config.DefaultMaxPlayTimeMinutes : 0;
            
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
            
            // Save playlist naming configuration
            config.PlaylistNamePrefix = page.querySelector('#playlistNamePrefix').value;
            config.PlaylistNameSuffix = page.querySelector('#playlistNameSuffix').value;
            
            apiClient.updatePluginConfiguration(getPluginId(), config).then(() => {
                Dashboard.hideLoadingMsg();
                showNotification('Settings saved.', 'success');
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
            url: getApiClient().getUrl(ENDPOINTS.refresh),
            contentType: 'application/json'
        }).then(() => {
            Dashboard.hideLoadingMsg();
            showNotification('SmartPlaylist refresh tasks have been triggered. All smart playlists will be updated shortly.', 'success');
        }).catch((err) => {
            Dashboard.hideLoadingMsg();
            console.error('Error refreshing playlists:', err);
            handleApiError(err, 'Failed to trigger playlist refresh');
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
        
        // Set up tab slider functionality
        setupTabSlider(page);
        
        // Load configuration (this can run independently)
        loadConfiguration(page);

        applyMainContainerLayoutFix(page);
        applyNotificationLayoutFix(page);
    }

    function setupTabSlider(page) {
        var tabSlider = page.querySelector('.emby-tabs-slider');
        if (!tabSlider) return;
        
        // Prevent multiple setups on the same slider
        if (tabSlider._sliderInitialized) return;
        tabSlider._sliderInitialized = true;

        // --- FORCE PARENT CONTAINERS TO ALLOW SCROLLING ---
        var parent = tabSlider.parentElement;
        while (parent && parent !== document.body) {
            parent.style.overflowX = 'visible';
            parent.style.overflowY = 'visible';
            parent.style.width = '100%';
            parent = parent.parentElement;
        }

        // --- TAB SLIDER STYLES ---
        applyStyles(tabSlider, STYLES.tabSlider.container);

        // Hide webkit scrollbar (best effort)
        tabSlider.style.setProperty('scrollbar-width', 'thin');
        tabSlider.style.setProperty('-webkit-scrollbar', 'display: none');

        // --- TAB BUTTON STYLES ---
        var tabButtons = tabSlider.querySelectorAll('.emby-tab-button');
        for (var i = 0; i < tabButtons.length; i++) {
            var button = tabButtons[i];
            applyStyles(button, STYLES.tabSlider.button);
            button.style.marginRight = (i < tabButtons.length - 1) ? '0.5em' : '0';
        }

        // --- LISTENER LOGIC (unchanged) ---
        tabSlider._sliderListeners = [];
        function checkOverflow() {
            var existingIndicator = tabSlider.querySelector('.tab-overflow-indicator');
            if (existingIndicator) existingIndicator.remove(); }
        checkOverflow();
        var resizeHandler = function() { checkOverflow(); };
        window.addEventListener('resize', resizeHandler);
        tabSlider._sliderListeners.push({ element: window, event: 'resize', handler: resizeHandler });
        var wheelHandler = function(e) {
            if (e.deltaY !== 0) {
                e.preventDefault();
                tabSlider.scrollLeft += e.deltaY;
            }
        };
        tabSlider.addEventListener('wheel', wheelHandler);
        tabSlider._sliderListeners.push({ element: tabSlider, event: 'wheel', handler: wheelHandler });
        var isScrolling = false;
        var startX = 0;
        var scrollLeft = 0;
        var touchStartHandler = function(e) {
            isScrolling = true;
            startX = e.touches[0].pageX - tabSlider.offsetLeft;
            scrollLeft = tabSlider.scrollLeft;
        };
        tabSlider.addEventListener('touchstart', touchStartHandler);
        tabSlider._sliderListeners.push({ element: tabSlider, event: 'touchstart', handler: touchStartHandler });
        var touchMoveHandler = function(e) {
            if (!isScrolling) return;
            e.preventDefault();
            var x = e.touches[0].pageX - tabSlider.offsetLeft;
            var walk = (x - startX) * 2;
            tabSlider.scrollLeft = scrollLeft - walk;
        };
        tabSlider.addEventListener('touchmove', touchMoveHandler);
        tabSlider._sliderListeners.push({ element: tabSlider, event: 'touchmove', handler: touchMoveHandler });
        var touchEndHandler = function() { isScrolling = false; };
        tabSlider.addEventListener('touchend', touchEndHandler);
        tabSlider._sliderListeners.push({ element: tabSlider, event: 'touchend', handler: touchEndHandler });
        function scrollToActiveTab() {
            var activeTab = tabSlider.querySelector('.emby-tab-button-active');
            if (activeTab) {
                var tabRect = activeTab.getBoundingClientRect();
                var sliderRect = tabSlider.getBoundingClientRect();
                if (tabRect.left < sliderRect.left || tabRect.right > sliderRect.right) {
                    activeTab.scrollIntoView({ behavior: 'smooth', block: 'nearest', inline: 'center' });
                }
            }
        }
        var clickHandler = function(e) {
            if (e.target.closest('.emby-tab-button')) {
                setTimeout(scrollToActiveTab, 100);
            }
        };
        tabSlider.addEventListener('click', clickHandler);
        tabSlider._sliderListeners.push({ element: tabSlider, event: 'click', handler: clickHandler });
        setTimeout(scrollToActiveTab, 100);
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
            if (target.closest('#refreshPlaylistsBtn')) { refreshAllPlaylists(); }
            if (target.closest('#refreshPlaylistListBtn')) { loadPlaylistList(page); }
            if (target.closest('.delete-playlist-btn')) {
                const button = target.closest('.delete-playlist-btn');
                showDeleteConfirm(page, button.getAttribute('data-playlist-id'), button.getAttribute('data-playlist-name'));
            }
            if (target.closest('.edit-playlist-btn')) {
                const button = target.closest('.edit-playlist-btn');
                editPlaylist(page, button.getAttribute('data-playlist-id'));
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
    }

    document.addEventListener('pageshow', function (e) {
        const page = e.target;
        if (page.classList.contains('SmartPlaylistConfigurationPage')) {
            const tabButtons = page.querySelectorAll('.emby-tab-button');
            const tabContents = page.querySelectorAll('[data-tab-content]');
            
            // Only add tab listeners once per page to prevent duplicates
            if (!page._tabListenersInitialized) {
                page._tabListenersInitialized = true;
                
                // Create AbortController for tab listeners
                const tabAbortController = createAbortController();
                const tabSignal = tabAbortController.signal;
                
                // Store controller on the page for cleanup
                page._tabAbortController = tabAbortController;
                
                tabButtons.forEach(button => {
                    button.addEventListener('click', () => {
                        const tabId = button.getAttribute('data-tab');
                        
                        // Hide any open modals when switching tabs and clean up listeners
                        const modal = page.querySelector('#delete-confirm-modal');
                        if (modal && !modal.classList.contains('hide')) {
                            modal.classList.add('hide');
                            cleanupModalListeners(modal);
                        }
                        
                        tabButtons.forEach(btn => btn.classList.remove('is-active', 'emby-tab-button-active'));
                        button.classList.add('is-active', 'emby-tab-button-active');
                        tabContents.forEach(content => {
                            content.classList.toggle('hide', content.getAttribute('data-tab-content') !== tabId);
                        });
                        if (tabId === 'manage') { loadPlaylistList(page); }
                        
                        // Scroll to the clicked tab if it's not fully visible
                        setTimeout(() => {
                            const tabSlider = page.querySelector('.emby-tabs-slider');
                            if (tabSlider) {
                                const buttonRect = button.getBoundingClientRect();
                                const sliderRect = tabSlider.getBoundingClientRect();
                                
                                if (buttonRect.left < sliderRect.left || buttonRect.right > sliderRect.right) {
                                    button.scrollIntoView({
                                        behavior: 'smooth',
                                        block: 'nearest',
                                        inline: 'center'
                                    });
                                }
                            }
                        }, 50);
                    }, getEventListenerOptions(tabSignal));
                });
            }
            
            const createTab = page.querySelector('.emby-tab-button[data-tab="create"]');
            if (createTab && !createTab.classList.contains('is-active')) {
                createTab.click();
            }
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
        const modal = page.querySelector('#delete-confirm-modal');
        if (modal) {
            cleanupModalListeners(modal);
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
        
        // Clean up tab slider listeners (including window event listeners)
        const tabSlider = page.querySelector('.emby-tabs-slider');
        if (tabSlider && tabSlider._sliderListeners) {
            tabSlider._sliderListeners.forEach(listener => {
                if (listener.element && listener.event && listener.handler) {
                    try {
                        listener.element.removeEventListener(listener.event, listener.handler);
                    } catch (e) {
                        // Ignore errors when removing listeners (element might be gone)
                        console.warn('Failed to remove event listener:', e);
                    }
                }
            });
            tabSlider._sliderListeners = null;
            tabSlider._sliderInitialized = false;
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

    function applyNotificationLayoutFix(page) {
        var notificationArea = page.querySelector('#plugin-notification-area');
        if (notificationArea) {
            applyStyles(notificationArea, STYLES.layout.notification);
        }
    }

    /**
     * Creates a tag-based input interface for IsIn/IsNotIn operators
     */
    function createTagBasedInput(valueContainer, currentValue) {
        // Create the main container with EXACT same styling as standard Jellyfin inputs
        const tagContainer = document.createElement('div');
        tagContainer.className = 'tag-input-container';
        tagContainer.style.cssText = 'width: 100%; min-height: 38px; border: none; border-radius: 0; background: #292929; padding: 0.5em; display: flex; flex-wrap: wrap; gap: 0.5em; align-items: flex-start; box-sizing: border-box; align-content: flex-start;';
        
        // Create the input field with standard Jellyfin styling - dynamic sizing
        const input = document.createElement('input');
        input.type = 'text';
        input.className = 'emby-input tag-input-field';
        input.placeholder = 'Type a value and press Enter';
        input.style.cssText = 'border: none; background: transparent; color: #fff; flex: 0 0 auto; width: 20px; min-width: 20px; max-width: 200px; outline: none; font-size: 0.9em; font-family: inherit; white-space: nowrap; transition: width 0.2s ease;';
        input.setAttribute('data-input-type', 'tag-input');
        
        // Set placeholder color
        input.style.setProperty('--placeholder-color', '#757575');
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
            if (e.key === 'Enter' || e.key === 'Tab') {
                e.preventDefault();
                const value = input.value.trim();
                if (value) {
                    addTagToContainer(valueContainer, value);
                    input.value = '';
                    updateHiddenInput(valueContainer);
                    hideAddOptionDropdown(valueContainer);
                }
            }
        });
        
        input.addEventListener('input', function() {
            const value = input.value.trim();
            
            // Hide placeholder when typing
            if (input.value.length > 0) {
                input.placeholder = '';
            } else {
                input.placeholder = 'Type a value and press Enter';
            }
            
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
        
        input.addEventListener('focus', function() {
            // Expand input when focused for better typing experience
            this.style.width = '200px';
            this.style.minWidth = '200px';
        });
        
        input.addEventListener('blur', function() {
            // Contract input when not focused to save space
            this.style.width = '20px';
            this.style.minWidth = '20px';
            
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
        
        // Check if tag already exists to prevent duplicates
        const existingTags = Array.from(tagContainer.querySelectorAll('.tag-item span')).map(span => span.textContent);
        if (existingTags.includes(tagText)) {
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
            const input = valueContainer.querySelector('.tag-input-field');
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
        
        const tags = Array.from(valueContainer.querySelectorAll('.tag-item span')).map(span => span.textContent);
        hiddenInput.value = tags.join(';');
        
        // Debug: Log layout information
        debugTagLayout(valueContainer);
    }
    
    /**
     * Debug function to understand tag layout
     */
    function debugTagLayout(valueContainer) {
        const tagContainer = valueContainer.querySelector('.tag-input-container');
        if (!tagContainer) return;
        
        const containerRect = tagContainer.getBoundingClientRect();
        const tags = tagContainer.querySelectorAll('.tag-item');
        
        console.log('Tag Container Layout Debug:');
        console.log('Container width:', containerRect.width, 'px');
        console.log('Container height:', containerRect.height, 'px');
        console.log('Number of tags:', tags.length);
        
        let totalTagWidth = 0;
        tags.forEach((tag, index) => {
            const tagRect = tag.getBoundingClientRect();
            totalTagWidth += tagRect.width;
            console.log(`Tag ${index + 1} (${tag.querySelector('span').textContent}):`, tagRect.width, 'px wide');
        });
        
        console.log('Total tag width:', totalTagWidth, 'px');
        console.log('Available space:', containerRect.width - totalTagWidth - 20, 'px (accounting for padding and gaps)');
        console.log('---');
    }
})();