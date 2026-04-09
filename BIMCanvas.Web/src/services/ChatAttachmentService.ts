import { SERVER_API } from '../config/api';
import type { ChatAttachmentRef, ChatAttachmentSourceKind } from '../types/chatAttachment';

const API_BASE = `${SERVER_API}/chat/attachments`;

interface UploadAttachmentOptions {
  projectPath: string;
  windowId: string;
  clientMessageId: string;
  sourceKind: ChatAttachmentSourceKind;
  file: File;
  width?: number;
  height?: number;
}

interface CommitAttachmentsOptions {
  projectPath: string;
  windowId: string;
  clientMessageId: string;
  attachmentIds: string[];
}

export class ChatAttachmentService {
  static async uploadAttachment(options: UploadAttachmentOptions): Promise<ChatAttachmentRef> {
    const formData = new FormData();
    formData.append('projectPath', options.projectPath);
    formData.append('windowId', options.windowId);
    formData.append('clientMessageId', options.clientMessageId);
    formData.append('sourceKind', options.sourceKind);
    formData.append('file', options.file);

    if (typeof options.width === 'number') {
      formData.append('width', String(options.width));
    }

    if (typeof options.height === 'number') {
      formData.append('height', String(options.height));
    }

    const response = await fetch(API_BASE, {
      method: 'POST',
      body: formData
    });

    if (!response.ok) {
      throw new Error(await readErrorMessage(response, '上传附件失败'));
    }

    return response.json() as Promise<ChatAttachmentRef>;
  }

  static async deleteAttachment(projectPath: string, attachmentId: string): Promise<void> {
    const response = await fetch(
      `${API_BASE}/${encodeURIComponent(attachmentId)}?projectPath=${encodeURIComponent(projectPath)}`,
      { method: 'DELETE' }
    );

    if (!response.ok && response.status !== 404) {
      throw new Error(await readErrorMessage(response, '删除附件失败'));
    }
  }

  static async commitAttachments(options: CommitAttachmentsOptions): Promise<void> {
    if (options.attachmentIds.length === 0) {
      return;
    }

    const response = await fetch(`${API_BASE}/commit`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(options)
    });

    if (!response.ok) {
      throw new Error(await readErrorMessage(response, '提交附件失败'));
    }
  }
}

export function createDraftMessageId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return `msg_${crypto.randomUUID()}`;
  }

  return `msg_${Date.now()}_${Math.random().toString(36).slice(2, 10)}`;
}

export async function dataUrlToFile(dataUrl: string, filename: string): Promise<File> {
  const response = await fetch(dataUrl);
  const blob = await response.blob();
  return new File([blob], filename, {
    type: blob.type || 'image/png'
  });
}

export async function getImageDimensions(file: Blob): Promise<{ width: number; height: number } | undefined> {
  const objectUrl = URL.createObjectURL(file);

  try {
    const dimensions = await new Promise<{ width: number; height: number }>((resolve, reject) => {
      const image = new Image();
      image.onload = () => resolve({ width: image.naturalWidth, height: image.naturalHeight });
      image.onerror = () => reject(new Error('读取图片尺寸失败'));
      image.src = objectUrl;
    });

    return dimensions;
  } finally {
    URL.revokeObjectURL(objectUrl);
  }
}

async function readErrorMessage(response: Response, fallback: string): Promise<string> {
  try {
    const payload = await response.json();
    return payload?.message || payload?.error || fallback;
  } catch {
    return fallback;
  }
}
