let grid;
let defaultLayout;

async function ensureGridStack() {
  if (window.GridStack) return;

  await new Promise((resolve, reject) => {
    const script = document.createElement('script');
    script.src = 'https://cdnjs.cloudflare.com/ajax/libs/gridstack.js/4.2.5/gridstack.all.min.js';
    script.onload = resolve;
    script.onerror = () => reject(new Error('failed to load GridStack'));
    document.head.appendChild(script);
  });
}

export async function init(layout) {
  await ensureGridStack();

  const GridStackLib = window.GridStack;
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
