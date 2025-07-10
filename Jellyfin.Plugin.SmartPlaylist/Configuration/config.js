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
    const mediaTypes = [ { Value: "Movie", Label: "Movie" }, { Value: "Episode", Label: "Episode (TV Show)" }, { Value: "Audio", Label: "Audio (Music)" } ];

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

    function populateStaticSelects(page) {
         const sortOptions = [
            { Value: 'Name', Label: 'Name' },
            { Value: 'ProductionYear', Label: 'Production Year' },
            { Value: 'CommunityRating', Label: 'Community Rating' },
            { Value: 'DateCreated', Label: 'Date Created' },
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
            
            if (sortBySelect.children.length === 0) { populateSelect(sortBySelect, sortOptions, defaultSortBy); }
            if (sortOrderSelect.children.length === 0) { populateSelect(sortOrderSelect, orderOptions, defaultSortOrder); }
            page.querySelector('#playlistIsPublic').checked = defaultMakePublic;
            
            // Populate settings tab dropdowns with current configuration values
            const defaultSortBySetting = page.querySelector('#defaultSortBy');
            const defaultSortOrderSetting = page.querySelector('#defaultSortOrder');
            if (defaultSortBySetting && defaultSortBySetting.children.length === 0) { 
                populateSelect(defaultSortBySetting, sortOptions, defaultSortBy); 
            }
            if (defaultSortOrderSetting && defaultSortOrderSetting.children.length === 0) { 
                populateSelect(defaultSortOrderSetting, orderOptions, defaultSortOrder); 
            }
        }).catch(() => {
            if (sortBySelect.children.length === 0) { populateSelect(sortBySelect, sortOptions, 'Name'); }
            if (sortOrderSelect.children.length === 0) { populateSelect(sortOrderSelect, orderOptions, 'Ascending'); }
            page.querySelector('#playlistIsPublic').checked = false;
            
            // Populate settings tab dropdowns with defaults even if config fails
            const defaultSortBySetting = page.querySelector('#defaultSortBy');
            const defaultSortOrderSetting = page.querySelector('#defaultSortOrder');
            if (defaultSortBySetting && defaultSortBySetting.children.length === 0) { 
                populateSelect(defaultSortBySetting, sortOptions, 'Name'); 
            }
            if (defaultSortOrderSetting && defaultSortOrderSetting.children.length === 0) { 
                populateSelect(defaultSortOrderSetting, orderOptions, 'Ascending'); 
            }
        });
    }

    function updateOperatorOptions(fieldValue, operatorSelect) {
        operatorSelect.innerHTML = '<option value="">-- Select Operator --</option>';
        let allowedOperators = [];
        
        const listFields = ['People', 'Genres', 'Studios', 'Tags'];
        const numericFields = ['ProductionYear', 'CommunityRating', 'CriticRating', 'RuntimeMinutes', 'PlayCount'];
        const dateFields = ['DateCreated', 'DateLastRefreshed', 'DateLastSaved', 'DateModified'];
        const booleanFields = ['IsPlayed', 'IsFavorite'];
        const simpleFields = ['ItemType'];

        if (listFields.includes(fieldValue)) {
            allowedOperators = availableFields.Operators.filter(op => op.Value === 'Contains' || op.Value === 'NotContains' || op.Value === 'MatchRegex');
        } else if (numericFields.includes(fieldValue) || dateFields.includes(fieldValue)) {
            allowedOperators = availableFields.Operators.filter(op => op.Value !== 'Contains' && op.Value !== 'NotContains' && op.Value !== 'MatchRegex');
        } else if (booleanFields.includes(fieldValue) || simpleFields.includes(fieldValue)) {
            allowedOperators = availableFields.Operators.filter(op => op.Value === 'Equal' || op.Value === 'NotEqual');
        } else { // Default to string fields
            allowedOperators = availableFields.Operators.filter(op => op.Value === 'Equal' || op.Value === 'NotEqual' || op.Value === 'Contains' || op.Value === 'NotContains' || op.Value === 'MatchRegex');
        }

        allowedOperators.forEach(opt => {
            const option = document.createElement('option');
            option.value = opt.Value;
            option.textContent = opt.Label;
            operatorSelect.appendChild(option);
        });
        if (fieldValue === 'ItemType' || fieldValue === 'IsPlayed' || fieldValue === 'IsFavorite') { operatorSelect.value = 'Equal'; }
    }

    function setValueInput(fieldValue, valueContainer) {
        valueContainer.innerHTML = '';

        const numericFields = ['ProductionYear', 'CommunityRating', 'CriticRating', 'RuntimeMinutes', 'PlayCount'];
        const dateFields = ['DateCreated', 'DateLastRefreshed', 'DateLastSaved', 'DateModified'];
        const booleanFields = ['IsPlayed', 'IsFavorite'];
        const simpleFields = ['ItemType'];

        if (simpleFields.includes(fieldValue)) {
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
        } else if (booleanFields.includes(fieldValue)) {
            const select = document.createElement('select');
            select.className = 'emby-select rule-value-input';
            select.setAttribute('is', 'emby-select');
            select.style.width = '100%';
            let boolOptions;
            if (fieldValue === 'IsPlayed') {
                boolOptions = [ { Value: "true", Label: "Yes (Played)" }, { Value: "false", Label: "No (Unplayed)" } ];
            } else if (fieldValue === 'IsFavorite') {
                boolOptions = [ { Value: "true", Label: "Yes (Favorite)" }, { Value: "false", Label: "No (Not Favorite)" } ];
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
        } else if (numericFields.includes(fieldValue)) {
            const input = document.createElement('input');
            input.type = 'number';
            input.className = 'emby-input rule-value-input';
            input.placeholder = 'Value';
            input.style.width = '100%';
            valueContainer.appendChild(input);
        } else if (dateFields.includes(fieldValue)) {
            const input = document.createElement('input');
            input.type = 'date';
            input.className = 'emby-input rule-value-input';
            input.style.width = '100%';
            valueContainer.appendChild(input);
        }
        else {
            const input = document.createElement('input');
            input.type = 'text';
            input.className = 'emby-input rule-value-input';
            input.placeholder = 'Value';
            input.style.width = '100%';
            valueContainer.appendChild(input);
        }
    }
    
    function updateRegexHelp(ruleGroup) {
        const operatorSelect = ruleGroup.querySelector('.rule-operator-select');
        const existingHelp = ruleGroup.querySelector('.regex-help');
        if (existingHelp) existingHelp.remove();
        
        if (operatorSelect && operatorSelect.value === 'MatchRegex') {
            const helpDiv = document.createElement('div');
            helpDiv.className = 'regex-help field-description';
            helpDiv.style.cssText = 'margin-top: 0.5em; margin-bottom: 0.5em; font-size: 0.85em; color: #aaa; background: rgba(255,255,255,0.05); padding: 0.5em; border-radius: 3px;';
            helpDiv.innerHTML = '<strong>Regex Help:</strong> Use .NET syntax. Examples: <code>(?i)swe</code> (case-insensitive), <code>(?i)(eng|en)</code> (multiple options), <code>^Action</code> (starts with). Do not use JavaScript-style /pattern/flags.<br><strong>Test patterns:</strong> <a href="https://regex101.com/?flavor=dotnet" target="_blank" style="color: #00a4dc;">Regex101.com (.NET flavor)</a>';
            ruleGroup.appendChild(helpDiv);
        }
    }

    function createInitialLogicGroup(page) {
        const rulesContainer = page.querySelector('#rules-container');
        const logicGroupId = 'logic-group-' + Date.now();
        
        const logicGroupDiv = document.createElement('div');
        logicGroupDiv.className = 'logic-group';
        logicGroupDiv.setAttribute('data-group-id', logicGroupId);
        
        // Apply styles directly via JavaScript to bypass CSS specificity issues
        logicGroupDiv.style.cssText = `
            border: 2px solid #666 !important;
            border-radius: 8px !important;
            padding: 1.5em 1.5em 0.5em 1.5em !important;
            margin-bottom: 1em !important;
            background: rgba(255, 255, 255, 0.05) !important;
            box-shadow: 0 2px 8px rgba(0, 0, 0, 0.3) !important;
            position: relative !important;
        `;
        
        rulesContainer.appendChild(logicGroupDiv);
        
        // Add the first rule to this group
        addRuleToGroup(page, logicGroupDiv);
        
        return logicGroupDiv;
    }

    function addRuleToGroup(page, logicGroup) {
        const existingRules = logicGroup.querySelectorAll('.rule-row');
        
        // Add AND separator if this isn't the first rule in the group
        if (existingRules.length > 0) {
            const andSeparator = document.createElement('div');
            andSeparator.className = 'rule-within-group-separator';
            andSeparator.style.cssText = `
                text-align: center !important;
                margin: 0.8em 0 !important;
                color: #888 !important;
                font-size: 0.8em !important;
                font-weight: bold !important;
                position: relative !important;
                padding: 0.3em 0 !important;
            `;
            andSeparator.textContent = 'AND';
            
            // Add subtle line behind AND text
            const line = document.createElement('div');
            line.style.cssText = `
                position: absolute !important;
                top: 50% !important;
                left: 20% !important;
                right: 20% !important;
                height: 1px !important;
                background: rgba(136, 136, 136, 0.3) !important;
                z-index: 1 !important;
            `;
            andSeparator.appendChild(line);
            
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
            </div>`;
        
        ruleDiv.innerHTML = fieldsHtml;
        logicGroup.appendChild(ruleDiv);
        
        const newRuleRow = logicGroup.lastElementChild;
        const fieldSelect = newRuleRow.querySelector('.rule-field-select');
        const operatorSelect = newRuleRow.querySelector('.rule-operator-select');
        const valueContainer = newRuleRow.querySelector('.rule-value-container');

        if (availableFields.ContentFields) {
            populateSelect(fieldSelect, availableFields.ContentFields.concat(availableFields.RatingsPlaybackFields, availableFields.DateFields, availableFields.FileFields, availableFields.CollectionFields), null, false);
        }
        if (availableFields.Operators) {
            populateSelect(operatorSelect, availableFields.Operators, null, false);
        }

        setValueInput(fieldSelect.value, valueContainer);
        updateOperatorOptions(fieldSelect.value, operatorSelect);
        
        // Add event listeners with AbortController signal (if supported)
        const listenerOptions = getEventListenerOptions(signal);
        fieldSelect.addEventListener('change', function() {
            setValueInput(fieldSelect.value, valueContainer);
            updateOperatorOptions(fieldSelect.value, operatorSelect);
            updateRegexHelp(newRuleRow);
        }, listenerOptions);
        
        operatorSelect.addEventListener('change', function() {
            updateRegexHelp(newRuleRow);
        }, listenerOptions);

        // Style the action buttons
        const actionButtons = newRuleRow.querySelectorAll('.rule-action-btn');
        actionButtons.forEach(button => {
            if (button.classList.contains('and-btn')) {
                button.style.cssText = `
                    padding: 0.3em 0.8em !important;
                    font-size: 0.8em !important;
                    border: 1px solid #666 !important;
                    background: rgba(255, 255, 255, 0.1) !important;
                    color: #aaa !important;
                    border-radius: 4px !important;
                    cursor: pointer !important;
                    font-weight: 500 !important;
                `;
            } else if (button.classList.contains('or-btn')) {
                button.style.cssText = `
                    padding: 0.3em 0.8em !important;
                    font-size: 0.8em !important;
                    border: 1px solid #777 !important;
                    background: rgba(255, 255, 255, 0.15) !important;
                    color: #bbb !important;
                    border-radius: 4px !important;
                    cursor: pointer !important;
                    font-weight: 500 !important;
                `;
            } else if (button.classList.contains('delete-btn')) {
                button.style.cssText = `
                    padding: 0.3em 0.8em !important;
                    font-size: 0.8em !important;
                    border: 1px solid #888 !important;
                    background: rgba(255, 255, 255, 0.08) !important;
                    color: #999 !important;
                    border-radius: 4px !important;
                    cursor: pointer !important;
                    font-weight: 600 !important;
                    line-height: 1.2 !important;
                    display: inline-flex !important;
                    align-items: center !important;
                    justify-content: center !important;
                `;
            }
            
            // Add hover effects with AbortController signal (if supported)
            button.addEventListener('mouseenter', function() {
                if (this.classList.contains('delete-btn')) {
                    this.style.background = 'rgba(255, 100, 100, 0.2) !important';
                    this.style.borderColor = '#ff6464 !important';
                    this.style.color = '#ff6464 !important';
                } else {
                    this.style.background = 'rgba(255, 255, 255, 0.25) !important';
                    this.style.borderColor = '#aaa !important';
                }
            }, listenerOptions);
            
            button.addEventListener('mouseleave', function() {
                if (this.classList.contains('and-btn')) {
                    this.style.background = 'rgba(255, 255, 255, 0.1) !important';
                    this.style.borderColor = '#666 !important';
                    this.style.color = '#aaa !important';
                } else if (this.classList.contains('or-btn')) {
                    this.style.background = 'rgba(255, 255, 255, 0.15) !important';
                    this.style.borderColor = '#777 !important';
                    this.style.color = '#bbb !important';
                } else if (this.classList.contains('delete-btn')) {
                    this.style.background = 'rgba(255, 255, 255, 0.08) !important';
                    this.style.borderColor = '#888 !important';
                    this.style.color = '#999 !important';
                }
            }, listenerOptions);
        });

        // Update button visibility for all rules in all groups
        updateRuleButtonVisibility(page);
    }

    function addNewLogicGroup(page) {
        const rulesContainer = page.querySelector('#rules-container');
        
        // Add OR separator between groups
        const orSeparator = document.createElement('div');
        orSeparator.className = 'logic-group-separator';
        orSeparator.style.cssText = `
            text-align: center !important;
            margin: 1em 0 !important;
            position: relative !important;
        `;
        
        // Create OR text with styling
        const orText = document.createElement('span');
        orText.style.cssText = `
            background: #1a1a1a !important;
            color: #bbb !important;
            padding: 0.4em !important;
            border-radius: 4px !important;
            font-weight: bold !important;
            font-size: 0.9em !important;
            position: relative !important;
            z-index: 2 !important;
            display: inline-block !important;
            border: 1px solid #777 !important;
            box-shadow: 0 2px 6px rgba(0, 0, 0, 0.4) !important;
        `;
        orText.textContent = 'OR';
        orSeparator.appendChild(orText);
        
        // Add gradient line behind OR text
        const gradientLine = document.createElement('div');
        gradientLine.style.cssText = `
            position: absolute !important;
            top: 50% !important;
            left: 0 !important;
            right: 0 !important;
            height: 2px !important;
            background: linear-gradient(to right, transparent, #777, transparent) !important;
            z-index: 1 !important;
        `;
        orSeparator.appendChild(gradientLine);
        
        rulesContainer.appendChild(orSeparator);
        
        // Create new logic group
        const logicGroupId = 'logic-group-' + Date.now();
        const logicGroupDiv = document.createElement('div');
        logicGroupDiv.className = 'logic-group';
        logicGroupDiv.setAttribute('data-group-id', logicGroupId);
        
        // Apply styles directly via JavaScript to bypass CSS specificity issues
        logicGroupDiv.style.cssText = `
            border: 2px solid #666 !important;
            border-radius: 8px !important;
            padding: 1.5em 1.5em 0.5em 1.5em !important;
            margin-bottom: 1em !important;
            background: rgba(255, 255, 255, 0.05) !important;
            box-shadow: 0 2px 8px rgba(0, 0, 0, 0.3) !important;
            position: relative !important;
        `;
        
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
                    const targetValue = rule.querySelector('.rule-value-input').value;
                    if (memberName && operator && targetValue) {
                        expressions.push({ MemberName: memberName, Operator: operator, TargetValue: targetValue });
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
            const orderName = sortByValue + ' ' + sortOrderValue;
            const isPublic = page.querySelector('#playlistIsPublic').checked || false;
            const isEnabled = page.querySelector('#playlistIsEnabled').checked !== false; // Default to true if checkbox doesn't exist

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
                MediaTypes: selectedMediaTypes
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
        }).catch(() => {
            page.querySelector('#sortBy').value = 'Name';
            page.querySelector('#sortOrder').value = 'Ascending';
            page.querySelector('#playlistIsPublic').checked = false;
            page.querySelector('#playlistIsEnabled').checked = true; // Default to enabled
        });
        
        // Create initial logic group with one rule
        createInitialLogicGroup(page);
        
        // Update button visibility after initial group is created
        updateRuleButtonVisibility(page);
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

    function loadPlaylistList(page) {
        const apiClient = getApiClient();
        const container = page.querySelector('#playlist-list-container');
        
        // Prevent multiple simultaneous requests
        if (page._loadingPlaylists) {
            return;
        }
        page._loadingPlaylists = true;
        
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
                
                let html = '<div class="input-container">';
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
                    
                    let rulesHtml = '';
                    if (playlist.ExpressionSets && playlist.ExpressionSets.length > 0) {
                        playlist.ExpressionSets.forEach((expressionSet, groupIndex) => {
                            if (groupIndex > 0) {
                                rulesHtml += '<strong style="color: #888;">OR</strong><br>';
                            }
                            
                            if (expressionSet.Expressions && expressionSet.Expressions.length > 0) {
                                rulesHtml += '<div style="border: 1px solid #555; padding: 0.5em; margin: 0.25em 0; border-radius: 3px; background: rgba(255,255,255,0.02);">';
                                
                                expressionSet.Expressions.forEach((rule, ruleIndex) => {
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
                                        case 'GreaterThan': operator = '>'; break;
                                        case 'LessThan': operator = '<'; break;
                                        case 'GreaterThanOrEqual': operator = '>='; break;
                                        case 'LessThanOrEqual': operator = '<='; break;
                                        case 'MatchRegex': operator = 'matches regex'; break;
                                    }
                                    let value = rule.TargetValue;
                                    if (rule.MemberName === 'IsPlayed') { value = value === 'true' ? 'Yes (Played)' : 'No (Unplayed)'; }
                                    rulesHtml += '<span style="font-family: monospace; background: rgba(255,255,255,0.1); padding: 2px 2px; border-radius: 2px;">' + fieldName + ' ' + operator + ' "' + value + '"</span>';
                                });
                                
                                rulesHtml += '</div>';
                            }
                        });
                    } else {
                        rulesHtml = '<em>No rules defined</em>';
                    }
                    
                    html += '<div class="input-container" style="border: 1px solid #444; padding: 1em; border-radius: 4px; margin-bottom: 1.5em;">' +
                        '<h4 style="margin-top: 0;">' + playlist.Name + '</h4>' +
                        '<div class="field-description">' +
                        '<strong>File:</strong> ' + playlist.FileName + '<br>' +
                        '<strong>User:</strong> ' + userName + '<br>' +
                        '<strong>Media Types:</strong> ' + mediaTypes + '<br>' +
                        '<strong>Rules:</strong><br>' + rulesHtml + '<br>' +
                        '<strong>Sort:</strong> ' + sortName + '<br>' +
                        '<strong>Visibility:</strong> ' + isPublic + '<br>' +
                        '<strong>Status:</strong> <span style="color: ' + enabledStatusColor + '; font-weight: bold;">' + enabledStatus + '</span>' +
                        '</div>' +
                        '<div style="margin-top: 1em;">' +
                        '<button type="button" is="emby-button" class="emby-button raised edit-playlist-btn" data-playlist-id="' + playlistId + '" style="margin-right: 0.5em;">Edit</button>' +
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
                container.innerHTML = '<div class="input-container"><p>No smart playlists found.</p></div>';
            }
            page._loadingPlaylists = false;
        }).catch(err => {
            console.error('Error loading playlists:', err);
            let errorMessage = (err && err.message) ? err.message : 'Unknown error occurred.';
            container.innerHTML = '<div class="input-container"><p style="color: #ff6b6b;">' + errorMessage + '</p></div>';
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
        if (!searchInput || !page._allPlaylists) return;
        
        const searchTerm = searchInput.value.trim().toLowerCase();
        
        if (!searchTerm) {
            // No search term, show all playlists
            displayFilteredPlaylists(page, page._allPlaylists, '');
            return;
        }
        
        // Show loading state for user search
        const container = page.querySelector('#playlist-list-container');
        if (container) {
            container.innerHTML = '<div class="input-container"><p>Searching playlists...</p></div>';
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
                container.innerHTML = '<div class="input-container"><p>No playlists match your search criteria.</p></div>';
                return;
            }
            
            // Re-use the existing display logic but with filtered data
            displayFilteredPlaylists(page, filteredPlaylists, searchTerm);
            
        } catch (err) {
            console.error('Error during search:', err);
            container.innerHTML = '<div class="input-container"><p style="color: #ff6b6b;">Search error: ' + err.message + '</p></div>';
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
        
        let html = '<div class="input-container">';
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
            
            let rulesHtml = '';
            if (playlist.ExpressionSets && playlist.ExpressionSets.length > 0) {
                playlist.ExpressionSets.forEach((expressionSet, groupIndex) => {
                    if (groupIndex > 0) {
                        rulesHtml += '<strong style="color: #888;">OR</strong><br>';
                    }
                    
                    if (expressionSet.Expressions && expressionSet.Expressions.length > 0) {
                        rulesHtml += '<div style="border: 1px solid #555; padding: 0.5em; margin: 0.25em 0; border-radius: 3px; background: rgba(255,255,255,0.02);">';
                        
                        expressionSet.Expressions.forEach((rule, ruleIndex) => {
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
                                case 'GreaterThan': operator = '>'; break;
                                case 'LessThan': operator = '<'; break;
                                case 'GreaterThanOrEqual': operator = '>='; break;
                                case 'LessThanOrEqual': operator = '<='; break;
                                case 'MatchRegex': operator = 'matches regex'; break;
                            }
                            let value = rule.TargetValue;
                            if (rule.MemberName === 'IsPlayed') { value = value === 'true' ? 'Yes (Played)' : 'No (Unplayed)'; }
                            rulesHtml += '<span style="font-family: monospace; background: rgba(255,255,255,0.1); padding: 2px 2px; border-radius: 2px;">' + fieldName + ' ' + operator + ' "' + value + '"</span>';
                        });
                        
                        rulesHtml += '</div>';
                    }
                });
            } else {
                rulesHtml = '<em>No rules defined</em>';
            }
            
            html += '<div class="input-container" style="border: 1px solid #444; padding: 1em; border-radius: 4px; margin-bottom: 1.5em;">' +
                '<h4 style="margin-top: 0;">' + playlist.Name + '</h4>' +
                '<div class="field-description">' +
                '<strong>File:</strong> ' + playlist.FileName + '<br>' +
                '<strong>User:</strong> ' + userName + '<br>' +
                '<strong>Media Types:</strong> ' + mediaTypes + '<br>' +
                '<strong>Rules:</strong><br>' + rulesHtml + '<br>' +
                '<strong>Sort:</strong> ' + sortName + '<br>' +
                '<strong>Visibility:</strong> ' + isPublic + '<br>' +
                '<strong>Status:</strong> <span style="color: ' + enabledStatusColor + '; font-weight: bold;">' + enabledStatus + '</span>' +
                '</div>' +
                '<div style="margin-top: 1em;">' +
                '<button type="button" is="emby-button" class="emby-button raised edit-playlist-btn" data-playlist-id="' + playlistId + '" style="margin-right: 0.5em;">Edit</button>' +
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
            const action = deleteJellyfinPlaylist ? 'deleted' : 'configuration deleted and [Smart] suffix removed';
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

        // Force positioning with JavaScript since CSS isn't working reliably
        modalContainer.style.position = 'fixed';
        modalContainer.style.top = '50%';
        modalContainer.style.left = '50%';
        modalContainer.style.transform = 'translate(-50%, -50%)';
        modalContainer.style.zIndex = '10001';
        modalContainer.style.backgroundColor = '#101010';
        modalContainer.style.padding = '1.5em';
        modalContainer.style.width = '90%';
        modalContainer.style.maxWidth = '400px';

        // Style the modal backdrop
        modal.style.position = 'fixed';
        modal.style.top = '0';
        modal.style.left = '0';
        modal.style.width = '100%';
        modal.style.height = '100%';
        modal.style.backgroundColor = 'rgba(0,0,0,0.5)';
        modal.style.zIndex = '10000';

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
            
            // Debug logging to see what we received
            // console.log('Playlist data received:', playlist);
            // console.log('Playlist name:', playlist ? playlist.Name : 'playlist is null/undefined');
            // console.log('Playlist keys:', playlist ? Object.keys(playlist) : 'no keys');
            
            if (!playlist) {
                showNotification('No playlist data received from server.');
                return;
            }
            
            try {
                // Populate form with playlist data
                page.querySelector('#playlistName').value = playlist.Name || '';
                page.querySelector('#playlistIsPublic').checked = playlist.Public || false;
                page.querySelector('#playlistIsEnabled').checked = playlist.Enabled !== false; // Default to true for backward compatibility
                
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
                const parts = orderName.split(' ');
                const sortBy = parts.slice(0, -1).join(' ') || 'Name';
                const sortOrder = parts[parts.length - 1] || 'Ascending';
                
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
                                
                                // Update UI elements based on the loaded rule data
                                setValueInput(expression.MemberName, valueContainer);
                                updateOperatorOptions(expression.MemberName, operatorSelect);
                                
                                // Set operator and value
                                const valueInput = currentRule.querySelector('.rule-value-input');
                                operatorSelect.value = expression.Operator;
                                if (valueInput) {
                                    valueInput.value = expression.TargetValue;
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
            showNotification('Smart playlist refresh task has been triggered. Playlists will be updated shortly.', 'success');
        }).catch((err) => {
            Dashboard.hideLoadingMsg();
            console.error('Error refreshing playlists:', err);
            handleApiError(err, 'Failed to trigger playlist refresh');
        });
    }
    
    function initPage(page) {
        // Check if this specific page is already initialized
        if (page._pageInitialized) {
            return;
        }
        page._pageInitialized = true;
        
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
        tabSlider.style.overflowX = 'auto';
        tabSlider.style.overflowY = 'hidden';
        tabSlider.style.whiteSpace = 'nowrap';
        tabSlider.style.scrollbarWidth = 'thin';
        tabSlider.style.msOverflowStyle = 'auto';
        tabSlider.style.marginBottom = '1em';
        tabSlider.style.paddingBottom = '0.5em';
        tabSlider.style.position = 'relative';
        tabSlider.style.width = '100%';
        tabSlider.style.minHeight = '44px'; // ensure visible
        tabSlider.style.background = 'inherit';

        // Hide webkit scrollbar (best effort)
        tabSlider.style.setProperty('scrollbar-width', 'thin');
        tabSlider.style.setProperty('-webkit-scrollbar', 'display: none');

        // --- TAB BUTTON STYLES ---
        var tabButtons = tabSlider.querySelectorAll('.emby-tab-button');
        for (var i = 0; i < tabButtons.length; i++) {
            var button = tabButtons[i];
            button.style.display = 'inline-block';
            button.style.whiteSpace = 'nowrap';
            button.style.marginRight = (i < tabButtons.length - 1) ? '0.5em' : '0';
            button.style.flexShrink = '0';
            button.style.minWidth = 'auto';
            button.style.flex = 'none';
            button.style.minHeight = '40px';
            button.style.verticalAlign = 'middle';
        }

        // --- LISTENER LOGIC (unchanged) ---
        tabSlider._sliderListeners = [];
        function checkOverflow() {
            var existingIndicator = tabSlider.querySelector('.tab-overflow-indicator');
            if (existingIndicator) { existingIndicator.remove(); }
        }
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

    function setupEventListeners(page) {
        // Create AbortController for page event listeners
        const pageAbortController = createAbortController();
        const pageSignal = pageAbortController.signal;
        
        // Store controller on the page for cleanup
        page._pageAbortController = pageAbortController;
        
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
            var el = tabContents[i];
            el.style.maxWidth = '830px';
            el.style.boxSizing = 'border-box';
            el.style.paddingRight = '25px';
        }
    }

    function applyNotificationLayoutFix(page) {
        var notificationArea = page.querySelector('#plugin-notification-area');
        if (notificationArea) {
            notificationArea.style.maxWidth = '805px';
            notificationArea.style.marginRight = '25px';
            notificationArea.style.boxSizing = 'border-box';
        }
    }

})();