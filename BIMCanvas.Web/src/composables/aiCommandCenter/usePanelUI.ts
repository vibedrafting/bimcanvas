import { ref } from 'vue';

export const usePanelUI = () => {
  const panelWidth = ref(480);
  const isResizing = ref(false);
  const windowTabsRef = ref<HTMLElement | null>(null);
  const carouselTrackRef = ref<HTMLElement | null>(null);

  const handleTabsWheel = (event: WheelEvent) => {
    if (!windowTabsRef.value) return;
    event.preventDefault();
    windowTabsRef.value.scrollLeft += event.deltaY;
  };

  const handleResize = (event: MouseEvent) => {
    const newWidth = window.innerWidth - event.clientX;
    if (newWidth >= 300 && newWidth <= 600) {
      panelWidth.value = newWidth;
    }
  };

  const stopResize = () => {
    isResizing.value = false;
    window.removeEventListener('mousemove', handleResize);
    window.removeEventListener('mouseup', stopResize);
    document.body.style.cursor = '';
    document.body.style.userSelect = '';
  };

  const startResize = () => {
    isResizing.value = true;
    window.addEventListener('mousemove', handleResize);
    window.addEventListener('mouseup', stopResize);
    document.body.style.cursor = 'ew-resize';
    document.body.style.userSelect = 'none';
  };

  const handleWheel = (event: WheelEvent) => {
    if (carouselTrackRef.value && event.deltaY !== 0) {
      event.preventDefault();
      carouselTrackRef.value.scrollLeft += event.deltaY;
    }
  };

  return {
    panelWidth,
    isResizing,
    windowTabsRef,
    carouselTrackRef,
    startResize,
    stopResize,
    handleTabsWheel,
    handleWheel
  };
};
