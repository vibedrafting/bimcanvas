export type ChatAttachmentStatus = 'draft' | 'submitted' | 'deleted';
export type ChatAttachmentSourceKind = 'screenshot' | 'upload' | 'paste';

export interface ChatAttachmentRef {
  attachmentId: string;
  clientMessageId: string;
  sourceKind: ChatAttachmentSourceKind;
  originalFileName: string;
  mimeType: string;
  sizeBytes: number;
  width?: number;
  height?: number;
  status: ChatAttachmentStatus;
  contentUrl: string;
}
