/**
 * Accessibility Module - Colorblind Support
 * Provides color transformations for different types of colorblindness
 */

const AccessibilityManager = {
    // SVG filters for colorblind simulation
    svgFilters: `
        <svg style="position: absolute; width: 0; height: 0;">
            <defs>
                <!-- Protanopia filter (red-blind) -->
                <filter id="protanopia-filter" color-interpolation-filters="sRGB">
                    <feColorMatrix type="matrix" values="0.567 0.433 0 0 0 0.558 0.442 0 0 0 0 0.242 0.758 0 0 0 0 0 1 0"/>
                </filter>
                <!-- Deuteranopia filter (green-blind) -->
                <filter id="deuteranopia-filter" color-interpolation-filters="sRGB">
                    <feColorMatrix type="matrix" values="0.625 0.375 0 0 0 0.7 0.3 0 0 0 0 0.3 0.7 0 0 0 0 0 1 0"/>
                </filter>
                <!-- Tritanopia filter (blue-blind) -->
                <filter id="tritanopia-filter" color-interpolation-filters="sRGB">
                    <feColorMatrix type="matrix" values="0.95 0.05 0 0 0 0 0.433 0.567 0 0 0 0.475 0.525 0 0 0 0 0 1 0"/>
                </filter>
            </defs>
        </svg>
    `,

    init() {
        // Inject SVG filters into the page
        if (!document.getElementById('accessibility-svg-filters')) {
            const svgContainer = document.createElement('div');
            svgContainer.id = 'accessibility-svg-filters';
            svgContainer.innerHTML = this.svgFilters;
            document.body.appendChild(svgContainer);
        }

        // Load saved preference
        const savedMode = localStorage.getItem('colorblindMode') || 'normal';
        this.applyColorblindMode(savedMode);

        // Set up modal handlers
        this.setupModalHandlers();
    },

    setupModalHandlers() {
        const modal = document.getElementById('accessibilityModal');
        const closeBtn = document.getElementById('accessibilityModalClose');
        const accessibilityMenuItem = document.getElementById('accessibilityMenuItem');
        const options = document.querySelectorAll('.accessibility-option');
        const radioInputs = document.querySelectorAll('input[name="colorblindMode"]');

        // Open modal when Accessibility menu item is clicked
        if (accessibilityMenuItem) {
            accessibilityMenuItem.addEventListener('click', (e) => {
                e.stopPropagation();
                const settingsDropdown = document.getElementById('settingsDropdown');
                if (settingsDropdown) {
                    settingsDropdown.classList.remove('active');
                }
                this.openModal();
            });
        }

        // Close modal
        if (closeBtn) {
            closeBtn.addEventListener('click', () => {
                this.closeModal();
            });
        }

        // Close modal when clicking outside
        if (modal) {
            modal.addEventListener('click', (e) => {
                if (e.target === modal) {
                    this.closeModal();
                }
            });
        }

        // Handle option selection
        options.forEach(option => {
            option.addEventListener('click', (e) => {
                const radio = option.querySelector('input[type="radio"]');
                if (radio) {
                    radio.checked = true;
                    this.selectOption(option);
                    const mode = radio.value;
                    this.applyColorblindMode(mode);
                    localStorage.setItem('colorblindMode', mode);
                }
            });
        });

        // Handle radio input changes
        radioInputs.forEach(input => {
            input.addEventListener('change', (e) => {
                const mode = e.target.value;
                this.applyColorblindMode(mode);
                localStorage.setItem('colorblindMode', mode);
                this.updateSelectedOption();
            });
        });

        // Set initial selected option
        this.updateSelectedOption();
    },

    openModal() {
        const modal = document.getElementById('accessibilityModal');
        if (modal) {
            modal.classList.add('active');
            // Set the current selection
            const savedMode = localStorage.getItem('colorblindMode') || 'normal';
            const radio = document.querySelector(`input[value="${savedMode}"]`);
            if (radio) {
                radio.checked = true;
            }
            this.updateSelectedOption();
        }
    },

    closeModal() {
        const modal = document.getElementById('accessibilityModal');
        if (modal) {
            modal.classList.remove('active');
        }
    },

    selectOption(optionElement) {
        // Remove selected class from all options
        document.querySelectorAll('.accessibility-option').forEach(opt => {
            opt.classList.remove('selected');
        });
        // Add selected class to clicked option
        if (optionElement) {
            optionElement.classList.add('selected');
        }
    },

    updateSelectedOption() {
        const selectedRadio = document.querySelector('input[name="colorblindMode"]:checked');
        if (selectedRadio) {
            const option = selectedRadio.closest('.accessibility-option');
            this.selectOption(option);
        }
    },

    applyColorblindMode(mode) {
        // Remove any existing colorblind class
        document.body.classList.remove('colorblind-protanopia', 'colorblind-deuteranopia', 'colorblind-tritanopia');

        if (mode === 'normal') {
            // Remove filter from body
            document.body.style.filter = 'none';
            // Reset all CSS variables to default
            this.resetColorVariables();
        } else {
            // Apply color variable transformations (primary method)
            this.applyColorTransformations(mode);
            document.body.classList.add(`colorblind-${mode}`);
            
            // Also apply CSS filter as additional support
            // Using a wrapper approach for better compatibility
            const filterValue = this.getFilterValue(mode);
            if (filterValue && filterValue !== 'none') {
                // Apply filter to body for overall color adjustment
                document.body.style.filter = filterValue;
            } else {
                document.body.style.filter = 'none';
            }
        }
    },

    getFilterValue(mode) {
        // Return CSS filter matrix values for better browser support
        const filters = {
            normal: 'none',
            protanopia: 'url(#protanopia-filter)',
            deuteranopia: 'url(#deuteranopia-filter)',
            tritanopia: 'url(#tritanopia-filter)'
        };
        return filters[mode] || 'none';
    },

    resetColorVariables() {
        // Reset to default colors - remove any overrides
        const root = document.documentElement;
        // The default colors are already defined in CSS, so we just need to remove overrides
        const variablesToReset = [
            '--bio-bright-green',
            '--bio-light-green',
            '--bio-medium-green',
            '--bio-accent-green',
            '--wst-color-action',
            '--color_18',
            '--color_41',
            '--color_48'
        ];
        variablesToReset.forEach(variable => {
            root.style.setProperty(variable, '');
        });
    },

    applyColorTransformations(mode) {
        const root = document.documentElement;
        
        // Color transformations for different colorblind types
        // These are alternative color palettes that are more distinguishable
        // Based on research into colorblind-friendly palettes
        const colorPalettes = {
            protanopia: {
                // Use blue-cyan palette instead of green for better visibility
                // Protanopia affects red perception, so blue/cyan works well
                '--bio-bright-green': '#4FC3F7', // Light cyan/blue
                '--bio-light-green': '#29B6F6', // Cyan
                '--bio-medium-green': '#0288D1', // Blue
                '--bio-accent-green': '#0277BD', // Darker blue
                '--wst-color-action': '#4FC3F7',
                '--color_18': '79, 195, 247', // RGB for --wst-color-action
                '--color_41': '79, 195, 247', // RGB for accent
                '--color_48': '79, 195, 247' // RGB for button primary
            },
            deuteranopia: {
                // Similar to protanopia - use blue tones
                // Deuteranopia affects green perception, blue is still visible
                '--bio-bright-green': '#5C9BD1', // Light blue
                '--bio-light-green': '#3F7FC0', // Medium blue
                '--bio-medium-green': '#2E5C8A', // Darker blue
                '--bio-accent-green': '#1E3F5F', // Dark blue
                '--wst-color-action': '#5C9BD1',
                '--color_18': '92, 155, 209', // RGB for --wst-color-action
                '--color_41': '92, 155, 209', // RGB for accent
                '--color_48': '92, 155, 209' // RGB for button primary
            },
            tritanopia: {
                // Use magenta/pink tones for better visibility
                // Tritanopia affects blue perception, so pink/magenta works well
                '--bio-bright-green': '#F48FB1', // Light pink
                '--bio-light-green': '#EC407A', // Pink
                '--bio-medium-green': '#C2185B', // Darker pink
                '--bio-accent-green': '#880E4F', // Dark pink
                '--wst-color-action': '#F48FB1',
                '--color_18': '244, 143, 177', // RGB for --wst-color-action
                '--color_41': '244, 143, 177', // RGB for accent
                '--color_48': '244, 143, 177' // RGB for button primary
            }
        };

        const palette = colorPalettes[mode];
        if (palette) {
            Object.entries(palette).forEach(([property, value]) => {
                root.style.setProperty(property, value);
            });
        }
    }
};

// Initialize when DOM is ready
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
        AccessibilityManager.init();
    });
} else {
    AccessibilityManager.init();
}

