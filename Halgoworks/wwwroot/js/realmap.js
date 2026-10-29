// A real street map for the routing lab, drawn with Leaflet over OpenStreetMap tiles.
// .NET hands over plain data (stops, lines, which stops are in focus); this module only draws it.
import * as L from '../lib/leaflet/leaflet-src.esm.js';

// Standard OpenStreetMap tiles (light use with attribution is allowed); app.css darkens them to match the page.
const TILES = 'https://tile.openstreetmap.org/{z}/{x}/{y}.png';
const ATTRIBUTION =
    '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors · ' +
    'roads by <a href="https://project-osrm.org/">OSRM</a>';

export function create(element, dotnet) {
    const map = L.map(element, { zoomSnap: 0.25, scrollWheelZoom: false, attributionControl: true });
    map.attributionControl.setPrefix('<a href="https://leafletjs.com">Leaflet</a>');
    L.tileLayer(TILES, { maxZoom: 19, attribution: ATTRIBUTION, referrerPolicy: 'strict-origin-when-cross-origin' }).addTo(map);
    const lines = L.layerGroup().addTo(map);
    const stops = L.layerGroup().addTo(map);
    let fitted = null;

    map.on('click', (e) => dotnet.invokeMethodAsync('MapClicked', e.latlng.lat, e.latlng.lng));
    // Wheel zoom only after a click on the map, so scrolling the page doesn't get stuck on it.
    map.on('focus click', () => map.scrollWheelZoom.enable());
    map.on('blur mouseout', () => map.scrollWheelZoom.disable());

    const resize = new ResizeObserver(() => map.invalidateSize());
    resize.observe(element);

    return {
        update(state) {
            if (state.fitKey !== fitted) {
                fitted = state.fitKey;
                const points = [state.depot, ...state.customers.map((c) => [c.lat, c.lon])];
                map.fitBounds(L.latLngBounds(points).pad(0.12), { animate: false });
            }

            lines.clearLayers();
            for (const line of state.lines) {
                L.polyline(line.points, {
                    color: line.color,
                    weight: line.weight,
                    opacity: line.opacity,
                    dashArray: line.dash ?? null,
                    lineJoin: 'round',
                    interactive: false,
                }).addTo(lines);
            }

            stops.clearLayers();
            for (const c of state.customers) {
                if (c.focus) {
                    L.circleMarker([c.lat, c.lon], { radius: c.radius + 6, color: '#ffb23e', weight: 2.5, fill: false, interactive: false }).addTo(stops);
                }
                L.circleMarker([c.lat, c.lon], {
                    radius: c.radius,
                    color: '#0e1828',
                    weight: 2,
                    fillColor: c.color,
                    fillOpacity: 1,
                    bubblingMouseEvents: false,
                })
                    .bindTooltip(c.title, { direction: 'top', offset: [0, -c.radius] })
                    .addTo(stops);
            }

            const depot = L.divIcon({ className: 'realmap-depot', html: '<span></span>', iconSize: [24, 24], iconAnchor: [12, 12] });
            L.marker(state.depot, { icon: depot, keyboard: false, bubblingMouseEvents: false }).bindTooltip('Depot', { direction: 'top', offset: [0, -12] }).addTo(stops);
        },
        dispose() {
            resize.disconnect();
            map.remove();
        },
    };
}
