import { ref } from 'vue';
import type { ComputedRef, Ref } from 'vue';
import { contextOptions } from '../../constants/aiCommandCenter';
import { createLogger } from '../../utils/logger';

const log = createLogger('USER');

interface ContextMenuOptions {
  inputMessage: Ref<string>;
  availableZones: ComputedRef<{ id: string; label: string }[]>;
}

export const useContextMenu = (options: ContextMenuOptions) => {
  const isContextMenuOpen = ref(false);
  const activeSubmenu = ref<string | null>(null);
  const submenuDirection = ref<'left' | 'right'>('left');
  const isAttachmentMenuOpen = ref(false);

  const toggleContextMenu = () => {
    isContextMenuOpen.value = !isContextMenuOpen.value;
    isAttachmentMenuOpen.value = false;
    if (!isContextMenuOpen.value) activeSubmenu.value = null;
  };

  const toggleAttachmentMenu = () => {
    isAttachmentMenuOpen.value = !isAttachmentMenuOpen.value;
    isContextMenuOpen.value = false;
    activeSubmenu.value = null;
  };

  const openSubmenu = (id: string, event: MouseEvent) => {
    activeSubmenu.value = id;
    const target = event.currentTarget as HTMLElement;
    const rect = target.getBoundingClientRect();
    const submenuWidth = 220;
    const windowWidth = window.innerWidth;

    if (rect.right + submenuWidth + 20 < windowWidth) {
      submenuDirection.value = 'right';
    } else {
      submenuDirection.value = 'left';
    }
  };

  const handleContextSelect = (type: string, item: { label: string }) => {
    log.debug('context selected', { type, label: item.label });

    if (type === 'zones') {
      // zone 选择追加 @zone 标记到输入框
      options.inputMessage.value += `@zone:${item.label} `;
    } else {
      options.inputMessage.value += ` [Context: ${item.label}] `;
    }

    isContextMenuOpen.value = false;
    activeSubmenu.value = null;
  };

  return {
    contextOptions,
    isContextMenuOpen,
    activeSubmenu,
    submenuDirection,
    isAttachmentMenuOpen,
    toggleContextMenu,
    toggleAttachmentMenu,
    openSubmenu,
    handleContextSelect
  };
};
