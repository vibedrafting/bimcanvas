<script setup lang="ts">
import { onMounted, onUnmounted, ref, watch } from 'vue';
import { themeService } from '../../services/theme/ThemeService';

const props = defineProps<{
  active: boolean;
}>();

const canvasRef = ref<HTMLCanvasElement | null>(null);
let ctx: CanvasRenderingContext2D | null = null;
let animationFrameId: number;
let particles: Particle[] = [];
let width = 0;
let height = 0;
let time = 0;

// Configuration
const GRID_SPACING = 100; 
let GRID_ROWS = 0;
let GRID_COLS = 0;

// Colors
let COLOR_BG = '#0a0a0f';
let COLOR_GRID = '#27272a';
let COLOR_PARTICLE = '#6b7280';

// Animation State
let isOrdered = false;

// Easing: Cubic for smooth start/stop
const easeInOutCubic = (t: number): number => {
  return t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2;
};

class Particle {
  x: number;
  y: number;
  
  // Bezier Points
  p0x: number; p0y: number; // Start
  p1x: number; p1y: number; // Control
  p2x: number; p2y: number; // End (Target)
  
  row: number;
  col: number;
  
  constructor(row: number, col: number, width: number, height: number) {
    this.row = row;
    this.col = col;
    
    // 1. Calculate Target (P2)
    const totalWidth = (GRID_COLS - 1) * GRID_SPACING;
    const totalHeight = (GRID_ROWS - 1) * GRID_SPACING;
    const centerX = width / 2;
    const centerY = height / 2;
    const startGridX = centerX - totalWidth / 2;
    const startGridY = centerY - totalHeight / 2;
    
    this.p2x = startGridX + this.col * GRID_SPACING;
    this.p2y = startGridY + this.row * GRID_SPACING;

    // 2. Start Position (P0) - Large spread
    const chaosRadius = 400; 
    this.p0x = this.p2x + (Math.random() - 0.5) * chaosRadius;
    this.p0y = this.p2y + (Math.random() - 0.5) * chaosRadius;
    
    // 3. Control Point (P1) - Defines the Arc
    // Calculate midpoint
    const midX = (this.p0x + this.p2x) / 2;
    const midY = (this.p0y + this.p2y) / 2;
    
    // Add a perpendicular offset to create the arc
    // Randomize direction and magnitude for variety
    const dx = this.p2x - this.p0x;
    const dy = this.p2y - this.p0y;
    const dist = Math.sqrt(dx*dx + dy*dy);
    
    // Offset magnitude proportional to distance
    const offset = dist * 0.3 * (Math.random() > 0.5 ? 1 : -1); 
    
    // Perpendicular vector (-dy, dx)
    this.p1x = midX - dy * 0.2 + (Math.random() - 0.5) * 100;
    this.p1y = midY + dx * 0.2 + (Math.random() - 0.5) * 100;

    // Initialize current pos
    this.x = this.p0x;
    this.y = this.p0y;
  }

  updateTarget(centerX: number, centerY: number) {
    const totalWidth = (GRID_COLS - 1) * GRID_SPACING;
    const totalHeight = (GRID_ROWS - 1) * GRID_SPACING;
    const startX = centerX - totalWidth / 2;
    const startY = centerY - totalHeight / 2;
    
    this.p2x = startX + this.col * GRID_SPACING;
    this.p2y = startY + this.row * GRID_SPACING;
  }

  update(progress: number) {
    // Quadratic Bezier Formula
    // B(t) = (1-t)^2 * P0 + 2(1-t)t * P1 + t^2 * P2
    
    const t = progress;
    const u = 1 - t;
    
    this.x = (u * u * this.p0x) + (2 * u * t * this.p1x) + (t * t * this.p2x);
    this.y = (u * u * this.p0y) + (2 * u * t * this.p1y) + (t * t * this.p2y);
  }

  draw(context: CanvasRenderingContext2D, opacity: number) {
    const size = isOrdered ? 1.5 : 2;
    context.fillStyle = COLOR_PARTICLE;
    context.globalAlpha = opacity;
    context.beginPath();
    context.arc(this.x, this.y, size, 0, Math.PI * 2);
    context.fill();
    context.globalAlpha = 1.0; // Reset
  }
}

const initParticles = () => {
  particles = [];
  GRID_COLS = Math.ceil(width / GRID_SPACING) + 2;
  GRID_ROWS = Math.ceil(height / GRID_SPACING) + 2;
  
  for (let r = 0; r < GRID_ROWS; r++) {
    for (let c = 0; c < GRID_COLS; c++) {
      particles.push(new Particle(r, c, width, height));
    }
  }
};

const drawConnections = (context: CanvasRenderingContext2D, progress: number) => {
    context.lineWidth = 1;
    
    // Fade in grid lines
    // Start fading in at 0.2, reach max opacity by 1.0
    // Increased max opacity from 0.3 to 0.8 for better visibility
    const maxOpacity = 0.8;
    const opacity = progress < 0.2 ? 0 : (progress - 0.2) / 0.8 * maxOpacity;
    
    context.globalAlpha = Math.min(opacity, maxOpacity);
    context.strokeStyle = COLOR_GRID;
    context.beginPath();

    // Horizontal lines
    for (let r = 0; r < GRID_ROWS; r++) {
        const rowParticles = particles.filter(p => p.row === r).sort((a,b) => a.col - b.col);
        if (rowParticles.length > 1) {
            context.moveTo(rowParticles[0].x, rowParticles[0].y);
            for(let i=1; i<rowParticles.length; i++) {
                context.lineTo(rowParticles[i].x, rowParticles[i].y);
            }
        }
    }
    // Vertical lines
    for (let c = 0; c < GRID_COLS; c++) {
        const colParticles = particles.filter(p => p.col === c).sort((a,b) => a.row - b.row);
        if (colParticles.length > 1) {
            context.moveTo(colParticles[0].x, colParticles[0].y);
            for(let i=1; i<colParticles.length; i++) {
                context.lineTo(colParticles[i].x, colParticles[i].y);
            }
        }
    }
    context.stroke();
    context.globalAlpha = 1.0; // Reset
}

const updateColors = () => {
    const theme = themeService.currentTheme.value;
    COLOR_BG = '#' + theme.background.toString(16).padStart(6, '0');
    COLOR_GRID = '#' + theme.grid.gridLine.toString(16).padStart(6, '0');
    COLOR_PARTICLE = '#' + theme.grid.centerLine.toString(16).padStart(6, '0');
}

let transitionStartTime = 0;
const TRANSITION_DURATION = 2500; 

const animate = () => {
  if (!ctx || !canvasRef.value) return;
  
  time += 16;
  ctx.clearRect(0, 0, width, height);

  const centerX = width / 2;
  const centerY = height / 2;
  particles.forEach(p => p.updateTarget(centerX, centerY));

  // Calculate Progress (0 to 1)
  let progress = 0;
  let opacityProgress = 0;
  if (isOrdered) {
      const rawProgress = (time - transitionStartTime) / TRANSITION_DURATION;
      opacityProgress = rawProgress > 1 ? 1 : (rawProgress < 0 ? 0 : rawProgress);
      progress = rawProgress > 1 ? 1 : easeInOutCubic(rawProgress);
  }

  // Update Position (Quadratic Bezier)
  particles.forEach(p => p.update(progress));
  
  // Draw Connections
  drawConnections(ctx, progress);
  
  // Calculate Particle Opacity
  // Fade out linearly from start (0.0) to 0.8 progress (fully invisible)
  let particleOpacity = 0;
  if (opacityProgress < 0.8) {
      particleOpacity = 1.0 - (opacityProgress / 0.8);
  } else {
      particleOpacity = 0;
  }

  // Draw Particles
  if (particleOpacity > 0) {
      particles.forEach(p => p.draw(ctx!, particleOpacity));
  }

  animationFrameId = requestAnimationFrame(animate);
};

const startAnimation = () => {
  if (!animationFrameId) {
    updateColors();
    isOrdered = true;
    time = 0;
    transitionStartTime = 0;
    animate();
  }
};

const stopAnimation = () => {
  if (animationFrameId) {
    cancelAnimationFrame(animationFrameId);
    animationFrameId = 0;
  }
};

const handleResize = () => {
  if (canvasRef.value) {
    width = window.innerWidth;
    height = window.innerHeight;
    canvasRef.value.width = width;
    canvasRef.value.height = height;
    initParticles();
  }
};

watch(() => props.active, (newVal) => {
  if (newVal) {
    startAnimation();
  } else {
    setTimeout(stopAnimation, 1000);
  }
});

onMounted(() => {
  if (canvasRef.value) {
    ctx = canvasRef.value.getContext('2d');
    handleResize();
    window.addEventListener('resize', handleResize);
    if (props.active) {
      startAnimation();
    }
  }
});

onUnmounted(() => {
  window.removeEventListener('resize', handleResize);
  stopAnimation();
});

</script>

<template>
  <div class="blueprint-loader" :class="{ 'is-hidden': !active }">
    <canvas ref="canvasRef"></canvas>
    <div class="loader-content">
      <div class="status-text">
          <span class="loading-text">ESTABLISHING GRID</span>
      </div>
      <div class="progress-bar">
          <div class="progress-fill"></div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.blueprint-loader {
  position: fixed;
  top: 0;
  left: 0;
  width: 100vw;
  height: 100vh;
  background: var(--bg-scene);
  z-index: 9999;
  transition: opacity 0.8s ease, visibility 0.8s ease;
  display: flex;
  justify-content: center;
  align-items: center;
  overflow: hidden;
}

.blueprint-loader.is-hidden {
  opacity: 0;
  visibility: hidden;
  pointer-events: none;
}

canvas {
  position: absolute;
  top: 0;
  left: 0;
  width: 100%;
  height: 100%;
}

.loader-content {
  position: relative;
  z-index: 10;
  text-align: center;
  color: var(--text-primary);
  font-family: 'Courier New', Courier, monospace;
  letter-spacing: 4px;
  margin-top: 0;
  opacity: 0.8;
  transition: all 0.5s ease;
}

.status-text {
    position: relative;
    height: 20px;
    overflow: hidden;
    margin-bottom: 10px;
}

.loading-text {
    display: block;
    position: absolute;
    width: 100%;
    color: var(--text-primary);
    animation: fadeIn 1s ease forwards;
}

@keyframes fadeIn {
    from { opacity: 0; transform: translateY(10px); }
    to { opacity: 1; transform: translateY(0); }
}

.progress-bar {
    width: 200px;
    height: 2px;
    background: var(--border-subtle);
    margin: 0 auto;
    position: relative;
    overflow: hidden;
}

.progress-fill {
    position: absolute;
    left: 0;
    top: 0;
    height: 100%;
    width: 0%;
    background: var(--text-primary);
    box-shadow: 0 0 10px rgba(255,255,255,0.2);
    animation: progress 2.5s cubic-bezier(0.22, 1, 0.36, 1) forwards;
}

@keyframes progress {
    0% { width: 0%; }
    100% { width: 100%; }
}
</style>
