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
}
