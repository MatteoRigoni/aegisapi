window.renderNetwork = (graph) => {
    const canvas = document.getElementById('networkCanvas');
    const width = canvas.clientWidth;
    const height = canvas.clientHeight;
    const renderer = new THREE.WebGLRenderer({ canvas });
    renderer.setSize(width, height);
    const scene = new THREE.Scene();
    const camera = new THREE.PerspectiveCamera(75, width / height, 0.1, 1000);
    camera.position.z = 5;

    const positions = graph.nodes.map((n, i) => {
        const angle = (i / graph.nodes.length) * Math.PI * 2;
        return new THREE.Vector3(Math.cos(angle) * 2, Math.sin(angle) * 2, 0);
    });

    graph.nodes.forEach((n, i) => {
        const geometry = new THREE.SphereGeometry(0.1, 16, 16);
        const material = new THREE.MeshBasicMaterial({ color: 0x00ff00 });
        const mesh = new THREE.Mesh(geometry, material);
        mesh.position.copy(positions[i]);
        scene.add(mesh);
    });

    graph.edges.forEach(e => {
        const src = positions[graph.nodes.findIndex(n => n.id === e.sourceId)];
        const dst = positions[graph.nodes.findIndex(n => n.id === e.targetId)];
        const material = new THREE.LineBasicMaterial({ color: 0x0000ff });
        const points = [src, dst];
        const geometry = new THREE.BufferGeometry().setFromPoints(points);
        const line = new THREE.Line(geometry, material);
        scene.add(line);
    });

    renderer.render(scene, camera);
};
