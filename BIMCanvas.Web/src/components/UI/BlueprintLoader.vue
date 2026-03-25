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
  
  // Linear Points
  p0x: number; p0y: number; // Start
  p2x: number = 0; p2y: number = 0; // End (Target)
  
  row: number;
  col: number;
  type: 'row' | 'col';
  
  // Meteor Tail Properties
  direction: number; // 1 or -1
  maxTailLength: number;
  currentTailLength: number = 0;
  currentProgress: number = 0;

  constructor(row: number, col: number, width: number, height: number, type: 'row' | 'col', direction: number) {
    this.row = row;
    this.col = col;
    this.type = type;
    
    // 1. Initialize P2 (Target) - Default centered grid
    this.calculateTarget(width, height);

    // 2. Start Position (P0) - Orthogonal Constraint based on Type
    const distance = 400 + Math.random() * 600; // Slide distance
    this.direction = direction;

    // Tail Length: Exactly GRID_SPACING to ensure perfect connection
    // This creates the effect of "drawing" the grid lines
    this.maxTailLength = GRID_SPACING;

    if (this.type === 'row') {
        // Row Particle: Moves Horizontally (Fixed Y)
        // Used to draw Horizontal lines
        this.p0x = this.p2x + distance * this.direction;
        this.p0y = this.p2y; 
    } else {
        // Col Particle: Moves Vertically (Fixed X)
        // Used to draw Vertical lines
        this.p0x = this.p2x; 
        this.p0y = this.p2y + distance * this.direction;
    }
    
    // Initialize current pos
    this.x = this.p0x;
    this.y = this.p0y;
  }

  calculateTarget(width: number, height: number) {
    // If target props are present, use them
    if (props.targetSpacing && props.targetOffsetX !== undefined && props.targetOffsetY !== undefined) {
        const centerX = width / 2;
        const centerY = height / 2;
        
        const kX = Math.round((centerX - props.targetOffsetX) / GRID_SPACING);
        const centerGridX = props.targetOffsetX + kX * GRID_SPACING;
        
        const kY = Math.round((centerY - props.targetOffsetY) / GRID_SPACING);
        const centerGridY = props.targetOffsetY + kY * GRID_SPACING;
        
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

  updateTarget(width: number, height: number) {
      this.calculateTarget(width, height);
  }

  update(progress: number) {
    const t = progress;
    // Linear Interpolation (Lerp)
    this.x = this.p0x + (this.p2x - this.p0x) * t;
    this.y = this.p0y + (this.p2y - this.p0y) * t;

    // Grow tail as we get closer to target (or just based on progress)
    // We want the tail to be fully grown when progress is 1
    this.currentTailLength = this.maxTailLength * Math.min(progress * 1.5, 1);
    this.currentProgress = progress;
  }

  draw(context: CanvasRenderingContext2D, opacity: number) {
    // Draw Head (Particle) - Shrink and disappear early (by 80% progress)
    // This creates the effect that the "meteor" burns out, leaving the grid line
    const headFadeProgress = Math.min(this.currentProgress / 0.8, 1);
    const headScale = 1 - headFadeProgress;
    
    if (headScale > 0) {
        const size = (isOrdered ? 1.5 : 2) * headScale;
        context.fillStyle = COLOR_PARTICLE;
        context.globalAlpha = opacity;
        context.beginPath();
        context.arc(this.x, this.y, size, 0, Math.PI * 2);
        context.fill();
    }

    // Draw Tail (Grid Line)
    if (this.currentTailLength > 1) {
        let tailEndX, tailEndY;
        
        if (this.type === 'row') {
            tailEndX = this.x + this.direction * this.currentTailLength;
            tailEndY = this.y;
        } else {
            tailEndX = this.x;
            tailEndY = this.y + this.direction * this.currentTailLength;
        }

        // Solid Grid Color (No Gradient) to match final grid style perfectly
        context.strokeStyle = COLOR_GRID;
        context.lineWidth = 1; 
        
        // Use 0.8 opacity to match the original grid look
        context.globalAlpha = 0.8 * opacity;
        
        context.beginPath();
        context.moveTo(this.x, this.y);
        context.lineTo(tailEndX, tailEndY);
        context.stroke();
    }

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
    for (let c = 0; c < GRID_COLS - 1; c++) {
      // Horizontal Segments (Row)
      // Segment between c and c+1
      // dir = -1: Lands at c+1, Tail goes Left to c (Motion L->R)
      // dir = 1:  Lands at c, Tail goes Right to c+1 (Motion R->L)
      const dir = Math.random() > 0.5 ? -1 : 1;
      const targetCol = dir === -1 ? c + 1 : c;
      particles.push(new Particle(r, targetCol, width, height, 'row', dir));
    }
  }

  for (let c = 0; c < GRID_COLS; c++) {
    for (let r = 0; r < GRID_ROWS - 1; r++) {
      // Vertical Segments (Col)
      // Segment between r and r+1
      // dir = -1: Lands at r+1, Tail goes Up to r (Motion U->D)
      // dir = 1:  Lands at r, Tail goes Down to r+1 (Motion D->U)
      const dir = Math.random() > 0.5 ? -1 : 1;
      const targetRow = dir === -1 ? r + 1 : r;
      particles.push(new Particle(targetRow, c, width, height, 'col', dir));
    }
  }
};



const updateColors = () => {
    const theme = themeService.currentTheme.value;
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
      // REMOVED: Premature fade out. Particles should stay to form the grid.
      // The entire component will fade out via CSS when props.active becomes false.
      opacityProgress = 0; 
      progress = rawProgress > 1 ? 1 : easeInOutCubic(rawProgress);
  } else {
      // Chaos Phase: Add subtle movement?
      // For now keep static or simple jitter
  }

  // ... update particles and draw ...
  const particleOpacity = 1.0 - opacityProgress;
  
  if (particleOpacity > 0) {
      particles.forEach(p => {
          p.update(progress);
          p.draw(ctx!, particleOpacity);
      });
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
