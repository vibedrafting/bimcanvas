import { ref } from 'vue';
import type { Ref } from 'vue';
import { getScreenshotService } from '../../services/ScreenshotService';

interface ScreenshotOptions {
  agentApiBase: string;
  pendingImages: Ref<string[]>;
}

export const useScreenshot = (options: ScreenshotOptions) => {
  const showScreenshotOverlay = ref(false);

  const startListening = () => {
    const screenshotService = getScreenshotService(options.agentApiBase);
    screenshotService.startListening();
  };

  const stopListening = () => {
    const screenshotService = getScreenshotService(options.agentApiBase);
    screenshotService.stopListening();
  };

  const handleScreenshotCapture = async (imageData: string) => {
    showScreenshotOverlay.value = false;
    try {
      const screenshotService = getScreenshotService(options.agentApiBase);
      const filePath = await screenshotService.saveToLocal(imageData);
      console.log(`[Screenshot] Saved to: ${filePath}`);
      options.pendingImages.value.push(imageData);
      console.log(`[Screenshot] Added to pending, total: ${options.pendingImages.value.length}`);
    } catch (error) {
      console.error('[Screenshot] Save failed:', error);
    }
  };

  const handleScreenshotCancel = () => {
    showScreenshotOverlay.value = false;
  };

  const removePendingImage = (index: number) => {
    options.pendingImages.value.splice(index, 1);
  };

  return {
    showScreenshotOverlay,
    startListening,
    stopListening,
    handleScreenshotCapture,
    handleScreenshotCancel,
    removePendingImage
  };
};
