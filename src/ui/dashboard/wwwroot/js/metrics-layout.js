window.metricLayout = {
  init: function (defaultLayout) {
    const grid = GridStack.init({float: true});
    this.grid = grid;
    this.defaultLayout = defaultLayout;

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
  },
  reset: function () {
    const grid = this.grid;
    if (!grid) return;
    localStorage.removeItem('metrics-layout');
    this.defaultLayout.forEach(n => {
      const el = document.getElementById(n.id);
      if (el) grid.update(el, n);
    });
  }
};
