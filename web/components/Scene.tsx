'use client';

import { Canvas, useFrame } from '@react-three/fiber';
import { Environment, Float, RoundedBox, ScrollControls, Scroll, useScroll } from '@react-three/drei';
import { EffectComposer, Bloom } from '@react-three/postprocessing';
import { useRef } from 'react';
import * as THREE from 'three';
import Overlay from './Overlay';

function DesktopScreen() {
  const meshRef = useRef<THREE.Group>(null);
  const scroll = useScroll();

  useFrame((state, delta) => {
    if (!meshRef.current) return;

    const offset = scroll.offset;

    // Calculate target positions based on scroll offset
    let targetX = 0;
    let targetY = 0;
    let targetZ = 0;
    let targetRotationY = 0;
    let targetRotationX = 0;
    let targetScale = 1;

    if (offset < 0.5) {
      // Transition from Page 1 to Page 2
      // offset goes from 0 to 0.5, progress goes from 0 to 1
      const progress = offset * 2; 
      // Easing function for smoother transition
      const ease = 1 - Math.pow(1 - progress, 3);
      
      targetX = ease * 3.5; // Move to right
      targetRotationY = ease * (Math.PI / 2.5); // Rotate slightly less than 90 deg for better view
    } else {
      // Transition from Page 2 to Page 3
      // offset goes from 0.5 to 1, progress goes from 0 to 1
      const progress = (offset - 0.5) * 2;
      const ease = progress < 0.5 ? 2 * progress * progress : 1 - Math.pow(-2 * progress + 2, 2) / 2;
      
      targetX = 3.5 - ease * 3.5; // Move back to center
      targetY = ease * -0.5; // Move down slightly
      targetZ = ease * 3; // Move closer
      targetRotationY = (Math.PI / 2.5) - ease * (Math.PI / 2.5); // Rotate back to 0
      targetRotationX = ease * (Math.PI / 12); // Tilt slightly back
      targetScale = 1 + ease * 2.5; // Scale up dramatically
    }

    // Smoothly interpolate
    meshRef.current.position.x = THREE.MathUtils.damp(meshRef.current.position.x, targetX, 5, delta);
    meshRef.current.position.y = THREE.MathUtils.damp(meshRef.current.position.y, targetY, 5, delta);
    meshRef.current.position.z = THREE.MathUtils.damp(meshRef.current.position.z, targetZ, 5, delta);

    meshRef.current.rotation.y = THREE.MathUtils.damp(meshRef.current.rotation.y, targetRotationY, 5, delta);
    meshRef.current.rotation.x = THREE.MathUtils.damp(meshRef.current.rotation.x, targetRotationX, 5, delta);

    meshRef.current.scale.setScalar(THREE.MathUtils.damp(meshRef.current.scale.x, targetScale, 5, delta));
  });

  return (
    <group ref={meshRef}>
      <Float speed={2.5} rotationIntensity={0.2} floatIntensity={0.5}>
        {/* Outer Glass Frame */}
        <RoundedBox args={[4.2, 2.6, 0.1]} radius={0.1} smoothness={4}>
          <meshPhysicalMaterial
            color="#ffffff"
            transmission={1}
            opacity={1}
            metalness={0.2}
            roughness={0.05}
            ior={1.5}
            thickness={0.5}
            specularIntensity={1}
            envMapIntensity={1}
            clearcoat={1}
            transparent
          />
        </RoundedBox>
        
        {/* Inner Glowing Screen */}
        <RoundedBox args={[4.0, 2.4, 0.05]} radius={0.05} smoothness={4} position={[0, 0, -0.02]}>
          {/* Using an array for color to boost intensity for bloom */}
          <meshBasicMaterial color={[0.3, 0.1, 1.5]} toneMapped={false} />
        </RoundedBox>
        
        {/* Screen Content / Abstract UI Elements */}
        <RoundedBox args={[3.8, 2.2, 0.06]} radius={0.05} smoothness={4} position={[0, 0, -0.01]}>
          <meshBasicMaterial color={[0.1, 0.05, 0.5]} toneMapped={false} transparent opacity={0.8} />
        </RoundedBox>
        
        {/* Abstract floating elements inside the screen */}
        <group position={[0, 0, 0.03]}>
          <mesh position={[-1.2, 0.5, 0]}>
            <planeGeometry args={[1, 0.8]} />
            <meshBasicMaterial color={[0.5, 0.2, 2]} toneMapped={false} transparent opacity={0.6} />
          </mesh>
          <mesh position={[0.5, -0.3, 0]}>
            <planeGeometry args={[2, 1.2]} />
            <meshBasicMaterial color={[0.2, 0.8, 2]} toneMapped={false} transparent opacity={0.4} />
          </mesh>
          <mesh position={[-1.4, -0.6, 0]}>
            <circleGeometry args={[0.3, 32]} />
            <meshBasicMaterial color={[2, 0.2, 1]} toneMapped={false} transparent opacity={0.7} />
          </mesh>
        </group>
      </Float>
    </group>
  );
}

export default function Scene() {
  return (
    <div className="fixed inset-0 w-full h-full bg-[#030305] z-0">
      <Canvas camera={{ position: [0, 0, 6], fov: 45 }} dpr={[1, 2]}>
        <color attach="background" args={['#030305']} />
        <ambientLight intensity={0.2} />
        <directionalLight position={[10, 10, 10]} intensity={1} />
        <spotLight position={[-10, 10, 10]} angle={0.15} penumbra={1} intensity={2} color="#4f46e5" />
        
        <ScrollControls pages={5} damping={0.2} distance={1.2}>
          <DesktopScreen />
          <Scroll html style={{ width: '100%', height: '100%' }}>
            <Overlay />
          </Scroll>
        </ScrollControls>

        <Environment preset="city" />
        <EffectComposer>
          <Bloom luminanceThreshold={0.2} mipmapBlur luminanceSmoothing={0.9} intensity={1.5} />
        </EffectComposer>
      </Canvas>
    </div>
  );
}
