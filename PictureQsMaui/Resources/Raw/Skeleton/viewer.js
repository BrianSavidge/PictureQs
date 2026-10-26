import * as THREE from "three";
import { OrbitControls } from "./examples/jsm/controls/OrbitControls.js";
import { GLTFLoader } from "./examples/jsm/loaders/GLTFLoader.js";
import { MeshoptDecoder } from "./examples/jsm/libs/meshopt_decoder.module.js";

const scene = new THREE.Scene();
scene.background = new THREE.Color("#101820");
const camera = new THREE.PerspectiveCamera(35, 1, 0.01, 100);
camera.position.set(0, 0, 9);

const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false });
renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
renderer.outputColorSpace = THREE.SRGBColorSpace;
renderer.toneMapping = THREE.ACESFilmicToneMapping;
renderer.toneMappingExposure = 1.15;
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFSoftShadowMap;
document.body.appendChild(renderer.domElement);

const controls = new OrbitControls(camera, renderer.domElement);
controls.enableDamping = true;
controls.dampingFactor = 0.07;
controls.enablePan = true;
controls.minDistance = 2;
controls.maxDistance = 16;
controls.target.set(0, 0, 0);

scene.add(new THREE.HemisphereLight(0xe7f1ff, 0x393029, 2.1));
const keyLight = new THREE.DirectionalLight(0xfff3df, 3.1);
keyLight.position.set(-3, 6, 5);
keyLight.castShadow = true;
scene.add(keyLight);
const fillLight = new THREE.DirectionalLight(0xc9dfff, 0.45);
fillLight.position.set(4, 1, -4);
scene.add(fillLight);

const normalizer = new THREE.Group();
scene.add(normalizer);
const markerGroup = new THREE.Group();
scene.add(markerGroup);
const raycaster = new THREE.Raycaster();
const pointer = new THREE.Vector2();
const loader = new GLTFLoader();
loader.setMeshoptDecoder(MeshoptDecoder);
let modelRoot;
let poiData = [];
let selectedIndex = -1;
let showPoints = true;
let guessPosition = null;
let guessCorrect = false;
let modelChunks = [];
let modelLoadError = null;

function resize() {
  const width = window.innerWidth;
  const height = window.innerHeight;
  if (!width || !height) return;
  renderer.setSize(width, height, false);
  camera.aspect = width / height;
  camera.updateProjectionMatrix();
}
window.addEventListener("resize", resize);
resize();

function postToHost(url) {
  window.location.href = url;
}

function renderMarkers() {
  const geometries = new Set();
  const materials = new Set();
  markerGroup.children.forEach(marker => {
    if (marker.geometry) geometries.add(marker.geometry);
    if (Array.isArray(marker.material)) marker.material.forEach(material => materials.add(material));
    else if (marker.material) materials.add(marker.material);
  });
  geometries.forEach(geometry => geometry.dispose());
  materials.forEach(material => material.dispose());
  markerGroup.clear();

  if (showPoints) {
    const markerGeometry = new THREE.SphereGeometry(0.045, 20, 16);
    poiData.forEach((poi, index) => {
      const material = new THREE.MeshStandardMaterial({
        color: index === selectedIndex ? 0xff334b : 0x36cfff,
        emissive: index === selectedIndex ? 0x57000e : 0x073a4b,
        roughness: 0.28,
        metalness: 0.1,
        depthTest: false
      });
      const marker = new THREE.Mesh(markerGeometry, material);
      marker.position.set(poi.X, poi.Y, poi.Z);
      marker.renderOrder = 10;
      markerGroup.add(marker);
    });
  }

  if (guessPosition) {
    const guess = new THREE.Mesh(
      new THREE.SphereGeometry(0.06, 24, 18),
      new THREE.MeshStandardMaterial({
        color: guessCorrect ? 0x36ef7b : 0xffa52f,
        emissive: guessCorrect ? 0x075421 : 0x512500,
        depthTest: false
      })
    );
    guess.position.set(guessPosition[0], guessPosition[1], guessPosition[2]);
    guess.renderOrder = 11;
    markerGroup.add(guess);
  }
}

window.setSceneState = state => {
  poiData = state.points || [];
  selectedIndex = state.selectedIndex;
  showPoints = state.showPoints;
  guessPosition = state.guess;
  guessCorrect = state.guessCorrect;
  renderMarkers();
};

window.zoomModel = factor => {
  const offset = camera.position.clone().sub(controls.target);
  offset.multiplyScalar(1 / factor);
  const distance = THREE.MathUtils.clamp(offset.length(), controls.minDistance, controls.maxDistance);
  offset.setLength(distance);
  camera.position.copy(controls.target).add(offset);
  controls.update();
};

window.resetView = () => {
  camera.position.set(0, 0, 9);
  controls.target.set(0, 0, 0);
  controls.update();
};

window.beginModelLoad = chunkCount => {
  modelChunks = new Array(chunkCount);
  modelLoadError = null;
};

window.appendModelChunk = (index, chunk) => {
  try {
    const binary = atob(chunk);
    const bytes = new Uint8Array(binary.length);
    for (let byteIndex = 0; byteIndex < binary.length; byteIndex++) {
      bytes[byteIndex] = binary.charCodeAt(byteIndex);
    }
    modelChunks[index] = bytes;
  } catch (error) {
    modelLoadError = error;
  }
};

function onModelLoaded(gltf) {
  modelRoot = gltf.scene;
  modelRoot.traverse(object => {
    if (object.isMesh) {
      object.castShadow = true;
      object.receiveShadow = true;
    }
  });
  const bounds = new THREE.Box3().setFromObject(modelRoot);
  const size = bounds.getSize(new THREE.Vector3());
  const center = bounds.getCenter(new THREE.Vector3());
  const scale = 5.2 / Math.max(size.x, size.y, size.z);
  modelRoot.scale.setScalar(scale);
  modelRoot.position.set(-center.x * scale, -center.y * scale, -center.z * scale);
  normalizer.add(modelRoot);

  const normalizedBounds = new THREE.Box3().setFromObject(normalizer);
  const normalizedSize = normalizedBounds.getSize(new THREE.Vector3());
  camera.position.set(0, normalizedSize.y * 0.04, Math.max(normalizedSize.y * 1.55, 8));
  controls.minDistance = normalizedSize.y * 0.32;
  controls.maxDistance = normalizedSize.y * 4;
  controls.target.set(0, 0, 0);
  controls.update();
  postToHost("pictureqs://loaded");
}

window.finishModelLoading = () => {
  if (modelLoadError || modelChunks.some(chunk => !(chunk instanceof Uint8Array))) {
    console.error("Unable to decode all skeleton model chunks", modelLoadError);
    postToHost("pictureqs://error");
    return;
  }

  const byteLength = modelChunks.reduce((total, chunk) => total + chunk.length, 0);
  const modelBytes = new Uint8Array(byteLength);
  let offset = 0;
  for (const chunk of modelChunks) {
    modelBytes.set(chunk, offset);
    offset += chunk.length;
  }

  modelChunks = [];
  loader.parse(
    modelBytes.buffer,
    "",
    onModelLoaded,
    error => {
      console.error("Unable to parse skeleton GLB", error);
      postToHost("pictureqs://error");
    }
  );
};

let downPointer = null;
let downPosition = null;
let canTap = false;
let focusOnDrag = false;
let dragPivotSelected = false;

function selectViewCenterAsOrbitPivot() {
  if (!modelRoot) return;
  raycaster.setFromCamera(new THREE.Vector2(0, 0), camera);
  const intersections = raycaster.intersectObject(modelRoot, true);
  if (!intersections.length) return;

  const nextTarget = intersections[0].point;
  const targetOffset = nextTarget.clone().sub(controls.target);
  camera.position.add(targetOffset);
  controls.target.copy(nextTarget);
  controls.update();
}

renderer.domElement.addEventListener("pointerdown", event => {
  if (event.button !== 0) return;
  if (downPointer !== null) {
    canTap = false;
    focusOnDrag = false;
    return;
  }
  downPointer = event.pointerId;
  downPosition = { x: event.clientX, y: event.clientY };
  canTap = true;
  focusOnDrag = true;
  dragPivotSelected = false;
});
renderer.domElement.addEventListener("pointermove", event => {
  if (event.pointerId !== downPointer || !downPosition) return;
  const dx = event.clientX - downPosition.x;
  const dy = event.clientY - downPosition.y;
  if ((dx * dx) + (dy * dy) > 36) {
    if (focusOnDrag && !dragPivotSelected) {
      selectViewCenterAsOrbitPivot();
      dragPivotSelected = true;
    }
    canTap = false;
  }
});
renderer.domElement.addEventListener("pointerup", event => {
  const wasTap = canTap && event.pointerId === downPointer;
  canTap = false;
  focusOnDrag = false;
  dragPivotSelected = false;
  downPointer = null;
  downPosition = null;
  if (!wasTap || !modelRoot) return;
  const bounds = renderer.domElement.getBoundingClientRect();
  pointer.x = ((event.clientX - bounds.left) / bounds.width) * 2 - 1;
  pointer.y = -((event.clientY - bounds.top) / bounds.height) * 2 + 1;
  raycaster.setFromCamera(pointer, camera);
  const intersections = raycaster.intersectObject(modelRoot, true);
  if (!intersections.length) return;
  const position = intersections[0].point;
  postToHost(`pictureqs://tap?x=${position.x}&y=${position.y}&z=${position.z}`);
});
renderer.domElement.addEventListener("pointercancel", () => {
  canTap = false;
  focusOnDrag = false;
  dragPivotSelected = false;
  downPointer = null;
  downPosition = null;
});

postToHost("pictureqs://ready");

function animate() {
  requestAnimationFrame(animate);
  controls.update();
  renderer.render(scene, camera);
}
animate();
