import { computed, ref } from 'vue';
import type { Ref } from 'vue';
import type { ChatWindow } from '../../types/aiCommandCenter';

interface ChatScrollOptions {
  mode: Ref<'chat' | 'tasks'>;
  windows: Ref<ChatWindow[]>;
  activeWindowId: Ref<string>;
}

export const useChatScroll = (options: ChatScrollOptions) => {
  const chatScrollRefs = ref<Record<string, HTMLElement | null>>({});
  const chatBottomRefs = ref<Record<string, HTMLElement | null>>({});
  const pendingScrollFrames = new Map<string, number>();

  const chatScrollRef = computed(() => chatScrollRefs.value[options.activeWindowId.value] || null);
  const chatBottomRef = computed(() => chatBottomRefs.value[options.activeWindowId.value] || null);

  const setChatScrollRef = (windowId: string, el: HTMLElement | null) => {
    if (el) {
      chatScrollRefs.value[windowId] = el;
    }
  };

  const setChatBottomRef = (windowId: string, el: HTMLElement | null) => {
    if (el) {
      chatBottomRefs.value[windowId] = el;
    }
  };

  const isNearBottom = (windowId?: string) => {
    const targetWindowId = windowId || options.activeWindowId.value;
    const el = chatScrollRefs.value[targetWindowId];
    if (!el) return true;
    const threshold = 100;
    return el.scrollHeight - el.scrollTop - el.clientHeight < threshold;
  };

  const handleChatScroll = (windowId: string) => {
    if (options.mode.value !== 'chat') return;
    const win = options.windows.value.find(w => w.id === windowId);
    if (win) {
      win.shouldAutoScroll = isNearBottom(windowId);
    }
  };

  const performScrollToBottom = (targetWindowId: string) => {
    const bottomRef = chatBottomRefs.value[targetWindowId];
    if (bottomRef) {
      bottomRef.scrollIntoView({ block: 'end' });
      return;
    }

    const el = chatScrollRefs.value[targetWindowId];
    if (el) {
      el.scrollTop = el.scrollHeight;
    }
  };

  const scrollToBottom = (scrollOptions?: { force?: boolean; windowId?: string }) => {
    if (!scrollOptions?.force && options.mode.value !== 'chat') return;

    const targetWindowId = scrollOptions?.windowId || options.activeWindowId.value;
    const targetWin = options.windows.value.find(w => w.id === targetWindowId);

    if (!scrollOptions?.force && targetWin && !targetWin.shouldAutoScroll) return;

    const pendingFrame = pendingScrollFrames.get(targetWindowId);
    if (scrollOptions?.force) {
      if (pendingFrame !== undefined) {
        cancelAnimationFrame(pendingFrame);
        pendingScrollFrames.delete(targetWindowId);
      }
      performScrollToBottom(targetWindowId);
      return;
    }

    if (pendingFrame !== undefined) return;

    const frame = requestAnimationFrame(() => {
      pendingScrollFrames.delete(targetWindowId);
      const latestTargetWin = options.windows.value.find(w => w.id === targetWindowId);
      if (latestTargetWin && !latestTargetWin.shouldAutoScroll) return;
      performScrollToBottom(targetWindowId);
    });
    pendingScrollFrames.set(targetWindowId, frame);
  };

  const handleTableWheel = (event: WheelEvent) => {
    const target = event.target as HTMLElement;
    const tableWrapper = target.closest('.table-node-wrapper');

    if (tableWrapper) {
      if (Math.abs(event.deltaY) > Math.abs(event.deltaX)) {
        event.preventDefault();
        const scrollContainer = chatScrollRef.value;
        if (scrollContainer) {
          scrollContainer.scrollTop += event.deltaY;
        }
      }
    }
  };

  return {
    chatScrollRefs,
    chatBottomRefs,
    chatScrollRef,
    chatBottomRef,
    setChatScrollRef,
    setChatBottomRef,
    isNearBottom,
    handleChatScroll,
    scrollToBottom,
    handleTableWheel
  };
};
