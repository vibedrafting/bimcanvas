import { ref } from 'vue';
import type { Ref } from 'vue';
import { getScreenshotService } from '../../services/ScreenshotService';
import { ChatAttachmentService, dataUrlToFile, getImageDimensions } from '../../services/ChatAttachmentService';
import type { ChatWindow } from '../../types/aiCommandCenter';
import type { ChatAttachmentRef } from '../../types/chatAttachment';
import type { InteractionRecord } from '../../types/agent';

interface ScreenshotOptions {
  agentApiBase: string;
  windows: Ref<ChatWindow[]>;
  pendingAttachments: Ref<ChatAttachmentRef[]>;
  currentProjectPath: Ref<string>;  // 当前项目路径，用于动态确定截图保存位置
  activeWindow: Ref<ChatWindow | undefined>;
  ensureProjectPath: () => Promise<void>;
}

export const useScreenshot = (options: ScreenshotOptions) => {
  const showScreenshotOverlay = ref(false);
  const processingInteractionIds = new Set<string>();

  const findTargetWindow = (windowId: string): ChatWindow | undefined => {
    return options.windows.value.find(window => window.id === windowId);
  };

  const handleScreenshotInteraction = async (record: InteractionRecord) => {
    if (record.status !== 'pending') {
      return;
    }

    const win = findTargetWindow(record.windowId);
    if (!win) {
      console.warn(`[useScreenshot] Pending screenshot points to missing window: ${record.windowId}`);
      return;
    }

    if (processingInteractionIds.has(record.interactionId)) {
      return;
    }

    processingInteractionIds.add(record.interactionId);
    let submitted = false;

    try {
      const roomId = typeof record.requestPayload?.roomId === 'string'
        ? record.requestPayload.roomId
        : undefined;

      const screenshotService = getScreenshotService(options.agentApiBase);
      const imageData = roomId
        ? await screenshotService.captureRoom(roomId)
        : await screenshotService.captureCanvas();

      await screenshotService.submitResult(record.interactionId, imageData);
      submitted = true;
      console.log(`[useScreenshot] Screenshot interaction submitted: ${record.interactionId}`);
    } catch (error) {
      console.error(`[useScreenshot] Screenshot interaction failed: ${record.interactionId}`, error);
      try {
        const screenshotService = getScreenshotService(options.agentApiBase);
        await screenshotService.submitResult(record.interactionId, null, String(error));
        submitted = true;
      } catch (submitError) {
        console.error(`[useScreenshot] Submit screenshot error failed: ${record.interactionId}`, submitError);
      }
    } finally {
      if (!submitted) {
        processingInteractionIds.delete(record.interactionId);
      }
    }
  };

  const handleScreenshotTerminal = (record: InteractionRecord) => {
    processingInteractionIds.delete(record.interactionId);
  };

  const startListening = async () => {
    const screenshotService = getScreenshotService(options.agentApiBase);
    screenshotService.startListening({
      onPushed: (record) => {
        void handleScreenshotInteraction(record);
      },
      onResolved: handleScreenshotTerminal,
      onCancelled: handleScreenshotTerminal,
      onExpired: handleScreenshotTerminal
    });

    const windowIds = options.windows.value.map(window => window.id);
    if (windowIds.length === 0) {
      return;
    }

    try {
      await screenshotService.restorePending(windowIds);
    } catch (error) {
      console.warn('[useScreenshot] Restore pending screenshots failed:', error);
    }
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
