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
import { createLogger } from './utils/logger';

const renderLog = createLogger('RENDER');
const sysLog = createLogger('SYS');

// 全局错误兜底:Vue 组件渲染/生命周期异常、未捕获 JS 错误、未处理 Promise 拒绝,
// 原本全部绕过 logger(只在 F12 裸红)。统一进结构化日志/面板——「关键渲染报错」的最后一道网。
function setupGlobalErrorLogging(app: ReturnType<typeof createApp>) {
  app.config.errorHandler = (err, _instance, info) => {
    renderLog.error('vue error', { info, msg: err instanceof Error ? err.message : String(err) });
    console.error(err); // 保留原始堆栈(errorHandler 抑制 Vue 默认打印);此处是错误兜底 sink,允许直接 console
  };
  window.addEventListener('error', (e) => {
    sysLog.error('uncaught error', { msg: e.message, src: e.filename, line: e.lineno });
  });
  window.addEventListener('unhandledrejection', (e) => {
    sysLog.error('unhandled rejection', { reason: e.reason instanceof Error ? e.reason.message : String(e.reason) });
  });
}

const isRenderMode = window.location.pathname.startsWith('/screenshot-render');
const rootComponent = isRenderMode ? ScreenshotRenderView : App;

const bootstrap = async () => {
  const runtime = await createWebRuntime();
  setWebRuntime(runtime);

  const app = createApp(rootComponent);
  const pinia = createPinia();

  setupGlobalErrorLogging(app);
  app.use(pinia);
  app.provide(WebRuntimeKey, runtime);
  app.mount('#app');

  (window as any).canvasStore = useCanvasStore();
};

void bootstrap();
