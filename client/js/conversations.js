// Conversations Management
let conversations = new Map(); // Map<userId, {user, messages, unreadCount}>
let currentConversationUserId = null;
let currentUser = null; // We'll need to know who "we" are

async function loadConversations() {
  try {
    const storedUser = localStorage.getItem("user");
    if (storedUser) {
      currentUser = JSON.parse(storedUser);
    } else {
      console.warn("Current user not found in local storage");
      window.location.href = "/login.html";
      return;
    }

    let messages = [];
    if (currentUser && currentUser.role === "Admin") {
      try {
        messages = (await apiClient.get("/admin/message/all")) || [];
      } catch (error) {
        console.error("Error loading admin messages:", error);
        messages = [];
      }
    } else {
      try {
        messages = (await apiClient.get("/message")) || [];
      } catch (error) {
        console.error("Error loading messages:", error);
        messages = [];
      }
    }

    conversations.clear();
    const myId = currentUser.id;

    messages.forEach((msg) => {
      const fromUserId = msg.fromUserId ?? msg.from_user_id;
      const toUserId = msg.toUserId ?? msg.to_user_id;
      const fromUserName = msg.fromUserName ?? msg.from_user_name;
      const toUserName = msg.toUserName ?? msg.to_user_name;

      if (!fromUserId || !toUserId) return;

      let isIncoming, otherUserId, otherUserName, facilityName, facilityType;

      if (fromUserId === myId) {
        isIncoming = false;
        otherUserId = toUserId;
        otherUserName = toUserName || `User ${toUserId}`;
      } else {
        isIncoming = true;
        otherUserId = fromUserId;
        otherUserName = fromUserName || `User ${fromUserId}`;
      }

      facilityName = msg.facilityName ?? msg.facility_name;
      facilityType = msg.facilityType ?? msg.facility_type;

      if (!conversations.has(otherUserId)) {
        conversations.set(otherUserId, {
          userId: otherUserId,
          userName: otherUserName,
          facilityName: facilityName,
          facilityType: facilityType,
          messages: [],
          unreadCount: 0,
          lastMessage: null,
        });
      }

      const conversation = conversations.get(otherUserId);

      const processedMsg = {
        id: msg.id,
        fromUserId: fromUserId,
        toUserId: toUserId,
        fromUserName: fromUserName,
        toUserName: toUserName,
        subject: msg.subject || "",
        body: msg.body || msg.subject || "",
        threatId: msg.threatId ?? msg.threat_id,
        readAt: msg.readAt ?? msg.read_at,
        createdAt: msg.createdAt ?? msg.created_at ?? new Date(),
        isRead: msg.isRead ?? !!(msg.readAt ?? msg.read_at),
      };

      conversation.messages.push(processedMsg);

      if (isIncoming && !processedMsg.isRead) {
        conversation.unreadCount++;
      }

      const msgDate = new Date(processedMsg.createdAt);
      if (
        !conversation.lastMessage ||
        new Date(conversation.lastMessage.createdAt) < msgDate
      ) {
        conversation.lastMessage = processedMsg;
      }
    });

    renderConversationsList();

    if (currentConversationUserId) {
      renderMessages(currentConversationUserId);
    }
  } catch (error) {
    console.error("Error loading conversations:", error);
  }
}

function renderConversationsList() {
  const container = document.querySelector(".conversations-list");
  if (!container) return;

  container.innerHTML = "";

  // Sort conversations by last message date
  const sortedConversations = Array.from(conversations.values()).sort(
    (a, b) => {
      const dateA = a.lastMessage
        ? new Date(a.lastMessage.createdAt)
        : new Date(0);
      const dateB = b.lastMessage
        ? new Date(b.lastMessage.createdAt)
        : new Date(0);
      return dateB - dateA;
    }
  );

  sortedConversations.forEach((conv) => {
    const item = document.createElement("div");
    item.className = `conversation-item ${
      conv.userId === currentConversationUserId ? "active" : ""
    }`;
    item.dataset.userId = conv.userId;
    item.onclick = () => selectConversation(conv.userId);

    const initials = getInitials(conv.userName);
    const time = conv.lastMessage ? formatTime(conv.lastMessage.createdAt) : "";
    const preview = conv.lastMessage
      ? truncate(conv.lastMessage.body || conv.lastMessage.subject || "", 30)
      : "No messages";
    const facilityInfo = conv.facilityName ? ` - ${conv.facilityName}` : "";
    const facilityTypeInfo = conv.facilityType ? ` (${conv.facilityType})` : "";

    item.innerHTML = `
            <div class="conversation-avatar">${initials}</div>
            <div class="conversation-info">
                <div class="conversation-name">${
                  conv.userName
                }${facilityInfo}${facilityTypeInfo}</div>
                <div class="conversation-preview">${preview}</div>
            </div>
            <div class="conversation-meta">
                <div class="conversation-time">${time}</div>
                ${
                  conv.unreadCount > 0
                    ? `<div class="conversation-unread">${conv.unreadCount}</div>`
                    : ""
                }
            </div>
        `;

    container.appendChild(item);
  });
}

function selectConversation(userId) {
  currentConversationUserId = userId;
  renderConversationsList(); // To update 'active' class
  renderMessages(userId);
}

function renderMessages(userId) {
  const container = document.querySelector(".messages-container");
  const headerName = document.querySelector(".messages-header-name");
  const headerStatus = document.querySelector(".messages-header-status");
  const headerAvatar = document.querySelector(
    ".messages-header-user .conversation-avatar"
  );

  if (!container || !conversations.has(userId)) return;

  const conversation = conversations.get(userId);

  // Update Header
  if (headerName) headerName.textContent = conversation.userName;
  if (headerStatus) {
    if (conversation.facilityType) {
      headerStatus.textContent = conversation.facilityType;
    } else {
      headerStatus.textContent = ""; // Remove misleading "Active"
    }
  }
  if (headerAvatar)
    headerAvatar.textContent = getInitials(conversation.userName);

  container.innerHTML = "";

  // Sort messages by time ascending
  const sortedMessages = conversation.messages.sort((a, b) => {
    return new Date(a.createdAt) - new Date(b.createdAt);
  });

  const myId = currentUser ? currentUser.id : 1;

  sortedMessages.forEach((msg) => {
    // Normalize message fields
    const fromUserId =
      msg.fromUserId !== undefined
        ? msg.fromUserId
        : msg.from_user_id !== undefined
        ? msg.from_user_id
        : null;
    const isSent = fromUserId === myId;
    const msgDiv = document.createElement("div");
    msgDiv.className = `message ${isSent ? "sent" : "received"}`;

    const initials = isSent ? "ME" : getInitials(conversation.userName);
    const createdAt = msg.createdAt || msg.created_at;
    const time = createdAt ? formatTime(createdAt) : "";

    // Get message content - handle subject and body properly
    const subject = msg.subject ? msg.subject.trim() : "";
    const body = msg.body ? msg.body.trim() : "";

    let messageHtml = "";

    // If we have both subject and body, and they're different, show subject as header
    if (subject && body && subject !== body && !body.startsWith(subject)) {
      messageHtml = `
                <div class="message-subject">${escapeHtml(subject)}</div>
                <div class="message-body">${escapeHtml(body)}</div>
            `;
    } else if (body) {
      // If body exists, use it (even if same as subject, body is more complete)
      messageHtml = escapeHtml(body);
    } else if (subject) {
      // Fallback to subject if no body
      messageHtml = escapeHtml(subject);
    } else {
      messageHtml = "No content";
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
    const isRead =
      msg.isRead !== undefined
        ? msg.isRead
        : (msg.readAt || msg.read_at) !== null &&
          (msg.readAt || msg.read_at) !== undefined;
    const readAt = msg.readAt || msg.read_at;
    if (!isSent && !isRead && !readAt) {
      markAsRead(msg.id);
    }
  });

  // Scroll to bottom
  container.scrollTop = container.scrollHeight;
}

async function sendMessage() {
  const input = document.querySelector(".message-input");
  if (!input || !input.value.trim() || !currentConversationUserId) return;

  const body = input.value.trim();
  const storedUser = localStorage.getItem("user");
  const user = storedUser ? JSON.parse(storedUser) : null;

  try {
    if (user && user.role === "Admin") {
      // Admin replying to a user - use reply endpoint
      const conversation = conversations.get(currentConversationUserId);
      const lastMessage = conversation?.lastMessage;
      if (lastMessage) {
        await apiClient.replyToMessage(lastMessage.id, {
          subject: lastMessage.subject || "Re: Message",
          body: body,
        });
      } else {
        // Fallback to regular message endpoint
        await apiClient.post("/message", {
          toUserId: currentConversationUserId,
          subject: "Reply",
          body: body,
        });
      }
    } else {
      // Regular user sending message
      await apiClient.post("/message", {
        toUserId: currentConversationUserId,
        subject: "New Message",
        body: body,
      });
    }

    input.value = "";
    loadConversations(); // Refresh to show new message
  } catch (error) {
    console.error("Error sending message:", error);
    alert("Failed to send message");
  }
}

async function markAsRead(messageId) {
  try {
    await apiClient.put(`/message/${messageId}/read`);
    // We could update the local state, but reloading conversations is safer to sync everything
    // However, to avoid flickering, we might just update the local count.
    // For now, let's just let the next poll update it.
  } catch (error) {
    console.error("Error marking message as read:", error);
  }
}

// Helpers
function getInitials(name) {
  return name
    ? name
        .split(" ")
        .map((n) => n[0])
        .join("")
        .toUpperCase()
        .substring(0, 2)
    : "??";
}

function formatTime(dateString) {
  const date = new Date(dateString);
  return date.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
}

function truncate(str, n) {
  if (!str) return "";
  return str.length > n ? str.substr(0, n - 1) + "&hellip;" : str;
}

function escapeHtml(text) {
  const div = document.createElement("div");
  div.textContent = text;
  return div.innerHTML;
}

// Initialize
document.addEventListener("DOMContentLoaded", function () {
  // Check if we are on the conversations page or if it becomes active
  const conversationsPage = document.getElementById("conversations-page");

  if (conversationsPage) {
    // Initial load
    if (conversationsPage.classList.contains("active")) {
      loadConversations();
    }

    // Observer for visibility changes
    const observer = new MutationObserver(function (mutations) {
      if (conversationsPage.classList.contains("active")) {
        loadConversations();
      }
    });
    observer.observe(conversationsPage, {
      attributes: true,
      attributeFilter: ["class"],
    });

    // Poll for new messages every 10 seconds
    setInterval(() => {
      if (conversationsPage.classList.contains("active")) {
        loadConversations();
      }
    }, 10000);
  }

  // Bind Send Button
  const sendBtn = document.querySelector(".send-btn");
  if (sendBtn) {
    sendBtn.addEventListener("click", sendMessage);
  }

  // Bind Enter key in textarea
  const input = document.querySelector(".message-input");
  if (input) {
    input.addEventListener("keypress", function (e) {
      if (e.key === "Enter" && !e.shiftKey) {
        e.preventDefault();
        sendMessage();
      }
    });
  }

  // Bind New Conversation Button
  const newBtn = document.querySelector(".new-conversation-btn");
  if (newBtn) {
    newBtn.addEventListener("click", () => {
      openNewConversationModal();
    });
  }
});

// New Conversation Modal Functions
async function openNewConversationModal() {
  // Create modal if it doesn't exist
  let modal = document.getElementById("newConversationModal");
  if (!modal) {
    modal = document.createElement("div");
    modal.id = "newConversationModal";
    modal.className = "alert-modal";
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
    document
      .getElementById("closeNewConversationModal")
      .addEventListener("click", closeNewConversationModal);
    document
      .getElementById("cancelNewConversationBtn")
      .addEventListener("click", closeNewConversationModal);
    document
      .getElementById("sendNewConversationBtn")
      .addEventListener("click", sendNewConversation);

    // Close on backdrop click
    modal.addEventListener("click", function (e) {
      if (e.target === modal) {
        closeNewConversationModal();
      }
    });
  }

  // Populate user dropdown with all registered users
  const userSelect = document.getElementById("newConversationUserId");
  userSelect.innerHTML = '<option value="">Loading users...</option>';

  try {
    // Fetch all users from API
    const response = await apiClient.get("/user/all");
    const users = response.users || [];

    userSelect.innerHTML = '<option value="">Select a user...</option>';

    // Add all users to dropdown
    users.forEach((user) => {
      const option = document.createElement("option");
      option.value = user.id;
      const displayName = user.fullName || user.email;
      const facilityInfo = user.facilityName ? ` - ${user.facilityName}` : "";
      option.textContent = `${displayName}${facilityInfo}`;
      userSelect.appendChild(option);
    });

    // Also add users from existing conversations (in case they're not in the all users list)
    conversations.forEach((conv, userId) => {
      // Check if user already exists in dropdown
      const exists = Array.from(userSelect.options).some(
        (opt) => opt.value == userId
      );
      if (!exists) {
        const option = document.createElement("option");
        option.value = userId;
        option.textContent = `${conv.userName}${
          conv.facilityName ? " - " + conv.facilityName : ""
        }`;
        userSelect.appendChild(option);
      }
    });
  } catch (error) {
    console.error("Error loading users:", error);
    userSelect.innerHTML = '<option value="">Error loading users</option>';

    // Fallback: populate with users from existing conversations
    conversations.forEach((conv, userId) => {
      const option = document.createElement("option");
      option.value = userId;
      option.textContent = `${conv.userName}${
        conv.facilityName ? " - " + conv.facilityName : ""
      }`;
      userSelect.appendChild(option);
    });
  }

  // Clear form
  document.getElementById("newConversationSubject").value = "";
  document.getElementById("newConversationMessage").value = "";

  // Show modal
  modal.classList.add("active");
}

function closeNewConversationModal() {
  const modal = document.getElementById("newConversationModal");
  if (modal) {
    modal.classList.remove("active");
  }
}

async function sendNewConversation() {
  const userId = parseInt(
    document.getElementById("newConversationUserId").value
  );
  const subject = document
    .getElementById("newConversationSubject")
    .value.trim();
  const message = document
    .getElementById("newConversationMessage")
    .value.trim();

  if (!userId) {
    alert("Please select a user");
    return;
  }
  if (!subject) {
    alert("Please enter a subject");
    return;
  }
  if (!message) {
    alert("Please enter a message");
    return;
  }

  const sendBtn = document.getElementById("sendNewConversationBtn");
  const originalText = sendBtn.innerHTML;
  sendBtn.disabled = true;
  sendBtn.innerHTML = '<i class="fas fa-spinner fa-spin"></i> Sending...';

  try {
    const response = await apiClient.post("/message", {
      toUserId: userId,
      subject: subject,
      body: message,
    });

    closeNewConversationModal();

    // Small delay to ensure database is updated
    await new Promise((resolve) => setTimeout(resolve, 300));

    // Reload conversations to show the new message
    await loadConversations();

    // Wait a bit more and ensure conversation is loaded
    await new Promise((resolve) => setTimeout(resolve, 200));

    // Select the new conversation - it should now be in the list
    if (conversations.has(userId)) {
      selectConversation(userId);
    } else {
      // If conversation still doesn't exist, reload one more time
      await loadConversations();
      if (conversations.has(userId)) {
        selectConversation(userId);
      } else {
        console.warn(
          "Conversation not found after sending message, userId:",
          userId
        );
        // Still reload to show any new conversations
        await loadConversations();
      }
    }
  } catch (error) {
    console.error("Error sending new conversation:", error);
    alert("Error sending message: " + (error.message || "Unknown error"));
  } finally {
    sendBtn.disabled = false;
    sendBtn.innerHTML = originalText;
  }
}
