window.dashboardLayout = {
    init: function () {
        const grid = GridStack.init({
            float: true
        });
        const saved = localStorage.getItem('dashboardLayout');
        if (saved) {
            grid.load(JSON.parse(saved));
        }
        grid.on('change', function () {
            const layout = grid.save();
            localStorage.setItem('dashboardLayout', JSON.stringify(layout));
        });
    },
    reset: function () {
        localStorage.removeItem('dashboardLayout');
        location.reload();
    }
};
