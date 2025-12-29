<script setup lang="ts">
import { onMounted, onUnmounted, ref, watch } from 'vue';
import { themeService } from '../../services/theme/ThemeService';

const props = defineProps<{
  active: boolean;
  targetSpacing?: number;
  targetOffsetX?: number;
  targetOffsetY?: number;
}>();

const canvasRef = ref<HTMLCanvasElement | null>(null);
let ctx: CanvasRenderingContext2D | null = null;
let animationFrameId: number;
let particles: Particle[] = [];
let width = 0;
let height = 0;
let time = 0;

// Configuration
let GRID_SPACING = 100; 
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
    
    // Initialize P2 (Target) - Default centered grid
    this.calculateTarget(width, height);

    // 2. Start Position (P0) - Large spread
    const chaosRadius = 400; 
    this.p0x = this.p2x + (Math.random() - 0.5) * chaosRadius;
    this.p0y = this.p2y + (Math.random() - 0.5) * chaosRadius;
    
    // 3. Control Point (P1)
    this.calculateControlPoint();

    // Initialize current pos
    this.x = this.p0x;
    this.y = this.p0y;
  }

  calculateTarget(width: number, height: number) {
    // If target props are present, use them
    if (props.targetSpacing && props.targetOffsetX !== undefined && props.targetOffsetY !== undefined) {
        // Align with World Grid (0,0) at (offsetX, offsetY)
        // We want grid lines at: targetOffset + k * spacing
        
        // 1. Calculate the starting line position (just outside or at the left/top edge)
        // We want the first column (col=0) to be at a specific position relative to the screen origin (0,0).
        // Let's say we want to cover from x=0.
        // Find k such that targetOffsetX + k * spacing <= 0 is maximized (closest to 0 from left)
        // k <= -targetOffsetX / spacing
        // k_start = Math.floor(-targetOffsetX / GRID_SPACING);
        
        // Actually, we just need to map 'col' to a valid 'k'.
        // We can start 'col=0' at the first visible line or slightly before.
        
        // Let's anchor the grid such that one line is EXACTLY at targetOffsetX.
        // The grid phase is (targetOffsetX % GRID_SPACING).
        
        const phaseX = props.targetOffsetX % GRID_SPACING;
        const phaseY = props.targetOffsetY % GRID_SPACING;
        
        // Start from the first grid line that is visible (or slightly off-screen)
        // We want startX to be roughly -spacing (to ensure coverage).
        // startX = phaseX + k * spacing.
        // We want startX approx -spacing.
        // k * spacing approx -spacing - phaseX.
        // k approx (-spacing - phaseX) / spacing.
        
        // Simpler: Just center the grid of particles around the screen, but snapped to phase.
        const totalWidth = (GRID_COLS - 1) * GRID_SPACING;
        const totalHeight = (GRID_ROWS - 1) * GRID_SPACING;
        
        const centerX = width / 2;
        const centerY = height / 2;
        
        // Find the grid line closest to center
        // lineX = targetOffsetX + k * spacing
        // We want lineX approx centerX
        // k approx (centerX - targetOffsetX) / spacing
        const kX = Math.round((centerX - props.targetOffsetX) / GRID_SPACING);
        const centerGridX = props.targetOffsetX + kX * GRID_SPACING;
        
        const kY = Math.round((centerY - props.targetOffsetY) / GRID_SPACING);
        const centerGridY = props.targetOffsetY + kY * GRID_SPACING;
        
        // Now distribute particles around this center grid line
        // col range: 0 to GRID_COLS-1
        // center col index: (GRID_COLS-1)/2
        
        // FIX: Use Math.floor to ensure we shift by an INTEGER number of spacings.
        // If we use (GRID_COLS - 1) / 2 and GRID_COLS is even, we get a 0.5 spacing shift.
        const colOffset = Math.floor(GRID_COLS / 2);
        const rowOffset = Math.floor(GRID_ROWS / 2);

        const startGridX = centerGridX - colOffset * GRID_SPACING;
        const startGridY = centerGridY - rowOffset * GRID_SPACING;
        
        this.p2x = startGridX + this.col * GRID_SPACING;
        this.p2y = startGridY + this.row * GRID_SPACING;
        
    } else {
        // Default Centered
        const totalWidth = (GRID_COLS - 1) * GRID_SPACING;
        const totalHeight = (GRID_ROWS - 1) * GRID_SPACING;
        const centerX = width / 2;
        const centerY = height / 2;
        const startGridX = centerX - totalWidth / 2;
        const startGridY = centerY - totalHeight / 2;
        
        this.p2x = startGridX + this.col * GRID_SPACING;
        this.p2y = startGridY + this.row * GRID_SPACING;
    }
  }

  calculateControlPoint() {
    const midX = (this.p0x + this.p2x) / 2;
    const midY = (this.p0y + this.p2y) / 2;
    const dx = this.p2x - this.p0x;
    const dy = this.p2y - this.p0y;
    const dist = Math.sqrt(dx*dx + dy*dy);
    const offset = dist * 0.3 * (Math.random() > 0.5 ? 1 : -1); 
    this.p1x = midX - dy * 0.2 + (Math.random() - 0.5) * 100;
    this.p1y = midY + dx * 0.2 + (Math.random() - 0.5) * 100;
  }

  updateTarget(width: number, height: number) {
      this.calculateTarget(width, height);
      // Re-calc control point to ensure smooth path if target changed significantly
      // this.calculateControlPoint(); 
  }

  // ... update and draw methods remain same
  update(progress: number) {
    // ...
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
    context.globalAlpha = 1.0; 
  }
}

const initParticles = () => {
  particles = [];
  // Use target spacing if available, otherwise default
  if (props.targetSpacing) {
      GRID_SPACING = props.targetSpacing;
  } else {
      GRID_SPACING = 100;
  }

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

  // Update Targets if window resized or props changed (handled by watchers/init)
  // But here we just update positions
  
  // Calculate Progress
  let progress = 0;
  let opacityProgress = 0;
  
  if (isOrdered) {
      const rawProgress = (time - transitionStartTime) / TRANSITION_DURATION;
      opacityProgress = rawProgress > 1 ? 1 : (rawProgress < 0 ? 0 : rawProgress);
      progress = rawProgress > 1 ? 1 : easeInOutCubic(rawProgress);
  } else {
      // Chaos Phase: Add subtle movement?
      // For now keep static or simple jitter
  }

  // ... update particles and draw ...
  particles.forEach(p => p.update(progress));
  drawConnections(ctx, progress);
  
  const particleOpacity = 1.0 - opacityProgress;
  if (particleOpacity > 0) {
      particles.forEach(p => p.draw(ctx!, particleOpacity));
  }

  animationFrameId = requestAnimationFrame(animate);
};

const startAnimation = () => {
  if (!animationFrameId) {
    updateColors();
    // Don't set isOrdered = true yet. Wait for props.
    isOrdered = false;
    time = 0;
    transitionStartTime = 0;
    animate();
  }
};

// Watch for target props to trigger transition
watch(() => props.targetSpacing, (newVal) => {
    if (newVal && !isOrdered) {
        // Received target spacing!
        // Re-init particles with new spacing
        initParticles();
        
        // Start Transition
        isOrdered = true;
        transitionStartTime = time; // Start transition NOW
    }
});

// ... handleResize, watch active ...


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
