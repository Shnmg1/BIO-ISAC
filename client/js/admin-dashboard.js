// Admin Dashboard JavaScript
// This file handles all API calls and DOM updates for the admin dashboard

// Utility function to format dates
function formatDate(dateString) {
    if (!dateString) return '';
    const date = new Date(dateString);
    return date.toLocaleDateString('en-US', {
        month: 'short',
        day: 'numeric',
        hour: 'numeric',
        minute: '2-digit',
        hour12: true
    });
}

// Load dashboard statistics
async function loadDashboardStats() {
    try {
        const stats = await apiClient.get('/threats/stats');

        // Update stat cards
        const statCards = document.querySelectorAll('.stat-card');
        if (statCards.length >= 4) {
            statCards[0].querySelector('.stat-number').textContent = stats.runningEvents || 0;
            statCards[1].querySelector('.stat-number').textContent = stats.running247 || 0;
            statCards[2].querySelector('.stat-number').textContent = stats.past30Days || 0;
            statCards[3].querySelector('.stat-number').textContent = stats.total || 0;
        }
    } catch (error) {
        console.error('Error loading stats:', error);
        // Set to 0 on error
        document.querySelectorAll('.stat-card .stat-number').forEach(el => {
            if (el.textContent === '158' || el.textContent === '11' || el.textContent === '189' || el.textContent === '811') {
                el.textContent = '0';
            }
        });
    }
}

// Load incoming alerts
async function loadIncomingAlerts() {
    try {
        // Load all pending threats (both Pending_AI and Pending_Review) - don't filter by status
        // This will show all alerts that need admin review
        const response = await apiClient.get('/threats/incoming?limit=10');
        const alertsList = document.querySelector('.widget-card:nth-of-type(2) .task-list');

        if (!alertsList) {
            console.warn('Incoming alerts list not found');
            return;
        }

        // Clear existing alerts (keep structure)
        const existingItems = alertsList.querySelectorAll('.alert-item');
        existingItems.forEach(item => item.remove());

        // Populate with real data
        if (response.threats && response.threats.length > 0) {
            response.threats.forEach(threat => {
                const alertItem = document.createElement('li');
                alertItem.className = 'task-item alert-item';

                // Map impactLevel/urgency to color class
                // Normalize the priority value to handle various formats
                const priorityValue = (threat.impactLevel || 'Medium').toString().trim().toLowerCase();
                
                // Map various possible values to standard priority levels
                let normalizedPriority = 'medium'; // default
                if (priorityValue === 'low' || priorityValue === '1' || priorityValue === 'lowest') {
                    normalizedPriority = 'low';
                } else if (priorityValue === 'medium' || priorityValue === '2' || priorityValue === 'moderate') {
                    normalizedPriority = 'medium';
                } else if (priorityValue === 'high' || priorityValue === '3' || priorityValue === 'urgent') {
                    normalizedPriority = 'high';
                } else if (priorityValue === 'critical' || priorityValue === '4' || priorityValue === 'highest' || priorityValue === 'severe') {
                    normalizedPriority = 'critical';
                }
                
                const statusClass = `status-priority-${normalizedPriority}`;

                alertItem.innerHTML = `
                    <div class="task-status ${statusClass}"></div>
                    <a href="#" class="task-id">#${String(threat.id).padStart(5, '0')}</a>
                    <div style="flex: 1;">
                        <div class="task-time">${formatDate(threat.createdAt)}</div>
                        <div class="task-name">${threat.title || 'Untitled Threat'}</div>
                    </div>
                `;

                // Add data attributes for modal
                alertItem.dataset.alertId = threat.id;
                alertItem.dataset.alertTitle = threat.title || '';
                alertItem.dataset.alertDesc = threat.description || '';
                alertItem.dataset.alertPriority = threat.impactLevel || 'Medium';
                alertItem.dataset.alertIndustry = threat.category || 'Other';
                alertItem.dataset.alertType = threat.category || 'Other';
                alertItem.dataset.alertTime = threat.createdAt || '';
                alertItem.dataset.alertStatus = threat.status || 'Pending';
                alertItem.dataset.alertAssigned = threat.submitter || 'Unassigned';

                // Add click handler with error handling
                alertItem.addEventListener('click', async () => {
                    try {
                        await openAlertModal(threat.id);
                    } catch (error) {
                        // If threat doesn't exist, remove it from the list and refresh
                        if (error.message && (error.message.includes('not found') || error.message.includes('404'))) {
                            alertItem.remove();
                            // Refresh the list to get updated data
                            await loadIncomingAlerts();
                        }
                    }
                });

                alertsList.appendChild(alertItem);
            });
        } else {
            alertsList.innerHTML = '<li class="task-item"><div class="task-name">No incoming alerts</div></li>';
        }
    } catch (error) {
        console.error('Error loading incoming alerts:', error);
        const alertsList = document.querySelector('.widget-card:nth-of-type(2) .task-list');
        if (alertsList) {
            alertsList.innerHTML = '<li class="task-item"><div class="task-name">Error loading alerts</div></li>';
        }
    }
}

// Load my alerts widget
async function loadMyAlerts() {
    try {
        const response = await apiClient.get('/threats/user/my-alerts');

        // Update donut chart number
        const chartNumber = document.querySelector('.donut-chart .chart-number');
        if (chartNumber) {
            chartNumber.textContent = response.alerts?.length || 0;
        }

        // Update status counts in legend
        if (response.statusCounts) {
            const legendItems = document.querySelectorAll('.legend-item');
            legendItems.forEach(item => {
                const text = item.textContent.trim();
                if (text.includes('New')) {
                    item.querySelector('span').textContent = `New(${response.statusCounts.new || 0})`;
                } else if (text.includes('Suspended')) {
                    item.querySelector('span').textContent = `Suspended(${response.statusCounts.suspended || 0})`;
                } else if (text.includes('Due')) {
                    item.querySelector('span').textContent = `Due(${response.statusCounts.due || 0})`;
                } else if (text.includes('In Progress')) {
                    item.querySelector('span').textContent = `In Progress(${response.statusCounts.inProgress || 0})`;
                } else if (text.includes('Closed')) {
                    item.querySelector('span').textContent = `Closed(${response.statusCounts.closed || 0})`;
                }
            });
        }

        // Update alerts list in "My Alerts" widget
        const myAlertsList = document.querySelector('.widget-card:first-of-type .task-list');
        if (myAlertsList && response.alerts) {
            // Clear existing items (keep first few as examples if needed)
            const existingItems = myAlertsList.querySelectorAll('.task-item');
            if (existingItems.length > 0 && response.alerts.length > 0) {
                existingItems.forEach(item => item.remove());
            }

            // Add real alerts (limit to 3 for display)
            response.alerts.slice(0, 3).forEach(alert => {
                const item = document.createElement('div');
                item.className = 'task-item';

                // Map impactLevel/urgency to color class
                // Normalize the priority value to handle various formats
                const priorityValue = (alert.impactLevel || 'Medium').toString().trim().toLowerCase();
                
                // Map various possible values to standard priority levels
                let normalizedPriority = 'medium'; // default
                if (priorityValue === 'low' || priorityValue === '1' || priorityValue === 'lowest') {
                    normalizedPriority = 'low';
                } else if (priorityValue === 'medium' || priorityValue === '2' || priorityValue === 'moderate') {
                    normalizedPriority = 'medium';
                } else if (priorityValue === 'high' || priorityValue === '3' || priorityValue === 'urgent') {
                    normalizedPriority = 'high';
                } else if (priorityValue === 'critical' || priorityValue === '4' || priorityValue === 'highest' || priorityValue === 'severe') {
                    normalizedPriority = 'critical';
                }
                
                const statusClass = `status-priority-${normalizedPriority}`;

                item.innerHTML = `
                    <div class="task-status ${statusClass}"></div>
                    <a href="#" class="task-id">#${String(alert.id).padStart(5, '0')}</a>
                    <span class="task-name">${alert.title || 'Untitled'}</span>
                `;

                myAlertsList.appendChild(item);
            });
        }
    } catch (error) {
        console.error('Error loading my alerts:', error);
    }
}

// Open alert detail modal
async function openAlertModal(alertId) {
    try {
        if (!alertId) {
            console.error('Alert ID is required');
            alert('Error: Alert ID is missing');
            return;
        }

        const threat = await apiClient.getThreat(alertId);

        if (!threat) {
            alert('Error: Threat not found');
            return;
        }

        // Populate modal fields
        const modal = document.getElementById('alertModal');
        if (!modal) {
            console.error('Alert modal not found');
            alert('Error: Alert modal not found on page');
            return;
        }

        // Get all modal elements with null checks
        const modalAlertId = document.getElementById('modalAlertId');
        const modalAlertTitle = document.getElementById('modalAlertTitle');
        const modalAlertDesc = document.getElementById('modalAlertDesc');
        const modalAlertIndustry = document.getElementById('modalAlertIndustry');
        const modalAlertType = document.getElementById('modalAlertType');
        const modalAlertTime = document.getElementById('modalAlertTime');
        const modalAlertStatus = document.getElementById('modalAlertStatus');
        const modalAlertAssigned = document.getElementById('modalAlertAssigned');

        if (modalAlertId) modalAlertId.textContent = `#${String(threat.id).padStart(5, '0')} `;
        
        // Make title and description editable
        if (modalAlertTitle) {
            // Convert to input if it's a div
            if (modalAlertTitle.tagName === 'DIV') {
                const input = document.createElement('input');
                input.type = 'text';
                input.className = 'form-control';
                input.id = 'modalAlertTitle';
                input.value = threat.title || '';
                input.style.width = '100%';
                input.style.padding = '8px';
                input.style.color = '#ffffff';
                input.style.backgroundColor = '#000000';
                input.style.border = '1px solid var(--bio-light-green)';
                modalAlertTitle.parentNode.replaceChild(input, modalAlertTitle);
            } else {
                modalAlertTitle.value = threat.title || '';
            }
        }
        
        if (modalAlertDesc) {
            // Convert to textarea if it's a div
            if (modalAlertDesc.tagName === 'DIV') {
                const textarea = document.createElement('textarea');
                textarea.className = 'form-control';
                textarea.id = 'modalAlertDesc';
                textarea.value = threat.description || '';
                textarea.rows = 4;
                textarea.style.width = '100%';
                textarea.style.padding = '8px';
                textarea.style.color = '#ffffff';
                textarea.style.backgroundColor = '#000000';
                textarea.style.border = '1px solid var(--bio-light-green)';
                textarea.style.resize = 'vertical';
                modalAlertDesc.parentNode.replaceChild(textarea, modalAlertDesc);
            } else {
                modalAlertDesc.value = threat.description || '';
            }
        }

        // Set priority radio button
        const priority = threat.impactLevel || 'Medium';
        // Try exact match first, then try capitalized version
        let priorityRadio = modal.querySelector(`input[name="priority"][value="${priority}"]`);
        if (!priorityRadio && priority) {
            // Try with first letter capitalized
            const capitalizedPriority = priority.charAt(0).toUpperCase() + priority.slice(1).toLowerCase();
            priorityRadio = modal.querySelector(`input[name="priority"][value="${capitalizedPriority}"]`);
        }
        if (priorityRadio) {
            priorityRadio.checked = true;
        } else {
            // Default to Medium if not found
            const defaultRadio = modal.querySelector(`input[name="priority"][value="Medium"]`);
            if (defaultRadio) defaultRadio.checked = true;
        }

        if (modalAlertIndustry) modalAlertIndustry.textContent = threat.category || 'Other';
        if (modalAlertType) modalAlertType.textContent = threat.category || 'Other';
        if (modalAlertTime) modalAlertTime.textContent = formatDate(threat.createdAt);
        if (modalAlertStatus) modalAlertStatus.textContent = threat.status || 'Pending';
        if (modalAlertAssigned) modalAlertAssigned.textContent = 'Unassigned';

        // Store threat ID for update operations
        modal.dataset.threatId = threat.id;

        // Show modal
        // Check if it's a Bootstrap modal (has 'modal' class)
        if (typeof bootstrap !== 'undefined' && modal.classList.contains('modal')) {
            const bsModal = new bootstrap.Modal(modal);
            bsModal.show();
        } else {
            // Custom modal: show manually
            modal.classList.add('active');
        }
    } catch (error) {
        console.error('Error loading alert details:', error);
        const errorMessage = error?.message || error?.toString() || 'Unknown error occurred';
        
        // If threat not found, it might have been deleted or doesn't exist
        if (errorMessage.includes('not found') || errorMessage.includes('404')) {
            alert(`Threat #${alertId} not found. It may have been deleted. Refreshing alerts list...`);
            // Refresh the incoming alerts list to remove deleted threats
            if (typeof loadIncomingAlerts === 'function') {
                await loadIncomingAlerts();
            }
        } else {
            alert('Error loading alert details: ' + errorMessage);
        }
    }
}

// Handle create alert form
function setupCreateAlertForm() {
    const createBtn = document.querySelector('.create-alert-btn');
    if (!createBtn) return;

    createBtn.addEventListener('click', async function () {
        const form = this.closest('.create-alert-form');
        if (!form) return;

        const title = form.querySelector('input[type="text"]')?.value || '';
        const description = form.querySelector('textarea')?.value || '';
        const priority = form.querySelector('select')?.value || 'Medium';
        const industry = form.querySelectorAll('select')[1]?.value || 'Other';
        const type = form.querySelectorAll('select')[2]?.value || 'Other';

        // Validation
        if (!title || !description) {
            alert('Please fill in title and description');
            return;
        }

        // Show loading
        this.disabled = true;
        const originalText = this.innerHTML;
        this.innerHTML = '<i class="fas fa-spinner fa-spin"></i> Creating...';

        try {
            await apiClient.submitThreat({
                title: title,
                description: description,
                category: type || industry,
                impact_level: priority,
                date_observed: new Date().toISOString().split('T')[0],
                source: 'Manual'
            });

            // Clear form
            form.querySelector('input[type="text"]').value = '';
            form.querySelector('textarea').value = '';

            // Refresh incoming alerts
            await loadIncomingAlerts();
            await loadDashboardStats();

            alert('Alert created successfully!');
        } catch (error) {
            alert('Error creating alert: ' + error.message);
            console.error('Error:', error);
        } finally {
            this.disabled = false;
            this.innerHTML = originalText;
        }
    });
}

// Handle modal action buttons
function setupModalActions() {
    const modal = document.getElementById('alertModal');
    if (!modal) return;

    // Save/Update button
    const saveBtn = document.getElementById('saveAlertBtn');
    if (saveBtn) {
        saveBtn.addEventListener('click', async function () {
            const threatId = modal.dataset.threatId;
            if (!threatId) return;

            const title = document.getElementById('modalAlertTitle').textContent;
            const description = document.getElementById('modalAlertDesc').textContent;
            const priority = modal.querySelector('input[name="priority"]:checked')?.value || 'Medium';
            const type = document.getElementById('modalAlertType').textContent;

            try {
                // Note: Update endpoint may need to be created
                // Note: Update endpoint may need to be created
                await apiClient.put(`/threats/${threatId}`, {
                    title: title,
                    description: description,
                    impact_level: priority,
                    category: type
                });

                // Update DOM immediately
                // Try both string and number versions of threatId
                const threatIdStr = String(threatId);
                const threatIdNum = parseInt(threatId);
                let alertItem = document.querySelector(`.alert-item[data-alert-id="${threatIdStr}"]`);
                if (!alertItem && !isNaN(threatIdNum)) {
                    alertItem = document.querySelector(`.alert-item[data-alert-id="${threatIdNum}"]`);
                }
                
                if (alertItem) {
                    // Update data attributes
                    alertItem.dataset.alertPriority = priority;
                    alertItem.dataset.alertTitle = title;
                    alertItem.dataset.alertDesc = description;

                    // Update UI elements
                    const statusDiv = alertItem.querySelector('.task-status');
                    if (statusDiv) {
                        // Remove inline style first (if it exists) so classes can work
                        statusDiv.style.backgroundColor = '';
                        
                        // Get current class list and rebuild it without priority classes
                        const currentClasses = Array.from(statusDiv.classList);
                        const baseClasses = currentClasses.filter(cls => !cls.startsWith('status-priority-'));
                        
                        // Normalize priority value and determine new class
                        const priorityValue = priority.toString().trim().toLowerCase();
                        
                        // Map various possible values to standard priority levels
                        let normalizedPriority = 'medium'; // default
                        if (priorityValue === 'low' || priorityValue === '1' || priorityValue === 'lowest') {
                            normalizedPriority = 'low';
                        } else if (priorityValue === 'medium' || priorityValue === '2' || priorityValue === 'moderate') {
                            normalizedPriority = 'medium';
                        } else if (priorityValue === 'high' || priorityValue === '3' || priorityValue === 'urgent') {
                            normalizedPriority = 'high';
                        } else if (priorityValue === 'critical' || priorityValue === '4' || priorityValue === 'highest' || priorityValue === 'severe') {
                            normalizedPriority = 'critical';
                        }
                        
                        const newPriorityClass = `status-priority-${normalizedPriority}`;
                        
                        // Replace all classes with base classes + new priority class
                        statusDiv.className = [...baseClasses, newPriorityClass].join(' ');
                        
                        // Force style recalculation
                        void statusDiv.offsetHeight;
                    }

                    const nameDiv = alertItem.querySelector('.task-name');
                    if (nameDiv) nameDiv.textContent = title;
                }

                alert('Alert updated successfully!');

                // Close modal
                // Close modal
                if (typeof bootstrap !== 'undefined' && modal.classList.contains('modal')) {
                    const bsModal = bootstrap.Modal.getInstance(modal);
                    if (bsModal) bsModal.hide();
                } else {
                    modal.classList.remove('active');
                }

                // Refresh alerts
                await loadIncomingAlerts();
            } catch (error) {
                alert('Error updating alert: ' + error.message);
            }
        });
    }

    // Mark as Resolved button
    const resolveBtn = document.getElementById('markResolvedBtn');
    if (resolveBtn) {
        resolveBtn.addEventListener('click', async function () {
            const threatId = modal.dataset.threatId;
            if (!threatId) return;

            try {
                // Disable button to prevent double-clicks
                this.disabled = true;
                const originalText = this.innerHTML;
                this.innerHTML = '<i class="fas fa-spinner fa-spin"></i> Processing...';

                await apiClient.approveThreat(threatId, {
                    justification: 'Resolved by admin'
                });

                // Close modal first
                if (typeof bootstrap !== 'undefined' && modal.classList.contains('modal')) {
                    const bsModal = bootstrap.Modal.getInstance(modal);
                    if (bsModal) bsModal.hide();
                } else {
                    modal.classList.remove('active');
                }

                // Find and remove the specific alert item from the incoming alerts list
                const alertItem = document.querySelector(`.alert-item[data-alert-id="${threatId}"]`);
                if (alertItem) {
                    // Add fade-out animation
                    alertItem.style.transition = 'opacity 0.3s ease-out, transform 0.3s ease-out';
                    alertItem.style.opacity = '0';
                    alertItem.style.transform = 'translateX(-20px)';
                    setTimeout(() => {
                        alertItem.remove();
                        // Refresh incoming alerts after removal to ensure list is in sync
                        loadIncomingAlerts();
                    }, 300);
                } else {
                    // If item not found, refresh the list anyway
                    await loadIncomingAlerts();
                }

                // Refresh stats and my alerts
                await Promise.all([
                    loadDashboardStats(),
                    loadMyAlerts()
                ]);

                // Re-enable button
                this.disabled = false;
                this.innerHTML = originalText;

                alert('Alert marked as resolved!');
            } catch (error) {
                alert('Error resolving alert: ' + error.message);
                // Re-enable button on error
                if (resolveBtn) {
                    resolveBtn.disabled = false;
                }
            }
        });
    }

    // Close button
    const closeBtn = document.getElementById('closeModal');
    if (closeBtn) {
        closeBtn.addEventListener('click', function () {
            if (typeof bootstrap !== 'undefined' && modal.classList.contains('modal')) {
                const bsModal = bootstrap.Modal.getInstance(modal);
                if (bsModal) bsModal.hide();
            } else {
                modal.classList.remove('active');
            }
        });
    }
}

// Initialize dashboard on page load
document.addEventListener('DOMContentLoaded', async function () {
    // Check if user is logged in
    const user = apiClient.getCurrentUserSync();
    if (!user) {
        window.location.href = 'index.html';
        return;
    }

    // Setup create alert form
    setupCreateAlertForm();

    // Setup modal actions
    setupModalActions();

    // Load all dashboard data
    try {
        await Promise.all([
            loadDashboardStats(),
            loadIncomingAlerts(),
            loadMyAlerts()
        ]);
    } catch (error) {
        console.error('Error initializing dashboard:', error);
    }

    // Set up click handlers for existing alert items (if any)
    document.querySelectorAll('.alert-item').forEach(item => {
        item.addEventListener('click', async function () {
            const alertId = this.dataset.alertId;
            if (alertId) {
                try {
                    await openAlertModal(parseInt(alertId));
                } catch (error) {
                    // If threat doesn't exist, remove it from the list and refresh
                    if (error.message && (error.message.includes('not found') || error.message.includes('404'))) {
                        this.remove();
                        // Refresh the list to get updated data
                        await loadIncomingAlerts();
                    }
                }
            }
        });
    });
});

