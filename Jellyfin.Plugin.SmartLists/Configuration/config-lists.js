(function (SmartLists) {
    'use strict';

    // Initialize namespace if it doesn't exist
    if (!SmartLists) {
        window.SmartLists = {};
        SmartLists = window.SmartLists;
    }

    // Cache for user ID to name lookups
    var userNameCache = new Map();

    // ===== USER MANAGEMENT =====
    // Note: loadUsers, loadUsersForRule, and setCurrentUserAsDefault are defined in config-api.js to avoid duplication

    SmartLists.resolveUsername = function(apiClient, playlist) {
        if (!playlist) {
            return Promise.resolve('Unknown User');
        }
        const userId = playlist.User;  // User field contains the user ID (as string)
        if (userId && userId !== '' && userId !== '00000000-0000-0000-0000-000000000000') {
            return SmartLists.resolveUserIdToName(apiClient, userId).then(function(name) {
                return name || 'Unknown User';
            });
        }
        return Promise.resolve('Unknown User');
    };
    
    SmartLists.resolveUserIdToName = function(apiClient, userId) {
        if (!userId || userId === '' || userId === '00000000-0000-0000-0000-000000000000') {
            return Promise.resolve(null);
        }
        
        // Check cache first
        if (userNameCache.has(userId)) {
            const cachedName = userNameCache.get(userId);
            return Promise.resolve(cachedName);
        }
        
        // Load all users and build cache if not already loaded
        return apiClient.ajax({
            type: 'GET',
            url: apiClient.getUrl(SmartLists.ENDPOINTS.users),
            contentType: 'application/json'
        }).then(function(response) {
            return response.json();
        }).then(function(users) {
            // Build cache from all users
            if (Array.isArray(users)) {
                users.forEach(function(user) {
                    if (user.Id && user.Name) {
                        userNameCache.set(user.Id, user.Name);
                    }
                });
            }
            
            // Return the requested user's name or fallback
            const resolvedName = userNameCache.get(userId) || 'Unknown User';
            return resolvedName;
        }).catch(function(err) {
            console.error('Error loading users for name resolution:', err);
            const fallback = 'Unknown User';
            
            // Cache the fallback too to avoid repeated failed lookups
            userNameCache.set(userId, fallback);
            return fallback;
        });
    };

    // ===== PLAYLIST CRUD OPERATIONS =====
    SmartLists.createPlaylist = function(page) {
        // Get edit state to determine if we're creating or updating
        const editState = SmartLists.getPageEditState(page);
        
        // Only scroll to top when creating new playlist (not when updating existing)
        if (!editState.editMode) {
            window.scrollTo({ top: 0, behavior: 'smooth' });
        }
        
        try {
            const apiClient = SmartLists.getApiClient();
            const playlistName = SmartLists.getElementValue(page, '#playlistName');

            // Get list type to provide appropriate error messages
            const listType = SmartLists.getElementValue(page, '#listType', 'Playlist');
            const isCollection = listType === 'Collection';
            const listTypeName = isCollection ? 'Collection' : 'Playlist';

            if (!playlistName) {
                SmartLists.showNotification(listTypeName + ' name is required.');
                return;
            }

            // Get selected media types early to gate series-only flags
            const selectedMediaTypes = SmartLists.getSelectedMediaTypes(page);
            if (selectedMediaTypes.length === 0) {
                SmartLists.showNotification('At least one media type must be selected.');
                return;
            }
            
            // Collect rules from form using helper function
            const expressionSets = SmartLists.collectRulesFromForm(page);

            // Collect sorting options from the new sort boxes
            const sortOptions = SmartLists.collectSortsFromForm(page);
            
            const isPublic = SmartLists.getElementChecked(page, '#playlistIsPublic', false);
            const isEnabled = SmartLists.getElementChecked(page, '#playlistIsEnabled', true); // Default to true
            const autoRefreshMode = SmartLists.getElementValue(page, '#autoRefreshMode', 'Never');
            
            // Collect schedules from the new schedule boxes
            const schedules = SmartLists.collectSchedulesFromForm(page);
            // Handle maxItems with validation using helper function
            const maxItemsInput = SmartLists.getElementValue(page, '#playlistMaxItems');
            let maxItems;
            if (maxItemsInput === '') {
                maxItems = 500;
            } else {
                const parsedValue = parseInt(maxItemsInput, 10);
                maxItems = isNaN(parsedValue) ? 500 : parsedValue;
            }

            // Handle maxPlayTimeMinutes with helper function
            const maxPlayTimeMinutesInput = SmartLists.getElementValue(page, '#playlistMaxPlayTimeMinutes');
            let maxPlayTimeMinutes;
            if (maxPlayTimeMinutesInput === '') {
                maxPlayTimeMinutes = 0;
            } else {
                const parsedValue = parseInt(maxPlayTimeMinutesInput, 10);
                maxPlayTimeMinutes = isNaN(parsedValue) ? 0 : parsedValue;
            }
            
            // Get selected user ID from dropdown (required for both playlists and collections)
            const userId = SmartLists.getElementValue(page, '#playlistUser');
            if (!userId) {
                SmartLists.showNotification('Please select a ' + (isCollection ? 'collection owner' : 'playlist owner') + '.');
                return;
            }
            
            // Collections are server-wide and don't have library assignments

            // Collect similarity comparison fields from SimilarTo rules
            let similarityComparisonFields = null;
            const allRules = page.querySelectorAll('.rule-row');
            for (var i = 0; i < allRules.length; i++) {
                const ruleRow = allRules[i];
                const fieldSelect = ruleRow.querySelector('.rule-field-select');
                if (fieldSelect && fieldSelect.value === 'SimilarTo') {
                    const fields = SmartLists.getSimilarityComparisonFields(ruleRow);
                    if (fields && fields.length > 0) {
                        similarityComparisonFields = fields;
                        break; // Use the first SimilarTo rule's settings for the entire playlist
                    }
                }
            }

            const playlistDto = {
                Type: listType,
                Name: playlistName,
                ExpressionSets: expressionSets,
                Order: { SortOptions: sortOptions },
                Enabled: isEnabled,
                MediaTypes: selectedMediaTypes,
                MaxItems: maxItems,
                MaxPlayTimeMinutes: maxPlayTimeMinutes,
                AutoRefresh: autoRefreshMode,
                Schedules: schedules.length > 0 ? schedules : []
            };
            
            // Add type-specific fields
            playlistDto.User = userId;  // User field is shared by both playlists and collections
            
            if (isCollection) {
                // Collections are server-wide, no library assignment needed
            } else {
                playlistDto.Public = isPublic;
            }
            
            // Add similarity comparison fields if specified
            if (similarityComparisonFields) {
                playlistDto.SimilarityComparisonFields = similarityComparisonFields;
            }

            // Add ID if in edit mode (reuse editState from top of function)
            if (editState.editMode && editState.editingPlaylistId) {
                playlistDto.Id = editState.editingPlaylistId;
            }

            Dashboard.showLoadingMsg();
            
            const requestType = editState.editMode ? 'PUT' : 'POST';
            const url = editState.editMode ? 
                apiClient.getUrl(SmartLists.ENDPOINTS.base + '/' + editState.editingPlaylistId) : 
                apiClient.getUrl(SmartLists.ENDPOINTS.base);
            
            apiClient.ajax({
                type: requestType,
                url: url,
                data: JSON.stringify(playlistDto),
                contentType: 'application/json'
            }).then(function() {
                Dashboard.hideLoadingMsg();
                const message = editState.editMode ? 
                    listTypeName + ' "' + playlistName + '" updated successfully.' : 
                    listTypeName + ' "' + playlistName + '" created. The ' + listTypeName.toLowerCase() + ' has been generated.';
                SmartLists.showNotification(message, 'success');
                
                // Exit edit mode and clear form
                if (editState.editMode) {
                    // Exit edit mode silently without showing cancellation message
                    SmartLists.setPageEditState(page, false, null);
                    const editIndicator = page.querySelector('#edit-mode-indicator');
                    if (editIndicator) {
                        editIndicator.style.display = 'none';
                    }
                    const submitBtn = page.querySelector('#submitBtn');
                    if (submitBtn) {
                        const currentListType = SmartLists.getElementValue(page, '#listType', 'Playlist');
                        submitBtn.textContent = 'Create ' + currentListType;
                    }
                    
                    // Restore tab button text
                    const createTabButton = page.querySelector('a[data-tab="create"]');
                    if (createTabButton) {
                        createTabButton.textContent = 'Create List';
                    }
                    
                    // Switch to Manage tab and scroll to top after successful update (auto for instant behavior)
                    SmartLists.switchToTab(page, 'manage');
                    window.scrollTo({ top: 0, behavior: 'auto' });
                }
                SmartLists.clearForm(page);
            }).catch(function(err) {
                Dashboard.hideLoadingMsg();
                console.error('Error creating ' + listTypeName.toLowerCase() + ':', err);
                const action = editState.editMode ? 'update' : 'create';
                SmartLists.handleApiError(err, 'Failed to ' + action + ' ' + listTypeName.toLowerCase() + ' ' + playlistName);
            });
        } catch (e) {
            Dashboard.hideLoadingMsg();
            console.error('A synchronous error occurred in createPlaylist:', e);
            SmartLists.showNotification('A critical client-side error occurred: ' + e.message);
        }
    };
    
    SmartLists.clearForm = function(page) {
        // Only handle form clearing - edit mode management should be done by caller
        
        SmartLists.setElementValue(page, '#playlistName', '');
        
        // Clean up all existing event listeners before clearing rules
        const rulesContainer = page.querySelector('#rules-container');
        const allRules = rulesContainer.querySelectorAll('.rule-row');
        allRules.forEach(function(rule) {
            SmartLists.cleanupRuleEventListeners(rule);
        });
        
        rulesContainer.innerHTML = '';
        
        // Clear media type selections
        const mediaTypesSelect = page.querySelectorAll('.media-type-checkbox');
        mediaTypesSelect.forEach(function(checkbox) {
            checkbox.checked = false;
        });
        
        const apiClient = SmartLists.getApiClient();
        apiClient.getPluginConfiguration(SmartLists.getPluginId()).then(function(config) {
            // Set default list type
            SmartLists.setElementValue(page, '#listType', config.DefaultListType || 'Playlist');
            
            // Trigger type change handler to show/hide relevant fields
            SmartLists.handleListTypeChange(page);
            
            SmartLists.setElementChecked(page, '#playlistIsPublic', config.DefaultMakePublic || false);
            SmartLists.setElementChecked(page, '#playlistIsEnabled', true); // Default to enabled
            const defaultMaxItems = config.DefaultMaxItems !== undefined && config.DefaultMaxItems !== null ? config.DefaultMaxItems : 500;
            SmartLists.setElementValue(page, '#playlistMaxItems', defaultMaxItems);
            const defaultMaxPlayTimeMinutes = config.DefaultMaxPlayTimeMinutes !== undefined && config.DefaultMaxPlayTimeMinutes !== null ? config.DefaultMaxPlayTimeMinutes : 0;
            SmartLists.setElementValue(page, '#playlistMaxPlayTimeMinutes', defaultMaxPlayTimeMinutes);
            SmartLists.setElementValue(page, '#autoRefreshMode', config.DefaultAutoRefresh || 'OnLibraryChanges');
            
            // Reinitialize schedule system with defaults
            SmartLists.initializeScheduleSystem(page);
            
            // Reinitialize sort system with defaults
            SmartLists.initializeSortSystem(page);
            const sortsContainer = page.querySelector('#sorts-container');
            if (sortsContainer && sortsContainer.querySelectorAll('.sort-box').length === 0) {
                SmartLists.addSortBox(page, { SortBy: config.DefaultSortBy || 'Name', SortOrder: config.DefaultSortOrder || 'Ascending' });
            }
            
            // Reset user dropdown to currently logged-in user
            const userSelect = page.querySelector('#playlistUser');
            if (userSelect) {
                userSelect.value = '';
                SmartLists.setCurrentUserAsDefault(page);
            }
        }).catch(function() {
            SmartLists.setElementChecked(page, '#playlistIsPublic', false);
            SmartLists.setElementChecked(page, '#playlistIsEnabled', true); // Default to enabled
            SmartLists.setElementValue(page, '#playlistMaxItems', 500);
            SmartLists.setElementValue(page, '#playlistMaxPlayTimeMinutes', 0);
            SmartLists.setElementValue(page, '#autoRefreshMode', 'OnLibraryChanges');
            
            // Reinitialize schedule system with fallback defaults
            SmartLists.initializeScheduleSystem(page);
            
            // Reinitialize sort system with fallback defaults
            SmartLists.initializeSortSystem(page);
            SmartLists.addSortBox(page, { SortBy: 'Name', SortOrder: 'Ascending' });
        });
        
        // Create initial logic group with one rule
        SmartLists.createInitialLogicGroup(page);
        
        // Update button visibility after initial group is created
        SmartLists.updateRuleButtonVisibility(page);
    };

    SmartLists.editPlaylist = function(page, playlistId) {
        const apiClient = SmartLists.getApiClient();
        Dashboard.showLoadingMsg();
        
        // Always scroll to top when entering edit mode (auto for instant behavior)
        window.scrollTo({ top: 0, behavior: 'auto' });
                
        apiClient.ajax({
            type: 'GET',
            url: apiClient.getUrl(SmartLists.ENDPOINTS.base + '/' + playlistId),
            contentType: 'application/json'
        }).then(function(response) {
            if (!response.ok) {
                throw new Error('HTTP ' + response.status + ': ' + response.statusText);
            }
            return response.json();
        }).then(function(playlist) {
            Dashboard.hideLoadingMsg();
            
            if (!playlist) {
                SmartLists.showNotification('No playlist data received from server.');
                return;
            }
            
            try {
                // Determine list type
                const listType = playlist.Type || 'Playlist';
                const isCollection = listType === 'Collection';
                
                // Set list type
                SmartLists.setElementValue(page, '#listType', listType);
                
                // Trigger type change handler to show/hide fields
                SmartLists.handleListTypeChange(page);
                
                // Populate form with playlist data using helper functions
                SmartLists.setElementValue(page, '#playlistName', playlist.Name || '');
                
                // Only set public for playlists
                if (!isCollection) {
                    SmartLists.setElementChecked(page, '#playlistIsPublic', playlist.Public || false);
                }
                
                SmartLists.setElementChecked(page, '#playlistIsEnabled', playlist.Enabled !== false); // Default to true for backward compatibility
                
                // Handle AutoRefresh with backward compatibility
                const autoRefreshValue = playlist.AutoRefresh !== undefined ? playlist.AutoRefresh : 'Never';
                const autoRefreshElement = page.querySelector('#autoRefreshMode');
                if (autoRefreshElement) {
                    autoRefreshElement.value = autoRefreshValue;
                }
                
                // Handle schedule settings with backward compatibility
                SmartLists.loadSchedulesIntoUI(page, playlist);
                
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
                    console.warn('Max Playtime Minutes element not found when trying to populate edit form');
                }
                
                // Set media types
                // Set flag to skip change event handlers while we programmatically set checkbox states
                page._skipMediaTypeChangeHandlers = true;
                
                const mediaTypesSelect = Array.from(page.querySelectorAll('.media-type-checkbox'));
                if (playlist.MediaTypes && playlist.MediaTypes.length > 0) {
                    playlist.MediaTypes.forEach(function(type) {
                        const checkbox = mediaTypesSelect.find(function(cb) {
                            return cb.value === type;
                        });
                        if (checkbox) {
                            checkbox.checked = true;
                        }
                    });
                }
                
                // Clear flag to re-enable change event handlers
                page._skipMediaTypeChangeHandlers = false;
                
                // Set the list owner (for both playlists and collections)
                // Convert User to string if it's not already (handles both Guid and string formats)
                const userIdString = playlist.User ? String(playlist.User) : null;
                if (userIdString) {
                    SmartLists.setUserIdValueWithRetry(page, userIdString);
                }
                
                // Clear existing rules (applies to both playlists and collections)
                const rulesContainer = page.querySelector('#rules-container');
                rulesContainer.innerHTML = '';
                
                // Populate logic groups and rules
                if (playlist.ExpressionSets && playlist.ExpressionSets.length > 0 && 
                    playlist.ExpressionSets.some(function(es) { return es.Expressions && es.Expressions.length > 0; })) {
                    playlist.ExpressionSets.forEach(function(expressionSet, groupIndex) {
                        let logicGroup;
                        
                        if (groupIndex === 0) {
                            // Create first logic group
                            logicGroup = SmartLists.createInitialLogicGroup(page);
                            // Remove only the rules, preserve the label
                            const rulesToRemove = logicGroup.querySelectorAll('.rule-row, .rule-within-group-separator');
                            rulesToRemove.forEach(function(rule) {
                                rule.remove();
                            });
                        } else {
                            // Add subsequent logic groups
                            logicGroup = SmartLists.addNewLogicGroup(page);
                            // Remove only the rules, preserve the label
                            const rulesToRemove = logicGroup.querySelectorAll('.rule-row, .rule-within-group-separator');
                            rulesToRemove.forEach(function(rule) {
                                rule.remove();
                            });
                        }
                        
                        // Add rules to this logic group
                        if (expressionSet.Expressions && expressionSet.Expressions.length > 0) {
                            expressionSet.Expressions.forEach(function(expression) {
                                SmartLists.addRuleToGroup(page, logicGroup);
                                const ruleRows = logicGroup.querySelectorAll('.rule-row');
                                const currentRule = ruleRows[ruleRows.length - 1];
                                
                                const fieldSelect = currentRule.querySelector('.rule-field-select');
                                const operatorSelect = currentRule.querySelector('.rule-operator-select');
                                const valueContainer = currentRule.querySelector('.rule-value-container');
                                
                                // Check if this is a people field (but not "People" itself)
                                const isPeopleSubFieldValue = SmartLists.isPeopleSubField(expression.MemberName);
                                const actualMemberName = expression.MemberName;
                                
                                if (isPeopleSubFieldValue) {
                                    // Set field select to "People" and submenu to the actual field
                                    fieldSelect.value = 'People';
                                    // Update operator options using the actual member name
                                    SmartLists.updateOperatorOptions(actualMemberName, operatorSelect);
                                } else {
                                    fieldSelect.value = expression.MemberName;
                                    // Update operator options first
                                    SmartLists.updateOperatorOptions(expression.MemberName, operatorSelect);
                                }
                                
                                // Set operator so setValueInput knows what type of input to create
                                operatorSelect.value = expression.Operator;
                                
                                // Update UI elements based on the loaded rule data
                                // Pass the operator and current value to ensure correct input type is created
                                SmartLists.setValueInput(actualMemberName, valueContainer, expression.Operator, expression.TargetValue);
                                SmartLists.updateUserSelectorVisibility(currentRule, actualMemberName);
                                SmartLists.updateNextUnwatchedOptionsVisibility(currentRule, actualMemberName, page);
                                SmartLists.updateCollectionsOptionsVisibility(currentRule, actualMemberName, page);
                                SmartLists.updateTagsOptionsVisibility(currentRule, actualMemberName, page);
                                SmartLists.updateStudiosOptionsVisibility(currentRule, actualMemberName, page);
                                SmartLists.updateGenresOptionsVisibility(currentRule, actualMemberName, page);
                                // Pass the playlist's saved similarity fields (if any) so they're loaded correctly
                                SmartLists.updateSimilarityOptionsVisibility(currentRule, actualMemberName, playlist.SimilarityComparisonFields);
                                
                                // Handle user-specific rules
                                if (expression.UserId) {
                                    const userSelect = currentRule.querySelector('.rule-user-select');
                                    if (userSelect) {
                                        // Ensure options are loaded before setting the value
                                        SmartLists.loadUsersForRule(userSelect, true).then(function() {
                                            userSelect.value = expression.UserId;
                                        }).catch(function() {
                                            // Fallback: set value anyway in case of error
                                            userSelect.value = expression.UserId;
                                        });
                                    }
                                }
                                
                                // Handle people submenu if this is a people field
                                if (isPeopleSubFieldValue) {
                                    SmartLists.updatePeopleOptionsVisibility(currentRule, 'People');
                                    const peopleSelect = currentRule.querySelector('.rule-people-select');
                                    if (peopleSelect) {
                                        // Wait for options to be populated, then set the value
                                        setTimeout(function() {
                                            peopleSelect.value = actualMemberName;
                                        }, 0);
                                    }
                                } else {
                                    SmartLists.updatePeopleOptionsVisibility(currentRule, fieldSelect.value);
                                }
                                
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
                                    }
                                }
                                
                                // Restore per-field option selects for edit flows
                                // Note: These must be set AFTER updateCollectionsOptionsVisibility/updateTagsOptionsVisibility
                                // to ensure the options divs are visible
                                if (expression.MemberName === 'NextUnwatched') {
                                    const nextUnwatchedSelect = currentRule.querySelector('.rule-nextunwatched-select');
                                    if (nextUnwatchedSelect) {
                                        const includeValue = expression.IncludeUnwatchedSeries !== false ? 'true' : 'false';
                                        nextUnwatchedSelect.value = includeValue;
                                    }
                                }
                                if (expression.MemberName === 'Collections') {
                                    const collectionsSelect = currentRule.querySelector('.rule-collections-select');
                                    if (collectionsSelect) {
                                        const includeValue = expression.IncludeEpisodesWithinSeries === true ? 'true' : 'false';
                                        collectionsSelect.value = includeValue;
                                    }
                                }
                                if (expression.MemberName === 'Tags') {
                                    const tagsSelect = currentRule.querySelector('.rule-tags-select');
                                    if (tagsSelect) {
                                        const includeValue = expression.IncludeParentSeriesTags === true ? 'true' : 'false';
                                        tagsSelect.value = includeValue;
                                    }
                                }
                                if (expression.MemberName === 'Studios') {
                                    const studiosSelect = currentRule.querySelector('.rule-studios-select');
                                    if (studiosSelect) {
                                        const includeValue = expression.IncludeParentSeriesStudios === true ? 'true' : 'false';
                                        studiosSelect.value = includeValue;
                                    }
                                }
                                if (expression.MemberName === 'Genres') {
                                    const genresSelect = currentRule.querySelector('.rule-genres-select');
                                    if (genresSelect) {
                                        const includeValue = expression.IncludeParentSeriesGenres === true ? 'true' : 'false';
                                        genresSelect.value = includeValue;
                                    }
                                }
                                
                                // Update regex help if needed
                                SmartLists.updateRegexHelp(currentRule);
                            });
                        }
                    });
                } else {
                    // No rules exist - create an initial logic group with a placeholder rule
                    // This matches the behavior when creating a new playlist
                    SmartLists.createInitialLogicGroup(page);
                }
                
                // Set sort options AFTER rules are populated so hasSimilarToRuleInForm() can detect them
                SmartLists.loadSortOptionsIntoUI(page, playlist);
                // Update sort options visibility based on populated rules
                SmartLists.updateAllSortOptionsVisibility(page);
                
                // Update field selects first, then per-field options visibility based on selected media types
                SmartLists.updateAllFieldSelects(page);
                SmartLists.updateAllTagsOptionsVisibility(page);
                SmartLists.updateAllStudiosOptionsVisibility(page);
                SmartLists.updateAllGenresOptionsVisibility(page);
                SmartLists.updateAllCollectionsOptionsVisibility(page);
                SmartLists.updateAllNextUnwatchedOptionsVisibility(page);
                
                // Update button visibility
                SmartLists.updateRuleButtonVisibility(page);
                
                // Set edit mode state
                SmartLists.setPageEditState(page, true, playlistId);
                
                // Update UI to show edit mode
                const editIndicator = page.querySelector('#edit-mode-indicator');
                if (editIndicator) {
                    editIndicator.style.display = 'block';
                }
                const submitBtn = page.querySelector('#submitBtn');
                if (submitBtn) {
                    const currentListType = SmartLists.getElementValue(page, '#listType', 'Playlist');
                    submitBtn.textContent = 'Update ' + currentListType;
                }
                
                // Update tab button text
                const createTabButton = page.querySelector('a[data-tab="create"]');
                if (createTabButton) {
                    createTabButton.textContent = 'Edit List';
                }
                
                // Switch to Create tab to show edit form
                SmartLists.switchToTab(page, 'create');
                
            } catch (formError) {
                console.error('Error populating form for edit:', formError);
                SmartLists.showNotification('Error loading playlist data for editing: ' + formError.message);
            }
        }).catch(function(err) {
            Dashboard.hideLoadingMsg();
            console.error('Error loading playlist for edit:', err);
            SmartLists.handleApiError(err, 'Failed to load playlist for editing');
        });
    };

    SmartLists.clonePlaylist = function(page, playlistId, playlistName) {
        const apiClient = SmartLists.getApiClient();
        Dashboard.showLoadingMsg();
        
        // Always scroll to top when entering clone mode (auto for instant behavior)
        window.scrollTo({ top: 0, behavior: 'auto' });
                
        apiClient.ajax({
            type: 'GET',
            url: apiClient.getUrl(SmartLists.ENDPOINTS.base + '/' + playlistId),
            contentType: 'application/json'
        }).then(function(response) {
            if (!response.ok) {
                throw new Error('HTTP ' + response.status + ': ' + response.statusText);
            }
            return response.json();
        }).then(function(playlist) {
            Dashboard.hideLoadingMsg();
            
            if (!playlist) {
                SmartLists.showNotification('No playlist data received from server.');
                return;
            }
            
            try {
                // Determine list type
                const listType = playlist.Type || 'Playlist';
                const isCollection = listType === 'Collection';
                
                // Set playlist name FIRST (before switchToTab) to prevent populateFormDefaults from being called
                // switchToTab checks if name is empty and calls populateFormDefaults if so, which would regenerate checkboxes
                SmartLists.setElementValue(page, '#playlistName', (playlist.Name || '') + ' (Copy)');
                
                // Switch to Create tab
                SmartLists.switchToTab(page, 'create');
                
                // Clear any existing edit state
                SmartLists.setPageEditState(page, false, null);
                
                // Set list type
                SmartLists.setElementValue(page, '#listType', listType);
                
                // Set flag to prevent media type change handlers from interfering during cloning setup
                page._skipMediaTypeChangeHandlers = true;
                
                // Trigger type change handler to show/hide fields
                SmartLists.handleListTypeChange(page);
                
                // Only set public for playlists
                if (!isCollection) {
                    SmartLists.setElementChecked(page, '#playlistIsPublic', playlist.Public || false);
                }
                
                SmartLists.setElementChecked(page, '#playlistIsEnabled', playlist.Enabled !== false);
                
                // Handle AutoRefresh
                const autoRefreshValue = playlist.AutoRefresh !== undefined ? playlist.AutoRefresh : 'Never';
                const autoRefreshElement = page.querySelector('#autoRefreshMode');
                if (autoRefreshElement) {
                    autoRefreshElement.value = autoRefreshValue;
                }
                
                // Handle schedule settings with backward compatibility (same as editPlaylist)
                SmartLists.loadSchedulesIntoUI(page, playlist);
                
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
                
                // Store media types to set later (after all updates are complete)
                const clonedMediaTypes = playlist.MediaTypes && playlist.MediaTypes.length > 0 ? playlist.MediaTypes : [];
                
                // Set the list owner (for both playlists and collections)
                const userIdString = playlist.User ? String(playlist.User) : null;
                if (userIdString) {
                    SmartLists.setUserIdValueWithRetry(page, userIdString);
                }
                
                // Clear existing rules and populate with cloned rules (applies to both playlists and collections)
                const rulesContainer = page.querySelector('#rules-container');
                if (rulesContainer) {
                    rulesContainer.innerHTML = '';
                }
                
                // Populate rules from cloned playlist
                if (playlist.ExpressionSets && playlist.ExpressionSets.length > 0) {
                    playlist.ExpressionSets.forEach(function(expressionSet, setIndex) {
                        const logicGroup = setIndex === 0 ? SmartLists.createInitialLogicGroup(page) : SmartLists.addNewLogicGroup(page);
                        
                        if (expressionSet.Expressions && expressionSet.Expressions.length > 0) {
                            expressionSet.Expressions.forEach(function(expression, expIndex) {
                                if (expIndex === 0) {
                                    // Use the first rule row that's already in the group
                                    const firstRuleRow = logicGroup.querySelector('.rule-row');
                                    if (firstRuleRow) {
                                        SmartLists.populateRuleRow(firstRuleRow, expression, page);
                                        // Restore similarity field selections when cloning
                                        if (expression.MemberName === 'SimilarTo') {
                                            SmartLists.updateSimilarityOptionsVisibility(firstRuleRow, expression.MemberName, playlist.SimilarityComparisonFields);
                                        }
                                    }
                                } else {
                                    // Add additional rule rows
                                    SmartLists.addRuleToGroup(page, logicGroup);
                                    const newRuleRow = logicGroup.querySelector('.rule-row:last-child');
                                    if (newRuleRow) {
                                        SmartLists.populateRuleRow(newRuleRow, expression, page);
                                        // Restore similarity field selections when cloning
                                        if (expression.MemberName === 'SimilarTo') {
                                            SmartLists.updateSimilarityOptionsVisibility(newRuleRow, expression.MemberName, playlist.SimilarityComparisonFields);
                                        }
                                    }
                                }
                            });
                        }
                    });
                } else {
                    // If no rules, create initial empty group
                    SmartLists.createInitialLogicGroup(page);
                }
                
                // Update button visibility
                SmartLists.updateRuleButtonVisibility(page);
                
                // Update field selects first, then per-field options visibility based on selected media types
                SmartLists.updateAllFieldSelects(page);
                SmartLists.updateAllTagsOptionsVisibility(page);
                SmartLists.updateAllStudiosOptionsVisibility(page);
                SmartLists.updateAllGenresOptionsVisibility(page);
                SmartLists.updateAllCollectionsOptionsVisibility(page);
                SmartLists.updateAllNextUnwatchedOptionsVisibility(page);
                
                // Set sort options AFTER rules are populated so hasSimilarToRuleInForm() can detect them
                SmartLists.loadSortOptionsIntoUI(page, playlist);
                // Update sort options visibility based on populated rules
                SmartLists.updateAllSortOptionsVisibility(page);
                
                // Set media types AFTER all field updates are complete to prevent them from being cleared
                // Flag was already set at the beginning of clone process to prevent interference
                const mediaTypesCheckboxes = Array.from(page.querySelectorAll('.media-type-checkbox'));
                
                // First clear all checkboxes
                mediaTypesCheckboxes.forEach(function(checkbox) {
                    checkbox.checked = false;
                });
                // Then set the ones from the cloned playlist
                if (clonedMediaTypes.length > 0) {
                    clonedMediaTypes.forEach(function(type) {
                        const checkbox = mediaTypesCheckboxes.find(function(cb) {
                            return cb.value === type;
                        });
                        if (checkbox) {
                            checkbox.checked = true;
                        }
                    });
                }
                
                // Clear flag to re-enable change event handlers
                page._skipMediaTypeChangeHandlers = false;
                
                // Clear any pending media type update timers just in case
                if (page._mediaTypeUpdateTimer) {
                    clearTimeout(page._mediaTypeUpdateTimer);
                    page._mediaTypeUpdateTimer = null;
                }
                
                // Show success message
                SmartLists.showNotification('Playlist "' + playlistName + '" cloned successfully! You can now modify and create the new playlist.', 'success');
                
            } catch (formError) {
                console.error('Error populating form for clone:', formError);
                SmartLists.showNotification('Error loading playlist data for cloning: ' + formError.message);
            }
        }).catch(function(err) {
            Dashboard.hideLoadingMsg();
            console.error('Error loading playlist for clone:', err);
            SmartLists.handleApiError(err, 'Failed to load playlist for cloning');
        });
    };

    SmartLists.cancelEdit = function(page) {
        SmartLists.setPageEditState(page, false, null);
        
        // Update UI to show create mode
        const editIndicator = page.querySelector('#edit-mode-indicator');
        if (editIndicator) {
            editIndicator.style.display = 'none';
        }
        const submitBtn = page.querySelector('#submitBtn');
        if (submitBtn) {
            submitBtn.textContent = 'Create Playlist';
        }
        
        // Restore tab button text
        const createTabButton = page.querySelector('a[data-tab="create"]');
        if (createTabButton) {
            createTabButton.textContent = 'Create Playlist';
        }
        
        // Clear form
        SmartLists.clearForm(page);
        
        // Switch to Manage tab after canceling edit
        SmartLists.switchToTab(page, 'manage');
        window.scrollTo({ top: 0, behavior: 'auto' });
        
        SmartLists.showNotification('Edit mode cancelled.', 'success');
    };

    SmartLists.refreshPlaylist = function(playlistId, playlistName) {
        const apiClient = SmartLists.getApiClient();
        
        Dashboard.showLoadingMsg();
        
        apiClient.ajax({
            type: 'POST',
            url: apiClient.getUrl(SmartLists.ENDPOINTS.base + '/' + playlistId + '/refresh'),
            contentType: 'application/json'
        }).then(function() {
            Dashboard.hideLoadingMsg();
            SmartLists.showNotification('Playlist "' + playlistName + '" has been refreshed successfully.', 'success');
            
            // Auto-refresh the playlist list to show updated LastRefreshed timestamp
            const page = document.querySelector('.SmartListsConfigurationPage');
            if (page) {
                SmartLists.loadPlaylistList(page);
            }
        }).catch(function(err) {
            Dashboard.hideLoadingMsg();
            
            // Enhanced error handling for API responses
            let errorMessage = 'An unexpected error occurred, check the logs for more details.';
            
            // Check if this is a Response object (from fetch API)
            if (err && typeof err.json === 'function') {
                err.json().then(function(errorData) {
                    if (errorData.message) {
                        errorMessage = errorData.message;
                    } else if (typeof errorData === 'string') {
                        errorMessage = errorData;
                    }
                }).catch(function() {
                    // If JSON parsing fails, try to get text
                    err.text().then(function(textContent) {
                        if (textContent) {
                            errorMessage = textContent;
                        }
                    }).catch(function() {
                        // Ignore text extraction errors
                    });
                });
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
            
            const fullMessage = 'Failed to refresh playlist "' + playlistName + '": ' + errorMessage;
            console.error('Playlist refresh error:', fullMessage, err);
            SmartLists.showNotification(fullMessage, 'error');
        });
    };

    SmartLists.deletePlaylist = function(page, playlistId, playlistName) {
        const apiClient = SmartLists.getApiClient();
        const deleteJellyfinPlaylist = page.querySelector('#delete-jellyfin-playlist-checkbox').checked;
        
        Dashboard.showLoadingMsg();
        apiClient.ajax({
            type: 'DELETE',
            url: apiClient.getUrl(SmartLists.ENDPOINTS.base + '/' + playlistId + '?deleteJellyfinPlaylist=' + deleteJellyfinPlaylist),
            contentType: 'application/json'
        }).then(function() {
            Dashboard.hideLoadingMsg();
            const action = deleteJellyfinPlaylist ? 'deleted' : 'suffix/prefix removed (if any) and configuration deleted';
            SmartLists.showNotification('Playlist "' + playlistName + '" ' + action + ' successfully.', 'success');
            SmartLists.loadPlaylistList(page);
        }).catch(function(err) {
            Dashboard.hideLoadingMsg();
            SmartLists.displayApiError(err, 'Failed to delete playlist "' + playlistName + '"');
        });
    };

    SmartLists.enablePlaylist = function(page, playlistId, playlistName) {
        const apiClient = SmartLists.getApiClient();
        
        Dashboard.showLoadingMsg();
        apiClient.ajax({
            type: 'POST',
            url: apiClient.getUrl(SmartLists.ENDPOINTS.base + '/' + playlistId + '/enable'),
            contentType: 'application/json'
        }).then(function(response) {
            if (!response.ok) {
                throw new Error('HTTP ' + response.status + ': ' + response.statusText);
            }
            return response.json();
        }).then(function(result) {
            Dashboard.hideLoadingMsg();
            SmartLists.showNotification(result.message || 'Playlist "' + playlistName + '" has been enabled.', 'success');
            SmartLists.loadPlaylistList(page);
        }).catch(function(err) {
            Dashboard.hideLoadingMsg();
            SmartLists.displayApiError(err, 'Failed to enable playlist "' + playlistName + '"');
        });
    };

    SmartLists.disablePlaylist = function(page, playlistId, playlistName) {
        const apiClient = SmartLists.getApiClient();
        
        Dashboard.showLoadingMsg();
        apiClient.ajax({
            type: 'POST',
            url: apiClient.getUrl(SmartLists.ENDPOINTS.base + '/' + playlistId + '/disable'),
            contentType: 'application/json'
        }).then(function(response) {
            if (!response.ok) {
                throw new Error('HTTP ' + response.status + ': ' + response.statusText);
            }
            return response.json();
        }).then(function(result) {
            Dashboard.hideLoadingMsg();
            SmartLists.showNotification(result.message || 'Playlist "' + playlistName + '" has been disabled.', 'success');
            SmartLists.loadPlaylistList(page);
        }).catch(function(err) {
            Dashboard.hideLoadingMsg();
            SmartLists.displayApiError(err, 'Failed to disable playlist "' + playlistName + '"');
        });
    };

    SmartLists.showDeleteConfirm = function(page, playlistId, playlistName) {
        const confirmText = 'Are you sure you want to delete the smart playlist "' + playlistName + '"? This cannot be undone.';
        
        SmartLists.showDeleteModal(page, confirmText, function() {
            SmartLists.deletePlaylist(page, playlistId, playlistName);
        });
    };

    // Note: formatPlaylistDisplayValues is defined in config-formatters.js to avoid duplication

    // ===== SEARCH INPUT STATE MANAGEMENT =====
    SmartLists.setSearchInputState = function(page, disabled, placeholder) {
        placeholder = placeholder !== undefined ? placeholder : 'Search list...';
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
                        placeholder: 'Search list...'
                    };
                }
            }
            
            // Only hide clear button when disabling, let updateClearButtonVisibility handle showing it
            if (clearSearchBtn && disabled) {
                clearSearchBtn.style.display = 'none';
            }
        } catch (err) {
            console.error('Error setting search input state:', err);
        }
    };

    // Note: getPeopleFieldDisplayName is defined in config-formatters.js to avoid duplication

    // ===== GENERATE RULES HTML =====
    SmartLists.generateRulesHtml = async function(playlist, apiClient) {
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
                        
                        // Map people field names to friendly display names
                        const displayName = SmartLists.getPeopleFieldDisplayName(fieldName);
                        if (displayName !== fieldName) {
                            fieldName = displayName;
                        }
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
                                const userName = await SmartLists.resolveUserIdToName(apiClient, rule.UserId);
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
                        
                        // Add Tags configuration info
                        let tagsInfo = '';
                        if (rule.MemberName === 'Tags' && rule.IncludeParentSeriesTags === true) {
                            tagsInfo = ' (including parent series tags)';
                        }
                        
                        // Add Studios configuration info
                        let studiosInfo = '';
                        if (rule.MemberName === 'Studios' && rule.IncludeParentSeriesStudios === true) {
                            studiosInfo = ' (including parent series studios)';
                        }
                        
                        // Add Genres configuration info
                        let genresInfo = '';
                        if (rule.MemberName === 'Genres' && rule.IncludeParentSeriesGenres === true) {
                            genresInfo = ' (including parent series genres)';
                        }
                        
                        // Add SimilarTo comparison fields info
                        let similarityInfo = '';
                        if (rule.MemberName === 'SimilarTo') {
                            if (playlist.SimilarityComparisonFields && playlist.SimilarityComparisonFields.length > 0) {
                                similarityInfo = ' (comparing: ' + playlist.SimilarityComparisonFields.join(', ') + ')';
                            } else {
                                similarityInfo = ' (comparing: Genre, Tags)'; // Default
                            }
                        }
                        
                        rulesHtml += '<span style="font-family: monospace; background: #232323; padding: 4px 4px; border-radius: 3px;">';
                        rulesHtml += SmartLists.escapeHtml(fieldName) + ' ' + SmartLists.escapeHtml(operator) + ' "' + SmartLists.escapeHtml(value) + '"' + SmartLists.escapeHtml(userInfo) + SmartLists.escapeHtml(nextUnwatchedInfo) + SmartLists.escapeHtml(collectionsInfo) + SmartLists.escapeHtml(tagsInfo) + SmartLists.escapeHtml(studiosInfo) + SmartLists.escapeHtml(genresInfo) + SmartLists.escapeHtml(similarityInfo);
                        rulesHtml += '</span>';
                    }
                    rulesHtml += '</div>';
                }
            }
        } else {
            rulesHtml = 'No rules defined';
        }
        return rulesHtml;
    };

    // ===== GENERATE PLAYLIST CARD HTML =====
    SmartLists.generatePlaylistCardHtml = function(playlist, rulesHtml, resolvedUserName) {
        // Determine list type
        const listType = playlist.Type || 'Playlist';
        const isCollection = listType === 'Collection';
        
        const isPublic = playlist.Public ? 'Public' : 'Private';
        const isEnabled = playlist.Enabled !== false; // Default to true for backward compatibility
        const enabledStatus = isEnabled ? '' : 'Disabled';
        const enabledStatusColor = isEnabled ? '#4CAF50' : '#f44336';
        const statusDisplayText = isEnabled ? 'Enabled' : 'Disabled';
        const autoRefreshMode = playlist.AutoRefresh || 'Never';
        const autoRefreshDisplay = autoRefreshMode === 'Never' ? 'Manual/scheduled only' :
                                 autoRefreshMode === 'OnLibraryChanges' ? 'On library changes - When new items are added' :
                                 autoRefreshMode === 'OnAllChanges' ? 'On all changes - Including playback status' : autoRefreshMode;
        const scheduleDisplay = SmartLists.formatScheduleDisplay(playlist);
        
        // Format last scheduled refresh display
        const lastRefreshDisplay = SmartLists.formatRelativeTimeFromIso(playlist.LastRefreshed, 'N/A') || 'N/A';
        const dateCreatedDisplay = SmartLists.formatRelativeTimeFromIso(playlist.DateCreated, 'Unknown');
        const sortName = SmartLists.formatSortDisplay(playlist);
        
        // Use the resolved username passed as parameter (for playlists) or libraries (for collections)
        const userName = resolvedUserName || 'Unknown User';
        const playlistId = playlist.Id || 'NO_ID';
        
        // Collections are server-wide, no library assignment needed
        // Create individual media type labels - filter out deprecated Series type
        let mediaTypesArray = [];
        if (playlist.MediaTypes && playlist.MediaTypes.length > 0) {
            const validTypes = playlist.MediaTypes.filter(function(type) { return type !== 'Series'; });
            mediaTypesArray = validTypes.length > 0 ? validTypes : ['Unknown'];
        } else {
            mediaTypesArray = ['Unknown'];
        }
        
        const displayValues = SmartLists.formatPlaylistDisplayValues(playlist);
        const maxItemsDisplay = displayValues.maxItemsDisplay;
        const maxPlayTimeDisplay = displayValues.maxPlayTimeDisplay;
        
        // Format media types for display in Properties table
        const mediaTypesDisplayText = mediaTypesArray.join(', ');
        
        // Format playlist statistics for header display
        const itemCount = playlist.ItemCount !== undefined && playlist.ItemCount !== null ? playlist.ItemCount : null;
        const totalRuntime = playlist.TotalRuntimeMinutes ? SmartLists.formatRuntime(playlist.TotalRuntimeMinutes) : null;
        const totalRuntimeLong = playlist.TotalRuntimeMinutes ? SmartLists.formatRuntimeLong(playlist.TotalRuntimeMinutes) : null;
        
        // Build stats display string for header
        const statsElements = [];
        if (itemCount !== null) {
            statsElements.push(itemCount + ' item' + (itemCount === 1 ? '' : 's'));
        }
        if (totalRuntime) {
            statsElements.push(totalRuntime);
        }
        const statsDisplay = statsElements.length > 0 ? statsElements.join(' | ') : '';
        
        // Escape all dynamic content to prevent XSS
        const eName = SmartLists.escapeHtml(playlist.Name || '');
        const eFileName = SmartLists.escapeHtml(playlist.FileName || '');
        const eUserName = SmartLists.escapeHtml(userName || '');
        const eSortName = SmartLists.escapeHtml(sortName);
        const eMaxItems = SmartLists.escapeHtml(maxItemsDisplay);
        const eMaxPlayTime = SmartLists.escapeHtml(maxPlayTimeDisplay);
        const eAutoRefreshDisplay = SmartLists.escapeHtml(autoRefreshDisplay);
        const eScheduleDisplay = SmartLists.escapeHtml(scheduleDisplay);
        const eLastRefreshDisplay = SmartLists.escapeHtml(lastRefreshDisplay);
        const eDateCreatedDisplay = SmartLists.escapeHtml(dateCreatedDisplay);
        const eStatusDisplayText = SmartLists.escapeHtml(statusDisplayText);
        const eMediaTypesDisplayText = SmartLists.escapeHtml(mediaTypesDisplayText);
        const eStatsDisplay = SmartLists.escapeHtml(statsDisplay);
        const eTotalRuntimeLong = totalRuntimeLong ? SmartLists.escapeHtml(totalRuntimeLong) : null;
        const eListType = SmartLists.escapeHtml(listType);
        
        // Build Jellyfin playlist/collection URL if ID exists and list is enabled
        let jellyfinListUrl = null;
        const jellyfinId = isCollection ? playlist.JellyfinCollectionId : playlist.JellyfinPlaylistId;
        const hasJellyfinId = jellyfinId && jellyfinId !== '' && jellyfinId !== '00000000-0000-0000-0000-000000000000';
        
        if (hasJellyfinId && isEnabled) {
            try {
                const apiClient = SmartLists.getApiClient();
                const serverId = apiClient.serverId();
                const baseUrl = apiClient.serverAddress();
                jellyfinListUrl = baseUrl + '/web/#/details?id=' + encodeURIComponent(jellyfinId) + '&serverId=' + encodeURIComponent(serverId);
            } catch (err) {
                console.error('Error building Jellyfin list URL:', err);
                // Fallback: try to build URL without serverId if that fails
                try {
                    const apiClient = SmartLists.getApiClient();
                    const baseUrl = apiClient.serverAddress();
                    jellyfinListUrl = baseUrl + '/web/#/details?id=' + encodeURIComponent(jellyfinId);
                } catch (fallbackErr) {
                    console.error('Error building Jellyfin list URL (fallback):', fallbackErr);
                }
            }
        }
        
        // Generate collapsible playlist card with improved styling
        return '<div class="inputContainer playlist-card" data-playlist-id="' + SmartLists.escapeHtmlAttribute(playlistId) + '" style="border: none; border-radius: 2px; margin-bottom: 1em; background: #202020;">' +
            // Compact header (always visible)
            '<div class="playlist-header" style="padding: 0.75em; cursor: pointer; display: flex; align-items: center; justify-content: space-between;">' +
                '<div class="playlist-header-left" style="display: flex; align-items: center; flex: 1; min-width: 0;">' +
                    '<label class="emby-checkbox-label" style="width: auto; min-width: auto; margin-right: 0.3em; margin-left: 0.3em; flex-shrink: 0;">' +
                        '<input type="checkbox" is="emby-checkbox" data-embycheckbox="true" class="emby-checkbox playlist-checkbox" data-playlist-id="' + SmartLists.escapeHtmlAttribute(playlistId) + '">' +
                        '<span class="checkboxLabel" style="display: none;"></span>' +
                        '<span class="checkboxOutline">' +
                            '<span class="material-icons checkboxIcon checkboxIcon-checked check" aria-hidden="true"></span>' +
                            '<span class="material-icons checkboxIcon checkboxIcon-unchecked" aria-hidden="true"></span>' +
                        '</span>' +
                    '</label>' +
                    '<span class="playlist-expand-icon" style="margin-right: 0.5em; font-family: monospace; font-size: 1.2em; color: #999; flex-shrink: 0;">▶</span>' +
                    '<h3 style="margin: 0; flex: 1.5; min-width: 0; word-wrap: break-word; padding-right: 0.5em;">' + eName + '</h3>' +
                    (enabledStatus ? '<span class="playlist-status" style="color: ' + enabledStatusColor + '; font-weight: bold; margin-right: 0.75em; flex-shrink: 0; line-height: 1.5; align-self: center;">' + enabledStatus + '</span>' : '') +
                    (eStatsDisplay ? '<span class="playlist-stats" style="color: #888; font-size: 0.85em; margin-right: 0.5em; flex-shrink: 0; font-weight: normal; line-height: 1.5; align-self: center;">' + eStatsDisplay + '</span>' : '') +
                '</div>' +
                '<div class="playlist-header-right" style="display: flex; align-items: center; margin-left: 1em; margin-right: 0.5em;">' +
                    '<div class="playlist-type-container" style="display: flex; flex-wrap: wrap; gap: 0.25em; flex-shrink: 0; max-width: 160px; justify-content: flex-end;">' +
                        '<span class="playlist-type-label" style="padding: 0.4em 0.6em; background: #333; border-radius: 3px; font-size: 0.8em; color: #ccc; white-space: nowrap;">' + SmartLists.escapeHtml(listType) + '</span>' +
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
                '<div class="properties-section" style="margin-bottom: 1em; margin-left: 0.5em;">' +
                    '<h4 style="margin: 0 0 0.5em 0; color: #fff; font-size: 1em;">Properties</h4>' +
                    '<table style="width: 100%; border-collapse: collapse; background: rgba(255,255,255,0.02); border-radius: 4px; overflow: hidden;">' +
                        '<tr style="border-bottom: 1px solid rgba(255,255,255,0.1);">' +
                            '<td style="padding: 0.5em 0.75em; font-weight: bold; color: #ccc; width: 40%; border-right: 1px solid rgba(255,255,255,0.1);">Type</td>' +
                            '<td style="padding: 0.5em 0.75em; color: #fff;">' + eListType + 
                                (hasJellyfinId && isEnabled ?
                                    (jellyfinListUrl ?
                                        ' - <a href="' + SmartLists.escapeHtmlAttribute(jellyfinListUrl) + '" target="_blank" rel="noopener noreferrer" style="color: #00a4dc; text-decoration: none;">View in Jellyfin</a>' :
                                        ' - <span style="color: #888; font-style: italic;">(Jellyfin ID: ' + SmartLists.escapeHtml(jellyfinId) + ')</span>'
                                    ) :
                                    ''
                                ) +
                            '</td>' +
                        '</tr>' +
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
                        (!isCollection ?
                            '<tr style="border-bottom: 1px solid rgba(255,255,255,0.1);">' +
                                '<td style="padding: 0.5em 0.75em; font-weight: bold; color: #ccc; width: 40%; border-right: 1px solid rgba(255,255,255,0.1);">Visibility</td>' +
                                '<td style="padding: 0.5em 0.75em; color: #fff;">' + SmartLists.escapeHtml(isPublic) + '</td>' +
                            '</tr>' :
                            ''
                        ) +
                        '<tr style="border-bottom: 1px solid rgba(255,255,255,0.1);">' +
                            '<td style="padding: 0.5em 0.75em; font-weight: bold; color: #ccc; width: 40%; border-right: 1px solid rgba(255,255,255,0.1);">Media Types</td>' +
                            '<td style="padding: 0.5em 0.75em; color: #fff;">' + eMediaTypesDisplayText + '</td>' +
                        '</tr>' +
                        (!isCollection ?
                            '<tr style="border-bottom: 1px solid rgba(255,255,255,0.1);">' +
                                '<td style="padding: 0.5em 0.75em; font-weight: bold; color: #ccc; width: 40%; border-right: 1px solid rgba(255,255,255,0.1);">Sort</td>' +
                                '<td style="padding: 0.5em 0.75em; color: #fff;">' + eSortName + '</td>' +
                            '</tr>' : ''
                        ) +
                        '<tr style="border-bottom: 1px solid rgba(255,255,255,0.1);">' +
                            '<td style="padding: 0.5em 0.75em; font-weight: bold; color: #ccc; width: 40%; border-right: 1px solid rgba(255,255,255,0.1);">Max Items</td>' +
                            '<td style="padding: 0.5em 0.75em; color: #fff;">' + eMaxItems + '</td>' +
                        '</tr>' +
                        (!isCollection ?
                            '<tr style="border-bottom: 1px solid rgba(255,255,255,0.1);">' +
                                '<td style="padding: 0.5em 0.75em; font-weight: bold; color: #ccc; width: 40%; border-right: 1px solid rgba(255,255,255,0.1);">Max Playtime</td>' +
                                '<td style="padding: 0.5em 0.75em; color: #fff;">' + eMaxPlayTime + '</td>' +
                            '</tr>' : ''
                        ) +
                        '<tr style="border-bottom: 1px solid rgba(255,255,255,0.1);">' +
                            '<td style="padding: 0.5em 0.75em; font-weight: bold; color: #ccc; width: 40%; border-right: 1px solid rgba(255,255,255,0.1);">Auto Refresh</td>' +
                            '<td style="padding: 0.5em 0.75em; color: #fff;">' + eAutoRefreshDisplay + '</td>' +
                        '</tr>' +
                        '<tr style="border-bottom: 1px solid rgba(255,255,255,0.1);">' +
                            '<td style="padding: 0.5em 0.75em; font-weight: bold; color: #ccc; width: 40%; border-right: 1px solid rgba(255,255,255,0.1);">Schedule</td>' +
                            '<td style="padding: 0.5em 0.75em; color: #fff;">' + eScheduleDisplay + '</td>' +
                        '</tr>' +
                        '<tr style="border-bottom: 1px solid rgba(255,255,255,0.1);">' +
                            '<td style="padding: 0.5em 0.75em; font-weight: bold; color: #ccc; width: 40%; border-right: 1px solid rgba(255,255,255,0.1);">Created</td>' +
                            '<td style="padding: 0.5em 0.75em; color: #fff;">' + eDateCreatedDisplay + '</td>' +
                        '</tr>' +
                    '</table>' +
                '</div>' +
                
                // Statistics table
                '<div class="statistics-section" style="margin-bottom: 1em; margin-left: 0.5em;">' +
                    '<h4 style="margin: 0 0 0.5em 0; color: #fff; font-size: 1em;">Statistics</h4>' +
                    '<table style="width: 100%; border-collapse: collapse; background: rgba(255,255,255,0.02); border-radius: 4px; overflow: hidden;">' +
                        '<tr style="border-bottom: 1px solid rgba(255,255,255,0.1);">' +
                            '<td style="padding: 0.5em 0.75em; font-weight: bold; color: #ccc; width: 40%; border-right: 1px solid rgba(255,255,255,0.1);">Item Count</td>' +
                            '<td style="padding: 0.5em 0.75em; color: #fff;">' + (itemCount !== null ? itemCount : 'N/A') + '</td>' +
                        '</tr>' +
                        '<tr style="border-bottom: 1px solid rgba(255,255,255,0.1);">' +
                            '<td style="padding: 0.5em 0.75em; font-weight: bold; color: #ccc; width: 40%; border-right: 1px solid rgba(255,255,255,0.1);">Total Playtime</td>' +
                            '<td style="padding: 0.5em 0.75em; color: #fff;">' + (eTotalRuntimeLong && playlist.TotalRuntimeMinutes && playlist.TotalRuntimeMinutes > 0 ? eTotalRuntimeLong : 'N/A') + '</td>' +
                        '</tr>' +
                        '<tr style="border-bottom: 1px solid rgba(255,255,255,0.1);">' +
                            '<td style="padding: 0.5em 0.75em; font-weight: bold; color: #ccc; width: 40%; border-right: 1px solid rgba(255,255,255,0.1);">Last Refreshed</td>' +
                            '<td style="padding: 0.5em 0.75em; color: #fff;">' + eLastRefreshDisplay + '</td>' +
                        '</tr>' +
                    '</table>' +
                '</div>' +
                
                // Action buttons
                '<div class="playlist-actions" style="margin-top: 1em; margin-left: 0.5em;">' +
                    '<button is="emby-button" type="button" class="emby-button raised edit-playlist-btn" data-playlist-id="' + SmartLists.escapeHtmlAttribute(playlistId) + '" data-playlist-name="' + SmartLists.escapeHtmlAttribute(playlist.Name || '') + '">Edit</button>' +
                    '<button is="emby-button" type="button" class="emby-button raised clone-playlist-btn" data-playlist-id="' + SmartLists.escapeHtmlAttribute(playlistId) + '" data-playlist-name="' + SmartLists.escapeHtmlAttribute(playlist.Name || '') + '">Clone</button>' +
                    '<button is="emby-button" type="button" class="emby-button raised refresh-playlist-btn" data-playlist-id="' + SmartLists.escapeHtmlAttribute(playlistId) + '" data-playlist-name="' + SmartLists.escapeHtmlAttribute(playlist.Name || '') + '">Refresh</button>' +
                    (isEnabled ?
                        '<button is="emby-button" type="button" class="emby-button raised disable-playlist-btn" data-playlist-id="' + SmartLists.escapeHtmlAttribute(playlistId) + '" data-playlist-name="' + SmartLists.escapeHtmlAttribute(playlist.Name || '') + '">Disable</button>' :
                        '<button is="emby-button" type="button" class="emby-button raised enable-playlist-btn" data-playlist-id="' + SmartLists.escapeHtmlAttribute(playlistId) + '" data-playlist-name="' + SmartLists.escapeHtmlAttribute(playlist.Name || '') + '">Enable</button>'
                    ) +
                    '<button is="emby-button" type="button" class="emby-button raised danger delete-playlist-btn" data-playlist-id="' + SmartLists.escapeHtmlAttribute(playlistId) + '" data-playlist-name="' + SmartLists.escapeHtmlAttribute(playlist.Name || '') + '">Delete</button>' +
                '</div>' +
            '</div>' +
        '</div>';
    };

    // ===== LOAD PLAYLIST LIST =====
    SmartLists.loadPlaylistList = async function(page) {
        const apiClient = SmartLists.getApiClient();
        const container = page.querySelector('#playlist-list-container');
        
        // Prevent multiple simultaneous requests
        if (page._loadingPlaylists) {
            return;
        }
        
        // Set loading state BEFORE any async operations
        page._loadingPlaylists = true;
        
        // Disable search input while loading
        SmartLists.setSearchInputState(page, true, 'Loading playlists...');
        
        container.innerHTML = '<p>Loading playlists...</p>';
        
        try {
            const response = await apiClient.ajax({
                type: "GET",
                url: apiClient.getUrl(SmartLists.ENDPOINTS.base),
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
                console.log('SmartLists: Loaded ' + processedPlaylists.length + ' playlist(s) successfully');
            }
            
            // Store playlists data for filtering
            page._allPlaylists = processedPlaylists;
            
            // Preload all users to populate cache for user name resolution
            try {
                const usersResponse = await apiClient.ajax({
                    type: 'GET',
                    url: apiClient.getUrl(SmartLists.ENDPOINTS.users),
                    contentType: 'application/json'
                });
                const users = await usersResponse.json();
                
                // Build cache from all users for user name resolution
                if (Array.isArray(users)) {
                    users.forEach(function(user) {
                        if (user.Id && user.Name) {
                            // Normalize GUID format when storing in cache (remove dashes)
                            const normalizedId = user.Id.replace(/-/g, '').toLowerCase();
                            userNameCache.set(normalizedId, user.Name);
                        }
                    });
                }
            } catch (err) {
                console.error('Error preloading users:', err);
                // Continue even if user preload fails
            }
            
            try {
                // Populate user filter dropdown
                if (SmartLists.populateUserFilter) {
                    await SmartLists.populateUserFilter(page, processedPlaylists);
                }
            } catch (err) {
                console.error('Error populating user filter:', err);
                // Continue even if user filter fails
            }
            
            if (processedPlaylists && processedPlaylists.length > 0) {
                // Apply all filters and sorting and display results
                const filteredPlaylists = SmartLists.applyAllFiltersAndSort ? SmartLists.applyAllFiltersAndSort(page, processedPlaylists) : processedPlaylists;
                
                // Use the existing playlist display logic instead of the async function for now
                const totalPlaylists = processedPlaylists.length;
                const filteredCount = filteredPlaylists.length;
                const enabledPlaylists = filteredPlaylists.filter(function(p) { return p.Enabled !== false; }).length;
                const disabledPlaylists = filteredCount - enabledPlaylists;
                
                let html = '';
                
                // Add bulk actions container after summary
                const summaryText = SmartLists.generateSummaryText ? SmartLists.generateSummaryText(totalPlaylists, enabledPlaylists, disabledPlaylists, filteredCount, null) : '';
                html += SmartLists.generateBulkActionsHTML ? SmartLists.generateBulkActionsHTML(summaryText) : '';
                
                // Process filtered playlists sequentially to resolve usernames
                for (let i = 0; i < filteredPlaylists.length; i++) {
                    const playlist = filteredPlaylists[i];
                    // Resolve username first
                    // Determine list type
                    const listType = playlist.Type || 'Playlist';
                    const isCollection = listType === 'Collection';
                    
                    // Resolve user name (both playlists and collections have a User/owner)
                    let resolvedUserName = await SmartLists.resolveUsername(apiClient, playlist);
                    
                    // Generate detailed rules display using helper function
                    const rulesHtml = await SmartLists.generateRulesHtml(playlist, apiClient);
                    
                    // Use helper function to generate playlist HTML (DRY)
                    html += SmartLists.generatePlaylistCardHtml(playlist, rulesHtml, resolvedUserName);
                }
                container.innerHTML = html;
                
                // Restore expand states from localStorage
                if (SmartLists.restorePlaylistExpandStates) {
                    SmartLists.restorePlaylistExpandStates(page);
                }
                
                // Update expand all button text based on current states
                if (SmartLists.updateExpandAllButtonText) {
                    SmartLists.updateExpandAllButtonText(page);
                }
                
                // Update bulk actions visibility and state
                if (SmartLists.updateBulkActionsVisibility) {
                    SmartLists.updateBulkActionsVisibility(page);
                }
            } else {
                container.innerHTML = '<div class="inputContainer"><p>No smart playlists found.</p></div>';
            }
            
        } catch (err) {
            const errorMessage = SmartLists.displayApiError ? SmartLists.displayApiError(err, 'Failed to load playlists') : (err.message || 'Failed to load playlists');
            container.innerHTML = '<div class="inputContainer"><p style="color: #ff6b6b;">' + SmartLists.escapeHtml(errorMessage) + '</p></div>';
        } finally {
            // Always re-enable search input and clear loading flag
            SmartLists.setSearchInputState(page, false);
            page._loadingPlaylists = false;
        }
    };

})(window.SmartLists = window.SmartLists || {});

