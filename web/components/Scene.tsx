'use client';

import { Canvas, useFrame } from '@react-three/fiber';
import { Float, RoundedBox } from '@react-three/drei';
import { EffectComposer, Bloom } from '@react-three/postprocessing';
import { useEffect, useRef, useState } from 'react';
import * as THREE from 'three';
import Overlay from './Overlay';

function DesktopScreen({ scrollOffset, isMobile }: { scrollOffset: number; isMobile: boolean }) {
  const meshRef = useRef<THREE.Group>(null);

  useFrame((_state, delta) => {
    if (!meshRef.current) return;

    const offset = scrollOffset;

    let targetX = 0;
    let targetY = 0;
    let targetZ = 0;
    let targetRotationY = 0;
    let targetRotationX = 0;
    let targetScale = 1;

    if (offset < 0.5) {
      const progress = offset * 2;
      const ease = 1 - Math.pow(1 - progress, 3);
      targetX = ease * 3.5;
      targetRotationY = ease * (Math.PI / 2.5);
    } else {
      const progress = (offset - 0.5) * 2;
      const ease = progress < 0.5 ? 2 * progress * progress : 1 - Math.pow(-2 * progress + 2, 2) / 2;
      targetX = 3.5 - ease * 3.5;
      targetY = ease * -0.5;
      targetZ = ease * 3;
      targetRotationY = (Math.PI / 2.5) - ease * (Math.PI / 2.5);
      targetRotationX = ease * (Math.PI / 12);
      targetScale = 1 + ease * 2.5;
    }

    meshRef.current.position.x = THREE.MathUtils.damp(meshRef.current.position.x, targetX, 5, delta);
    meshRef.current.position.y = THREE.MathUtils.damp(meshRef.current.position.y, targetY, 5, delta);
    meshRef.current.position.z = THREE.MathUtils.damp(meshRef.current.position.z, targetZ, 5, delta);
    meshRef.current.rotation.y = THREE.MathUtils.damp(meshRef.current.rotation.y, targetRotationY, 5, delta);
    meshRef.current.rotation.x = THREE.MathUtils.damp(meshRef.current.rotation.x, targetRotationX, 5, delta);
    meshRef.current.scale.setScalar(THREE.MathUtils.damp(meshRef.current.scale.x, targetScale, 5, delta));
  });

  return (
    <group ref={meshRef}>
      <Float
        speed={isMobile ? 1.6 : 2.5}
        rotationIntensity={isMobile ? 0.1 : 0.2}
        floatIntensity={isMobile ? 0.25 : 0.5}
      >
        <RoundedBox args={[4.2, 2.6, 0.1]} radius={0.1} smoothness={isMobile ? 2 : 4}>
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

        <RoundedBox args={[4.0, 2.4, 0.05]} radius={0.05} smoothness={isMobile ? 2 : 4} position={[0, 0, -0.02]}>
          <meshBasicMaterial color={[0.3, 0.1, 1.5]} toneMapped={false} />
        </RoundedBox>

        <RoundedBox args={[3.8, 2.2, 0.06]} radius={0.05} smoothness={isMobile ? 2 : 4} position={[0, 0, -0.01]}>
          <meshBasicMaterial color={[0.1, 0.05, 0.5]} toneMapped={false} transparent opacity={0.8} />
        </RoundedBox>

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
  const [isMobile, setIsMobile] = useState(false);
  const [mounted, setMounted] = useState(false);
  const [scrollOffset, setScrollOffset] = useState(0);
  const scrollContainerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    setMounted(true);
    const mediaQuery = window.matchMedia('(max-width: 768px)');
    const apply = () => setIsMobile(mediaQuery.matches);
    apply();
    mediaQuery.addEventListener('change', apply);
    return () => mediaQuery.removeEventListener('change', apply);
  }, []);

  useEffect(() => {
    const el = scrollContainerRef.current;
    if (!el) return;
    const onScroll = () => {
      const max = el.scrollHeight - el.clientHeight;
      setScrollOffset(max > 0 ? el.scrollTop / max : 0);
    };
    el.addEventListener('scroll', onScroll, { passive: true });
    return () => el.removeEventListener('scroll', onScroll);
  }, [mounted]);

  return (
    <div className="fixed inset-0 w-full h-full bg-[#030305] z-0">
      {mounted && (
        <Canvas camera={{ position: [0, 0, 6], fov: 45 }} dpr={[1, 2]} className="!absolute !inset-0">
          <color attach="background" args={['#030305']} />
          <ambientLight intensity={isMobile ? 0.15 : 0.2} />
          <directionalLight position={[10, 10, 10]} intensity={1} />
          <spotLight
            position={[-10, 10, 10]}
            angle={0.15}
            penumbra={1}
            intensity={isMobile ? 1.25 : 2}
            color="#4f46e5"
          />

          <DesktopScreen scrollOffset={scrollOffset} isMobile={isMobile} />

          <EffectComposer>
            <Bloom luminanceThreshold={0.2} mipmapBlur luminanceSmoothing={0.9} intensity={isMobile ? 0.5 : 1.5} />
          </EffectComposer>
        </Canvas>
      )}
      {mounted && (
        <div
          ref={scrollContainerRef}
          className="absolute inset-0 w-full h-full overflow-y-auto"
          style={{ zIndex: 1 }}
        >
          <div style={{ height: '500vh', width: '100%' }}>
            <div className="sticky top-0 h-screen w-full pointer-events-none">
              <div className="pointer-events-auto">
                <Overlay />
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}