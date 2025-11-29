async function loadAuditLogs(filters = {}) {
    try {
        let endpoint = '/admin/audit-logs?';
        if (filters.actionType) endpoint += `actionType=${encodeURIComponent(filters.actionType)}&`;
        if (filters.alertId) endpoint += `alertId=${encodeURIComponent(filters.alertId)}&`;
        if (filters.search) endpoint += `search=${encodeURIComponent(filters.search)}&`;
        if (filters.hours) endpoint += `hours=${filters.hours}&`;

        const response = await apiClient.get(endpoint);
        const tableBody = document.getElementById('auditLogBody');

        if (!tableBody) {
            console.warn('Audit log table body not found');
            return;
        }

        tableBody.innerHTML = '';

        if (response.logs && response.logs.length > 0) {
            response.logs.forEach(log => {
                const row = document.createElement('tr');

                const timestamp = log.timestamp ? formatDate(log.timestamp) : '';
                const actionType = log.actionType || 'Unknown';
                const threatId = log.threatId ? `#${String(log.threatId).padStart(5, '0')}` : 'N/A';
                const userDisplay = log.userId === 1
                    ? 'System'
                    : (log.userName && log.userName.trim().length > 0
                        ? log.userName
                        : (log.userId ? `User ${log.userId}` : 'N/A'));
                const details = log.details || '';

                const actionClassMap = {
                    'created': 'created',
                    'updated': 'updated',
                    'deleted': 'deleted',
                    'status-changed': 'status-changed',
                    'assigned': 'assigned'
                };
                const actionClass = actionClassMap[actionType.toLowerCase()] || 'default';

                row.innerHTML = `
                    <td>${timestamp}</td>
                    <td><span class="audit-action ${actionClass}">${actionType}</span></td>
                    <td>${threatId}</td>
                    <td>${userDisplay}</td>
                    <td>${details}</td>
                `;
                tableBody.appendChild(row);
            });
        } else {
            tableBody.innerHTML = '<tr><td colspan="5" style="text-align: center;">No audit logs found</td></tr>';
        }
    } catch (error) {
        console.error('Error loading audit logs:', error);
        const tableBody = document.getElementById('auditLogBody');
        if (tableBody) {
            tableBody.innerHTML = '<tr><td colspan="5" style="text-align: center;">Error loading audit logs</td></tr>';
        }
    }
}

function formatDate(dateString) {
    if (!dateString) return '';
    const date = new Date(dateString);
    return date.toLocaleDateString('en-US', {
        month: 'short',
        day: 'numeric',
        year: 'numeric',
        hour: 'numeric',
        minute: '2-digit',
        hour12: true
    });
}

document.addEventListener('DOMContentLoaded', function () {
    const auditLogPage = document.getElementById('audit-log-page');
    if (!auditLogPage) return;

    const observer = new MutationObserver(function (mutations) {
        if (auditLogPage.classList.contains('active')) {
            loadAuditLogs();
        }
    });

    observer.observe(auditLogPage, { attributes: true, attributeFilter: ['class'] });

    if (auditLogPage.classList.contains('active')) {
        loadAuditLogs();
    }

    const actionFilter = document.getElementById('auditFilterAction');
    if (actionFilter) {
        actionFilter.addEventListener('change', function () {
            loadAuditLogs({ actionType: this.value });
        });
    }

    const alertFilter = document.getElementById('auditFilterAlert');
    if (alertFilter) {
        alertFilter.addEventListener('change', function () {
            loadAuditLogs({ alertId: this.value });
        });
    }

    const searchInput = document.getElementById('auditSearch');
    if (searchInput) {
        let searchTimeout;
        searchInput.addEventListener('input', function () {
            clearTimeout(searchTimeout);
            searchTimeout = setTimeout(() => {
                loadAuditLogs({ search: this.value });
            }, 500);
        });
    }

    const downloadPdfBtn = document.getElementById('downloadPdfBtn');
    const downloadPdfDropdown = document.getElementById('downloadPdfDropdown');

    if (downloadPdfBtn && downloadPdfDropdown) {
        downloadPdfBtn.addEventListener('click', function (e) {
            e.stopPropagation();
            downloadPdfDropdown.classList.toggle('active');
        });

        downloadPdfDropdown.querySelectorAll('.download-pdf-option').forEach(option => {
            option.addEventListener('click', async function () {
                const hours = parseInt(this.dataset.hours);
                await downloadAuditLogPDF(hours);
                downloadPdfDropdown.classList.remove('active');
            });
        });
    }
});

async function downloadAuditLogPDF(hours) {
    try {
        if (typeof window.jspdf === 'undefined') {
            alert('PDF library not loaded. Please refresh the page.');
            return;
        }

        const { jsPDF } = window.jspdf;
        const doc = new jsPDF();

        const now = new Date();
        const cutoffTime = new Date(now.getTime() - (hours * 60 * 60 * 1000));

        let endpoint = '/admin/audit-logs?';
        endpoint += `startDate=${cutoffTime.toISOString()}&`;
        endpoint += `pageSize=10000&`;
        endpoint += `page=1`;

        const response = await apiClient.get(endpoint);
        const logs = response.logs || [];

        let filteredLogs = logs.filter(log => {
            if (!log.timestamp) return false;
            const logDate = new Date(log.timestamp);
            return logDate >= cutoffTime;
        });

        filteredLogs.sort((a, b) => {
            return new Date(b.timestamp) - new Date(a.timestamp);
        });

        // PDF Header
        doc.setFontSize(18);
        doc.setTextColor(107, 207, 143); // BIO-ISAC green
        doc.text('BIO-ISAC Audit Log', 14, 20);

        // Time range info
        doc.setFontSize(10);
        doc.setTextColor(100, 100, 100);
        const timeRangeText = hours === 1 ? 'Last 1 Hour' :
            hours === 4 ? 'Last 4 Hours' :
                hours === 24 ? 'Last 24 Hours' :
                    hours === 168 ? 'Last 7 Days' :
                        hours === 720 ? 'Last 30 Days' : 'All Logs';
        doc.text(`Time Range: ${timeRangeText}`, 14, 28);
        doc.text(`Generated: ${now.toLocaleString()}`, 14, 33);
        doc.text(`Total Entries: ${filteredLogs.length}`, 14, 38);

        // Table headers
        let yPos = 50;
        doc.setFontSize(10);
        doc.setTextColor(0, 0, 0);
        doc.setFillColor(45, 106, 63); // BIO-ISAC medium green
        doc.rect(14, yPos, 182, 8, 'F');
        doc.setTextColor(240, 249, 244);
        doc.setFont(undefined, 'bold');
        doc.text('Timestamp', 16, yPos + 6);
        doc.text('Action', 60, yPos + 6);
        doc.text('Threat ID', 90, yPos + 6);
        doc.text('User', 120, yPos + 6);
        doc.text('Details', 150, yPos + 6);

        // Table rows
        yPos += 12;
        doc.setFont(undefined, 'normal');
        doc.setTextColor(0, 0, 0);

        filteredLogs.forEach((log, index) => {
            // Check if we need a new page
            if (yPos > 270) {
                doc.addPage();
                yPos = 20;
            }

            // Alternate row colors
            if (index % 2 === 0) {
                doc.setFillColor(245, 245, 245);
                doc.rect(14, yPos - 6, 182, 8, 'F');
            }

            // Row data
            doc.setFontSize(8);
            const timestamp = log.timestamp ? formatDateForPDF(log.timestamp) : 'N/A';
            doc.text(timestamp, 16, yPos);
            doc.text(log.actionType || 'Unknown', 60, yPos);
            const threatId = log.threatId ? `#${String(log.threatId).padStart(5, '0')}` : 'N/A';
            doc.text(threatId, 90, yPos);
            // Use userName if available, otherwise fallback to User ID or System
            const userDisplay = log.userId === 1
                ? 'System'
                : (log.userName && log.userName.trim().length > 0
                    ? log.userName
                    : (log.userId ? `User ${log.userId}` : 'N/A'));
            doc.text(userDisplay, 120, yPos);

            // Wrap details text if too long
            const details = log.details || '';
            const detailsText = doc.splitTextToSize(details, 50);
            doc.text(detailsText, 150, yPos);

            yPos += Math.max(8, detailsText.length * 4);
        });

        // Footer
        const pageCount = doc.internal.getNumberOfPages();
        for (let i = 1; i <= pageCount; i++) {
            doc.setPage(i);
            doc.setFontSize(8);
            doc.setTextColor(100, 100, 100);
            doc.text(
                `Page ${i} of ${pageCount}`,
                105,
                doc.internal.pageSize.height - 10,
                { align: 'center' }
            );
        }

        // Generate filename
        const dateStr = now.toISOString().split('T')[0];
        const timeStr = hours === 1 ? '1hr' :
            hours === 4 ? '4hrs' :
                hours === 24 ? '24hrs' :
                    hours === 168 ? '7days' :
                        hours === 720 ? '30days' : 'all';
        const filename = `audit-log-${timeStr}-${dateStr}.pdf`;

        // Open PDF in a new browser tab instead of forcing download
        const pdfBlob = doc.output('blob');
        const pdfBlobUrl = URL.createObjectURL(pdfBlob);
        const newWindow = window.open(pdfBlobUrl, '_blank');
        
        // Clean up the blob URL after a delay (give the browser time to load it)
        if (newWindow) {
            setTimeout(() => URL.revokeObjectURL(pdfBlobUrl), 1000);
        } else {
            // If popup was blocked, fall back to download
            doc.save(filename);
            URL.revokeObjectURL(pdfBlobUrl);
        }
    } catch (error) {
        console.error('Error generating PDF:', error);
        alert('Error generating PDF: ' + error.message);
    }
}

function formatDateForPDF(dateString) {
    if (!dateString) return '';
    const date = new Date(dateString);
    return date.toLocaleString('en-US', {
        month: 'short',
        day: 'numeric',
        year: 'numeric',
        hour: 'numeric',
        minute: '2-digit',
        hour12: true
    });
}


