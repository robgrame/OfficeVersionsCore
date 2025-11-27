// Cookie Consent Manager
class CookieConsent {
    constructor() {
        this.consentKey = 'office365versions_cookie_consent';
        this.consentDuration = 365 * 24 * 60 * 60 * 1000; // 1 year in milliseconds
        this.init();
    }

    init() {
        // Check if user has already made a choice
        const hasConsent = this.getConsent();
        
        if (hasConsent === null) {
            // No consent recorded, show banner
            this.showBanner();
        } else if (hasConsent === true) {
            // Load GTM only if consent given
            this.loadGTM();
        }
        // If hasConsent === false, don't load GTM
    }

    showBanner() {
        const banner = document.createElement('div');
        banner.id = 'cookie-consent-banner';
        banner.className = 'cookie-consent-banner';
        banner.innerHTML = `
            <div class="cookie-consent-content">
                <div class="cookie-consent-text">
                    <h5><i class="bi bi-cookie"></i> Cookie Consent</h5>
                    <p>
                        We use cookies to analyze site traffic and improve your experience. 
                        We collect only technical data (browser info, pages visited, performance metrics) 
                        and <strong>never collect personal information</strong> (name, email, IP address, etc.).
                    </p>
                    <p>
                        <a href="/Privacy#cookies" target="_blank">Learn more about our cookie policy</a>
                    </p>
                </div>
                <div class="cookie-consent-buttons">
                    <button class="btn btn-sm btn-outline-secondary" id="cookie-reject">
                        <i class="bi bi-x-circle me-1"></i>Decline
                    </button>
                    <button class="btn btn-sm btn-primary" id="cookie-accept">
                        <i class="bi bi-check-circle me-1"></i>Accept
                    </button>
                </div>
            </div>
        `;

        document.body.insertBefore(banner, document.body.firstChild);

        // Add event listeners
        document.getElementById('cookie-accept').addEventListener('click', () => this.acceptConsent());
        document.getElementById('cookie-reject').addEventListener('click', () => this.rejectConsent());

        // Auto-hide after 10 seconds if not interacted (accessibility)
        setTimeout(() => {
            if (document.getElementById('cookie-consent-banner')) {
                this.closeBanner();
            }
        }, 10000);
    }

    acceptConsent() {
        this.setConsent(true);
        this.closeBanner();
        this.loadGTM();
    }

    rejectConsent() {
        this.setConsent(false);
        this.closeBanner();
    }

    closeBanner() {
        const banner = document.getElementById('cookie-consent-banner');
        if (banner) {
            banner.classList.add('fade-out');
            setTimeout(() => {
                banner.remove();
            }, 300);
        }
    }

    setConsent(value) {
        const expiryDate = new Date(Date.now() + this.consentDuration);
        document.cookie = `${this.consentKey}=${value};path=/;expires=${expiryDate.toUTCString()};SameSite=Strict`;
    }

    getConsent() {
        const cookies = document.cookie.split(';');
        for (let cookie of cookies) {
            const [key, value] = cookie.trim().split('=');
            if (key === this.consentKey) {
                return value === 'true';
            }
        }
        return null; // No consent recorded
    }

    // Load GTM only after consent is given
    loadGTM() {
        // GTM ID from configuration will be injected here
        const gtmId = window.gtmId; // This will be set in _Layout.cshtml
        if (!gtmId) return;

        // Load Google Tag Manager
        (function(w,d,s,l,i){
            w[l]=w[l]||[];
            w[l].push({'gtm.start': new Date().getTime(),event:'gtm.js'});
            const f=d.getElementsByTagName(s)[0],
                j=d.createElement(s),
                dl=l!='dataLayer'?'&l='+l:'';
            j.async=true;
            j.src='https://www.googletagmanager.com/gtm.js?id='+i+dl;
            f.parentNode.insertBefore(j,f);
        })(window,document,'script','dataLayer',gtmId);

        // GTM noscript fallback
        const noscript = document.createElement('noscript');
        noscript.innerHTML = `<iframe src="https://www.googletagmanager.com/gtm.ns.html?id=${gtmId}" height="0" width="0" style="display:none;visibility:hidden"></iframe>`;
        document.body.insertBefore(noscript, document.body.firstChild);
    }
}

// Initialize when DOM is ready
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
        new CookieConsent();
    });
} else {
    new CookieConsent();
}
