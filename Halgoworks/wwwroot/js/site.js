// Page-level helpers: keyboard shortcuts for the playback bar and smooth in-page scrolling.

const listeners = new Map();
let nextId = 1;

export function listenKeys(dotnet) {
    const handler = (e) => {
        if (e.defaultPrevented || e.altKey || e.ctrlKey || e.metaKey) return;
        const target = e.target;
        const tag = target?.tagName;
        if (tag === 'TEXTAREA' || tag === 'SELECT' || target?.isContentEditable) return;
        if (tag === 'INPUT' && target.type !== 'range') return;

        let action = null;
        switch (e.key) {
            case ' ':
                // A focused button already handles space itself.
                if (tag === 'BUTTON' || tag === 'A') return;
                action = 'toggle';
                break;
            case 'ArrowRight':
                if (tag === 'INPUT') return;
                action = 'next';
                break;
            case 'ArrowLeft':
                if (tag === 'INPUT') return;
                action = 'prev';
                break;
            case 'Home':
                action = 'first';
                break;
            case 'End':
                action = 'last';
                break;
        }

        if (!action) return;
        e.preventDefault();
        dotnet.invokeMethodAsync('OnKey', action);
    };

    document.addEventListener('keydown', handler);
    const id = nextId++;
    listeners.set(id, handler);
    return id;
}

export function unlistenKeys(id) {
    const handler = listeners.get(id);
    if (handler) document.removeEventListener('keydown', handler);
    listeners.delete(id);
}

export function scrollToId(id) {
    const reduce = matchMedia('(prefers-reduced-motion: reduce)').matches;
    document.getElementById(id)?.scrollIntoView({ behavior: reduce ? 'auto' : 'smooth', block: 'start' });
}

export async function copyText(text) {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch {
        return false;
    }
}

export function selectAll(input) {
    input?.select();
}
