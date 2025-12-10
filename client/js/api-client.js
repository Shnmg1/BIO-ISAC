const API_BASE_URL = 'http://localhost:5064/api';

class ApiClient {
    constructor() {
        this.baseUrl = API_BASE_URL;
    }

    getAuthHeaders() {
        const headers = {
            'Content-Type': 'application/json'
        };
        const user = this.getCurrentUserSync();
        if (user && user.id) {
            headers['X-User-Id'] = user.id.toString();
        }
        return headers;
    }

    async request(endpoint, options = {}) {
        const url = `${this.baseUrl}${endpoint}`;
        const isFileOrigin = window.location.protocol === 'file:';
        const config = {
            ...options,
            credentials: isFileOrigin ? 'omit' : 'include',
            headers: {
                ...this.getAuthHeaders(),
                ...options.headers
            }
        };

        try {
            const response = await fetch(url, config);
            
            if (!response.ok) {
                let errorMessage = `HTTP error! status: ${response.status}`;
                try {
                    const errorData = await response.json();
                    errorMessage = errorData.message || errorData.error || errorData.title || errorMessage;
                } catch (jsonError) {
                    try {
                        const text = await response.text();
                        if (text) errorMessage = text;
                    } catch (textError) {
                    }
                }
                const error = new Error(errorMessage);
                error.status = response.status;
                throw error;
            }

            return await response.json();
        } catch (error) {
            console.error('API request failed:', error);
            if (error instanceof Error) {
                throw error;
            }
            throw new Error(error?.message || error?.toString() || 'An unknown error occurred');
        }
    }

    async get(endpoint) {
        return this.request(endpoint, { method: 'GET' });
    }

    async post(endpoint, data) {
        return this.request(endpoint, {
            method: 'POST',
            body: JSON.stringify(data)
        });
    }

    async put(endpoint, data) {
        return this.request(endpoint, {
            method: 'PUT',
            body: JSON.stringify(data)
        });
    }

    async delete(endpoint) {
        return this.request(endpoint, { method: 'DELETE' });
    }

    async login(email, password, rememberMe = false) {
        const response = await this.post('/auth/login', { email, password, rememberMe });
        if (response.user) {
            localStorage.setItem('user', JSON.stringify(response.user));
        }
        return response;
    }

    async setup2FA(userId) {
        return this.post('/auth/setup-2fa', { userId });
    }

    async verify2FASetup(userId, code) {
        const response = await this.post('/auth/verify-2fa-setup', { userId, code });
        if (response.user) {
            localStorage.setItem('user', JSON.stringify(response.user));
        }
        return response;
    }

    async verify2FALogin(userId, code) {
        const response = await this.post('/auth/verify-2fa-login', { userId, code });
        if (response.user) {
            localStorage.setItem('user', JSON.stringify(response.user));
        }
        return response;
    }

    async register(data) {
        return this.post('/auth/register', data);
    }

    async logout() {
        try {
            await this.post('/auth/logout', {});
        } finally {
            localStorage.removeItem('user');
        }
    }

    async getCurrentUser() {
        try {
            const response = await this.get('/auth/me');
            if (response.user) {
                localStorage.setItem('user', JSON.stringify(response.user));
                return response.user;
            }
            return null;
        } catch {
            localStorage.removeItem('user');
            return null;
        }
    }

    isAuthenticated() {
        const user = this.getCurrentUserSync();
        return !!user;
    }

    getCurrentUserSync() {
        const userStr = localStorage.getItem('user');
        return userStr ? JSON.parse(userStr) : null;
    }

    // Threat methods
    async submitThreat(threatData) {
        return this.post('/threats', threatData);
    }

    async getUserThreats(status = null) {
        const endpoint = status ? `/threats/user/submitted?status=${status}` : '/threats/user/submitted';
        return this.get(endpoint);
    }

    async getThreat(id) {
        return this.get(`/threats/${id}`);
    }

    async updateThreat(id, threatData) {
        return this.put(`/threats/${id}`, threatData);
    }

    async getProfile() {
        return this.get('/user/profile');
    }

    async updateProfile(profileData) {
        return this.put('/user/profile', profileData);
    }

    async changePassword(passwordData) {
        return this.put('/user/password', passwordData);
    }

    async getNotificationPreferences() {
        return this.get('/user/notification-preferences');
    }

    async updateNotificationPreferences(preferences) {
        return this.put('/user/notification-preferences', preferences);
    }

    async getPendingThreats() {
        return this.get('/admin/pending-threats');
    }

    async approveThreat(threatId, data) {
        return this.post(`/admin/threats/${threatId}/approve`, data);
    }

    async overrideThreat(threatId, data) {
        return this.post(`/admin/threats/${threatId}/override`, data);
    }

    async rejectThreat(threatId, data) {
        return this.post(`/admin/threats/${threatId}/reject`, data);
    }

    async getMessages(unreadOnly = false) {
        const endpoint = unreadOnly ? '/message?unreadOnly=true' : '/message';
        return this.get(endpoint);
    }

    async getMessage(id) {
        return this.get(`/message/${id}`);
    }

    async sendMessage(messageData) {
        return this.post('/message', messageData);
    }

    async markMessageAsRead(id) {
        return this.put(`/message/${id}/read`, {});
    }

    async getAdminInbox(unreadOnly = false, facilityType = null) {
        const params = [];
        if (unreadOnly) params.push('unreadOnly=true');
        if (facilityType) params.push(`facilityType=${encodeURIComponent(facilityType)}`);
        const queryString = params.length > 0 ? '?' + params.join('&') : '';
        return this.get(`/admin/message${queryString}`);
    }

    async replyToMessage(messageId, replyData) {
        return this.post(`/admin/message/${messageId}/reply`, replyData);
    }

    // User Management (Admin)
    async getPendingUsers() {
        return this.get('/admin/pending-users');
    }

    async getUserDetails(userId) {
        return this.get(`/admin/user/${userId}`);
    }

    async getUserStatistics() {
        return this.get('/admin/user-statistics');
    }

    async approveUser(userId, notes) {
        return this.post(`/admin/user/${userId}/approve`, { notes });
    }

    async rejectUser(userId, reason, notes) {
        return this.post(`/admin/user/${userId}/reject`, { reason, notes });
    }

    getDocumentUrl(userId, type) {
        return `${this.baseUrl}/admin/document/${userId}/${type}`;
    }

    // Active User Management
    async getActiveUsers(page = 1, pageSize = 20, search = '') {
        const params = new URLSearchParams({ page, pageSize });
        if (search) params.append('search', search);
        return this.get(`/admin/users?${params.toString()}`);
    }

    async promoteUser(userId) {
        return this.post(`/admin/users/${userId}/promote`, {});
    }

    async demoteUser(userId) {
        return this.post(`/admin/users/${userId}/demote`, {});
    }

    async editUser(userId, data) {
        return this.put(`/admin/users/${userId}`, data);
    }

    async deleteUser(userId) {
        return this.delete(`/admin/users/${userId}`);
    }

    async getUserAlerts() {
        return this.get('/threats/user/alerts');
    }

    async assignThreatToIndustries(threatId, request) {
        return this.post(`/threats/${threatId}/assign-industries`, request);
    }
}

const apiClient = new ApiClient();

