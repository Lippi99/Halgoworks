// Pointer helpers for Play and Edit modes. Coordinates are reported in the SVG's own user space,
// so components can work in the same units they draw with.
//
// A component calls attach(svg, dotnet) once. From then on, pressing on any element inside the SVG
// that carries a data-drag="key" attribute starts a drag right here in the browser (no round trip,
// so a quick click can never leave a half-started drag behind), and .NET hears OnDragStart, OnDragMove
// (at most once per frame, and only after the pointer really moves) and OnDragEnd.

export function svgPoint(svg, clientX, clientY) {
    const matrix = svg.getScreenCTM();
    if (!matrix) return [0, 0];
    const point = new DOMPoint(clientX, clientY).matrixTransform(matrix.inverse());
    return [point.x, point.y];
}

const attached = new WeakMap();

export function attach(svg, dotnet) {
    if (!svg || attached.has(svg)) return;
    const down = (e) => {
        if (e.button !== 0) return;
        const target = e.target.closest?.('[data-drag]');
        if (!target || !svg.contains(target)) return;
        e.preventDefault();
        track(svg, dotnet, target.getAttribute('data-drag'), e);
    };
    svg.addEventListener('pointerdown', down);
    attached.set(svg, down);
}

export function detach(svg) {
    const down = svg && attached.get(svg);
    if (down) svg.removeEventListener('pointerdown', down);
    if (svg) attached.delete(svg);
}

function track(svg, dotnet, key, start) {
    let frame = 0;
    let last = null;
    let moved = false;
    const [sx, sy] = svgPoint(svg, start.clientX, start.clientY);
    dotnet.invokeMethodAsync('OnDragStart', key, sx, sy);

    const move = (e) => {
        last = e;
        if (!moved && Math.hypot(e.clientX - start.clientX, e.clientY - start.clientY) > 4) moved = true;
        if (!moved || frame) return;
        frame = requestAnimationFrame(() => {
            frame = 0;
            const [x, y] = svgPoint(svg, last.clientX, last.clientY);
            dotnet.invokeMethodAsync('OnDragMove', key, x, y);
        });
    };

    // A real drag shouldn't also count as a click on whatever is under the pointer when it ends.
    const swallowClick = () => {
        const stop = (e) => {
            e.stopPropagation();
            e.preventDefault();
        };
        window.addEventListener('click', stop, { capture: true, once: true });
        setTimeout(() => window.removeEventListener('click', stop, { capture: true }), 0);
    };

    const finish = (e, cancelled) => {
        window.removeEventListener('pointermove', move);
        window.removeEventListener('pointerup', up);
        window.removeEventListener('pointercancel', cancel);
        window.removeEventListener('keydown', onKey);
        cancelAnimationFrame(frame);
        const [x, y] = e ? svgPoint(svg, e.clientX, e.clientY) : [sx, sy];
        dotnet.invokeMethodAsync('OnDragEnd', key, x, y, moved && !cancelled);
    };

    const up = (e) => {
        if (moved) swallowClick();
        finish(e, false);
    };
    const cancel = () => finish(null, true);
    const onKey = (e) => {
        if (e.key === 'Escape') finish(null, true);
    };

    window.addEventListener('pointermove', move);
    window.addEventListener('pointerup', up);
    window.addEventListener('pointercancel', cancel);
    window.addEventListener('keydown', onKey);
}
