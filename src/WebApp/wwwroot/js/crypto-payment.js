/**
 * Crypto Payment JavaScript Interop Functions
 * Provides enhanced functionality for cryptocurrency payments
 */

window.cryptoPayment = {
    /**
     * Attempts to open payment URI in wallet app
     * @param {string} paymentUri - The crypto payment URI (bitcoin:, ethereum:, etc.)
     */
    openWallet: function(paymentUri) {
        try {
            // For mobile devices, try to open the wallet app directly
            if (this.isMobile()) {
                window.location.href = paymentUri;
                return true;
            }
            
            // For desktop, open in new window/tab
            window.open(paymentUri, '_blank');
            return true;
        } catch (error) {
            console.error('Failed to open wallet:', error);
            return false;
        }
    },

    /**
     * Copies text to clipboard with enhanced error handling
     * @param {string} text - Text to copy
     */
    copyToClipboard: async function(text) {
        try {
            if (navigator.clipboard && window.isSecureContext) {
                await navigator.clipboard.writeText(text);
                return true;
            } else {
                // Fallback for older browsers
                return this.fallbackCopyToClipboard(text);
            }
        } catch (error) {
            console.error('Failed to copy to clipboard:', error);
            return this.fallbackCopyToClipboard(text);
        }
    },

    /**
     * Fallback clipboard copy method for older browsers
     * @param {string} text - Text to copy
     */
    fallbackCopyToClipboard: function(text) {
        try {
            const textArea = document.createElement('textarea');
            textArea.value = text;
            textArea.style.position = 'fixed';
            textArea.style.left = '-999999px';
            textArea.style.top = '-999999px';
            document.body.appendChild(textArea);
            textArea.focus();
            textArea.select();
            
            const successful = document.execCommand('copy');
            document.body.removeChild(textArea);
            return successful;
        } catch (error) {
            console.error('Fallback copy failed:', error);
            return false;
        }
    },

    /**
     * Detects if device is mobile
     */
    isMobile: function() {
        return /Android|webOS|iPhone|iPad|iPod|BlackBerry|IEMobile|Opera Mini/i.test(navigator.userAgent);
    },

    /**
     * Shows a temporary notification
     * @param {string} message - Message to display
     * @param {string} type - Notification type: 'success', 'error', 'info'
     */
    showNotification: function(message, type = 'info') {
        const notification = document.createElement('div');
        notification.className = `crypto-notification crypto-notification-${type}`;
        notification.textContent = message;
        
        // Add styles
        Object.assign(notification.style, {
            position: 'fixed',
            top: '20px',
            right: '20px',
            padding: '12px 20px',
            borderRadius: '8px',
            color: 'white',
            fontWeight: '500',
            zIndex: '10000',
            opacity: '0',
            transform: 'translateY(-20px)',
            transition: 'all 0.3s ease',
            maxWidth: '300px',
            wordWrap: 'break-word'
        });

        // Set background color based on type
        const colors = {
            success: '#10B981',
            error: '#EF4444',
            info: '#3B82F6',
            warning: '#F59E0B'
        };
        notification.style.backgroundColor = colors[type] || colors.info;

        document.body.appendChild(notification);

        // Animate in
        setTimeout(() => {
            notification.style.opacity = '1';
            notification.style.transform = 'translateY(0)';
        }, 10);

        // Remove after 3 seconds
        setTimeout(() => {
            notification.style.opacity = '0';
            notification.style.transform = 'translateY(-20px)';
            setTimeout(() => {
                if (notification.parentNode) {
                    notification.parentNode.removeChild(notification);
                }
            }, 300);
        }, 3000);
    },

    /**
     * Vibrates device if supported (for mobile payment feedback)
     * @param {number|number[]} pattern - Vibration pattern in milliseconds
     */
    vibrate: function(pattern = 200) {
        if ('vibrate' in navigator) {
            navigator.vibrate(pattern);
        }
    },

    /**
     * Detects if user has specific wallet apps installed (Android)
     * @param {string} currency - Currency type to check wallet for
     */
    detectWalletApp: function(currency) {
        if (!this.isMobile()) {
            return false;
        }

        const walletSchemes = {
            bitcoin: ['bitcoin:', 'breadwallet:', 'copay:'],
            ethereum: ['ethereum:', 'metamask:', 'trust:'],
            usdt: ['ethereum:', 'metamask:', 'trust:'],
            usdc: ['ethereum:', 'metamask:', 'trust:']
        };

        const schemes = walletSchemes[currency.toLowerCase()] || [];
        
        // This is a simplified detection - in a real app, you might use
        // more sophisticated methods to detect installed apps
        return schemes.length > 0;
    },

    /**
     * Formats cryptocurrency amount for display
     * @param {number} amount - Amount to format
     * @param {string} currency - Currency symbol
     * @param {number} decimals - Number of decimal places
     */
    formatCryptoAmount: function(amount, currency, decimals = 8) {
        const formatter = new Intl.NumberFormat('en-US', {
            minimumFractionDigits: decimals,
            maximumFractionDigits: decimals
        });
        
        return `${formatter.format(amount)} ${currency}`;
    },

    /**
     * Validates crypto address format (basic validation)
     * @param {string} address - Address to validate
     * @param {string} currency - Currency type
     */
    validateAddress: function(address, currency) {
        const patterns = {
            bitcoin: /^[13][a-km-zA-HJ-NP-Z1-9]{25,34}$|^bc1[a-z0-9]{39,59}$/,
            ethereum: /^0x[a-fA-F0-9]{40}$/,
            usdt: /^0x[a-fA-F0-9]{40}$/,
            usdc: /^0x[a-fA-F0-9]{40}$/
        };

        const pattern = patterns[currency.toLowerCase()];
        return pattern ? pattern.test(address) : false;
    },

    /**
     * Handles page visibility changes for auto-refresh control
     * @param {function} callback - Function to call when visibility changes
     */
    onVisibilityChange: function(callback) {
        document.addEventListener('visibilitychange', () => {
            callback(!document.hidden);
        });
    },

    /**
     * Sets up payment monitoring interval
     * @param {function} checkFunction - Function to call for status check
     * @param {number} interval - Check interval in milliseconds
     */
    setupPaymentMonitoring: function(checkFunction, interval = 10000) {
        let intervalId = null;
        
        const startMonitoring = () => {
            if (intervalId) return;
            intervalId = setInterval(checkFunction, interval);
        };

        const stopMonitoring = () => {
            if (intervalId) {
                clearInterval(intervalId);
                intervalId = null;
            }
        };

        // Start monitoring initially
        startMonitoring();

        // Pause/resume based on page visibility
        this.onVisibilityChange((isVisible) => {
            if (isVisible) {
                startMonitoring();
            } else {
                stopMonitoring();
            }
        });

        // Return control functions
        return {
            start: startMonitoring,
            stop: stopMonitoring
        };
    },

    /**
     * Generates QR code as Data URL using a simple QR library
     * Note: This is a placeholder - in production you'd use a proper QR library
     * @param {string} text - Text to encode
     * @param {number} size - QR code size in pixels
     */
    generateQRCode: function(text, size = 200) {
        // This is a placeholder implementation
        // In the actual implementation, the QR code is generated server-side
        // using the QRCoder library, but this function could be used for
        // client-side generation if needed
        console.log(`Would generate QR code for: ${text} at size ${size}px`);
        return null;
    }
};

// Initialize crypto payment functionality when DOM is loaded
document.addEventListener('DOMContentLoaded', function() {
    console.log('Crypto payment JavaScript initialized');
});

// Handle beforeunload to clean up any monitoring
window.addEventListener('beforeunload', function() {
    // Cleanup code if needed
});