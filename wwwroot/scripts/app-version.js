(function () {
    async function loadVersionStamp() {
        const targets = Array.from(document.querySelectorAll('[data-app-version]'));
        if (!targets.length) {
            return;
        }

        try {
            const response = await fetch('/api/system/version', { cache: 'no-store' });
            if (!response.ok) {
                return;
            }

            const data = await response.json();
            const applicationName = data.ApplicationName || data.applicationName || 'QuizAPI';
            const version = data.InformationalVersion || data.informationalVersion || data.Version || data.version || 'unknown';
            const releaseLabelValue = data.ReleaseLabel || data.releaseLabel || '';
            const buildStampValue = data.BuildStamp || data.buildStamp || '';
            const releaseLabel = releaseLabelValue ? ' | Release: ' + releaseLabelValue : '';
            const buildStamp = buildStampValue ? ' | Build: ' + buildStampValue : '';
            const text = applicationName + ' ' + version + releaseLabel + buildStamp;

            for (const target of targets) {
                target.textContent = text;
                target.style.display = 'block';
            }
        } catch {
            // Leave the footer hidden if version info is unavailable.
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', loadVersionStamp);
    } else {
        loadVersionStamp();
    }
})();
