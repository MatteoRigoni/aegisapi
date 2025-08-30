let grid;
let defaultLayout;

export async function init(layout) {
    defaultLayout = layout;

    const GridStackLib = window.GridStack;
    if (!GridStackLib) {
        console.error('GridStack non è caricato');
        return;
    }

    grid = GridStackLib.init({ float: true });

    const saved = localStorage.getItem('metrics-layout');
    if (saved) {
        try {
            const nodes = JSON.parse(saved);
            nodes.forEach(n => {
                const el = document.getElementById(n.id);
                if (el) grid.update(el, n);
            });
        } catch (e) {
            console.error('failed to load layout', e);
        }
    }

    grid.on('change', () => {
        const nodes = grid.save(true);
        localStorage.setItem('metrics-layout', JSON.stringify(nodes));
    });
}

export function reset() {
    if (!grid) return;
    localStorage.removeItem('metrics-layout');
    defaultLayout.forEach(n => {
        const el = document.getElementById(n.id);
        if (el) grid.update(el, n);
    });
}
