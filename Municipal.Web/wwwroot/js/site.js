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
    const clear = document.getElementById('clear-location');
    const map = document.getElementById('location-map');
    let generation = 0;
    let generatedAddress = address?.value.startsWith('GPS:') || address?.value.startsWith('Current location')
        ? address.value : '';
    let detectedMunicipality = '';
    let pinnedPosition = false;
    let lookupController = null;
    let cancelRefinement = null;
    const preferredAccuracy = 25;
    const addressAccuracyLimit = 100;
    const cancelLocationRequest = () => {
        generation++;
        cancelRefinement?.();
        cancelRefinement = null;
        lookupController?.abort();
        lookupController = null;
        if (locate) {
            locate.disabled = false;
            locate.querySelector('span').textContent = 'Locate me';
        }
    };
    const refinePosition = position => {
        const validAccuracy = fix => Number.isFinite(fix.coords.accuracy) && fix.coords.accuracy > 0;
        if (validAccuracy(position) && position.coords.accuracy <= preferredAccuracy
            || !navigator.geolocation.watchPosition) return Promise.resolve(position);
        return new Promise(resolve => {
            let best = position;
            let watchId = null;
            let finished = false;
            let timer;
            const finish = result => {
                if (finished) return;
                finished = true;
                clearTimeout(timer);
                if (watchId !== null) navigator.geolocation.clearWatch(watchId);
                cancelRefinement = null;
                resolve(result);
            };
            const showAccuracy = () => {
                locate.querySelector('span').textContent = 'Improving GPS...';
                feedback.textContent = `Looking for a better GPS fix${validAccuracy(best) ? ` (currently within about ${Math.ceil(best.coords.accuracy)} m)` : ''}. You can select the problem directly on the map.`;
            };
            cancelRefinement = () => finish(null);
            showAccuracy();
            // Give GPS a short chance to improve without holding up the form.
            timer = setTimeout(() => finish(best), 2500);
            watchId = navigator.geolocation.watchPosition(fix => {
                if (finished || !Number.isFinite(fix.coords.latitude) || !Number.isFinite(fix.coords.longitude)) return;
                if (validAccuracy(fix) && (!validAccuracy(best) || fix.coords.accuracy < best.coords.accuracy)) {
                    best = fix;
                    report.dispatchEvent(new CustomEvent('position-found', { detail: best.coords }));
                    showAccuracy();
                }
                if (validAccuracy(best) && best.coords.accuracy <= preferredAccuracy) finish(best);
            }, () => finish(best), { enableHighAccuracy: true, timeout: 2500, maximumAge: 0 });
        });
    };
    const applyPosition = position => {
        const { latitude: lat, longitude: lon } = position.coords;
        if (!Number.isFinite(lat) || !Number.isFinite(lon)) return false;
        latitude.value = lat.toFixed(6);
        longitude.value = lon.toFixed(6);
        if (!address.value.trim() || address.value === generatedAddress) {
            generatedAddress = '';
            address.value = '';
        }
        locate.querySelector('span').textContent = 'Finding location...';
        feedback.textContent = 'Location found. Looking up your address and municipality...';
        refreshLocationDetails();
        report.dispatchEvent(new CustomEvent('position-found', { detail: position.coords }));
        updateSteps();
        return true;
    };
    const locationErrorMessage = error => {
        const messages = {
            1: 'Location permission was declined. Allow location in your browser settings, or select your municipality manually.',
            2: 'Your device could not find a location. Try again, or select your municipality manually.',
            3: 'Finding your location took too long. Try again, or select your municipality manually.'
        };
        return messages[error.code] || 'Location is unavailable. Select your municipality manually.';
    };
    const refreshLocationDetails = () => {
        details.hidden = false;
        map.href = `https://www.google.com/maps?q=${encodeURIComponent(`${latitude.value},${longitude.value}`)}`;
    };
    if (locate && address) {
        if (latitude.value && longitude.value) refreshLocationDetails();
        if (address.value.startsWith('GPS:')) address.value = '';
        address.addEventListener('input', () => {
            if (pinnedPosition) {
                generatedAddress = '';
                if (!lookupController) feedback.textContent = 'Location text updated. Your selected map position is kept.';
                updateSteps();
                return;
            }
            cancelLocationRequest();
            latitude.value = '';
            longitude.value = '';
            if (municipality.value === detectedMunicipality) municipality.value = '';
            detectedMunicipality = '';
            details.hidden = true;
            feedback.textContent = '';
            report.dispatchEvent(new Event('position-cleared'));
            updateSteps();
        });
        const resolvePosition = async (position, currentGeneration) => {
                if (generation !== currentGeneration) return;
                const accuracy = position.coords.accuracy;
                if (!applyPosition(position)) {
                    cancelLocationRequest();
                    feedback.textContent = 'Your device returned an invalid location. Try again.';
                    return;
                }
                // Clear an old selection so a failed lookup cannot attach the wrong municipality.
                municipality.value = '';
                detectedMunicipality = '';
                updateSteps();
                lookupController = new AbortController();
                try {
                    const url = new URL(locate.dataset.municipalityUrl, window.location.origin);
                    url.searchParams.set('latitude', latitude.value);
                    url.searchParams.set('longitude', longitude.value);
                    if (Number.isFinite(accuracy) && accuracy >= 0) url.searchParams.set('accuracy', accuracy);
                    const response = await fetch(url, { signal: lookupController.signal });
                    const result = await response.json();
                    if (generation !== currentGeneration) return;
                    if (!response.ok) throw new Error(result.message || 'Municipality lookup is unavailable. Select your municipality manually.');
                    if (result.code && Array.from(municipality.options).some(option => option.value === result.code)) {
                        municipality.value = result.code;
                        detectedMunicipality = result.code;
                    }
                    const locationLabel = result.address || (result.area ? `Approximate area: ${result.area}`
                        : result.label ? `Approximate area: ${result.label}` : '');
                    const suggestedAddress = pinnedPosition ? result.address : locationLabel;
                    if (suggestedAddress && (!address.value.trim() || address.value === generatedAddress)) {
                        generatedAddress = suggestedAddress;
                        address.value = generatedAddress;
                    }
                    const approximate = !Number.isFinite(accuracy) || accuracy < 0 || accuracy > addressAccuracyLimit;
                    feedback.textContent = approximate
                        ? `Approximate location${Number.isFinite(accuracy) && accuracy >= 0 ? ` (within about ${Math.round(accuracy)} m)` : ''}. Check your municipality and enter the problem's street or landmark.`
                        : result.message || (pinnedPosition
                            ? `Nearest mapped road: ${result.address}. Check the address. Your municipality: ${result.label}.`
                            : `GPS accuracy: within about ${Math.ceil(accuracy)} m. Nearby street found; check that it matches the problem. Your municipality: ${result.label}.`);
                    updateSteps();
                    report.dispatchEvent(new Event('location-resolved'));
                } catch (error) {
                    if (generation !== currentGeneration) return;
                    feedback.textContent = error instanceof TypeError || error instanceof SyntaxError
                        ? 'Address and municipality lookup is unavailable. Try again or enter the location manually.'
                        : error.message;
                } finally {
                    if (generation === currentGeneration) cancelLocationRequest();
                }
        };
        report.addEventListener('location-pin', event => {
            cancelLocationRequest();
            pinnedPosition = true;
            address.value = event.detail.address || '';
            generatedAddress = '';
            locate.disabled = true;
            resolvePosition({ coords: { latitude: event.detail.lat, longitude: event.detail.lng, accuracy: 0 } }, generation);
        });
        locate.addEventListener('click', () => {
            if (!window.isSecureContext || !navigator.geolocation) {
                feedback.textContent = 'Location is unavailable here. Select the problem on the map.';
                return;
            }
            cancelLocationRequest();
            const currentGeneration = generation;
            pinnedPosition = false;
            locate.disabled = true;
            locate.querySelector('span').textContent = 'Locating...';
            feedback.textContent = 'Waiting for location permission from your browser...';
            navigator.geolocation.getCurrentPosition(async position => {
                if (generation !== currentGeneration) return;
                if (!applyPosition(position)) {
                    cancelLocationRequest();
                    feedback.textContent = 'Your device returned an invalid location. Try again.';
                    return;
                }
                municipality.value = '';
                detectedMunicipality = '';
                updateSteps();
                const best = await refinePosition(position);
                if (best && generation === currentGeneration) await resolvePosition(best, currentGeneration);
            }, error => {
                if (generation !== currentGeneration) return;
                cancelLocationRequest();
                feedback.textContent = locationErrorMessage(error);
                if (latitude.value) feedback.textContent += ' Your last GPS position has been kept.';
            }, { enableHighAccuracy: true, timeout: 8000, maximumAge: 15000 });
        });
        municipality.addEventListener('change', () => {
            cancelLocationRequest();
            detectedMunicipality = '';
            feedback.textContent = '';
            updateSteps();
        });
        clear.addEventListener('click', () => {
            cancelLocationRequest();
            pinnedPosition = false;
            latitude.value = '';
            longitude.value = '';
            if (address.value === generatedAddress) address.value = '';
            if (municipality.value === detectedMunicipality) municipality.value = '';
            detectedMunicipality = '';
            details.hidden = true;
            feedback.textContent = 'Location removed.';
            report.dispatchEvent(new Event('position-cleared'));
            updateSteps();
        });
        window.addEventListener('pagehide', cancelLocationRequest);
    }

    const suggestions = document.getElementById('address-suggestions');
    const searchStatus = document.getElementById('address-search-status');
    if (address?.dataset.searchUrl && suggestions && report) {
        let timer;
        let searchController;
        let searchGeneration = 0;
        let choices = [];
        let active = -1;
        const dismiss = () => {
            clearTimeout(timer);
            searchController?.abort();
            searchGeneration++;
            choices = [];
            active = -1;
            suggestions.replaceChildren();
            suggestions.hidden = true;
            address.setAttribute('aria-expanded', 'false');
            address.removeAttribute('aria-activedescendant');
            searchStatus.textContent = '';
        };
        const select = index => {
            const choice = choices[index];
            if (!choice) return;
            dismiss();
            report.dispatchEvent(new CustomEvent('location-pin', {
                detail: { lat: choice.latitude, lng: choice.longitude, address: choice.address }
            }));
            address.focus();
        };
        const highlight = index => {
            active = index;
            Array.from(suggestions.children).forEach((item, i) => {
                item.setAttribute('aria-selected', String(i === active));
                if (i === active) {
                    address.setAttribute('aria-activedescendant', item.id);
                    item.scrollIntoView({ block: 'nearest' });
                }
            });
        };
        address.addEventListener('input', () => {
            dismiss();
            const query = address.value.trim();
            if (query.length < 3) return;
            const current = searchGeneration;
            timer = setTimeout(async () => {
                searchController = new AbortController();
                searchStatus.textContent = 'Finding places...';
                try {
                    const url = new URL(address.dataset.searchUrl, location.origin);
                    url.searchParams.set('query', query);
                    const response = await fetch(url, { signal: searchController.signal });
                    const result = await response.json();
                    if (current !== searchGeneration || address.value.trim() !== query) return;
                    if (!response.ok) throw new Error(result.message);
                    choices = result.filter(item => typeof item.address === 'string'
                        && Number.isFinite(item.latitude) && Number.isFinite(item.longitude));
                    choices.forEach((choice, index) => {
                        const item = document.createElement('li');
                        item.id = `address-option-${index}`;
                        item.setAttribute('role', 'option');
                        item.setAttribute('aria-selected', 'false');
                        item.textContent = choice.address;
                        item.addEventListener('mousedown', event => event.preventDefault());
                        item.addEventListener('click', () => select(index));
                        suggestions.append(item);
                    });
                    suggestions.hidden = choices.length === 0;
                    address.setAttribute('aria-expanded', String(choices.length > 0));
                    searchStatus.textContent = choices.length ? `${choices.length} matching places.` : 'No matching places found.';
                } catch (error) {
                    if (current !== searchGeneration || error.name === 'AbortError') return;
                    searchStatus.textContent = 'Address search is unavailable. Your typed address has been kept.';
                }
            }, 300);
        });
        address.addEventListener('keydown', event => {
            if (event.key === 'Escape') { dismiss(); return; }
            if (!choices.length) return;
            if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
                event.preventDefault();
                highlight((active + (event.key === 'ArrowDown' ? 1 : -1) + choices.length) % choices.length);
            } else if (event.key === 'Enter' && active >= 0) {
                event.preventDefault();
                select(active);
            }
        });
        address.addEventListener('blur', dismiss);
        report.addEventListener('position-found', dismiss);
        report.addEventListener('position-cleared', dismiss);
        report.addEventListener('submit', dismiss);
        window.addEventListener('pagehide', dismiss);
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
            if (form === report) cancelLocationRequest();
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
