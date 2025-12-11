<script setup lang="ts">
import { ref, onMounted } from 'vue';
import SvgCanvas from './components/Canvas/SvgCanvas.vue';
import { ApiService } from './services/ApiService';
import type { CanvasDocument } from './types/canvas';

const document = ref<CanvasDocument | null>(null);
const loading = ref(false);
const error = ref<string | null>(null);
const serverStatus = ref<string>('checking...');
const canvasIds = ref<string[]>([]);

// 检查服务器状态
async function checkServer() {
  try {
    const health = await ApiService.healthCheck();
    serverStatus.value = `Server: ${health.status}`;
  } catch (e) {
    serverStatus.value = 'Server: offline';
  }
}

// 从本地文件加载测试数据
async function loadTestData() {
  loading.value = true;
  error.value = null;
  try {
    // 从 public 目录加载测试 JSON
    const response = await fetch('/test-data.json');
    if (!response.ok) {
      throw new Error('Test data not found');
    }
    const data = await response.json();
    document.value = data;

    // 同时上传到 Server
    try {
      const stored = await ApiService.createCanvas(data);
      document.value = stored;
      console.log('Canvas stored on server:', stored.id);
    } catch (e) {
      console.warn('Failed to upload to server:', e);
    }
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Unknown error';
  } finally {
    loading.value = false;
  }
}

// 从服务器加载画布
async function loadFromServer(id: string) {
  loading.value = true;
  error.value = null;
  try {
    document.value = await ApiService.getCanvas(id);
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Unknown error';
  } finally {
    loading.value = false;
  }
}

// 获取服务器上的画布列表
async function refreshCanvasList() {
  try {
    canvasIds.value = await ApiService.getAllCanvasIds();
  } catch (e) {
    console.warn('Failed to get canvas list:', e);
  }
}

onMounted(async () => {
  await checkServer();
  await refreshCanvasList();
});
</script>

<template>
  <div class="app">
    <header class="header">
      <h1>BIMCanvas Web</h1>
      <div class="status">
        <span :class="serverStatus.includes('healthy') ? 'online' : 'offline'">
          {{ serverStatus }}
        </span>
      </div>
    </header>

    <div class="toolbar">
      <button @click="loadTestData" :disabled="loading">
        {{ loading ? 'Loading...' : 'Load Test Data' }}
      </button>
      <button @click="refreshCanvasList" :disabled="loading">
        Refresh List
      </button>

      <div v-if="canvasIds.length > 0" class="canvas-list">
        <span>Server Canvases:</span>
        <button
          v-for="id in canvasIds"
          :key="id"
          @click="loadFromServer(id)"
          class="canvas-id-btn"
        >
          {{ id.substring(0, 20) }}...
        </button>
      </div>
    </div>

    <div v-if="error" class="error">
      Error: {{ error }}
    </div>

    <main class="main">
      <SvgCanvas :document="document" />
    </main>

    <footer class="footer">
      <div v-if="document">
        Walls: {{ document.walls.length }} |
        Columns: {{ document.columns.length }} |
        Openings: {{ document.openings.length }} |
        Rooms: {{ document.rooms.length }}
      </div>
    </footer>
  </div>
</template>

<style>
* {
  margin: 0;
  padding: 0;
  box-sizing: border-box;
}

body {
  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
  background: #1a1a1a;
  color: #fff;
}

.app {
  display: flex;
  flex-direction: column;
  min-height: 100vh;
}

.header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 12px 20px;
  background: #2d2d2d;
  border-bottom: 1px solid #444;
}

.header h1 {
  font-size: 1.2rem;
  font-weight: 500;
}

.status {
  font-size: 0.85rem;
}

.status .online {
  color: #27ae60;
}

.status .offline {
  color: #e74c3c;
}

.toolbar {
  display: flex;
  gap: 10px;
  padding: 12px 20px;
  background: #252525;
  border-bottom: 1px solid #444;
  flex-wrap: wrap;
  align-items: center;
}

.toolbar button {
  padding: 8px 16px;
  background: #3498db;
  color: white;
  border: none;
  border-radius: 4px;
  cursor: pointer;
  font-size: 0.9rem;
}

.toolbar button:hover {
  background: #2980b9;
}

.toolbar button:disabled {
  background: #666;
  cursor: not-allowed;
}

.canvas-list {
  display: flex;
  gap: 8px;
  align-items: center;
  margin-left: 20px;
}

.canvas-list span {
  color: #888;
  font-size: 0.85rem;
}

.canvas-id-btn {
  padding: 4px 8px !important;
  background: #555 !important;
  font-size: 0.8rem !important;
}

.canvas-id-btn:hover {
  background: #666 !important;
}

.error {
  padding: 12px 20px;
  background: #e74c3c;
  color: white;
}

.main {
  flex: 1;
  overflow: auto;
}

.footer {
  padding: 10px 20px;
  background: #2d2d2d;
  border-top: 1px solid #444;
  font-size: 0.85rem;
  color: #888;
}
</style>
