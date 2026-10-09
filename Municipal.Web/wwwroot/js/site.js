document.addEventListener('DOMContentLoaded', () => {
    window.lucide?.createIcons();
    const toggle = document.querySelector('.menu-toggle');
    const nav = document.getElementById('main-nav');
    toggle?.addEventListener('click', () => {
        const open = nav.classList.toggle('open');
        toggle.setAttribute('aria-expanded', String(open));
    });
    document.querySelectorAll('nav a').forEach(link => {
        if (link.pathname === location.pathname || (location.pathname === '/' && link.pathname === '/Requests/Report')) {
            link.classList.add('active');
            link.setAttribute('aria-current', 'page');
        }
    });
    document.addEventListener('keydown', event => {
        if (event.key === 'Escape' && nav?.classList.contains('open')) {
            nav.classList.remove('open');
            toggle.setAttribute('aria-expanded', 'false');
            toggle.focus();
        }
    });

    document.querySelectorAll('[data-copy]').forEach(button => {
        button.addEventListener('click', async () => {
            const input = document.getElementById(button.dataset.copy);
            const feedback = document.getElementById('copy-feedback');
            try {
                await navigator.clipboard.writeText(input.value);
                if (feedback) feedback.textContent = 'Reference copied.';
                const previousText = button.textContent;
                button.textContent = 'Copied';
                setTimeout(() => { button.textContent = previousText; }, 2000);
            } catch {
                input.select();
                if (feedback) feedback.textContent = 'Select and copy the reference.';
            }
        });
    });

    const category = document.getElementById('Category');
    const tiles = document.querySelectorAll('.tile');
    const categoryError = document.getElementById('category-error');
    const syncTiles = () => tiles.forEach(tile => {
        const selected = tile.dataset.category === category?.value;
        tile.classList.toggle('selected', selected);
        tile.setAttribute('aria-pressed', String(selected));
    });
    if (category && tiles.length) {
        tiles.forEach(tile => tile.addEventListener('click', () => {
            category.value = tile.dataset.category;
            categoryError.textContent = '';
            category.dispatchEvent(new Event('change', { bubbles: true }));
        }));
        category.addEventListener('change', syncTiles);
        syncTiles();
    }
    const report = document.getElementById('report-form');
    const address = document.getElementById('Location');
    const latitude = document.getElementById('Latitude');
    const longitude = document.getElementById('Longitude');
    const email = document.getElementById('ResidentEmail');
    const municipality = document.getElementById('MunicipalityCode');
    const updateSteps = () => {
        const states = {
            problem: Boolean(category?.value),
            place: Boolean(municipality?.value && address?.value.trim()),
            contact: Boolean(email?.value && email.validity.valid)
        };
        document.querySelectorAll('[data-step]').forEach(step => step.classList.toggle('complete', states[step.dataset.step]));
        const summaryCategory = document.getElementById('summary-category');
        const summaryMunicipality = document.getElementById('summary-municipality');
        const summaryGps = document.getElementById('summary-gps');
        if (summaryCategory) summaryCategory.textContent = category.value ? category.selectedOptions[0].textContent : 'Not selected';
        if (summaryMunicipality) summaryMunicipality.textContent = municipality.value ? municipality.selectedOptions[0].textContent : 'Not selected';
        if (summaryGps) summaryGps.textContent = latitude.value && longitude.value ? 'Attached to report' : 'Not attached';
    };
    report?.addEventListener('input', updateSteps);
    report?.addEventListener('change', updateSteps);
    updateSteps();

    const locate = document.getElementById('locate-button');
    const feedback = document.getElementById('location-feedback');
    const details = document.getElementById('location-details');
    const stop = document.getElementById('stop-location');
    const clear = document.getElementById('clear-location');
    const coordinates = document.getElementById('location-coordinates');
    const map = document.getElementById('location-map');
    let watchId = null;
    let generation = 0;
    let generatedAddress = '';
    const stopWatching = () => {
        generation++;
        if (watchId !== null) navigator.geolocation.clearWatch(watchId);
        watchId = null;
        if (locate) {
            locate.disabled = false;
            locate.querySelector('span').textContent = 'Get live location';
        }
        if (stop) stop.hidden = true;
    };
    const refreshLocationDetails = () => {
        details.hidden = false;
        coordinates.textContent = `${latitude.value}, ${longitude.value}`;
        map.href = `https://www.google.com/maps?q=${encodeURIComponent(`${latitude.value},${longitude.value}`)}`;
    };
    if (locate && address) {
        stop.hidden = true;
        if (latitude.value && longitude.value) refreshLocationDetails();
        address.addEventListener('input', () => {
            stopWatching();
            latitude.value = '';
            longitude.value = '';
            details.hidden = true;
            feedback.textContent = '';
            updateSteps();
        });
        locate.addEventListener('click', () => {
            if (!window.isSecureContext || !navigator.geolocation) {
                feedback.textContent = 'GPS needs a secure HTTPS connection. You can still enter an address.';
                return;
            }
            stopWatching();
            const currentGeneration = generation;
            locate.disabled = true;
            locate.querySelector('span').textContent = 'Locating...';
            feedback.textContent = 'Waiting for your device location. Allow location access when asked.';
            stop.hidden = false;
            // The generation prevents callbacks from an old watch changing a typed address.
            watchId = navigator.geolocation.watchPosition(position => {
                if (generation !== currentGeneration) return;
                const { latitude: lat, longitude: lon, accuracy } = position.coords;
                if (!Number.isFinite(lat) || !Number.isFinite(lon) || !Number.isFinite(accuracy)) return;
                latitude.value = lat.toFixed(6);
                longitude.value = lon.toFixed(6);
                if (!address.value.trim() || address.value === generatedAddress) {
                    generatedAddress = `GPS: ${latitude.value}, ${longitude.value}`;
                    address.value = generatedAddress;
                }
                locate.querySelector('span').textContent = 'Location is live';
                feedback.textContent = `Live GPS attached. Accuracy: approximately ${Math.round(accuracy)} m. Updates as your device location changes.`;
                refreshLocationDetails();
                updateSteps();
            }, error => {
                if (generation !== currentGeneration) return;
                if (error.code !== 1 && latitude.value) {
                    feedback.textContent = 'GPS is temporarily unavailable. Your last position is attached; live updates will resume when a new position is available.';
                    return;
                }
                stopWatching();
                const messages = {
                    1: 'Location permission was declined. Allow location in your browser settings, or enter an address.',
                    2: 'Your device could not find a location. Try again, or enter an address.',
                    3: 'Finding your location took too long. Try again, or enter an address.'
                };
                feedback.textContent = messages[error.code] || 'Location is unavailable. Enter an address.';
                if (latitude.value) feedback.textContent += ' Your last GPS position has been kept.';
            }, { enableHighAccuracy: true, timeout: 15000, maximumAge: 0 });
        });
        stop.addEventListener('click', () => {
            stopWatching();
            feedback.textContent = 'Live updates stopped. Your last GPS position is attached to this report.';
        });
        clear.addEventListener('click', () => {
            stopWatching();
            latitude.value = '';
            longitude.value = '';
            if (address.value === generatedAddress) address.value = '';
            details.hidden = true;
            feedback.textContent = 'GPS removed. Enter the address or attach a new location.';
            updateSteps();
        });
        document.addEventListener('visibilitychange', () => {
            if (document.hidden && watchId !== null) {
                stopWatching();
                feedback.textContent = latitude.value ? 'Live updates paused. Your last GPS position is attached.' : 'Location paused. Select live location to try again.';
            }
        });
        window.addEventListener('pagehide', stopWatching);
    }

    const description = document.getElementById('Description');
    const count = document.getElementById('char-count');
    if (description && count) {
        const update = () => { count.textContent = description.value.length; };
        description.addEventListener('input', update);
        update();
    }
    const reference = document.getElementById('ReferenceCode');
    reference?.addEventListener('input', () => { reference.value = reference.value.toUpperCase().trim(); });
    const passwordToggle = document.getElementById('password-toggle');
    const passwordField = document.getElementById('Password');
    passwordToggle?.addEventListener('click', () => {
        const show = passwordField.type === 'password';
        passwordField.type = show ? 'text' : 'password';
        passwordToggle.setAttribute('aria-pressed', String(show));
        passwordToggle.setAttribute('aria-label', show ? 'Hide password' : 'Show password');
        passwordToggle.title = show ? 'Hide password' : 'Show password';
        passwordToggle.querySelector('.show-password-icon').toggleAttribute('hidden', show);
        passwordToggle.querySelector('.hide-password-icon').toggleAttribute('hidden', !show);
    });
    document.querySelectorAll('form').forEach(form => {
        form.addEventListener('submit', event => {
            if (form === report && !category.value) {
                event.preventDefault();
                categoryError.textContent = 'Choose a problem category.';
                tiles[0].focus();
                return;
            }
            if (event.defaultPrevented) return;
            if (form === report) stopWatching();
            const button = form.querySelector('button[type="submit"].button');
            if (button) {
                button.classList.add('loading');
                const text = button.querySelector('span');
                if (text) text.textContent = 'Submitting...';
                else button.textContent = 'Please wait...';
            }
        });
    });
    window.addEventListener('pageshow', () => {
        document.querySelectorAll('.button.loading').forEach(button => {
            button.classList.remove('loading');
            const text = button.querySelector('span');
            if (text && report?.contains(button)) text.textContent = 'Submit problem';
        });
    });
});
