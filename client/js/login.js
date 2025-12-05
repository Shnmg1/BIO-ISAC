// Login Page JavaScript
document.addEventListener('DOMContentLoaded', function() {
    // State
    let currentUserId = null;
    let isSetupMode = false;

    // Password toggle functionality
    const togglePassword = document.getElementById('togglePassword');
    const passwordInput = document.getElementById('password');
    const eyeIcon = document.getElementById('eyeIcon');
    
    if (togglePassword && passwordInput && eyeIcon) {
        togglePassword.addEventListener('click', function() {
            if (passwordInput.type === 'password') {
                passwordInput.type = 'text';
                eyeIcon.classList.remove('fa-eye');
                eyeIcon.classList.add('fa-eye-slash');
            } else {
                passwordInput.type = 'password';
                eyeIcon.classList.remove('fa-eye-slash');
                eyeIcon.classList.add('fa-eye');
            }
        });
    }

    // Login form submission
    const loginForm = document.getElementById('loginForm');
    if (loginForm) {
        loginForm.addEventListener('submit', async function(e) {
            e.preventDefault();
            
            const usernameInput = document.getElementById('username');
            const passwordInput = document.getElementById('password');
            const rememberMeCheckbox = document.getElementById('rememberMe');
            
            const email = usernameInput?.value || '';
            const password = passwordInput?.value || '';
            const rememberMe = rememberMeCheckbox?.checked || false;
            
            // Show loading state
            const submitButton = loginForm.querySelector('button[type="submit"]');
            const originalText = submitButton?.innerHTML;
            if (submitButton) {
                submitButton.disabled = true;
                submitButton.innerHTML = '<i class="fas fa-spinner fa-spin"></i> Signing in...';
            }
            
            try {
                const response = await apiClient.login(email, password, rememberMe);
                
                if (response.requiresTwoFactorSetup) {
                    // First time login - show QR code setup
                    currentUserId = response.userId;
                    isSetupMode = true;
                    await showQrCodeSetup(response.userId);
                } else if (response.requiresTwoFactorCode) {
                    // Returning user - show TOTP input
                    currentUserId = response.userId;
                    isSetupMode = false;
                    showTotpInput();
                } else if (response.user) {
                    // Direct login (shouldn't happen with 2FA enforced)
                    localStorage.setItem('user', JSON.stringify(response.user));
                    redirectBasedOnRole(response.user);
                }
            } catch (error) {
                alert('Login failed: ' + (error.message || 'Invalid credentials'));
                console.error('Login error:', error);
            } finally {
                if (submitButton) {
                    submitButton.disabled = false;
                    submitButton.innerHTML = originalText;
                }
            }
        });
    }

    // QR Code Setup
    async function showQrCodeSetup(userId) {
        try {
            const response = await apiClient.setup2FA(userId);
            
            // Hide login form
            const loginSection = document.getElementById('loginForm');
            if (loginSection) loginSection.style.display = 'none';
            
            // Show setup section
            const setupSection = document.getElementById('setup-section');
            if (setupSection) {
                setupSection.style.display = 'block';
                
                const qrCodeImg = document.getElementById('qr-code');
                if (qrCodeImg) qrCodeImg.src = response.qrCode;
                
                const secretText = document.getElementById('secret-text');
                if (secretText) secretText.textContent = response.secret;
            }
        } catch (error) {
            alert('Failed to setup 2FA: ' + (error.message || 'Unknown error'));
            console.error('2FA setup error:', error);
        }
    }

    // Show TOTP Input for returning users
    function showTotpInput() {
        // Hide login form
        const loginSection = document.getElementById('loginForm');
        if (loginSection) loginSection.style.display = 'none';
        
        // Show TOTP section
        const totpSection = document.getElementById('totp-section');
        if (totpSection) {
            totpSection.style.display = 'block';
            const codeInput = document.getElementById('totp-code');
            if (codeInput) codeInput.focus();
        }
    }

    // Verify TOTP code
    window.verifyCode = async function() {
        const codeInput = isSetupMode 
            ? document.getElementById('setup-code') 
            : document.getElementById('totp-code');
        const code = codeInput?.value || '';
        
        if (code.length !== 6) {
            alert('Please enter a 6-digit code');
            return;
        }
        
        const verifyButton = isSetupMode 
            ? document.getElementById('confirm-setup-btn')
            : document.getElementById('verify-btn');
        const originalText = verifyButton?.innerHTML;
        
        if (verifyButton) {
            verifyButton.disabled = true;
            verifyButton.innerHTML = '<i class="fas fa-spinner fa-spin"></i> Verifying...';
        }
        
        try {
            let response;
            if (isSetupMode) {
                response = await apiClient.verify2FASetup(currentUserId, code);
            } else {
                response = await apiClient.verify2FALogin(currentUserId, code);
            }
            
            if (response.success && response.user) {
                redirectBasedOnRole(response.user);
            }
        } catch (error) {
            alert('Verification failed: ' + (error.message || 'Invalid code'));
            console.error('Verification error:', error);
            if (codeInput) {
                codeInput.value = '';
                codeInput.focus();
            }
        } finally {
            if (verifyButton) {
                verifyButton.disabled = false;
                verifyButton.innerHTML = originalText;
            }
        }
    };

    // Go back to login
    window.backToLogin = function() {
        currentUserId = null;
        isSetupMode = false;
        
        const loginSection = document.getElementById('loginForm');
        const setupSection = document.getElementById('setup-section');
        const totpSection = document.getElementById('totp-section');
        
        if (loginSection) loginSection.style.display = 'block';
        if (setupSection) setupSection.style.display = 'none';
        if (totpSection) totpSection.style.display = 'none';
        
        // Clear inputs
        const setupCode = document.getElementById('setup-code');
        const totpCode = document.getElementById('totp-code');
        if (setupCode) setupCode.value = '';
        if (totpCode) totpCode.value = '';
    };

    // Redirect based on user role
    function redirectBasedOnRole(user) {
        if (user.role === 'Admin') {
            window.location.href = 'admin.html';
        } else {
            window.location.href = 'reports.html';
        }
    }

    // Handle Enter key on TOTP inputs
    document.addEventListener('keypress', function(e) {
        if (e.key === 'Enter') {
            const setupCode = document.getElementById('setup-code');
            const totpCode = document.getElementById('totp-code');
            
            if (document.activeElement === setupCode || document.activeElement === totpCode) {
                e.preventDefault();
                verifyCode();
            }
        }
    });
});
