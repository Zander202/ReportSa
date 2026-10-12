document.addEventListener('DOMContentLoaded', () => {
    document.querySelectorAll('[data-photo-input]').forEach(input => {
        const preview = input.parentElement.querySelector('[data-photo-preview]');
        let urls = [];
        const render = () => {
            urls.forEach(URL.revokeObjectURL);
            urls = [];
            preview.replaceChildren();
            const files = Array.from(input.files);
            const invalid = files.length > 3 || files.some(file => file.size > 5 * 1024 * 1024
                || !['image/jpeg', 'image/png', 'image/webp'].includes(file.type));
            input.setCustomValidity(invalid ? 'Choose up to three JPEG, PNG or WebP photos, each 5 MB or smaller.' : '');
            if (invalid) {
                const error = document.createElement('p');
                error.className = 'photo-error'; error.textContent = input.validationMessage;
                preview.append(error);
            }
            files.forEach((file, index) => {
                const figure = document.createElement('figure');
                if (file.type.startsWith('image/') && file.size <= 5 * 1024 * 1024) {
                    const img = document.createElement('img');
                    img.src = URL.createObjectURL(file); urls.push(img.src); img.alt = 'Selected photo'; figure.append(img);
                }
                const name = document.createElement('figcaption'); name.textContent = file.name; figure.append(name);
                const remove = document.createElement('button'); remove.type = 'button'; remove.className = 'icon-button';
                remove.title = 'Remove photo'; remove.setAttribute('aria-label', `Remove ${file.name}`);
                const icon = document.createElement('i'); icon.dataset.lucide = 'x'; remove.append(icon);
                remove.addEventListener('click', () => {
                    const transfer = new DataTransfer(); files.forEach((item, i) => { if (i !== index) transfer.items.add(item); });
                    input.files = transfer.files; render();
                });
                figure.append(remove); preview.append(figure);
            });
            window.lucide?.createIcons();
        };
        input.addEventListener('change', render);
        window.addEventListener('pagehide', () => urls.forEach(URL.revokeObjectURL));
    });

    if (!window.L) return;
    const createMap = id => {
        const map = L.map(id, { scrollWheelZoom: false }).setView([-29, 24], 5);
        const tiles = L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
            maxZoom: 19, attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
        }).addTo(map);
        tiles.on('tileerror', () => {
            map.getContainer().setAttribute('data-tile-error', 'true');
            const feedback = document.getElementById('map-feedback') || document.getElementById('nearby-feedback');
            if (feedback) feedback.textContent = 'Map imagery could not be loaded. You can still enter the location or choose a municipality.';
        });
        return map;
    };
    const report = document.getElementById('report-form');
    if (report && document.getElementById('report-map')) {
        const map = createMap('report-map');
        let pin = null;
        let accuracyCircle = null;
        let nearbyController = null;
        const setPin = (lat, lng, accuracy) => {
            if (!pin) {
                pin = L.marker([lat, lng], { draggable: true, title: 'Problem location', alt: 'Problem location pin' }).addTo(map);
                pin.on('dragend', () => choose(pin.getLatLng()));
            } else pin.setLatLng([lat, lng]);
            if (accuracyCircle) accuracyCircle.remove();
            accuracyCircle = Number.isFinite(accuracy) && accuracy > 0
                ? L.circle([lat, lng], { radius: accuracy, color: '#087f66', fillOpacity: .08, weight: 1 }).addTo(map) : null;
        };
        const choose = point => {
            setPin(point.lat, point.lng, 0);
            report.dispatchEvent(new CustomEvent('location-pin', { detail: point }));
        };
        map.on('click', event => choose(event.latlng));
        document.getElementById('use-map-centre').addEventListener('click', () => choose(map.getCenter()));
        report.addEventListener('position-found', event => {
            const { latitude, longitude, accuracy } = event.detail;
            setPin(latitude, longitude, accuracy);
            if (accuracyCircle && accuracy > 100) map.fitBounds(accuracyCircle.getBounds(), { maxZoom: 15 });
            else map.setView([latitude, longitude], 18);
        });
        report.addEventListener('position-cleared', () => {
            if (pin) { pin.remove(); pin = null; }
            if (accuracyCircle) { accuracyCircle.remove(); accuracyCircle = null; }
            nearbyController?.abort(); document.getElementById('nearby-suggestions').hidden = true;
        });
        const lat = Number(document.getElementById('Latitude').value);
        const lng = Number(document.getElementById('Longitude').value);
        if (document.getElementById('Latitude').value && Number.isFinite(lat) && Number.isFinite(lng)) { setPin(lat, lng, 0); map.setView([lat, lng], 16); }
        const loadNearby = async () => {
            const group = document.getElementById('nearby-suggestions');
            const municipality = document.getElementById('MunicipalityCode').value;
            if (!municipality) { group.hidden = true; return; }
            nearbyController?.abort(); nearbyController = new AbortController();
            const url = new URL(group.dataset.url, location.origin); url.searchParams.set('municipalityCode', municipality);
            const category = document.getElementById('Category').value;
            if (category) url.searchParams.set('category', category);
            if (document.getElementById('Latitude').value) {
                url.searchParams.set('latitude', document.getElementById('Latitude').value);
                url.searchParams.set('longitude', document.getElementById('Longitude').value);
            }
            try {
                const response = await fetch(url, { signal: nearbyController.signal });
                if (!response.ok) throw new Error();
                const reports = await response.json();
                const list = document.getElementById('nearby-suggestion-list'); list.replaceChildren();
                reports.slice(0, 4).forEach(item => {
                    const link = document.createElement('a'); link.className = 'nearby-suggestion';
                    link.href = `/Requests/Nearby?municipalityCode=${encodeURIComponent(municipality)}#report-${item.id}`;
                    link.textContent = `${item.category} · ${item.status} · ${item.affected} affected${item.distanceKm != null ? ` · ${item.distanceKm.toFixed(1)} km` : ''}`;
                    list.append(link);
                });
                group.hidden = reports.length === 0;
            } catch (error) { if (error.name !== 'AbortError') group.hidden = true; }
        };
        report.addEventListener('location-resolved', loadNearby);
        document.getElementById('MunicipalityCode').addEventListener('change', loadNearby);
        document.getElementById('Category').addEventListener('change', loadNearby);
    }

    const nearbyPage = document.getElementById('nearby-page');
    if (nearbyPage) {
        const map = createMap('nearby-map');
        const markers = L.layerGroup().addTo(map);
        const draw = () => {
            markers.clearLayers();
            const points = [];
            document.querySelectorAll('[data-report-id]').forEach(item => {
                if (!item.dataset.latitude || !item.dataset.longitude) return;
                const point = [Number(item.dataset.latitude), Number(item.dataset.longitude)]; points.push(point);
                const label = document.createElement('a'); label.href = `#${item.id}`; label.textContent = item.querySelector('h3').textContent;
                L.marker(point).addTo(markers).bindPopup(label);
            });
            if (points.length) map.fitBounds(L.latLngBounds(points).pad(.2), { maxZoom: 14 });
        };
        draw();
        document.getElementById('nearby-locate').addEventListener('click', () => {
            const feedback = document.getElementById('nearby-feedback');
            if (!navigator.geolocation) { feedback.textContent = 'Location is unavailable. Choose a municipality.'; return; }
            feedback.textContent = 'Finding nearby reports...';
            navigator.geolocation.getCurrentPosition(async position => {
                const url = new URL(nearbyPage.dataset.url, location.origin);
                url.searchParams.set('latitude', position.coords.latitude); url.searchParams.set('longitude', position.coords.longitude);
                const category = document.getElementById('category').value;
                if (category) url.searchParams.set('category', category);
                const municipality = document.getElementById('municipalityCode').value;
                if (municipality) url.searchParams.set('municipalityCode', municipality);
                try {
                    const response = await fetch(url); if (!response.ok) throw new Error();
                    const reports = await response.json();
                    const list = document.getElementById('nearby-report-list'); list.replaceChildren();
                    reports.forEach(item => {
                        const article = document.createElement('article'); article.className = 'community-report';
                        article.id = `report-${item.id}`; article.dataset.reportId = item.id;
                        article.dataset.latitude = item.latitude ?? ''; article.dataset.longitude = item.longitude ?? '';
                        const heading = document.createElement('h3'); heading.textContent = item.category;
                        const summary = document.createElement('p'); summary.textContent = `${item.municipality} · ${item.status} · ${item.affected} affected · ${item.distanceKm.toFixed(1)} km`;
                        const form = document.getElementById('support-template').cloneNode(true); form.removeAttribute('id'); form.hidden = false; form.className = 'support-form';
                        const id = document.createElement('input'); id.type = 'hidden'; id.name = 'RequestId'; id.value = item.id;
                        const field = document.createElement('div'); const label = document.createElement('label');
                        label.htmlFor = `support-${item.id}`; label.textContent = 'Your email';
                        const email = document.createElement('input'); email.id = label.htmlFor; email.type = 'email'; email.name = 'Email'; email.required = true; email.maxLength = 254;
                        field.append(label, email);
                        const button = document.createElement('button'); button.type = 'submit'; button.className = 'button secondary'; button.textContent = "I'm affected too";
                        form.append(id, field, button); article.append(heading, summary, form); list.append(article);
                    });
                    draw();
                    feedback.textContent = reports.length ? `${reports.length} open reports within 5 km.` : 'No open reports within 5 km.';
                    map.setView([position.coords.latitude, position.coords.longitude], 13);
                    L.circleMarker([position.coords.latitude, position.coords.longitude], { radius: 6, color: '#087f66' }).addTo(map);
                    document.getElementById('nearby-count').textContent = `${reports.length} reports`;
                } catch { feedback.textContent = 'Nearby reports could not be loaded. Try again.'; }
            }, () => { feedback.textContent = 'Location could not be accessed. Choose a municipality.'; }, { enableHighAccuracy: true, timeout: 15000 });
        });
    }
});
