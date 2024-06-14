import * as THREE from 'three';
const LABEL_TEXT = 'ABC'

const clock = new THREE.Clock()
const scene = new THREE.Scene()

var width = innerWidth * devicePixelRatio
var height = innerHeight * devicePixelRatio
// Create a new framebuffer we will use to render to
// the video card memory
const renderBufferA = new THREE.WebGLRenderTarget(
  width,
  height
) 

// Create a threejs renderer:
// 1. Size it correctly
// 2. Set default background color
// 3. Append it to the page
const renderer = new THREE.WebGLRenderer()
renderer.setClearColor(0x222222)
renderer.setClearAlpha(0)
renderer.setSize(innerWidth, innerHeight)
renderer.setPixelRatio(devicePixelRatio || 1)
document.body.appendChild(renderer.domElement)


const camera = new THREE.PerspectiveCamera( 75, window.innerWidth / window.innerHeight, 0.1, 1000 );
const geometry = new THREE.BoxGeometry( 1, 1, 1 );
const material = new THREE.MeshBasicMaterial( { color: 0x00ff00 } );
const cube = new THREE.Mesh( geometry, material );
scene.add( cube );

camera.position.z = 5;

// Start out animation render loop
renderer.setAnimationLoop(onAnimLoop)


const exampleSocket = new WebSocket("ws://localhost:4322/render");
exampleSocket.onopen = (event) => {
};

var sendNext = true;
exampleSocket.onmessage = (event) => {
  console.log(event.data);
  sendNext = true;
};
 
function onAnimLoop() {
	
  cube.rotation.x += 0.01;
  cube.rotation.y += 0.01;
  
  renderer.setRenderTarget(null)
  renderer.render(scene, camera)
  
  renderer.setRenderTarget(renderBufferA)
  camera.x = 0.2;
  renderer.render(scene, camera)
  const a = new Uint8Array(width*height*4);
  renderer.readRenderTargetPixels(renderBufferA, 0, 0, width, height, a, 0)

  renderer.setRenderTarget(renderBufferA)
  camera.x = -0.2;
  renderer.render(scene, camera)
  const b = new Uint8Array(width*height*4); 
  renderer.readRenderTargetPixels(renderBufferA, 0, 0, width, height, b, 0)
  
  if(exampleSocket.readyState !== WebSocket.CLOSED && sendNext) {
	  try {
		exampleSocket.send(width + ";" + height);
		exampleSocket.send(a);
		exampleSocket.send(b);
		sendNext = false;
	  } catch(e){
	  }
  }
}
