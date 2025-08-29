let grid;
let defaultLayout;

export function init(layout) {
  grid = GridStack.init({ float: true });
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
