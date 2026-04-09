import { ref } from 'vue';
import type { Ref } from 'vue';
import { getScreenshotService } from '../../services/ScreenshotService';
import { ChatAttachmentService, dataUrlToFile, getImageDimensions } from '../../services/ChatAttachmentService';
import type { ChatWindow } from '../../types/aiCommandCenter';
import type { ChatAttachmentRef } from '../../types/chatAttachment';

interface ScreenshotOptions {
  agentApiBase: string;
  pendingAttachments: Ref<ChatAttachmentRef[]>;
  currentProjectPath: Ref<string>;  // 当前项目路径，用于动态确定截图保存位置
  activeWindow: Ref<ChatWindow | undefined>;
  ensureProjectPath: () => Promise<void>;
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
      if (!options.currentProjectPath.value) {
        await options.ensureProjectPath();
      }

      const projectPath = options.currentProjectPath.value;
      const activeWindow = options.activeWindow.value;
      if (!projectPath || !activeWindow) {
        throw new Error('项目路径或活动窗口不存在，无法上传截图附件');
      }

      const file = await dataUrlToFile(imageData, `chat_capture_${Date.now()}.png`);
      const dimensions = await getImageDimensions(file);
      const attachment = await ChatAttachmentService.uploadAttachment({
        projectPath,
        windowId: activeWindow.id,
        clientMessageId: activeWindow.draftMessageId,
        sourceKind: 'screenshot',
        file,
        width: dimensions?.width,
        height: dimensions?.height
      });

      options.pendingAttachments.value.push(attachment);
      console.log(`[Screenshot] Uploaded attachment: ${attachment.attachmentId}`);
    } catch (error) {
      console.error('[Screenshot] Save failed:', error);
    }
  };

  const handleScreenshotCancel = () => {
    showScreenshotOverlay.value = false;
  };

  const removePendingAttachment = async (index: number) => {
    const attachment = options.pendingAttachments.value[index];
    if (!attachment) return;

    options.pendingAttachments.value.splice(index, 1);

    if (!options.currentProjectPath.value) {
      return;
    }

    try {
      await ChatAttachmentService.deleteAttachment(options.currentProjectPath.value, attachment.attachmentId);
    } catch (error) {
      console.warn('[Screenshot] 删除附件失败:', error);
    }
  };

  return {
    showScreenshotOverlay,
    startListening,
    stopListening,
    handleScreenshotCapture,
    handleScreenshotCancel,
    removePendingAttachment
  };
};
