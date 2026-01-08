import { createApp } from 'vue';
import { createPinia } from 'pinia';
import './style.css';
import 'markstream-vue/index.css';
import App from './App.vue';

const app = createApp(App);
const pinia = createPinia();

app.use(pinia);
app.mount('#app');

// Expose store for debugging/testing
import { useCanvasStore } from './stores/canvasStore';
(window as any).canvasStore = useCanvasStore();
