import axios from 'axios';
import { SERVER_API } from '../config/api';

const MODULES_API = `${SERVER_API}/modules`;
const VALIDATION_API = `${SERVER_API}/validation`;

export interface Diagnostic {
  code: string;
  severity: 'error' | 'warning';
  message: string;
  moduleId: string;
  moduleName?: string | null;
  conflictId?: string | null;
  conflictType?: string | null;
  overlapAreaMm2?: number | null;
  penetrationDepthMm?: number | null;
  penetrationDirection?: string | null;
}

export interface ModuleNormalizationReport {
  isValid: boolean;
  totalModules: number;
  normalizedCount: number;
  errorCount: number;
  warningCount: number;
  diagnostics: Diagnostic[];
  elapsedMs: number;
}

export interface SchemeValidationReport {
  isValid: boolean;
  totalModules: number;
  errorCount: number;
  warningCount: number;
  diagnostics: Diagnostic[];
  elapsedMs: number;
}

export interface LayoutRequest {
  zoneIds?: string[];
  /** 非空时必须同时提供 zoneIds（server 强约束：variantId 非空时 zoneIds 必填） */
  variantId?: string;
}

export class LayoutValidationService {
  static async normalizeModules(request: LayoutRequest = {}): Promise<ModuleNormalizationReport> {
    const response = await axios.post<ModuleNormalizationReport>(`${MODULES_API}/normalize`, request);
    return response.data;
  }

  static async validateLayout(request: LayoutRequest = {}): Promise<SchemeValidationReport> {
    const response = await axios.post<SchemeValidationReport>(`${VALIDATION_API}/layout`, request);
    return response.data;
  }

  /**
   * 批量规范化：一次请求、服务端一个 python 子进程跑完所有 scope（取代逐 scope 各起子进程）。
   * 返回与 scopes 顺序对齐的报告数组。
   */
  static async normalizeBatch(scopes: LayoutRequest[]): Promise<ModuleNormalizationReport[]> {
    const response = await axios.post<{ reports: ModuleNormalizationReport[] }>(
      `${VALIDATION_API}/batch`,
      { mode: 'normalize', scopes }
    );
    return response.data.reports;
  }

  /** 批量布局验证：同 normalizeBatch，mode=validate。返回与 scopes 顺序对齐的报告数组。 */
  static async validateBatch(scopes: LayoutRequest[]): Promise<SchemeValidationReport[]> {
    const response = await axios.post<{ reports: SchemeValidationReport[] }>(
      `${VALIDATION_API}/batch`,
      { mode: 'validate', scopes }
    );
    return response.data.reports;
  }
}
