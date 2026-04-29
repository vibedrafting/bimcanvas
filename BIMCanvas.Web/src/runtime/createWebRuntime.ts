import { SERVER_API } from '../config/api';
import type { WebRuntime } from './WebRuntimeProtocol';
import { ConnectedRuntime } from './ConnectedRuntime';
import { StandaloneRuntime } from './StandaloneRuntime';

const probeServer = async (timeoutMs: number): Promise<boolean> => {
  const controller = new AbortController();
  const timeoutId = window.setTimeout(() => controller.abort(), timeoutMs);

  try {
    const response = await fetch(`${SERVER_API}/project/status`, {
      signal: controller.signal
    });
    return response.ok;
  } catch {
    return false;
  } finally {
    window.clearTimeout(timeoutId);
  }
};

export const createWebRuntime = async (): Promise<WebRuntime> => {
  const forced = String(import.meta.env.VITE_WEB_RUNTIME ?? '').trim().toLowerCase();

  if (forced === 'standalone') {
    return new StandaloneRuntime();
  }

  if (forced === 'connected') {
    return new ConnectedRuntime();
  }

  return (await probeServer(1000))
    ? new ConnectedRuntime()
    : new StandaloneRuntime();
};
