// Login Page JavaScript
document.addEventListener('DOMContentLoaded', function() {
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
                
                // Store user info
                if (response.user) {
                    localStorage.setItem('user', JSON.stringify(response.user));
                }
                
                // Redirect based on role
                if (response.user?.role === 'Admin') {
                    window.location.href = 'admin.html';
                } else {
                    window.location.href = 'reports.html';
                }
            } catch (error) {
                // Show error message
                alert('Login failed: ' + (error.message || 'Invalid credentials'));
                console.error('Login error:', error);
                
                // Reset button
                if (submitButton) {
                    submitButton.disabled = false;
                    submitButton.innerHTML = originalText;
                }
            }
        });
    }
});

