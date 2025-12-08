// Conversations Management
let conversations = new Map(); // Map<userId, {user, messages, unreadCount}>
let currentConversationUserId = null;
let currentUser = null; // We'll need to know who "we" are

async function loadConversations() {
    try {
        // Check if user is admin - admins should see all messages sent to any admin
        const storedUser = localStorage.getItem('user');
        if (storedUser) {
            currentUser = JSON.parse(storedUser);
        } else {
            console.warn('Current user not found in local storage');
        }

        let messages = [];
        if (currentUser && currentUser.role === 'Admin') {
            // For admins, get both incoming messages (to admins) and outgoing messages (from admins)
            try {
                const incomingMessages = await apiClient.getAdminInbox() || [];
                const allMessages = await apiClient.get('/message') || [];
                // Combine both - incoming from admin inbox, outgoing from regular endpoint
                // Filter allMessages to only get messages where admin is the sender
                // Normalize field names for comparison
                const outgoingMessages = Array.isArray(allMessages) 
                    ? allMessages.filter(msg => {
                        const fromId = msg.fromUserId || msg.from_user_id;
                        return fromId === currentUser.id;
                    })
                    : [];
                
                // Combine and remove duplicates
                const allMessagesCombined = [...incomingMessages, ...outgoingMessages];
                const uniqueMessages = new Map();
                allMessagesCombined.forEach(msg => {
                    if (!uniqueMessages.has(msg.id)) {
                        uniqueMessages.set(msg.id, msg);
                    }
                });
                messages = Array.from(uniqueMessages.values());
            } catch (error) {
                console.error('Error loading admin messages:', error);
                // Fallback: try to get all messages
                try {
                    const response = await apiClient.get('/message');
                    messages = response || [];
                } catch (fallbackError) {
                    console.error('Fallback error loading messages:', fallbackError);
                    messages = [];
                }
            }
        } else {
            // Regular users get their own messages
            const response = await apiClient.get('/message');
            messages = response || [];
        }

        // Process messages into conversations
        conversations.clear();

        // We need to know our own ID to determine who the "other" person is.
        // Since the API doesn't explicitly tell us "who am I" in this endpoint,
        // we might need to infer it or fetch it. 
        // For now, let's assume we can get it from the first message where we are the sender,
        // or we might need a separate /auth/me endpoint. 
        // However, the GetMessages endpoint returns all messages for the current user.
        // If we are logged in, we are one of the participants.

        const myId = currentUser ? currentUser.id : 1; // Default to 1 if not found (System/Admin)
        
        // For admins, all messages in the inbox are from users (incoming)
        // For regular users, we need to check if we're sender or recipient

        messages.forEach(msg => {
            // Normalize message fields (handle both camelCase and snake_case)
            const fromUserId = (msg.fromUserId !== undefined && msg.fromUserId !== null) ? msg.fromUserId : (msg.from_user_id !== undefined && msg.from_user_id !== null ? msg.from_user_id : null);
            const toUserId = (msg.toUserId !== undefined && msg.toUserId !== null) ? msg.toUserId : (msg.to_user_id !== undefined && msg.to_user_id !== null ? msg.to_user_id : null);
            const fromUserName = msg.fromUserName || msg.from_user_name || null;
            const toUserName = msg.toUserName || msg.to_user_name || null;
            
            // Skip messages that don't have valid user IDs
            if (!fromUserId || !toUserId) {
                console.warn('Skipping message with invalid user IDs:', msg);
                return;
            }
            
            // Determine if message is incoming or outgoing and who the other user is
            let isIncoming, otherUserId, otherUserName, facilityName, facilityType;
            
            if (currentUser && currentUser.role === 'Admin') {
                // For admins: check if we're sender or recipient
                if (fromUserId === myId) {
                    // Admin sent this message - it's outgoing
                    isIncoming = false;
                    otherUserId = toUserId;
                    otherUserName = toUserName || `User ${toUserId}`;
                    facilityName = msg.facilityName || msg.facility_name || null;
                    facilityType = msg.facilityType || msg.facility_type || null;
                } else {
                    // Admin received this message - it's incoming
                    isIncoming = true;
                    otherUserId = fromUserId;
                    otherUserName = fromUserName || `User ${fromUserId}`;
                    facilityName = msg.facilityName || msg.facility_name || null;
                    facilityType = msg.facilityType || msg.facility_type || null;
                }
            } else {
                // Regular user: determine if message is incoming or outgoing
                isIncoming = toUserId === myId;
                otherUserId = isIncoming ? fromUserId : toUserId;
                otherUserName = isIncoming ? (fromUserName || `User ${fromUserId}`) : (toUserName || `User ${toUserId}`);
                facilityName = msg.facilityName || msg.facility_name || null;
                facilityType = msg.facilityType || msg.facility_type || null;
            }

            if (!conversations.has(otherUserId)) {
                conversations.set(otherUserId, {
                    userId: otherUserId,
                    userName: otherUserName || `User ${otherUserId}`,
                    facilityName: facilityName,
                    facilityType: facilityType,
                    messages: [],
                    unreadCount: 0,
                    lastMessage: null
                });
            }

            const conversation = conversations.get(otherUserId);
            
            // Ensure message has all required fields (normalize field names)
            const processedMsg = {
                id: msg.id,
                fromUserId: msg.fromUserId !== undefined ? msg.fromUserId : (msg.from_user_id !== undefined ? msg.from_user_id : null),
                toUserId: msg.toUserId !== undefined ? msg.toUserId : (msg.to_user_id !== undefined ? msg.to_user_id : null),
                fromUserName: msg.fromUserName || msg.from_user_name || null,
                toUserName: msg.toUserName || msg.to_user_name || null,
                subject: msg.subject || '',
                body: msg.body || msg.subject || '',
                threatId: msg.threatId !== undefined ? msg.threatId : (msg.threat_id !== undefined ? msg.threat_id : null),
                readAt: msg.readAt || msg.read_at || null,
                createdAt: msg.createdAt || msg.created_at || new Date(),
                isRead: msg.isRead !== undefined ? msg.isRead : ((msg.readAt || msg.read_at) !== null && (msg.readAt || msg.read_at) !== undefined)
            };
            
            conversation.messages.push(processedMsg);

            if (isIncoming && !processedMsg.isRead && !processedMsg.readAt) {
                conversation.unreadCount++;
            }

            // Track last message for sorting
            const msgDate = new Date(processedMsg.createdAt);
            if (!conversation.lastMessage || new Date(conversation.lastMessage.createdAt) < msgDate) {
                conversation.lastMessage = processedMsg;
            }
        });

        // Get current search query to preserve it
        const searchInput = document.querySelector('.conversations-search .search-input');
        const searchQuery = searchInput ? searchInput.value : '';
        
        renderConversationsList(searchQuery);

        // If a conversation is selected, re-render it to show new messages
        if (currentConversationUserId) {
            renderMessages(currentConversationUserId);
        }

    } catch (error) {
        console.error('Error loading conversations:', error);
    }
}

function renderConversationsList(searchQuery = '') {
    const container = document.querySelector('.conversations-list');
    if (!container) return;

    container.innerHTML = '';

    // Sort conversations by last message date
    let sortedConversations = Array.from(conversations.values()).sort((a, b) => {
        const dateA = a.lastMessage ? new Date(a.lastMessage.createdAt) : new Date(0);
        const dateB = b.lastMessage ? new Date(b.lastMessage.createdAt) : new Date(0);
        return dateB - dateA;
    });

    // Filter by search query if provided
    if (searchQuery && searchQuery.trim()) {
        const query = searchQuery.toLowerCase().trim();
        sortedConversations = sortedConversations.filter(conv => {
            const userName = (conv.userName || '').toLowerCase();
            const facilityName = (conv.facilityName || '').toLowerCase();
            const facilityType = (conv.facilityType || '').toLowerCase();
            const preview = (conv.lastMessage ? (conv.lastMessage.body || conv.lastMessage.subject || '') : '').toLowerCase();
            return userName.includes(query) || 
                   facilityName.includes(query) || 
                   facilityType.includes(query) ||
                   preview.includes(query);
        });
    }

    // Show empty state if no conversations
    if (sortedConversations.length === 0) {
        const emptyState = document.createElement('div');
        emptyState.className = 'conversations-empty-state';
        emptyState.style.cssText = 'text-align: center; padding: 40px 20px; color: var(--bio-text-light); opacity: 0.6;';
        emptyState.innerHTML = `
            <i class="fas fa-comments" style="font-size: 48px; margin-bottom: 15px; opacity: 0.5;"></i>
            <p style="margin: 0; font-size: 14px;">${searchQuery ? 'No conversations match your search' : 'No conversations yet'}</p>
            ${!searchQuery ? '<p style="margin: 10px 0 0 0; font-size: 12px; opacity: 0.7;">Start a new conversation to get started</p>' : ''}
        `;
        container.appendChild(emptyState);
        return;
    }

    sortedConversations.forEach(conv => {
        const item = document.createElement('div');
        item.className = `conversation-item ${conv.userId === currentConversationUserId ? 'active' : ''}`;
        item.dataset.userId = conv.userId;
        
        // Click handler for selecting conversation
        item.addEventListener('click', (e) => {
             // Do not select if delete button was clicked
            if (e.target.closest('.delete-conversation-btn')) return;
            selectConversation(conv.userId);
        });

        const initials = getInitials(conv.userName);
        const time = conv.lastMessage ? formatTime(conv.lastMessage.createdAt) : '';
        const preview = conv.lastMessage ? truncate(conv.lastMessage.body || conv.lastMessage.subject || '', 30) : 'No messages';
        const facilityInfo = conv.facilityName ? ` - ${conv.facilityName}` : '';
        const facilityTypeInfo = conv.facilityType ? ` (${conv.facilityType})` : '';

        item.innerHTML = `
            <div class="conversation-avatar">${initials}</div>
            <div class="conversation-info">
                <div class="conversation-name">${escapeHtml(conv.userName)}${facilityInfo ? escapeHtml(facilityInfo) : ''}${facilityTypeInfo ? escapeHtml(facilityTypeInfo) : ''}</div>
                <div class="conversation-preview">${escapeHtml(preview)}</div>
            </div>
            <div class="conversation-meta">
                <div class="conversation-time">${time}</div>
                ${conv.unreadCount > 0 ? `<div class="conversation-unread">${conv.unreadCount}</div>` : ''}
            </div>
            <button class="delete-conversation-btn" title="Delete Conversation">
                <i class="fas fa-trash"></i>
            </button>
        `;

        // Attach delete handler
        const deleteBtn = item.querySelector('.delete-conversation-btn');
        if (deleteBtn) {
            deleteBtn.addEventListener('click', (e) => {
                e.stopPropagation();
                deleteConversation(conv.userId);
            });
        }

        container.appendChild(item);
    });
}

async function deleteConversation(userId) {
    if (!confirm('Are you sure you want to delete this conversation? This will delete all messages between you and this user.')) {
        return;
    }

    try {
        await apiClient.delete(`/admin/message/conversation/${userId}`);
        
        // Remove from map
        conversations.delete(userId);
        
        // If it was selected, clear selection
        if (currentConversationUserId === userId) {
            currentConversationUserId = null;
            
            // Clear messages container
            const container = document.querySelector('.messages-container');
            if (container) {
                container.innerHTML = `
                    <div style="text-align: center; padding: 40px 20px; color: var(--bio-text-light); opacity: 0.6;">
                        <i class="fas fa-inbox" style="font-size: 48px; margin-bottom: 15px; opacity: 0.5;"></i>
                        <p style="margin: 0; font-size: 14px;">Select a conversation to start messaging</p>
                    </div>
                `;
            }
            
            // Clear header
            const headerName = document.querySelector('.messages-header-name');
            const headerStatus = document.querySelector('.messages-header-status');
            const headerAvatar = document.querySelector('.messages-header-user .conversation-avatar');
            
            if(headerName) headerName.textContent = 'Select a Conversation';
            if(headerStatus) headerStatus.textContent = '';
            if(headerAvatar) headerAvatar.textContent = '';
            
            // Disable input
            const input = document.querySelector('.message-input');
            if (input) {
                input.disabled = true;
                input.placeholder = 'Select a conversation to start messaging...';
            }
        }
        
        // Refresh list
        const searchInput = document.querySelector('.conversations-search .search-input');
        renderConversationsList(searchInput ? searchInput.value : '');
        
    } catch (error) {
        console.error('Error deleting conversation:', error);
        alert('Failed to delete conversation: ' + (error.message || 'Unknown error'));
    }
}

function selectConversation(userId) {
    currentConversationUserId = userId;
    
    // Get current search query to preserve it
    const searchInput = document.querySelector('.conversations-search .search-input');
    const searchQuery = searchInput ? searchInput.value : '';
    
    renderConversationsList(searchQuery); // To update 'active' class
    renderMessages(userId);
    
    // Enable message input
    const messageInput = document.querySelector('.message-input');
    if (messageInput) {
        messageInput.disabled = false;
        messageInput.placeholder = 'Type a message...';
    }
}

function renderMessages(userId) {
    const container = document.querySelector('.messages-container');
    const headerName = document.querySelector('.messages-header-name');
    const headerStatus = document.querySelector('.messages-header-status');
    const headerAvatar = document.querySelector('.messages-header-user .conversation-avatar');

    if (!container || !conversations.has(userId)) {
        // Show empty state if conversation not found
        if (container) {
            container.innerHTML = `
                <div style="text-align: center; padding: 40px 20px; color: var(--bio-text-light); opacity: 0.6;">
                    <i class="fas fa-comment-slash" style="font-size: 48px; margin-bottom: 15px; opacity: 0.5;"></i>
                    <p style="margin: 0; font-size: 14px;">Conversation not found</p>
                </div>
            `;
        }
        return;
    }

    const conversation = conversations.get(userId);

    // Update Header
    if (headerName) headerName.textContent = conversation.userName;
    if (headerStatus) {
        const facilityInfo = conversation.facilityName ? ` - ${conversation.facilityName}` : '';
        const facilityTypeInfo = conversation.facilityType ? ` (${conversation.facilityType})` : '';
        headerStatus.textContent = facilityInfo || facilityTypeInfo || 'Active';
    }
    if (headerAvatar) headerAvatar.textContent = getInitials(conversation.userName);

    container.innerHTML = '';

    // Sort messages by time ascending
    const sortedMessages = conversation.messages.sort((a, b) => {
        return new Date(a.createdAt) - new Date(b.createdAt);
    });

    // Show empty state if no messages
    if (sortedMessages.length === 0) {
        container.innerHTML = `
            <div style="text-align: center; padding: 40px 20px; color: var(--bio-text-light); opacity: 0.6;">
                <i class="fas fa-comments" style="font-size: 48px; margin-bottom: 15px; opacity: 0.5;"></i>
                <p style="margin: 0; font-size: 14px;">No messages yet</p>
                <p style="margin: 10px 0 0 0; font-size: 12px; opacity: 0.7;">Start the conversation by sending a message</p>
            </div>
        `;
        return;
    }

    const myId = currentUser ? currentUser.id : 1;

    sortedMessages.forEach(msg => {
        // Normalize message fields
        const fromUserId = msg.fromUserId !== undefined ? msg.fromUserId : (msg.from_user_id !== undefined ? msg.from_user_id : null);
        const isSent = fromUserId === myId;
        const msgDiv = document.createElement('div');
        msgDiv.className = `message ${isSent ? 'sent' : 'received'}`;

        const initials = isSent ? 'ME' : getInitials(conversation.userName);
        const createdAt = msg.createdAt || msg.created_at;
        const time = createdAt ? formatTime(createdAt) : '';

        // Get message content - handle subject and body properly
        const subject = msg.subject ? msg.subject.trim() : '';
        const body = msg.body ? msg.body.trim() : '';
        
        // Display subject centered if it exists and is different from body
        if (subject && body && subject !== body && !body.startsWith(subject)) {
             const subjectDiv = document.createElement('div');
             subjectDiv.className = 'message-subject-header';
             subjectDiv.innerHTML = escapeHtml(subject);
             container.appendChild(subjectDiv);
        }
        
        let messageHtml = '';
        if (body) {
            messageHtml = escapeHtml(body);
        } else if (subject) {
             // If only subject exists or matches body, put it in bubble
             messageHtml = escapeHtml(subject);
        } else {
            messageHtml = 'No content';
        }
        
        msgDiv.innerHTML = `
            <div class="message-avatar">${initials}</div>
            <div class="message-content">
                <div class="message-bubble">${messageHtml}</div>
                <div class="message-time">${time}</div>
            </div>
        `;

        container.appendChild(msgDiv);

        // Mark as read if it's incoming and unread
        const isRead = msg.isRead !== undefined ? msg.isRead : ((msg.readAt || msg.read_at) !== null && (msg.readAt || msg.read_at) !== undefined);
        const readAt = msg.readAt || msg.read_at;
        if (!isSent && !isRead && !readAt) {
            markAsRead(msg.id);
        }
    });

    // Scroll to bottom
    container.scrollTop = container.scrollHeight;
}

async function sendMessage() {
    const input = document.querySelector('.message-input');
    if (!input || !input.value.trim() || !currentConversationUserId) return;

    const body = input.value.trim();
    const storedUser = localStorage.getItem('user');
    const user = storedUser ? JSON.parse(storedUser) : null;

    try {
        if (user && user.role === 'Admin') {
            // Admin replying to a user - use reply endpoint
            const conversation = conversations.get(currentConversationUserId);
            const lastMessage = conversation?.lastMessage;
            if (lastMessage) {
                await apiClient.replyToMessage(lastMessage.id, {
                    subject: lastMessage.subject || 'Re: Message',
                    body: body
                });
            } else {
                // Fallback to regular message endpoint
                await apiClient.post('/message', {
                    toUserId: currentConversationUserId,
                    subject: 'Reply',
                    body: body
                });
            }
        } else {
            // Regular user sending message
            await apiClient.post('/message', {
                toUserId: currentConversationUserId,
                subject: 'New Message',
                body: body
            });
        }

        input.value = '';
        loadConversations(); // Refresh to show new message
    } catch (error) {
        console.error('Error sending message:', error);
        alert('Failed to send message');
    }
}

async function markAsRead(messageId) {
    try {
        await apiClient.put(`/message/${messageId}/read`);
        // We could update the local state, but reloading conversations is safer to sync everything
        // However, to avoid flickering, we might just update the local count.
        // For now, let's just let the next poll update it.
    } catch (error) {
        console.error('Error marking message as read:', error);
    }
}

// Helpers
function getInitials(name) {
    return name ? name.split(' ').map(n => n[0]).join('').toUpperCase().substring(0, 2) : '??';
}

function formatTime(dateString) {
    const date = new Date(dateString);
    return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
}

function truncate(str, n) {
    if (!str) return '';
    return (str.length > n) ? str.substr(0, n - 1) + '&hellip;' : str;
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

// Initialize
document.addEventListener('DOMContentLoaded', function () {
    // Check if we are on the conversations page or if it becomes active
    const conversationsPage = document.getElementById('conversations-page');

    if (conversationsPage) {
        // Function to check if page is visible
        function isPageVisible() {
            return conversationsPage.classList.contains('active') || 
                   conversationsPage.style.display === 'block' ||
                   (conversationsPage.style.display !== 'none' && conversationsPage.offsetParent !== null);
        }

        // Initial load if page is visible
        if (isPageVisible()) {
            loadConversations();
        }

        // Observer for visibility changes (both class and style changes)
        const observer = new MutationObserver(function (mutations) {
            if (isPageVisible()) {
                loadConversations();
            }
        });
        observer.observe(conversationsPage, { 
            attributes: true, 
            attributeFilter: ['class'],
            attributeOldValue: false
        });
        
        // Also observe style changes
        const styleObserver = new MutationObserver(function (mutations) {
            if (isPageVisible()) {
                loadConversations();
            }
        });
        styleObserver.observe(conversationsPage, {
            attributes: true,
            attributeFilter: ['style']
        });

        // Poll for new messages every 10 seconds when page is visible
        setInterval(() => {
            if (isPageVisible()) {
                loadConversations();
            }
        }, 10000);
    }

    // Bind Send Button
    const sendBtn = document.querySelector('.send-btn');
    if (sendBtn) {
        sendBtn.addEventListener('click', sendMessage);
    }

    // Bind Enter key in textarea
    const input = document.querySelector('.message-input');
    if (input) {
        // Initially disable input until a conversation is selected
        input.disabled = true;
        input.placeholder = 'Select a conversation to start messaging...';
        
        input.addEventListener('keypress', function (e) {
            if (e.key === 'Enter' && !e.shiftKey && !input.disabled) {
                e.preventDefault();
                sendMessage();
            }
        });
    }

    // Bind New Conversation Button
    const newBtn = document.querySelector('.new-conversation-btn');
    if (newBtn) {
        newBtn.addEventListener('click', () => {
            openNewConversationModal();
        });
    }

    // Bind Search Input
    const searchInput = document.querySelector('.conversations-search .search-input');
    if (searchInput) {
        let searchTimeout;
        searchInput.addEventListener('input', function(e) {
            clearTimeout(searchTimeout);
            const query = e.target.value;
            searchTimeout = setTimeout(() => {
                renderConversationsList(query);
            }, 300); // Debounce search
        });
    }
});

// New Conversation Modal Functions
async function openNewConversationModal() {
    // Create modal if it doesn't exist
    let modal = document.getElementById('newConversationModal');
    if (!modal) {
        modal = document.createElement('div');
        modal.id = 'newConversationModal';
        modal.className = 'alert-modal';
        modal.innerHTML = `
            <div class="alert-modal-content">
                <div class="alert-modal-header">
                    <h2><i class="fas fa-plus"></i> New Conversation</h2>
                    <button class="alert-modal-close" id="closeNewConversationModal">
                        <i class="fas fa-times"></i>
                    </button>
                </div>
                <div class="alert-modal-body">
                    <div class="form-group">
                        <label class="form-label">Select User</label>
                        <select class="form-control" id="newConversationUserId" style="width: 100%; padding: 8px; margin-top: 5px;">
                            <option value="">Select a user...</option>
                        </select>
                    </div>
                    <div class="form-group">
                        <label class="form-label">Subject</label>
                        <input type="text" class="form-control" id="newConversationSubject" placeholder="Enter subject..." required style="width: 100%; padding: 8px; margin-top: 5px;">
                    </div>
                    <div class="form-group">
                        <label class="form-label">Message</label>
                        <textarea class="form-control" id="newConversationMessage" placeholder="Enter your message..." rows="5" required style="width: 100%; padding: 8px; margin-top: 5px; resize: vertical;"></textarea>
                    </div>
                </div>
                <div class="alert-modal-actions">
                    <button class="alert-modal-btn alert-modal-btn-primary" id="sendNewConversationBtn">
                        <i class="fas fa-paper-plane"></i> Send
                    </button>
                    <button class="alert-modal-btn alert-modal-btn-outline" id="cancelNewConversationBtn">
                        Cancel
                    </button>
                </div>
            </div>
        `;
        document.body.appendChild(modal);

        // Add event listeners
        document.getElementById('closeNewConversationModal').addEventListener('click', closeNewConversationModal);
        document.getElementById('cancelNewConversationBtn').addEventListener('click', closeNewConversationModal);
        document.getElementById('sendNewConversationBtn').addEventListener('click', sendNewConversation);
        
        // Close on backdrop click
        modal.addEventListener('click', function(e) {
            if (e.target === modal) {
                closeNewConversationModal();
            }
        });
    }

    // Populate user dropdown with all registered users
    const userSelect = document.getElementById('newConversationUserId');
    userSelect.innerHTML = '<option value="">Loading users...</option>';
    
    try {
        // Fetch all users from API
        const response = await apiClient.get('/user/all');
        const users = response.users || [];
        
        userSelect.innerHTML = '<option value="">Select a user...</option>';
        
        // Add all users to dropdown
        users.forEach(user => {
            const option = document.createElement('option');
            option.value = user.id;
            const displayName = user.fullName || user.email;
            const facilityInfo = user.facilityName ? ` - ${user.facilityName}` : '';
            option.textContent = `${displayName}${facilityInfo}`;
            userSelect.appendChild(option);
        });
        
        // Also add users from existing conversations (in case they're not in the all users list)
        conversations.forEach((conv, userId) => {
            // Check if user already exists in dropdown
            const exists = Array.from(userSelect.options).some(opt => opt.value == userId);
            if (!exists) {
                const option = document.createElement('option');
                option.value = userId;
                option.textContent = `${conv.userName}${conv.facilityName ? ' - ' + conv.facilityName : ''}`;
                userSelect.appendChild(option);
            }
        });
    } catch (error) {
        console.error('Error loading users:', error);
        userSelect.innerHTML = '<option value="">Error loading users</option>';
        
        // Fallback: populate with users from existing conversations
        conversations.forEach((conv, userId) => {
            const option = document.createElement('option');
            option.value = userId;
            option.textContent = `${conv.userName}${conv.facilityName ? ' - ' + conv.facilityName : ''}`;
            userSelect.appendChild(option);
        });
    }

    // Clear form
    document.getElementById('newConversationSubject').value = '';
    document.getElementById('newConversationMessage').value = '';

    // Show modal
    modal.classList.add('active');
}

function closeNewConversationModal() {
    const modal = document.getElementById('newConversationModal');
    if (modal) {
        modal.classList.remove('active');
    }
}

async function sendNewConversation() {
    const userId = parseInt(document.getElementById('newConversationUserId').value);
    const subject = document.getElementById('newConversationSubject').value.trim();
    const message = document.getElementById('newConversationMessage').value.trim();

    if (!userId) {
        alert('Please select a user');
        return;
    }
    if (!subject) {
        alert('Please enter a subject');
        return;
    }
    if (!message) {
        alert('Please enter a message');
        return;
    }

    const sendBtn = document.getElementById('sendNewConversationBtn');
    const originalText = sendBtn.innerHTML;
    sendBtn.disabled = true;
    sendBtn.innerHTML = '<i class="fas fa-spinner fa-spin"></i> Sending...';

    try {
        const response = await apiClient.post('/message', {
            toUserId: userId,
            subject: subject,
            body: message
        });

        closeNewConversationModal();
        
        // Small delay to ensure database is updated
        await new Promise(resolve => setTimeout(resolve, 300));
        
        // Reload conversations to show the new message
        await loadConversations();
        
        // Wait a bit more and ensure conversation is loaded
        await new Promise(resolve => setTimeout(resolve, 200));
        
        // Select the new conversation - it should now be in the list
        if (conversations.has(userId)) {
            selectConversation(userId);
        } else {
            // If conversation still doesn't exist, reload one more time
            await loadConversations();
            if (conversations.has(userId)) {
                selectConversation(userId);
            } else {
                console.warn('Conversation not found after sending message, userId:', userId);
                // Still reload to show any new conversations
                await loadConversations();
            }
        }
    } catch (error) {
        console.error('Error sending new conversation:', error);
        alert('Error sending message: ' + (error.message || 'Unknown error'));
    } finally {
        sendBtn.disabled = false;
        sendBtn.innerHTML = originalText;
    }
}
