import * as THREE from 'three';
const LABEL_TEXT = 'ABC'

const clock = new THREE.Clock()
const scene = new THREE.Scene()

var width = innerWidth 
var height = innerHeight 
// Create a new framebuffer we will use to render to
// the video card memory
const renderBufferA = new THREE.WebGLRenderTarget(
  width,
  height
) 

const renderBufferB = new THREE.WebGLRenderTarget(
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

var sendNext = false;
const exampleSocket = new WebSocket("ws://localhost:4322/render");
exampleSocket.onopen = (event) => {
  sendNext = true;
};

exampleSocket.onmessage = (event) => {
  console.log(event.data);
  sendNext = true;
};

function imageData2Blob(image, type, quality) {
  const canvas = document.createElement('canvas');
  canvas.width = image.width;
  canvas.height = image.height;
  canvas.getContext('2d').putImageData(image, 0, 0);
  return new Promise((res) => 
	canvas.toBlob(res, type, quality)
);
}

function str2ab(str) {
  var buf = new ArrayBuffer(str.length);
  var bufView = new Uint8Array(buf);
  for (var i=0, strLen=str.length; i<strLen; i++) {
      bufView[i] = str.charCodeAt(i);
  }
  return buf;
}
 
function onAnimLoop() {
	
  cube.rotation.x += 0.001;
  cube.rotation.y += 0.001;
  cube.material.color.setHex(0x00ff00);
  renderer.setRenderTarget(null);
  renderer.render(scene, camera);

  var asString = renderer.domElement.toDataURL()//("image/jpeg", 0.2);
  var b = str2ab(asString);
  if(exampleSocket.readyState !== WebSocket.CLOSED && sendNext) {
    sendNext = false;
    exampleSocket.send(width + ";" + height);
    console.warn(b.byteLength);
    exampleSocket.send(b);
    exampleSocket.send(b);
  }
  //renderer.setRenderTarget(renderBufferA)
  //camera.position.x = -0.5;
  //renderer.render(scene, camera)
  //const a = new Uint8Array(width*height*4);
  //renderer.readRenderTargetPixels(renderBufferA, 0, 0, width, height, a, 0)
//
  //renderer.setRenderTarget(renderBufferB)
  //camera.position.x = 0.5;
  ////cube.material.color.setHex(0xff0000);
  //renderer.render(scene, camera)
  //const b = new Uint8Array(width*height*4); 
  //renderer.readRenderTargetPixels(renderBufferB, 0, 0, width, height, b, 0)



	//const ui8ca = new Uint8ClampedArray(a);
	//const imageData = new ImageData(ui8ca, width, height);
//
	//const ui8ca2 = new Uint8ClampedArray(b);
	//const imageData2 = new ImageData(ui8ca2, width, height);
	//  if(exampleSocket.readyState !== WebSocket.CLOSED && sendNext) {
	//sendNext = true;
	//	imageData2Blob(imageData, 'jpg', 10).then(l => 
	//		{
	//			imageData2Blob(imageData2, 'jpg', 10).then(r => 
	//			{
	//				exampleSocket.send(width + ";" + height);
	//				exampleSocket.send(l);
	//				exampleSocket.send(r);
	//			});
	//	});
	//  }
}
