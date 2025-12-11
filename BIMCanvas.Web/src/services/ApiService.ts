import type { CanvasDocument } from '../types/canvas';

const API_BASE_URL = 'http://localhost:5000/api';

export const ApiService = {
  /**
   * 获取画布文档
   */
  async getCanvas(id: string): Promise<CanvasDocument> {
    const response = await fetch(`${API_BASE_URL}/canvas/${id}`);
    if (!response.ok) {
      throw new Error(`Failed to get canvas: ${response.statusText}`);
    }
    return response.json();
  },

  /**
   * 创建/更新画布文档
   */
  async createCanvas(document: CanvasDocument): Promise<CanvasDocument> {
    const response = await fetch(`${API_BASE_URL}/canvas`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(document),
    });
    if (!response.ok) {
      throw new Error(`Failed to create canvas: ${response.statusText}`);
    }
    return response.json();
  },

  /**
   * 获取所有画布ID列表
   */
  async getAllCanvasIds(): Promise<string[]> {
    const response = await fetch(`${API_BASE_URL}/canvas`);
    if (!response.ok) {
      throw new Error(`Failed to get canvas list: ${response.statusText}`);
    }
    return response.json();
  },

  /**
   * 从本地文件加载 JSON（用于测试）
   */
  async loadFromFile(filePath: string): Promise<CanvasDocument> {
    const response = await fetch(filePath);
    if (!response.ok) {
      throw new Error(`Failed to load file: ${response.statusText}`);
    }
    return response.json();
  },

  /**
   * 健康检查
   */
  async healthCheck(): Promise<{ status: string; timestamp: string }> {
    const response = await fetch(`http://localhost:5000/health`);
    if (!response.ok) {
      throw new Error(`Health check failed: ${response.statusText}`);
    }
    return response.json();
  },
};
