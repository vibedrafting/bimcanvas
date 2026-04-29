import type { WebRuntime } from './WebRuntimeProtocol';

let currentRuntime: WebRuntime | null = null;

export const setWebRuntime = (runtime: WebRuntime) => {
  currentRuntime = runtime;
};

export const getWebRuntime = (): WebRuntime => {
  if (!currentRuntime) {
    throw new Error('WebRuntime has not been initialized');
  }
  return currentRuntime;
};
