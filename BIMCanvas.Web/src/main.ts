import { createApp } from 'vue';
import { createPinia } from 'pinia';
import './style.css';
import 'markstream-vue/index.css';
import App from './App.vue';
import ScreenshotRenderView from './views/ScreenshotRenderView.vue';

const isRenderMode = window.location.pathname.startsWith('/screenshot-render');
const rootComponent = isRenderMode ? ScreenshotRenderView : App;
const app = createApp(rootComponent);
const pinia = createPinia();

app.use(pinia);
app.mount('#app');

// Expose store for debugging/testing
import { useCanvasStore } from './stores/canvasStore';
(window as any).canvasStore = useCanvasStore();
