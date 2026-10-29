// 3D views for Halgoworks. Blazor mounts a scene on a canvas, then sends a fresh state object
// whenever the playback step changes. Each builder turns that state into meshes and eases
// toward it every frame, so stepping back and forth animates in both directions.

import * as THREE from '../lib/three/three.module.js';
import { OrbitControls } from '../lib/three/OrbitControls.js';

const C = {
    page: 0x0e1828,
    surface: 0x14223a,
    raised: 0x1b2c48,
    line: 0x2a3d5e,
    text: '#eaf0f8',
    textDim: '#aab7cb',
    muted: '#7d8ba3',
    signal: 0xffb23e,
    good: 0x0ca30c,
    bad: 0xd03b3b,
    other: 0x5b6b85,
    depot: 0xd8e1ee,
    series: [0x3987e5, 0xd95926, 0x199e70, 0xc98500, 0xd55181, 0x008300, 0x9085e9, 0xe66767],
    ramp: [0x0d366b, 0x104281, 0x184f95, 0x1c5cab, 0x256abf, 0x2a78d6, 0x3987e5, 0x5598e7, 0x6da7ec, 0x86b6ef, 0x9ec5f4, 0xb7d3f6, 0xcde2fb],
    size: [0x1c5cab, 0x3987e5, 0x86b6ef],
};

const reduceMotion = matchMedia('(prefers-reduced-motion: reduce)').matches;
const stages = new Map();

// ------------------------------------------------------------------ Stage lifecycle

export function mount(id, canvas, kind, state) {
    const factory = builders[kind];
    if (!factory) return false;

    let renderer;
    try {
        renderer = new THREE.WebGLRenderer({ canvas, antialias: true, powerPreference: 'high-performance' });
    } catch (e) {
        console.warn('WebGL unavailable', e);
        return false;
    }

    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    renderer.outputColorSpace = THREE.SRGBColorSpace;
    renderer.shadowMap.enabled = kind !== 'hero';
    renderer.shadowMap.type = THREE.PCFShadowMap;

    const scene = new THREE.Scene();
    scene.background = new THREE.Color(kind === 'hero' ? C.page : C.surface);
    if (kind !== 'hero') scene.fog = new THREE.Fog(C.surface, 40, 120);
    const camera = new THREE.PerspectiveCamera(36, 1, 0.1, 1000);

    const stage = { id, kind, renderer, scene, camera, canvas, controls: null, builder: null, raf: 0, visible: true, last: performance.now(), time: 0 };

    if (kind !== 'hero') {
        const controls = new OrbitControls(camera, canvas);
        controls.enableDamping = true;
        controls.dampingFactor = 0.08;
        controls.enablePan = false;
        controls.maxPolarAngle = Math.PI * 0.48;
        // Scrolling the page over the canvas shouldn't zoom until the user has engaged with it.
        controls.enableZoom = false;
        stage.engage = () => { controls.enableZoom = true; };
        stage.disengage = () => { controls.enableZoom = false; };
        canvas.addEventListener('pointerdown', stage.engage);
        canvas.addEventListener('pointerleave', stage.disengage);
        controls.addEventListener('start', () => { stage.userMoved = true; });
        stage.controls = controls;
    }

    stage.builder = factory(stage);
    stage.builder.update?.(state);
    resize(stage);

    stage.resizeObserver = new ResizeObserver(() => resize(stage));
    stage.resizeObserver.observe(canvas.parentElement);
    stage.visibility = new IntersectionObserver(([entry]) => { stage.visible = entry.isIntersecting; });
    stage.visibility.observe(canvas);

    const loop = (now) => {
        stage.raf = requestAnimationFrame(loop);
        const dt = Math.min((now - stage.last) / 1000, 0.05);
        stage.last = now;
        if (!stage.visible) return;
        stage.time += dt;
        stage.controls?.update();
        stage.builder.tick?.(dt, stage.time);
        renderer.render(scene, camera);
    };
    stage.raf = requestAnimationFrame(loop);
    stages.set(id, stage);
    return true;
}

export function update(id, state) {
    stages.get(id)?.builder.update?.(state);
}

export function unmount(id) {
    const stage = stages.get(id);
    if (!stage) return;
    cancelAnimationFrame(stage.raf);
    stage.resizeObserver?.disconnect();
    stage.visibility?.disconnect();
    stage.canvas.removeEventListener('pointerdown', stage.engage);
    stage.canvas.removeEventListener('pointerleave', stage.disengage);
    stage.controls?.dispose();
    disposeTree(stage.scene);
    stage.renderer.dispose();
    stages.delete(id);
}

function resize(stage) {
    const host = stage.canvas.parentElement;
    const w = Math.max(1, host.clientWidth);
    const h = Math.max(1, host.clientHeight);
    stage.renderer.setSize(w, h, false);
    stage.camera.aspect = w / h;
    stage.camera.updateProjectionMatrix();
    stage.builder?.resize?.(w, h);
    if (!stage.userMoved) applyFit(stage);
}

function disposeTree(root) {
    root.traverse((obj) => {
        // Sprites share one geometry inside three.js; only their materials are ours to free.
        if (!obj.isSprite) obj.geometry?.dispose();
        const materials = Array.isArray(obj.material) ? obj.material : obj.material ? [obj.material] : [];
        for (const m of materials) {
            m.map?.dispose();
            m.dispose();
        }
    });
}

function removeAndDispose(obj) {
    obj.parent?.remove(obj);
    disposeTree(obj);
}

// ------------------------------------------------------------------ Shared helpers

function rampColor(t) {
    const i = Math.round(Math.max(0, Math.min(1, t)) * (C.ramp.length - 1));
    return C.ramp[i];
}

function seriesColor(slot) {
    return slot >= 0 && slot < C.series.length ? C.series[slot] : C.other;
}

function ease(dt, rate = 10) {
    return reduceMotion ? 1 : 1 - Math.exp(-dt * rate);
}

function addLights(scene, size) {
    scene.add(new THREE.HemisphereLight(0xdfe8ff, 0x0b1424, 1.25));
    const sun = new THREE.DirectionalLight(0xffffff, 1.7);
    sun.position.set(size * 0.5, size * 1.3, size * 0.9);
    sun.castShadow = true;
    sun.shadow.mapSize.set(2048, 2048);
    const cam = sun.shadow.camera;
    cam.left = -size;
    cam.right = size;
    cam.top = size;
    cam.bottom = -size;
    cam.near = 0.5;
    cam.far = size * 5;
    sun.shadow.bias = -0.0004;
    scene.add(sun);
    return sun;
}

function addFloor(scene, width, depth, y = 0) {
    const floor = new THREE.Mesh(
        new THREE.PlaneGeometry(width, depth),
        new THREE.MeshStandardMaterial({ color: C.surface, roughness: 0.95, metalness: 0 }));
    floor.rotation.x = -Math.PI / 2;
    floor.position.y = y;
    floor.receiveShadow = true;
    scene.add(floor);
    return floor;
}

function roundRect(ctx, x, y, w, h, r) {
    ctx.beginPath();
    ctx.moveTo(x + r, y);
    ctx.arcTo(x + w, y, x + w, y + h, r);
    ctx.arcTo(x + w, y + h, x, y + h, r);
    ctx.arcTo(x, y + h, x, y, r);
    ctx.arcTo(x, y, x + w, y, r);
    ctx.closePath();
}

// A text sprite that always faces the camera. `height` is its height in world units.
function makeLabel(text, { height = 0.5, color = C.text, background = null, weight = 600, depthTest = false } = {}) {
    const size = 48;
    const padX = background ? 18 : 4;
    const padY = background ? 10 : 4;
    const canvas = document.createElement('canvas');
    const ctx = canvas.getContext('2d');
    const font = `${weight} ${size}px Barlow, "Segoe UI", system-ui, sans-serif`;
    ctx.font = font;
    const w = Math.ceil(ctx.measureText(text).width) + padX * 2;
    const h = size + padY * 2;
    canvas.width = w;
    canvas.height = h;
    ctx.font = font;
    if (background) {
        ctx.fillStyle = background;
        roundRect(ctx, 0, 0, w, h, 14);
        ctx.fill();
    }
    ctx.fillStyle = color;
    ctx.textBaseline = 'middle';
    ctx.fillText(text, padX, h / 2 + 2);
    const texture = new THREE.CanvasTexture(canvas);
    texture.colorSpace = THREE.SRGBColorSpace;
    texture.anisotropy = 4;
    const sprite = new THREE.Sprite(new THREE.SpriteMaterial({ map: texture, transparent: true, depthTest }));
    sprite.scale.set(height * (w / h), height, 1);
    sprite.renderOrder = 10;
    return sprite;
}

// Points the camera at `target` from the given angles, far enough back that a sphere of `radius`
// fits the view. Re-applied on resize until the user starts orbiting.
function frame(stage, target, radius, { yaw = 0.35, pitch = 0.62 } = {}) {
    stage.fit = { target: target.clone(), radius, yaw, pitch };
    applyFit(stage);
}

function applyFit(stage) {
    const fit = stage.fit;
    if (!fit) return;
    const { camera, controls } = stage;
    const vfov = THREE.MathUtils.degToRad(camera.fov);
    const hfov = 2 * Math.atan(Math.tan(vfov / 2) * camera.aspect);
    const distance = fit.radius / Math.sin(Math.min(vfov, hfov) / 2);
    const { target, yaw, pitch } = fit;
    if (stage.scene.fog) {
        stage.scene.fog.near = distance * 1.1;
        stage.scene.fog.far = distance * 2.6;
    }
    camera.position.set(
        target.x + distance * Math.sin(yaw) * Math.cos(pitch),
        target.y + distance * Math.sin(pitch),
        target.z + distance * Math.cos(yaw) * Math.cos(pitch));
    camera.lookAt(target);
    if (controls) {
        controls.target.copy(target);
        controls.minDistance = distance * 0.3;
        controls.maxDistance = distance * 2;
        controls.update();
    }
}

// ------------------------------------------------------------------ Builders

const builders = {};

// Knapsack: the DP table as a skyline. Rows of items run front to back, capacities left to right,
// and each bar's height is the best value found for that cell.
builders.knapsack = (stage) => {
    const { scene } = stage;
    addLights(scene, 22);
    const root = new THREE.Group();
    scene.add(root);

    const H = 5;
    const gap = 0.14;
    let rows = 0;
    let cols = 0;
    let rowKey = '';
    let mesh = null;
    let heights = null;
    let targets = null;
    let labels = new THREE.Group();
    root.add(labels);
    const dummy = new THREE.Object3D();
    const color = new THREE.Color();

    const focus = new THREE.LineSegments(
        new THREE.EdgesGeometry(new THREE.BoxGeometry(1, 1, 1)),
        new THREE.LineBasicMaterial({ color: C.signal }));
    focus.visible = false;
    root.add(focus);
    const focusHeight = { now: 0, target: 0 };

    const ringGeometry = new THREE.RingGeometry(0.22, 0.34, 32);
    const skipRing = new THREE.Mesh(ringGeometry, new THREE.MeshBasicMaterial({ color: C.signal, side: THREE.DoubleSide, depthTest: false }));
    const takeRing = new THREE.Mesh(ringGeometry, new THREE.MeshBasicMaterial({ color: C.signal, side: THREE.DoubleSide, depthTest: false }));
    for (const ring of [skipRing, takeRing]) {
        ring.rotation.x = -Math.PI / 2;
        ring.renderOrder = 5;
        ring.visible = false;
        root.add(ring);
    }

    let valueLabel = null;
    let pathLine = null;

    const pos = (r, c) => new THREE.Vector3(c - (cols - 1) / 2, 0, r - (rows - 1) / 2);

    function build(state) {
        rows = state.table.length;
        cols = state.table[0].length;
        rowKey = state.rows.join('|');
        if (mesh) removeAndDispose(mesh);
        removeAndDispose(labels);
        labels = new THREE.Group();
        root.add(labels);

        const geometry = new THREE.BoxGeometry(1 - gap, 1, 1 - gap);
        geometry.translate(0, 0.5, 0);
        mesh = new THREE.InstancedMesh(geometry, new THREE.MeshStandardMaterial({ roughness: 0.5, metalness: 0.08 }), rows * cols);
        mesh.castShadow = true;
        mesh.receiveShadow = true;
        heights = new Float32Array(rows * cols).fill(0.04);
        targets = new Float32Array(rows * cols).fill(0.04);
        for (let i = 0; i < rows * cols; i++) mesh.setColorAt(i, color.set(C.line));
        root.add(mesh);

        const plate = new THREE.Mesh(
            new THREE.BoxGeometry(cols + 0.6, 0.1, rows + 0.6),
            new THREE.MeshStandardMaterial({ color: C.raised, roughness: 0.9 }));
        plate.position.y = -0.05;
        plate.receiveShadow = true;
        labels.add(plate);

        state.rows.forEach((text, r) => {
            const label = makeLabel(text, { height: 0.42, color: C.textDim });
            const p = pos(r, 0);
            label.position.set(p.x - 0.9 - label.scale.x / 2, 0.25, p.z);
            labels.add(label);
        });
        const every = cols > 18 ? 5 : cols > 12 ? 2 : 1;
        for (let c = 0; c < cols; c += every) {
            const label = makeLabel(String(c), { height: 0.4, color: C.muted, weight: 500 });
            const p = pos(rows - 1, c);
            label.position.set(p.x, 0.2, p.z + 1.0);
            labels.add(label);
        }
        const axis = makeLabel('capacity', { height: 0.36, color: C.muted, weight: 500 });
        axis.position.set(pos(rows - 1, cols - 1).x + 1.6, 0.2, pos(rows - 1, 0).z + 1.0);
        labels.add(axis);

        // The skyline is wide and low, so a sphere bound is too loose; frame by width instead.
        frame(stage, new THREE.Vector3(-1.2, H * 0.3, 0.4), 0.4 * (cols + 4), { yaw: 0.36, pitch: 0.55 });
    }

    function update(state) {
        if (!state) return;
        if (!mesh || state.table.length !== rows || state.table[0].length !== cols || state.rows.join('|') !== rowKey) build(state);
        const max = Math.max(1, state.max);
        const onPath = new Set(state.path.map(([r, c]) => r * cols + c));
        for (let r = 0; r < rows; r++) {
            for (let c = 0; c < cols; c++) {
                const i = r * cols + c;
                const v = state.table[r][c];
                targets[i] = v < 0 ? 0.04 : 0.12 + (v / max) * H;
                mesh.setColorAt(i, color.set(onPath.has(i) ? C.signal : v < 0 ? C.line : rampColor(v / max)));
            }
        }
        mesh.instanceColor.needsUpdate = true;

        if (valueLabel) removeAndDispose(valueLabel);
        valueLabel = null;
        if (state.cell) {
            const [r, c] = state.cell;
            const p = pos(r, c);
            const v = state.table[r][c];
            focus.visible = true;
            focus.position.set(p.x, 0, p.z);
            focusHeight.target = Math.max(0.2, targets[r * cols + c]) + 0.08;
            if (v >= 0) {
                valueLabel = makeLabel(String(v), { height: 0.62, color: '#0e1828', background: '#ffb23e', weight: 700 });
                valueLabel.position.set(p.x, focusHeight.target + 0.7, p.z);
                root.add(valueLabel);
            }
        } else {
            focus.visible = false;
        }

        placeRing(skipRing, state.skip, !state.took);
        placeRing(takeRing, state.take, state.took);

        if (pathLine) removeAndDispose(pathLine);
        pathLine = null;
        if (state.path.length > 1) {
            const points = state.path.map(([r, c]) => {
                const p = pos(r, c);
                return new THREE.Vector3(p.x, targets[r * cols + c] + 0.35, p.z);
            });
            pathLine = new THREE.Line(new THREE.BufferGeometry().setFromPoints(points), new THREE.LineBasicMaterial({ color: C.signal }));
            root.add(pathLine);
        }
    }

    function placeRing(ring, cell, chosen) {
        ring.visible = !!cell;
        if (!cell) return;
        const [r, c] = cell;
        const p = pos(r, c);
        ring.position.set(p.x, targets[r * cols + c] + 0.06, p.z);
        ring.material.color.set(chosen ? C.signal : C.muted);
    }

    function tick(dt) {
        if (!mesh) return;
        const k = ease(dt, 9);
        for (let r = 0; r < rows; r++) {
            for (let c = 0; c < cols; c++) {
                const i = r * cols + c;
                heights[i] += (targets[i] - heights[i]) * k;
                const p = pos(r, c);
                dummy.position.set(p.x, 0, p.z);
                dummy.scale.set(1, heights[i], 1);
                dummy.updateMatrix();
                mesh.setMatrixAt(i, dummy.matrix);
            }
        }
        mesh.instanceMatrix.needsUpdate = true;
        focusHeight.now += (focusHeight.target - focusHeight.now) * k;
        focus.scale.set(1.04, focusHeight.now, 1.04);
        focus.position.y = focusHeight.now / 2;
    }

    return { update, tick };
};

// Bin packing: bins are glass columns; items are blocks that drop into place.
builders.bins = (stage) => {
    const { scene } = stage;
    addLights(scene, 24);
    addFloor(scene, 200, 200, -0.01);
    const grid = new THREE.GridHelper(120, 120, C.line, 0x1a2a45);
    grid.position.y = 0.001;
    scene.add(grid);

    const H = 6;
    const W = 1.6;
    const spacing = 2.4;
    const containers = new THREE.Group();
    scene.add(containers);
    const items = new Map();
    const boxGeometry = new THREE.BoxGeometry(1, 1, 1);
    let framedSlots = -1;

    const slotX = (b, count) => (b - (count - 1) / 2) * spacing;

    function container(x, kind, label) {
        const group = new THREE.Group();
        group.position.x = x;
        const glass = new THREE.Mesh(
            new THREE.BoxGeometry(W + 0.12, H, W + 0.12),
            new THREE.MeshStandardMaterial({ color: 0x9fb7d9, transparent: true, opacity: kind === 'ghost' ? 0.03 : 0.07, roughness: 0.1, depthWrite: false }));
        glass.position.y = H / 2;
        group.add(glass);
        const edgeColor = { chosen: C.signal, full: C.bad, fits: C.good, ghost: C.signal, closed: 0x223552 }[kind] ?? C.line;
        const edges = new THREE.LineSegments(new THREE.EdgesGeometry(glass.geometry), new THREE.LineBasicMaterial({ color: edgeColor, transparent: kind === 'ghost', opacity: kind === 'ghost' ? 0.7 : 1 }));
        edges.position.y = H / 2;
        group.add(edges);
        const base = new THREE.Mesh(new THREE.BoxGeometry(W + 0.3, 0.08, W + 0.3), new THREE.MeshStandardMaterial({ color: kind === 'closed' ? 0x1a2944 : C.raised }));
        base.position.y = 0.04;
        base.receiveShadow = true;
        group.add(base);
        const tag = makeLabel(label, { height: 0.42, color: kind === 'chosen' || kind === 'ghost' ? '#ffb23e' : C.textDim });
        tag.position.set(0, -0.45, W / 2 + 0.4);
        group.add(tag);
        return group;
    }

    const outlineMaterial = new THREE.LineBasicMaterial({ color: C.signal });
    const outlineGeometry = new THREE.EdgesGeometry(new THREE.BoxGeometry(1.04, 1.04, 1.04));

    function setOutline(mesh, on) {
        if (on && !mesh.userData.outline) {
            mesh.userData.outline = new THREE.LineSegments(outlineGeometry, outlineMaterial);
            mesh.add(mesh.userData.outline);
        } else if (!on && mesh.userData.outline) {
            mesh.remove(mesh.userData.outline);
            mesh.userData.outline = null;
        }
    }

    function itemMesh(entry) {
        const mesh = new THREE.Mesh(boxGeometry, new THREE.MeshStandardMaterial({ color: C.size[entry.cls], roughness: 0.45, metalness: 0.05, emissive: C.signal, emissiveIntensity: 0 }));
        mesh.castShadow = true;
        mesh.receiveShadow = true;
        mesh.userData = { target: new THREE.Vector3(), height: 1, glow: 0 };
        scene.add(mesh);
        return mesh;
    }

    function update(state) {
        if (!state) return;
        const opening = state.target !== null && state.target === state.bins.length;
        const count = Math.max(1, state.bins.length + (opening ? 1 : 0));

        // Probe colors change every step, so the (cheap) containers are rebuilt each time.
        removeAndDispose(containers);
        containers.clear();
        scene.add(containers);
        for (let b = 0; b < count; b++) {
            const isGhost = b >= state.bins.length;
            const probe = state.probes[b] ?? 'none';
            const kind = isGhost ? 'ghost' : state.closed[b] ? 'closed' : probe === 'chosen' ? 'chosen' : probe === 'tooFull' ? 'full' : probe === 'fits' ? 'fits' : 'idle';
            containers.add(container(slotX(b, count), kind, isGhost ? 'new bin' : `bin ${b + 1}`));
        }

        const seen = new Set();
        state.bins.forEach((bin, b) => {
            let level = 0;
            for (const entry of bin) {
                const h = (entry.size / state.capacity) * H;
                let mesh = items.get(entry.id);
                if (!mesh) {
                    mesh = itemMesh(entry);
                    mesh.position.set(slotX(b, count), H + 2.5, 0);
                    mesh.scale.set(W, h - 0.06, W);
                    items.set(entry.id, mesh);
                }
                mesh.userData.target.set(slotX(b, count), level + h / 2, 0);
                mesh.userData.height = h - 0.06;
                mesh.userData.glow = entry.id === state.placed ? 0.35 : 0;
                setOutline(mesh, entry.id === state.placed);
                level += h;
                seen.add(entry.id);
            }
        });

        if (state.item) {
            const entry = state.item;
            const h = (entry.size / state.capacity) * H;
            let mesh = items.get(entry.id);
            if (!mesh) {
                mesh = itemMesh(entry);
                mesh.position.set(0, H + 4, 0);
                items.set(entry.id, mesh);
            }
            const b = state.target ?? 0;
            mesh.userData.target.set(slotX(b, count), H + 0.9 + h / 2, 0);
            mesh.userData.height = h - 0.06;
            mesh.userData.glow = 0.25;
            setOutline(mesh, true);
            seen.add(entry.id);
        }

        for (const [id, mesh] of items) {
            if (seen.has(id)) continue;
            scene.remove(mesh);
            mesh.material.dispose();
            items.delete(id);
        }

        if (framedSlots !== count) {
            framedSlots = count;
            const width = Math.max(4, count) * spacing;
            frame(stage, new THREE.Vector3(0, H * 0.42, 0), 0.5 * Math.hypot(width, H + 2, 3) + 0.5, { yaw: 0.25, pitch: 0.34 });
        }
    }

    function tick(dt) {
        const k = ease(dt, 8);
        for (const mesh of items.values()) {
            mesh.position.lerp(mesh.userData.target, k);
            mesh.scale.y += (mesh.userData.height - mesh.scale.y) * k;
            mesh.scale.x = W;
            mesh.scale.z = W;
            const m = mesh.material;
            m.emissiveIntensity += (mesh.userData.glow - m.emissiveIntensity) * k;
        }
    }

    return { update, tick };
};

// Vehicle routing: a small city with routes arcing between stops and trucks driving them.
builders.vrp = (stage) => {
    const { scene } = stage;
    addLights(scene, 18);
    addFloor(scene, 400, 400, -0.02);
    const plate = new THREE.Mesh(new THREE.BoxGeometry(20.6, 0.1, 20.6), new THREE.MeshStandardMaterial({ color: C.raised, roughness: 0.95 }));
    plate.position.y = -0.05;
    plate.receiveShadow = true;
    scene.add(plate);
    const grid = new THREE.GridHelper(20, 20, 0x31476d, 0x24385a);
    grid.position.y = 0.005;
    scene.add(grid);

    const world = (x, y) => new THREE.Vector3(x - 10, 0, y - 10);
    const buildings = new THREE.Group();
    const routes = new THREE.Group();
    const overlay = new THREE.Group();
    scene.add(buildings, routes, overlay);
    let key = '';
    let trucks = [];
    const customerMeshes = new Map();
    let depotPos = new THREE.Vector3();

    function buildCity(state) {
        removeAndDispose(buildings);
        buildings.clear();
        scene.add(buildings);
        customerMeshes.clear();
        depotPos = world(state.depot[0], state.depot[1]);
        const depot = new THREE.Mesh(new THREE.BoxGeometry(1.1, 1.5, 1.1), new THREE.MeshStandardMaterial({ color: C.depot, roughness: 0.6 }));
        depot.position.copy(depotPos).setY(0.75);
        depot.castShadow = true;
        buildings.add(depot);
        const roof = new THREE.Mesh(new THREE.ConeGeometry(0.95, 0.6, 4), new THREE.MeshStandardMaterial({ color: 0x9fb0c8, roughness: 0.6 }));
        roof.position.copy(depotPos).setY(1.8);
        roof.rotation.y = Math.PI / 4;
        roof.castShadow = true;
        buildings.add(roof);
        const tag = makeLabel('Depot', { height: 0.5, color: C.text });
        tag.position.copy(depotPos).setY(2.6);
        buildings.add(tag);

        for (const c of state.customers) {
            const h = 0.25 + c.demand * 0.2;
            const mesh = new THREE.Mesh(new THREE.BoxGeometry(0.55, h, 0.55), new THREE.MeshStandardMaterial({ color: C.other, roughness: 0.55, emissive: C.signal, emissiveIntensity: 0 }));
            mesh.position.copy(world(c.x, c.y)).setY(h / 2);
            mesh.castShadow = true;
            mesh.receiveShadow = true;
            mesh.userData = { glow: 0, height: h };
            buildings.add(mesh);
            customerMeshes.set(c.id, mesh);
        }
        frame(stage, new THREE.Vector3(0, 0, 0.8), 12.5, { yaw: 0.0, pitch: 0.86 });
    }

    // A route is a chain of arcs: higher arcs for longer legs, so overlapping legs separate visually.
    function routeCurve(points) {
        const path = new THREE.CurvePath();
        for (let i = 0; i + 1 < points.length; i++) {
            const a = points[i].clone().setY(0.12);
            const b = points[i + 1].clone().setY(0.12);
            const mid = a.clone().add(b).multiplyScalar(0.5);
            mid.y = 0.35 + a.distanceTo(b) * 0.12;
            path.add(new THREE.QuadraticBezierCurve3(a, mid, b));
        }
        return path;
    }

    function update(state) {
        if (!state) return;
        const cityKey = state.customers.map(c => `${c.id}:${c.x}:${c.y}:${c.demand}`).join('|');
        if (cityKey !== key) {
            key = cityKey;
            buildCity(state);
        }

        removeAndDispose(routes);
        routes.clear();
        scene.add(routes);
        removeAndDispose(overlay);
        overlay.clear();
        scene.add(overlay);
        for (const t of trucks) removeAndDispose(t.mesh);
        trucks = [];

        const colorOf = new Map();
        state.routes.forEach((route) => {
            const points = [depotPos, ...route.stops.map(id => customerMeshes.get(id).position), depotPos];
            const curve = routeCurve(points);
            const active = state.active === null || state.active === undefined || route.index === state.active;
            const color = route.color < 0 ? C.other : seriesColor(route.color);
            const tube = new THREE.Mesh(
                new THREE.TubeGeometry(curve, Math.max(24, points.length * 24), route.color < 0 ? 0.025 : 0.055, 6, false),
                new THREE.MeshStandardMaterial({ color, roughness: 0.4, transparent: !active, opacity: active ? 1 : 0.35, emissive: color, emissiveIntensity: 0.15 }));
            routes.add(tube);
            for (const id of route.stops) colorOf.set(id, color);
            if (state.final) {
                const truck = new THREE.Mesh(new THREE.BoxGeometry(0.42, 0.26, 0.26), new THREE.MeshStandardMaterial({ color, roughness: 0.4, emissive: color, emissiveIntensity: 0.25 }));
                truck.castShadow = true;
                overlay.add(truck);
                trucks.push({ mesh: truck, curve, length: curve.getLength(), u: Math.random() });
            }
        });

        const focus = new Set(state.focus);
        for (const [id, mesh] of customerMeshes) {
            mesh.material.color.set(colorOf.get(id) ?? C.other);
            mesh.userData.glow = focus.has(id) ? 0.85 : 0;
        }

        if (state.link) {
            const [a, b] = state.link.map(n => n === 0 ? depotPos : customerMeshes.get(n).position);
            const curve = routeCurve([a, b]);
            const line = new THREE.Line(
                new THREE.BufferGeometry().setFromPoints(curve.getPoints(40)).translate(0, 0.25, 0),
                new THREE.LineDashedMaterial({ color: state.linkOk === false ? C.bad : C.signal, dashSize: 0.25, gapSize: 0.18 }));
            line.computeLineDistances();
            overlay.add(line);
        }

        if (state.sweep !== null && state.sweep !== undefined) {
            const dir = new THREE.Vector3(Math.cos(state.sweep), 0, Math.sin(state.sweep));
            const end = depotPos.clone().addScaledVector(dir, 15).setY(0.15);
            const ray = new THREE.Line(new THREE.BufferGeometry().setFromPoints([depotPos.clone().setY(0.15), end]), new THREE.LineBasicMaterial({ color: C.signal }));
            overlay.add(ray);
        }
    }

    function tick(dt) {
        const k = ease(dt, 8);
        for (const mesh of customerMeshes.values()) {
            const m = mesh.material;
            m.emissiveIntensity += (mesh.userData.glow - m.emissiveIntensity) * k;
        }
        if (reduceMotion) return;
        for (const t of trucks) {
            t.u = (t.u + dt * 2.2 / t.length) % 1;
            const p = t.curve.getPointAt(t.u);
            const tangent = t.curve.getTangentAt(t.u);
            t.mesh.position.copy(p).setY(p.y + 0.16);
            t.mesh.rotation.y = Math.atan2(-tangent.z, tangent.x);
        }
    }

    return { update, tick };
};

// Home page: simulated annealing walking a cost landscape. Lower ground is a better solution.
builders.hero = (stage) => {
    const { scene, camera } = stage;
    const size = 64;
    const wells = [
        { x: -15, z: -9, a: -6.5, s: 6.5 },
        { x: 11, z: 11, a: -9.5, s: 5.5 },
        { x: 17, z: -13, a: -5.5, s: 4.5 },
        { x: -6, z: 15, a: -4.5, s: 5 },
        { x: 1, z: -2, a: 5, s: 7.5 },
        { x: -20, z: 12, a: 3.5, s: 5 },
        { x: 4, z: -20, a: -3.5, s: 4 },
    ];
    const f = (x, z) => {
        let y = 0.9 * Math.sin(x * 0.33) * Math.cos(z * 0.29) + 0.45 * Math.sin(x * 0.85 + z * 0.6) + 0.3 * Math.cos(z * 1.1 - x * 0.4);
        for (const w of wells) y += w.a * Math.exp(-((x - w.x) ** 2 + (z - w.z) ** 2) / (2 * w.s * w.s));
        return y;
    };

    const geometry = new THREE.PlaneGeometry(size, size, 180, 180);
    geometry.rotateX(-Math.PI / 2);
    const position = geometry.attributes.position;
    let minY = Infinity;
    let maxY = -Infinity;
    for (let i = 0; i < position.count; i++) {
        const y = f(position.getX(i), position.getZ(i));
        position.setY(i, y);
        minY = Math.min(minY, y);
        maxY = Math.max(maxY, y);
    }
    geometry.computeVertexNormals();

    const material = new THREE.ShaderMaterial({
        uniforms: {
            uMin: { value: minY },
            uMax: { value: maxY },
            uLow: { value: new THREE.Color(0x0b2a52) },
            uHigh: { value: new THREE.Color(0x6da7ec) },
            uLine: { value: new THREE.Color(0xcde2fb) },
            uFog: { value: new THREE.Color(C.page) },
            uLight: { value: new THREE.Vector3(0.4, 0.85, 0.35).normalize() },
        },
        vertexShader: `
            varying float vH;
            varying vec3 vNormal;
            varying float vDepth;
            void main() {
                vH = position.y;
                vNormal = normalize(normalMatrix * normal);
                vec4 mv = modelViewMatrix * vec4(position, 1.0);
                vDepth = -mv.z;
                gl_Position = projectionMatrix * mv;
            }`,
        fragmentShader: `
            uniform float uMin;
            uniform float uMax;
            uniform vec3 uLow;
            uniform vec3 uHigh;
            uniform vec3 uLine;
            uniform vec3 uFog;
            uniform vec3 uLight;
            varying float vH;
            varying vec3 vNormal;
            varying float vDepth;
            void main() {
                float t = clamp((vH - uMin) / (uMax - uMin), 0.0, 1.0);
                vec3 base = mix(uLow, uHigh, pow(t, 1.35));
                vec3 n = normalize(vNormal);
                vec3 l = normalize((viewMatrix * vec4(uLight, 0.0)).xyz);
                float shade = 0.5 + 0.5 * max(dot(n, l), 0.0);
                float k = vH * 1.6;
                // Distance to the nearest whole contour level, measured in screen pixels.
                float d = 0.5 - abs(fract(k) - 0.5);
                float contour = 1.0 - smoothstep(fwidth(k) * 0.6, fwidth(k) * 1.6, d);
                vec3 color = base * shade + uLine * contour * 0.22;
                float fog = smoothstep(38.0, 92.0, vDepth);
                gl_FragColor = vec4(mix(color, uFog, fog), 1.0);
            }`,
    });
    const terrain = new THREE.Mesh(geometry, material);
    const world = new THREE.Group();
    world.add(terrain);
    scene.add(world);

    // Everything below lives on the surface: positions are (x, z) and y always comes from f.
    const lift = 0.35;
    const onSurface = (x, z, extra = 0) => new THREE.Vector3(x, f(x, z) + lift + extra, z);

    const walker = new THREE.Mesh(new THREE.SphereGeometry(0.6, 32, 16), new THREE.MeshBasicMaterial({ color: C.signal }));
    const halo = new THREE.Mesh(new THREE.RingGeometry(0.85, 1.1, 40), new THREE.MeshBasicMaterial({ color: C.signal, transparent: true, opacity: 0.5, side: THREE.DoubleSide, depthWrite: false }));
    halo.rotation.x = -Math.PI / 2;
    world.add(walker, halo);

    // The pin marks the deepest valley found across all runs.
    const pin = new THREE.Group();
    const pinHead = new THREE.Mesh(new THREE.ConeGeometry(0.45, 1.1, 20), new THREE.MeshBasicMaterial({ color: 0xeaf0f8 }));
    pinHead.rotation.x = Math.PI;
    pinHead.position.y = 2.4;
    const pinStem = new THREE.Line(new THREE.BufferGeometry().setFromPoints([new THREE.Vector3(0, 0, 0), new THREE.Vector3(0, 1.9, 0)]), new THREE.LineBasicMaterial({ color: 0xeaf0f8 }));
    pin.add(pinHead, pinStem);
    pin.visible = false;
    world.add(pin);

    // Small rings where earlier runs settled: the local minima found so far.
    const ringGeometry = new THREE.RingGeometry(0.45, 0.62, 32);
    const minima = [];

    // A trail that hugs the terrain and fades from amber into the ground color.
    const trailMax = 260;
    const trailPositions = new Float32Array(trailMax * 3);
    const trailColors = new Float32Array(trailMax * 3);
    const trailGeometry = new THREE.BufferGeometry();
    trailGeometry.setAttribute('position', new THREE.BufferAttribute(trailPositions, 3));
    trailGeometry.setAttribute('color', new THREE.BufferAttribute(trailColors, 3));
    trailGeometry.setDrawRange(0, 0);
    world.add(new THREE.Line(trailGeometry, new THREE.LineBasicMaterial({ vertexColors: true })));
    const trail = [];
    const amber = new THREE.Color(C.signal);
    const ground = new THREE.Color(0x163a6b);
    const mixed = new THREE.Color();

    // Rejected proposals flash briefly where they would have landed.
    const ghosts = Array.from({ length: 12 }, () => {
        const m = new THREE.Mesh(new THREE.SphereGeometry(0.28, 12, 8), new THREE.MeshBasicMaterial({ color: 0xff7a72, transparent: true, opacity: 0 }));
        m.userData.life = 0;
        world.add(m);
        return m;
    });
    let ghostIndex = 0;

    // ---- Simulated annealing, paced so each decision is visible.
    // Keep the search in the front-right of the landscape, where the camera sees it clearly.
    const area = { x0: -6, x1: 26, z0: -8, z1: 24 };
    const startTemperature = 2.4;
    const gaussian = () => Math.sqrt(-2 * Math.log(1 - Math.random())) * Math.cos(2 * Math.PI * Math.random());
    const clampToArea = (x, z) => [Math.min(area.x1, Math.max(area.x0, x)), Math.min(area.z1, Math.max(area.z0, z))];

    let pos = { x: 0, z: 0 };        // where the ball is drawn
    let current = { x: 0, z: 0 };    // the accepted solution it is walking to
    let runBest = null;
    let best = null;
    let temperature = startTemperature;
    let phase = 'walk';              // walk | think | settle | rest
    let wait = 0;

    function startRun() {
        // Start somewhere high, so there's a visible descent.
        let x = 0, z = 0;
        for (let k = 0; k < 40; k++) {
            x = area.x0 + Math.random() * (area.x1 - area.x0);
            z = area.z0 + Math.random() * (area.z1 - area.z0);
            if (f(x, z) > 0.4) break;
        }
        pos = { x, z };
        current = { x, z };
        runBest = { x, z, cost: f(x, z) };
        temperature = startTemperature;
        trail.length = 0;
        phase = 'think';
        wait = 0.6;
    }

    function propose() {
        // Big exploratory steps while hot, small careful ones as it cools.
        const step = 0.7 + 3.2 * Math.sqrt(temperature / startTemperature);
        const [nx, nz] = clampToArea(current.x + gaussian() * step, current.z + gaussian() * step);
        const now = f(current.x, current.z);
        const next = f(nx, nz);
        temperature *= 0.965;
        if (next < now || Math.random() < Math.exp((now - next) / temperature)) {
            current = { x: nx, z: nz };
            if (next < runBest.cost) runBest = { x: nx, z: nz, cost: next };
            phase = 'walk';
        } else {
            const g = ghosts[ghostIndex++ % ghosts.length];
            g.position.copy(onSurface(nx, nz));
            g.userData.life = 1;
            phase = 'think';
            wait = 0.12;
        }
        if (temperature < 0.05) {
            // Cooled down: finish by walking to the best point this run found.
            current = { x: runBest.x, z: runBest.z };
            phase = 'settle';
        }
    }

    function markMinimum() {
        const ring = new THREE.Mesh(ringGeometry, new THREE.MeshBasicMaterial({ color: 0xeaf0f8, transparent: true, opacity: 0.55, side: THREE.DoubleSide, depthWrite: false }));
        ring.rotation.x = -Math.PI / 2;
        ring.position.copy(onSurface(runBest.x, runBest.z, -0.3));
        world.add(ring);
        minima.push(ring);
        if (minima.length > 6) {
            const old = minima.shift();
            world.remove(old);
            old.material.dispose();
        }
        if (!best || runBest.cost < best.cost) best = { ...runBest };
        pin.visible = true;
        pin.position.copy(onSurface(best.x, best.z, -lift));
    }

    function pushTrail() {
        const p = onSurface(pos.x, pos.z, -0.2);
        const last = trail[trail.length - 1];
        if (last && last.distanceToSquared(p) < 0.04) return;
        trail.push(p);
        if (trail.length > trailMax) trail.shift();
    }

    function advance(dt) {
        if (phase === 'think' || phase === 'rest') {
            wait -= dt;
            if (wait > 0) return;
            if (phase === 'rest') startRun();
            else propose();
            return;
        }
        // Walk along the ground toward the target at a steady pace.
        const dx = current.x - pos.x;
        const dz = current.z - pos.z;
        const dist = Math.hypot(dx, dz);
        const move = Math.min(dist, (phase === 'settle' ? 6 : 9) * dt);
        if (dist > 1e-4) {
            pos.x += dx / dist * move;
            pos.z += dz / dist * move;
            pushTrail();
        }
        if (dist - move > 1e-3) return;
        if (phase === 'settle') {
            markMinimum();
            phase = 'rest';
            wait = 2.4;
        } else {
            phase = 'think';
            wait = 0.08;
        }
    }

    function resize(w, h) {
        // Keep the landscape on the right so the headline on the left stays readable.
        const wide = w / h > 1.25;
        camera.fov = wide ? 34 : 46;
        camera.setViewOffset(w, h, wide ? -w * 0.18 : 0, wide ? h * 0.02 : -h * 0.12, w, h);
        camera.updateProjectionMatrix();
    }

    camera.position.set(0, 30, 52);
    camera.lookAt(0, -2, 0);

    function draw(time) {
        walker.position.copy(onSurface(pos.x, pos.z, 0.25));
        halo.position.copy(onSurface(pos.x, pos.z, -0.2));
        halo.scale.setScalar(1 + 0.12 * Math.sin(time * 4));
        const n = trail.length;
        for (let i = 0; i < n; i++) {
            trail[i].toArray(trailPositions, i * 3);
            mixed.copy(ground).lerp(amber, Math.pow((i + 1) / n, 1.6));
            mixed.toArray(trailColors, i * 3);
        }
        trailGeometry.attributes.position.needsUpdate = true;
        trailGeometry.attributes.color.needsUpdate = true;
        trailGeometry.setDrawRange(0, n);
    }

    function tick(dt, time) {
        if (!reduceMotion) {
            // A slow sway instead of spinning the landscape, so the search stays in view.
            camera.position.x = Math.sin(time * 0.06) * 5;
            camera.lookAt(0, -2, 0);
            advance(dt);
        }
        for (const g of ghosts) {
            if (g.userData.life <= 0) continue;
            g.userData.life = Math.max(0, g.userData.life - dt * 1.4);
            g.material.opacity = g.userData.life * 0.9;
        }
        draw(time);
    }

    startRun();
    if (reduceMotion) {
        // No animation: show one finished run instead.
        for (let i = 0; i < 4000 && phase !== 'rest'; i++) advance(0.05);
    }

    return { tick, resize };
};
