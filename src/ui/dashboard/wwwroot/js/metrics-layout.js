import { GridStack } from 'https://cdn.jsdelivr.net/npm/gridstack@4.2.5/dist/gridstack-h5.js';

let grid;
let defaultLayout;

export async function init(layout) {
  const GridStackLib = GridStack;
  if (!GridStackLib) {
    console.error('GridStack library is missing');
    return;
  }
  grid = GridStackLib.init({ float: true });
  defaultLayout = layout;

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
