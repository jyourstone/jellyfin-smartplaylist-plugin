(function(SmartLists) {
    'use strict';
    
    // Initialize namespace if it doesn't exist
    if (!SmartLists) {
        window.SmartLists = {};
        SmartLists = window.SmartLists;
    }
    
    // Centralized filter configuration - eliminates DRY violations
    SmartLists.PLAYLIST_FILTER_CONFIGS = {
        search: {
            selector: '#playlistSearchInput',
            defaultValue: '',
            getValue: function(element) {
                return element ? element.value.trim().toLowerCase() : '';
            },
            filterFn: function(playlists, searchTerm, page) {
                if (!searchTerm) return playlists;
                return SmartLists.filterPlaylists(playlists, searchTerm, page);
            }
        },
        type: {
            selector: '#typeFilter',
            defaultValue: 'all',
            getValue: function(element) {
                return element ? element.value : 'all';
            },
            filterFn: function(playlists, typeFilter) {
                if (!typeFilter || typeFilter === 'all') return playlists;
                
                return playlists.filter(function(list) {
                    const listType = list.Type || 'Playlist'; // Default to Playlist for backward compatibility
                    return listType === typeFilter;
                });
            }
        },
        mediaType: {
            selector: '#mediaTypeFilter',
            defaultValue: 'all',
            getValue: function(element) {
                return element ? element.value : 'all';
            },
            filterFn: function(playlists, mediaTypeFilter) {
                if (!mediaTypeFilter || mediaTypeFilter === 'all') return playlists;
                
                return playlists.filter(function(playlist) {
                    const mediaTypes = playlist.MediaTypes || [];
                    return mediaTypes.indexOf(mediaTypeFilter) !== -1;
                });
            }
        },
        user: {
            selector: '#userFilter',
            defaultValue: 'all',
            getValue: function(element) {
                return element ? element.value : 'all';
            },
            filterFn: function(playlists, userFilter) {
                if (!userFilter || userFilter === 'all') return playlists;
                
                return playlists.filter(function(playlist) {
                    // User filter applies to both playlists (owner) and collections (rule context user)
                    return playlist.UserId === userFilter;
                });
            }
        },
        sort: {
            selector: '#playlistSortSelect',
            defaultValue: 'name-asc',
            getValue: function(element) {
                return element ? element.value : 'name-asc';
            }
            // Note: sorting is handled by sortPlaylists function, not as a filter
        }
    };
    
    SmartLists.filterPlaylists = function(playlists, searchTerm, page) {
        if (!searchTerm) return playlists;
        
        return playlists.filter(function(playlist) {
            // Search in playlist name
            if (playlist.Name && playlist.Name.toLowerCase().indexOf(searchTerm) !== -1) {
                return true;
            }
            
            // Search in filename
            if (playlist.FileName && playlist.FileName.toLowerCase().indexOf(searchTerm) !== -1) {
                return true;
            }
            
            // Search in media types
            if (playlist.MediaTypes && playlist.MediaTypes.some(function(type) {
                return type.toLowerCase().indexOf(searchTerm) !== -1;
            })) {
                return true;
            }
            
            // Search in rules (field names, operators, and values)
            if (playlist.ExpressionSets) {
                for (var i = 0; i < playlist.ExpressionSets.length; i++) {
                    const expressionSet = playlist.ExpressionSets[i];
                    if (expressionSet.Expressions) {
                        for (var j = 0; j < expressionSet.Expressions.length; j++) {
                            const expression = expressionSet.Expressions[j];
                            // Search in field name
                            if (expression.MemberName && expression.MemberName.toLowerCase().indexOf(searchTerm) !== -1) {
                                return true;
                            }
                            
                            // Search in operator
                            if (expression.Operator && expression.Operator.toLowerCase().indexOf(searchTerm) !== -1) {
                                return true;
                            }
                            
                            // Search in target value
                            if (expression.TargetValue && expression.TargetValue.toLowerCase().indexOf(searchTerm) !== -1) {
                                return true;
                            }
                        }
                    }
                }
            }
            
            // Search in sort order (both legacy and new multi-sort formats)
            if (playlist.Order && playlist.Order.Name && playlist.Order.Name.toLowerCase().indexOf(searchTerm) !== -1) {
                return true;
            }
            if (playlist.Order && Array.isArray(playlist.Order.SortOptions)) {
                const sortText = playlist.Order.SortOptions
                    .map(function(o) {
                        return ((o.SortBy || '') + ' ' + (o.SortOrder || '')).toLowerCase();
                    })
                    .join(' ');
                if (sortText.indexOf(searchTerm) !== -1) {
                    return true;
                }
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
            
            // Search in username (resolved from User ID)
            if (page && page._usernameCache && playlist.UserId) {
                const username = page._usernameCache.get(playlist.UserId);
                if (username && username.toLowerCase().indexOf(searchTerm) !== -1) {
                    return true;
                }
            }
            
            return false;
        });
    };
    
    // Generic DOM query helper - eliminates repetitive querySelector patterns
    SmartLists.getFilterValue = function(page, filterKey) {
        const config = SmartLists.PLAYLIST_FILTER_CONFIGS[filterKey];
        if (!config) {
            return '';
        }
        
        const element = page.querySelector(config.selector);
        return config.getValue(element);
    };
    
    // Generic filter application function - replaces all individual filter functions
    SmartLists.applyFilter = function(playlists, filterKey, filterValue, page) {
        const config = SmartLists.PLAYLIST_FILTER_CONFIGS[filterKey];
        if (!config || !config.filterFn) return playlists;
        
        return config.filterFn(playlists, filterValue, page);
    };
    
    // Initialize page-level AbortController for better event listener management
    SmartLists.initializePageEventListeners = function(page) {
        // Create page-level AbortController if it doesn't exist
        if (!page._pageAbortController) {
            page._pageAbortController = SmartLists.createAbortController();
        }
        return page._pageAbortController ? page._pageAbortController.signal : null;
    };
    
    // Generic event listener setup - eliminates repetitive filter change handlers
    SmartLists.setupFilterEventListeners = function(page, pageSignal) {
        pageSignal = pageSignal || SmartLists.initializePageEventListeners(page);
        const filterKeys = ['sort', 'type', 'mediaType', 'user'];
        
        filterKeys.forEach(function(filterKey) {
            const config = SmartLists.PLAYLIST_FILTER_CONFIGS[filterKey];
            if (!config) return;
            
            const element = page.querySelector(config.selector);
            if (element) {
                element.addEventListener('change', function() {
                    SmartLists.savePlaylistFilterPreferences(page);
                    SmartLists.applySearchFilter(page).catch(function(err) {
                        console.error('Error during ' + filterKey + ' filter:', err);
                        SmartLists.showNotification('Filter error: ' + err.message);
                    });
                }, SmartLists.getEventListenerOptions(pageSignal));
            }
        });
    };
    
    SmartLists.clearAllFilters = function(page) {
        // Clear search
        const searchInput = page.querySelector('#playlistSearchInput');
        if (searchInput) {
            searchInput.value = '';
        }
        
        // Reset filters to default
        const typeFilter = page.querySelector('#typeFilter');
        if (typeFilter) {
            typeFilter.value = 'all';
        }
        
        const mediaTypeFilter = page.querySelector('#mediaTypeFilter');
        if (mediaTypeFilter) {
            mediaTypeFilter.value = 'all';
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
        SmartLists.savePlaylistFilterPreferences(page);
        
        // Apply filters
        SmartLists.applySearchFilter(page).catch(function(err) {
            console.error('Error during clear filters:', err);
            SmartLists.showNotification('Filter error: ' + err.message);
        });
        
        // Update clear button visibility
        const clearSearchBtn = page.querySelector('#clearSearchBtn');
        if (clearSearchBtn) {
            clearSearchBtn.style.display = 'none';
        }
    };
    
    // Enhanced preferences system with validation and error recovery
    SmartLists.savePlaylistFilterPreferences = function(page) {
        try {
            const preferences = {};
            
            // Get preferences for all filters except search (session-specific)
            const persistentFilters = ['sort', 'type', 'mediaType', 'user'];
            
            for (var i = 0; i < persistentFilters.length; i++) {
                const filterKey = persistentFilters[i];
                const config = SmartLists.PLAYLIST_FILTER_CONFIGS[filterKey];
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
            
            localStorage.setItem('smartListsFilterPreferences', JSON.stringify(preferences));
            console.debug('Saved filter preferences:', preferences);
        } catch (err) {
            console.warn('Failed to save playlist filter preferences:', err);
        }
    };
    
    SmartLists.loadPlaylistFilterPreferences = function(page) {
        try {
            const saved = localStorage.getItem('smartListsFilterPreferences');
            if (!saved) {
                console.debug('No saved filter preferences found, using defaults');
                return;
            }
            
            const preferences = JSON.parse(saved);
            console.debug('Loading filter preferences:', preferences);
            
            // Apply saved preferences using the generic system with validation
            Object.keys(preferences).forEach(function(filterKey) {
                const value = preferences[filterKey];
                const config = SmartLists.PLAYLIST_FILTER_CONFIGS[filterKey];
                if (config && value !== undefined) {
                    const element = page.querySelector(config.selector);
                    if (element) {
                        // Validate that the saved value is still valid for this element
                        const options = Array.prototype.slice.call(element.options || []);
                        const isValidOption = options.length === 0 || options.some(function(opt) {
                            return opt.value === value;
                        });
                        
                        if (isValidOption) {
                            element.value = value;
                            console.debug('Restored ' + filterKey + ' filter to:', value);
                        } else {
                            console.warn('Invalid saved value for ' + filterKey + ':', value, 'Available options:', options.map(function(o) {
                                return o.value;
                            }));
                            // Fall back to default value
                            element.value = config.defaultValue;
                        }
                    } else {
                        console.warn('Filter element not found for ' + filterKey + ':', config.selector);
                    }
                } else {
                    console.warn('Invalid filter configuration for ' + filterKey);
                }
            });
            
            // Ensure all filters have valid values, even if not in saved preferences
            const persistentFilters = ['sort', 'type', 'mediaType', 'user'];
            persistentFilters.forEach(function(filterKey) {
                if (!preferences.hasOwnProperty(filterKey)) {
                    const config = SmartLists.PLAYLIST_FILTER_CONFIGS[filterKey];
                    if (config) {
                        const element = page.querySelector(config.selector);
                        if (element && !element.value) {
                            element.value = config.defaultValue;
                            console.debug('Set default value for ' + filterKey + ':', config.defaultValue);
                        }
                    }
                }
            });
            
        } catch (err) {
            console.warn('Failed to load playlist filter preferences:', err);
            // Reset to defaults on error
            SmartLists.resetFiltersToDefaults(page);
        }
    };
    
    SmartLists.resetFiltersToDefaults = function(page) {
        try {
            const persistentFilters = ['sort', 'type', 'mediaType', 'user'];
            persistentFilters.forEach(function(filterKey) {
                const config = SmartLists.PLAYLIST_FILTER_CONFIGS[filterKey];
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
    };
    
    SmartLists.populateUserFilter = function(page, playlists) {
        const userFilter = page.querySelector('#userFilter');
        if (!userFilter || !playlists) return Promise.resolve();
        
        try {
            // Ensure playlists is an array
            if (!Array.isArray(playlists)) {
                console.warn('Playlists is not an array:', typeof playlists, playlists);
                return Promise.resolve();
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
                entries.slice(-keepCount).forEach(function(entry) {
                    page._usernameCache.set(entry[0], entry[1]);
                });
            }
            
            // Get unique user IDs from playlists
            const userIds = [];
            const seenIds = {};
            for (var i = 0; i < playlists.length; i++) {
                const userId = playlists[i].User;  // User field contains the user ID
                if (userId && !seenIds[userId]) {
                    userIds.push(userId);
                    seenIds[userId] = true;
                }
            }
            
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
            const apiClient = SmartLists.getApiClient();
            const promises = [];
            
            for (var j = 0; j < userIds.length; j++) {
                const userId = userIds[j];
                promises.push(
                    SmartLists.resolveUserIdToName(apiClient, userId).then(function(userName) {
                        if (userName) {
                            const option = document.createElement('option');
                            option.value = userId;
                            option.textContent = userName;
                            userFilter.appendChild(option);
                            // Cache the username
                            page._usernameCache.set(userId, userName);
                        }
                    }).catch(function(err) {
                        console.error('Error resolving user ID ' + userId + ':', err);
                        // Add option with fallback name
                        const option = document.createElement('option');
                        option.value = userId;
                        option.textContent = 'Unknown User';
                        userFilter.appendChild(option);
                        page._usernameCache.set(userId, 'Unknown User');
                    })
                );
            }
            
            return Promise.all(promises);
        } catch (err) {
            console.error('Error populating user filter:', err);
            return Promise.resolve();
        }
    };
    
    SmartLists.applySearchFilter = function(page) {
        const searchInput = page.querySelector('#playlistSearchInput');
        if (!searchInput || !page._allPlaylists) {
            return Promise.resolve();
        }
        
        // Don't search while loading playlists
        if (page._loadingPlaylists) {
            return Promise.resolve();
        }
        
        // Apply all filters and sorting
        const filteredPlaylists = SmartLists.applyAllFiltersAndSort(page, page._allPlaylists);
        
        // Display the filtered results
        return SmartLists.displayFilteredPlaylists(page, filteredPlaylists, '');
    };
    
    SmartLists.displayFilteredPlaylists = async function(page, filteredPlaylists, searchTerm) {
        const container = page.querySelector('#playlist-list-container');
        const apiClient = SmartLists.getApiClient();
        
        // Calculate summary statistics for filtered results
        const totalPlaylists = page._allPlaylists.length;
        const filteredCount = filteredPlaylists.length;
        const enabledPlaylists = filteredPlaylists.filter(function(p) {
            return p.Enabled !== false;
        }).length;
        const disabledPlaylists = filteredCount - enabledPlaylists;
        
        let html = '';
        
        // Add bulk actions container after summary
        let summaryText;
        summaryText = SmartLists.generateSummaryText(totalPlaylists, enabledPlaylists, disabledPlaylists, filteredCount, searchTerm);
        html += SmartLists.generateBulkActionsHTML(summaryText);
        
        // Process filtered playlists using the helper function
        const promises = [];
        for (var i = 0; i < filteredPlaylists.length; i++) {
            const playlist = filteredPlaylists[i];
            promises.push(
                SmartLists.resolveUsername(apiClient, playlist).then(function(resolvedUserName) {
                    return SmartLists.generateRulesHtml(playlist, apiClient).then(function(rulesHtml) {
                        return {
                            playlist: playlist,
                            rulesHtml: rulesHtml,
                            resolvedUserName: resolvedUserName
                        };
                    });
                })
            );
        }
        
        const results = await Promise.all(promises);
        for (var j = 0; j < results.length; j++) {
            const result = results[j];
            html += SmartLists.generatePlaylistCardHtml(result.playlist, result.rulesHtml, result.resolvedUserName);
        }
        container.innerHTML = html;
        // Restore expand states from localStorage after regenerating HTML
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
    };
    
    // Note: getPeopleFieldDisplayName is defined in config-formatters.js to avoid duplication
    
})(window.SmartLists = window.SmartLists || {});

