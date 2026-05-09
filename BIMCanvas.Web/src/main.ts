import { createApp } from 'vue';
import { createPinia } from 'pinia';
import './style.css';
import 'markstream-vue/index.css';
import App from './App.vue';
import ScreenshotRenderView from './views/ScreenshotRenderView.vue';
import { createWebRuntime } from './runtime/createWebRuntime';
import { setWebRuntime } from './runtime/runtimeRegistry';
import { WebRuntimeKey } from './runtime/WebRuntimeProtocol';
import { useCanvasStore } from './stores/canvasStore';

const isRenderMode = window.location.pathname.startsWith('/screenshot-render');
const rootComponent = isRenderMode ? ScreenshotRenderView : App;

const bootstrap = async () => {
  const runtime = await createWebRuntime();
  setWebRuntime(runtime);

  const app = createApp(rootComponent);
  const pinia = createPinia();

  app.use(pinia);
  app.provide(WebRuntimeKey, runtime);
  app.mount('#app');

  (window as any).canvasStore = useCanvasStore();
};

void bootstrap();
