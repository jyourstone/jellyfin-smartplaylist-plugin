(function (SmartLists) {
    'use strict';

    // Initialize namespace if it doesn't exist
    if (!window.SmartLists) {
        window.SmartLists = {};
        SmartLists = window.SmartLists;
    }

    // ===== MULTI-SELECT USER COMPONENT =====

    /**
     * Initialize the multi-select user component for playlists
     */
    SmartLists.initializeUserMultiSelect = function (page) {
        // Prevent double-initialization
        if (page._userMultiSelectInitialized) {
            return;
        }

        const multiSelectContainer = page.querySelector('#playlistUserMultiSelect');
        if (!multiSelectContainer) return;

        const display = page.querySelector('#userMultiSelectDisplay');
        const dropdown = page.querySelector('#userMultiSelectDropdown');
        const options = page.querySelector('#userMultiSelectOptions');

        if (!display || !dropdown || !options) return;

        // Mark as initialized
        page._userMultiSelectInitialized = true;

        // Create AbortController for this component if it doesn't exist
        if (!page._userMultiSelectAbortController) {
            page._userMultiSelectAbortController = new AbortController();
        }

        // Track if dropdown is open
        let isOpen = false;

        // Toggle dropdown on display click
        display.addEventListener('click', function (e) {
            e.stopPropagation();
            isOpen = !isOpen;
            dropdown.style.display = isOpen ? 'block' : 'none';
            if (isOpen) {
                // Focus first checkbox when opening
                const firstCheckbox = options.querySelector('input[type="checkbox"]');
                if (firstCheckbox) {
                    firstCheckbox.focus();
                }
            }
        }, { signal: page._userMultiSelectAbortController.signal });

        // Close dropdown when clicking outside
        document.addEventListener('click', function (e) {
            if (isOpen && !multiSelectContainer.contains(e.target)) {
                isOpen = false;
                dropdown.style.display = 'none';
            }
        }, { signal: page._userMultiSelectAbortController.signal });

        // Prevent dropdown from closing when clicking inside
        dropdown.addEventListener('click', function (e) {
            e.stopPropagation();
        }, { signal: page._userMultiSelectAbortController.signal });

        // Update display when checkboxes change
        options.addEventListener('change', function (e) {
            if (e.target.type === 'checkbox') {
                SmartLists.updateUserMultiSelectDisplay(page);
                SmartLists.updatePublicCheckboxVisibility(page);
            }
        }, { signal: page._userMultiSelectAbortController.signal });

        // Add dropdown arrow to display (only if not already added)
        if (!display.querySelector('.multi-select-arrow')) {
            const arrow = document.createElement('span');
            arrow.className = 'multi-select-arrow';
            arrow.innerHTML = '▼';
            arrow.style.cssText = 'margin-left: auto; margin-right: 1em; font-size: 0.6em; color: #999;';
            display.appendChild(arrow);
        }
    };

    /**
     * Load users into the multi-select component
     */
    SmartLists.loadUsersIntoMultiSelect = function (page, users) {
        const options = page.querySelector('#userMultiSelectOptions');
        if (!options) return;

        // Preserve currently selected user IDs before clearing
        const currentlySelected = SmartLists.getSelectedUserIds(page);

        // Clear existing options
        options.innerHTML = '';

        if (!users || users.length === 0) {
            const noUsers = document.createElement('div');
            noUsers.className = 'multi-select-option';
            noUsers.style.padding = '0.5em';
            noUsers.style.color = '#999';
            noUsers.textContent = 'No users available';
            options.appendChild(noUsers);
            return;
        }

        // Create checkbox for each user
        users.forEach(function (user) {
            const option = document.createElement('div');
            option.className = 'multi-select-option';

            const label = document.createElement('label');
            label.className = 'emby-checkbox-label';
            label.style.cssText = 'display: flex; align-items: center; padding: 0.75em 1em; cursor: pointer;';

            const checkbox = document.createElement('input');
            checkbox.type = 'checkbox';
            checkbox.setAttribute('is', 'emby-checkbox');
            checkbox.setAttribute('data-embycheckbox', 'true');
            checkbox.className = 'emby-checkbox user-multi-select-checkbox';
            checkbox.value = user.Id;
            checkbox.id = 'userMultiSelect_' + user.Id;

            const checkboxLabel = document.createElement('span');
            checkboxLabel.className = 'checkboxLabel';
            checkboxLabel.textContent = user.Name || user.Username || user.Id;

            const checkboxOutline = document.createElement('span');
            checkboxOutline.className = 'checkboxOutline';

            const checkedIcon = document.createElement('span');
            checkedIcon.className = 'material-icons checkboxIcon checkboxIcon-checked check';
            checkedIcon.setAttribute('aria-hidden', 'true');
            // Empty content - icon rendered via CSS

            const uncheckedIcon = document.createElement('span');
            uncheckedIcon.className = 'material-icons checkboxIcon checkboxIcon-unchecked';
            uncheckedIcon.setAttribute('aria-hidden', 'true');
            // Empty content - icon rendered via CSS

            checkboxOutline.appendChild(checkedIcon);
            checkboxOutline.appendChild(uncheckedIcon);

            // Order: checkbox, label, outline (matching Jellyfin HTML)
            label.appendChild(checkbox);
            label.appendChild(checkboxLabel);
            label.appendChild(checkboxOutline);
            option.appendChild(label);
            options.appendChild(option);
        });

        // Restore previously selected user IDs after recreating checkboxes
        if (currentlySelected && currentlySelected.length > 0) {
            // Use setTimeout to ensure checkboxes are fully rendered
            setTimeout(function() {
                if (SmartLists.setSelectedUserIds) {
                    SmartLists.setSelectedUserIds(page, currentlySelected);
                }
            }, 0);
        }
    };

    /**
     * Get array of selected user IDs
     */
    SmartLists.getSelectedUserIds = function (page) {
        const checkboxes = page.querySelectorAll('#userMultiSelectOptions .user-multi-select-checkbox:checked');
        const userIds = [];
        checkboxes.forEach(function (checkbox) {
            if (checkbox.value) {
                userIds.push(checkbox.value);
            }
        });
        return userIds;
    };

    /**
     * Set selected users by user ID array
     */
    SmartLists.setSelectedUserIds = function (page, userIds) {
        if (!userIds || !Array.isArray(userIds)) {
            userIds = [];
        }

        const checkboxes = page.querySelectorAll('#userMultiSelectOptions .user-multi-select-checkbox');
        if (checkboxes.length === 0) {
            console.warn('SmartLists.setSelectedUserIds: No checkboxes found, users may not be loaded yet');
            return;
        }

        // Normalize userIds for comparison (remove dashes, lowercase)
        const normalizedUserIds = userIds.map(function(id) {
            return id ? String(id).replace(/-/g, '').toLowerCase() : '';
        });

        checkboxes.forEach(function (checkbox) {
            const checkboxValue = checkbox.value ? String(checkbox.value).replace(/-/g, '').toLowerCase() : '';
            checkbox.checked = normalizedUserIds.indexOf(checkboxValue) !== -1;
        });

        SmartLists.updateUserMultiSelectDisplay(page);
        SmartLists.updatePublicCheckboxVisibility(page);
    };

    /**
     * Update the display text showing selected users
     */
    SmartLists.updateUserMultiSelectDisplay = function (page) {
        const display = page.querySelector('#userMultiSelectDisplay');
        if (!display) return;

        const userIds = SmartLists.getSelectedUserIds(page);
        const placeholder = display.querySelector('.multi-select-placeholder');

        if (userIds.length === 0) {
            if (placeholder) {
                placeholder.textContent = 'Select users...';
                placeholder.style.display = 'inline';
            }
            // Hide any selected users display
            const selectedUsers = display.querySelector('.multi-select-selected-users');
            if (selectedUsers) {
                selectedUsers.remove();
            }
        } else {
            // Get user names
            const userNames = [];
            userIds.forEach(function (userId) {
                const checkbox = page.querySelector('#userMultiSelect_' + userId);
                if (checkbox) {
                    const label = checkbox.closest('label');
                    if (label) {
                        const labelText = label.querySelector('.checkboxLabel');
                        if (labelText) {
                            userNames.push(labelText.textContent);
                        }
                    }
                }
            });

            if (placeholder) {
                placeholder.style.display = 'none';
            }

            // Remove existing selected users display
            const existingSelected = display.querySelector('.multi-select-selected-users');
            if (existingSelected) {
                existingSelected.remove();
            }

            // Create new selected users display
            const selectedUsers = document.createElement('span');
            selectedUsers.className = 'multi-select-selected-users';

            // Show comma-separated names (no count)
            const displayText = userNames.length > 0 ? userNames.join(', ') : userIds.join(', ');
            selectedUsers.textContent = displayText;

            // Insert before the arrow (if it exists)
            const arrow = display.querySelector('.multi-select-arrow');
            if (arrow) {
                display.insertBefore(selectedUsers, arrow);
            } else {
                display.appendChild(selectedUsers);
            }
        }
    };

    /**
     * Update public checkbox visibility based on selected user count
     */
    SmartLists.updatePublicCheckboxVisibility = function (page) {
        const listType = SmartLists.getElementValue(page, '#listType', 'Playlist');
        const isCollection = listType === 'Collection';
        if (isCollection) {
            // Collections don't have public checkbox
            return;
        }

        const userIds = SmartLists.getSelectedUserIds(page);
        const publicCheckboxContainer = page.querySelector('#publicCheckboxContainer');

        if (publicCheckboxContainer) {
            if (userIds.length > 1) {
                // Hide public checkbox for multi-user playlists
                publicCheckboxContainer.style.display = 'none';
            } else {
                // Show public checkbox for single-user playlists
                publicCheckboxContainer.style.display = '';
            }
        }
    };

    /**
     * Cleanup function to be called on page navigation to prevent memory leaks
     */
    SmartLists.cleanupUserMultiSelect = function (page) {
        if (page._userMultiSelectAbortController) {
            page._userMultiSelectAbortController.abort();
            delete page._userMultiSelectAbortController;
        }
        // Reset initialization flag on cleanup
        page._userMultiSelectInitialized = false;
    };

})(window.SmartLists);

