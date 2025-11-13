(function(SmartLists) {
    'use strict';
    
    // Initialize namespace if it doesn't exist
    if (!SmartLists) {
        window.SmartLists = {};
        SmartLists = window.SmartLists;
    }
    
    SmartLists.getBulkActionElements = function(page, forceRefresh) {
        forceRefresh = forceRefresh !== undefined ? forceRefresh : false;
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
    };
    
    // Bulk operations functionality
    SmartLists.updateBulkActionsVisibility = function(page) {
        const elements = SmartLists.getBulkActionElements(page, true); // Force refresh after HTML changes
        const checkboxes = page.querySelectorAll('.playlist-checkbox');
        
        // Show bulk actions if any playlists exist
        if (elements.bulkContainer) {
            elements.bulkContainer.style.display = checkboxes.length > 0 ? 'block' : 'none';
        }
        
        // Update selected count and button states
        SmartLists.updateSelectedCount(page);
    };
    
    SmartLists.updateSelectedCount = function(page) {
        const selectedCheckboxes = page.querySelectorAll('.playlist-checkbox:checked');
        const selectedCount = selectedCheckboxes.length;
        const elements = SmartLists.getBulkActionElements(page);
        
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
    };
    
    SmartLists.toggleSelectAll = function(page) {
        const elements = SmartLists.getBulkActionElements(page);
        const playlistCheckboxes = page.querySelectorAll('.playlist-checkbox');
        
        const shouldSelect = elements.selectAllCheckbox ? elements.selectAllCheckbox.checked : false;
        
        playlistCheckboxes.forEach(function(checkbox) {
            checkbox.checked = shouldSelect;
        });
        
        SmartLists.updateSelectedCount(page);
    };
    
    SmartLists.bulkEnablePlaylists = function(page) {
        const selectedCheckboxes = page.querySelectorAll('.playlist-checkbox:checked');
        const playlistIds = Array.prototype.slice.call(selectedCheckboxes).map(function(cb) {
            return cb.getAttribute('data-playlist-id');
        });
        
        if (playlistIds.length === 0) {
            SmartLists.showNotification('No playlists selected', 'error');
            return Promise.resolve();
        }
        
        // Filter to only playlists that are currently disabled
        const playlistsToEnable = [];
        const alreadyEnabled = [];
        
        for (var i = 0; i < selectedCheckboxes.length; i++) {
            const checkbox = selectedCheckboxes[i];
            const playlistId = checkbox.getAttribute('data-playlist-id');
            const playlistCard = checkbox.closest('.playlist-card');
            const statusElement = playlistCard ? playlistCard.querySelector('.playlist-status') : null;
            const isCurrentlyEnabled = !statusElement || statusElement.textContent.indexOf('Disabled') === -1;
            
            if (isCurrentlyEnabled) {
                alreadyEnabled.push(playlistId);
            } else {
                playlistsToEnable.push(playlistId);
            }
        }
        
        if (playlistsToEnable.length === 0) {
            SmartLists.showNotification('All selected playlists are already enabled', 'info');
            return Promise.resolve();
        }
        
        const apiClient = SmartLists.getApiClient();
        let successCount = 0;
        let errorCount = 0;
        
        Dashboard.showLoadingMsg();
        
        const promises = [];
        for (var j = 0; j < playlistsToEnable.length; j++) {
            const playlistId = playlistsToEnable[j];
            promises.push(
                apiClient.ajax({
                    type: "POST",
                    url: apiClient.getUrl(SmartLists.ENDPOINTS.base + '/' + playlistId + '/enable'),
                    contentType: 'application/json'
                }).then(function() {
                    successCount++;
                }).catch(function(err) {
                    console.error('Error enabling playlist:', playlistId, err);
                    errorCount++;
                })
            );
        }
        
        return Promise.all(promises).then(function() {
            Dashboard.hideLoadingMsg();
            
            if (errorCount === 0) {
                SmartLists.showNotification(successCount + ' playlist(s) enabled successfully', 'success');
            } else {
                SmartLists.showNotification(successCount + ' enabled, ' + errorCount + ' failed', 'error');
            }
            
            // Refresh the list and clear selections
            if (SmartLists.loadPlaylistList) {
                SmartLists.loadPlaylistList(page);
            }
        });
    };
    
    SmartLists.bulkDisablePlaylists = function(page) {
        const selectedCheckboxes = page.querySelectorAll('.playlist-checkbox:checked');
        const playlistIds = Array.prototype.slice.call(selectedCheckboxes).map(function(cb) {
            return cb.getAttribute('data-playlist-id');
        });
        
        if (playlistIds.length === 0) {
            SmartLists.showNotification('No playlists selected', 'error');
            return Promise.resolve();
        }
        
        // Filter to only playlists that are currently enabled
        const playlistsToDisable = [];
        const alreadyDisabled = [];
        
        for (var i = 0; i < selectedCheckboxes.length; i++) {
            const checkbox = selectedCheckboxes[i];
            const playlistId = checkbox.getAttribute('data-playlist-id');
            const playlistCard = checkbox.closest('.playlist-card');
            const statusElement = playlistCard ? playlistCard.querySelector('.playlist-status') : null;
            const isCurrentlyEnabled = !statusElement || statusElement.textContent.indexOf('Disabled') === -1;
            
            if (isCurrentlyEnabled) {
                playlistsToDisable.push(playlistId);
            } else {
                alreadyDisabled.push(playlistId);
            }
        }
        
        if (playlistsToDisable.length === 0) {
            SmartLists.showNotification('All selected playlists are already disabled', 'info');
            return Promise.resolve();
        }
        
        const apiClient = SmartLists.getApiClient();
        let successCount = 0;
        let errorCount = 0;
        
        Dashboard.showLoadingMsg();
        
        const promises = [];
        for (var j = 0; j < playlistsToDisable.length; j++) {
            const playlistId = playlistsToDisable[j];
            promises.push(
                apiClient.ajax({
                    type: "POST",
                    url: apiClient.getUrl(SmartLists.ENDPOINTS.base + '/' + playlistId + '/disable'),
                    contentType: 'application/json'
                }).then(function() {
                    successCount++;
                }).catch(function(err) {
                    console.error('Error disabling playlist:', playlistId, err);
                    errorCount++;
                })
            );
        }
        
        return Promise.all(promises).then(function() {
            Dashboard.hideLoadingMsg();
            
            if (errorCount === 0) {
                SmartLists.showNotification(successCount + ' playlist(s) disabled successfully', 'success');
            } else {
                SmartLists.showNotification(successCount + ' disabled, ' + errorCount + ' failed', 'error');
            }
            
            // Refresh the list and clear selections
            if (SmartLists.loadPlaylistList) {
                SmartLists.loadPlaylistList(page);
            }
        });
    };
    
    // Refresh confirmation modal function
    SmartLists.showRefreshConfirmModal = function(page, onConfirm) {
        const modal = page.querySelector('#refresh-confirm-modal');
        if (!modal) return;
        
        // Clean up any existing modal listeners
        SmartLists.cleanupModalListeners(modal);
        
        // Apply modal styles using centralized configuration
        const modalContainer = modal.querySelector('.custom-modal-container');
        SmartLists.applyStyles(modalContainer, SmartLists.STYLES.modal.container);
        SmartLists.applyStyles(modal, SmartLists.STYLES.modal.backdrop);
        
        // Show the modal
        modal.classList.remove('hide');
        
        // Create AbortController for modal event listeners
        const modalAbortController = SmartLists.createAbortController();
        const modalSignal = modalAbortController ? modalAbortController.signal : null;
        
        // Clean up function to close modal and remove all listeners
        const cleanupAndClose = function() {
            modal.classList.add('hide');
            SmartLists.cleanupModalListeners(modal);
        };
        
        // Handle confirm button
        const confirmBtn = modal.querySelector('.modal-confirm-btn');
        confirmBtn.addEventListener('click', function() {
            cleanupAndClose();
            onConfirm();
        }, SmartLists.getEventListenerOptions(modalSignal));
        
        // Handle cancel button
        const cancelBtn = modal.querySelector('.modal-cancel-btn');
        cancelBtn.addEventListener('click', function() {
            cleanupAndClose();
        }, SmartLists.getEventListenerOptions(modalSignal));
        
        // Handle backdrop click
        modal.addEventListener('click', function(e) {
            if (e.target === modal) {
                cleanupAndClose();
            }
        }, SmartLists.getEventListenerOptions(modalSignal));
        
        // Store abort controller for cleanup
        modal._modalAbortController = modalAbortController;
    };
    
    // Generic delete modal function to reduce duplication
    SmartLists.showDeleteModal = function(page, confirmText, onConfirm) {
        const modal = page.querySelector('#delete-confirm-modal');
        if (!modal) return;
        
        // Clean up any existing modal listeners
        SmartLists.cleanupModalListeners(modal);
        
        // Apply modal styles using centralized configuration
        const modalContainer = modal.querySelector('.custom-modal-container');
        SmartLists.applyStyles(modalContainer, SmartLists.STYLES.modal.container);
        SmartLists.applyStyles(modal, SmartLists.STYLES.modal.backdrop);
        
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
        const modalAbortController = SmartLists.createAbortController();
        const modalSignal = modalAbortController ? modalAbortController.signal : null;
        
        // Clean up function to close modal and remove all listeners
        const cleanupAndClose = function() {
            modal.classList.add('hide');
            SmartLists.cleanupModalListeners(modal);
        };
        
        // Handle confirm button
        const confirmBtn = modal.querySelector('#delete-confirm-btn');
        confirmBtn.addEventListener('click', function() {
            cleanupAndClose();
            onConfirm();
        }, SmartLists.getEventListenerOptions(modalSignal));
        
        // Handle cancel button
        const cancelBtn = modal.querySelector('#delete-cancel-btn');
        cancelBtn.addEventListener('click', function() {
            cleanupAndClose();
        }, SmartLists.getEventListenerOptions(modalSignal));
        
        // Handle backdrop click
        modal.addEventListener('click', function(e) {
            if (e.target === modal) {
                cleanupAndClose();
            }
        }, SmartLists.getEventListenerOptions(modalSignal));
        
        // Store abort controller for cleanup
        modal._modalAbortController = modalAbortController;
    };
    
    SmartLists.showBulkDeleteConfirm = function(page, playlistIds, playlistNames) {
        const playlistList = playlistNames.length > 5 
            ? playlistNames.slice(0, 5).join('\n') + '\n... and ' + (playlistNames.length - 5) + ' more'
            : playlistNames.join('\n');
        
        const isPlural = playlistNames.length !== 1;
        const confirmText = 'Are you sure you want to delete the following ' + (isPlural ? 'playlists' : 'playlist') + '?\n\n' + playlistList + '\n\nThis action cannot be undone.';
        
        SmartLists.showDeleteModal(page, confirmText, function() {
            SmartLists.performBulkDelete(page, playlistIds);
        });
    };
    
    SmartLists.performBulkDelete = function(page, playlistIds) {
        const apiClient = SmartLists.getApiClient();
        const deleteJellyfinPlaylist = page.querySelector('#delete-jellyfin-playlist-checkbox').checked;
        let successCount = 0;
        let errorCount = 0;
        
        Dashboard.showLoadingMsg();
        
        const promises = [];
        for (var i = 0; i < playlistIds.length; i++) {
            const playlistId = playlistIds[i];
            promises.push(
                apiClient.ajax({
                    type: "DELETE",
                    url: apiClient.getUrl(SmartLists.ENDPOINTS.base + '/' + playlistId + '?deleteJellyfinPlaylist=' + deleteJellyfinPlaylist),
                    contentType: 'application/json'
                }).then(function() {
                    successCount++;
                }).catch(function(err) {
                    console.error('Error deleting playlist:', playlistId, err);
                    errorCount++;
                })
            );
        }
        
        return Promise.all(promises).then(function() {
            Dashboard.hideLoadingMsg();
            
            if (successCount > 0) {
                const action = deleteJellyfinPlaylist ? 'deleted' : 'suffix/prefix removed (if any) and configuration deleted';
                SmartLists.showNotification('Successfully ' + action + ' ' + successCount + ' playlist(s).', 'success');
            }
            if (errorCount > 0) {
                SmartLists.showNotification('Failed to delete ' + errorCount + ' playlist(s).', 'error');
            }
            
            // Clear selections and reload
            const selectAllCheckbox = page.querySelector('#selectAllCheckbox');
            if (selectAllCheckbox) {
                selectAllCheckbox.checked = false;
            }
            
            if (SmartLists.loadPlaylistList) {
                SmartLists.loadPlaylistList(page);
            }
        });
    };
    
    SmartLists.bulkDeletePlaylists = function(page) {
        const selectedCheckboxes = page.querySelectorAll('.playlist-checkbox:checked');
        const playlistIds = Array.prototype.slice.call(selectedCheckboxes).map(function(cb) {
            return cb.getAttribute('data-playlist-id');
        });
        
        if (playlistIds.length === 0) {
            SmartLists.showNotification('No playlists selected', 'error');
            return;
        }
        
        const playlistNames = Array.prototype.slice.call(selectedCheckboxes).map(function(cb) {
            const playlistCard = cb.closest('.playlist-card');
            const nameElement = playlistCard ? playlistCard.querySelector('.playlist-header-left h3') : null;
            return nameElement ? nameElement.textContent : 'Unknown';
        });
        
        // Show the custom modal instead of browser confirm
        SmartLists.showBulkDeleteConfirm(page, playlistIds, playlistNames);
    };
    
    // Collapsible playlist functionality
    SmartLists.togglePlaylistCard = function(playlistCard) {
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
        SmartLists.savePlaylistExpandStates();
    };
    
    SmartLists.toggleAllPlaylists = function(page) {
        const expandAllBtn = page.querySelector('#expandAllBtn');
        const playlistCards = page.querySelectorAll('.playlist-card');
        
        if (!playlistCards.length) return;
        
        // Base action on current button text, not on current state
        const shouldExpand = expandAllBtn.textContent.trim() === 'Expand All';
        
        // Preserve scroll position when expanding to prevent unwanted scrolling
        const currentScrollTop = window.pageYOffset || document.documentElement.scrollTop;
        
        if (shouldExpand) {
            // Expand all
            for (var i = 0; i < playlistCards.length; i++) {
                const card = playlistCards[i];
                const details = card.querySelector('.playlist-details');
                const actions = card.querySelector('.playlist-actions');
                const icon = card.querySelector('.playlist-expand-icon');
                details.style.display = 'block';
                actions.style.display = 'block';
                icon.textContent = '▼';
                card.setAttribute('data-expanded', 'true');
            }
            expandAllBtn.textContent = 'Collapse All';
            
            // Restore scroll position after DOM changes to prevent unwanted scrolling
            if (window.requestAnimationFrame) {
                requestAnimationFrame(function() {
                    window.scrollTo(0, currentScrollTop);
                });
            } else {
                setTimeout(function() {
                    window.scrollTo(0, currentScrollTop);
                }, 0);
            }
        } else {
            // Collapse all
            for (var j = 0; j < playlistCards.length; j++) {
                const card = playlistCards[j];
                const details = card.querySelector('.playlist-details');
                const actions = card.querySelector('.playlist-actions');
                const icon = card.querySelector('.playlist-expand-icon');
                details.style.display = 'none';
                actions.style.display = 'none';
                icon.textContent = '▶';
                card.setAttribute('data-expanded', 'false');
            }
            expandAllBtn.textContent = 'Expand All';
        }
        
        // Save state to localStorage
        SmartLists.savePlaylistExpandStates();
    };
    
    SmartLists.savePlaylistExpandStates = function() {
        try {
            const playlistCards = document.querySelectorAll('.playlist-card');
            const states = {};
            
            for (var i = 0; i < playlistCards.length; i++) {
                const card = playlistCards[i];
                const playlistId = card.getAttribute('data-playlist-id');
                const isExpanded = card.getAttribute('data-expanded') === 'true';
                if (playlistId) {
                    states[playlistId] = isExpanded;
                }
            }
            
            localStorage.setItem('smartListsExpandStates', JSON.stringify(states));
        } catch (err) {
            console.warn('Failed to save playlist expand states:', err);
        }
    };
    
    SmartLists.loadPlaylistExpandStates = function() {
        try {
            const saved = localStorage.getItem('smartListsExpandStates');
            if (!saved) return {};
            
            return JSON.parse(saved);
        } catch (err) {
            console.warn('Failed to load playlist expand states:', err);
            return {};
        }
    };
    
    SmartLists.restorePlaylistExpandStates = function(page) {
        const savedStates = SmartLists.loadPlaylistExpandStates();
        const playlistCards = page.querySelectorAll('.playlist-card');
        
        for (var i = 0; i < playlistCards.length; i++) {
            const card = playlistCards[i];
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
        }
    };
    
    SmartLists.updateExpandAllButtonText = function(page) {
        const expandAllBtn = page.querySelector('#expandAllBtn');
        const playlistCards = page.querySelectorAll('.playlist-card');
        
        if (!expandAllBtn || !playlistCards.length) return;
        
        // Count how many playlists are currently expanded
        let expandedCount = 0;
        for (var i = 0; i < playlistCards.length; i++) {
            if (playlistCards[i].getAttribute('data-expanded') === 'true') {
                expandedCount++;
            }
        }
        const totalCount = playlistCards.length;
        
        // Update button text based on current state
        if (expandedCount === totalCount) {
            expandAllBtn.textContent = 'Collapse All';
        } else {
            expandAllBtn.textContent = 'Expand All';
        }
    };
    
})(window.SmartLists = window.SmartLists || {});

