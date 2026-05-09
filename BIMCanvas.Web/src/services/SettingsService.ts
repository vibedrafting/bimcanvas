import axios from 'axios'
import { SERVER_API } from '../config/api'
import type {
  SettingsSnapshot,
  UpdateSettingsRequest,
  UpdateSettingsResponse
} from '../types/settings'

const API_BASE = `${SERVER_API}/settings`

export type LlmEndpointTestErrorType =
  | 'ok'
  | 'network_unreachable'
  | 'auth_failed'
  | 'rate_limited'
  | 'server_error'
  | 'timeout'
  | 'bad_request'
  | 'unknown'

export interface LlmEndpointTestRequest {
  runtimeProvider: 'claude' | 'openai'
  baseUrl: string
  apiKey: string
  model: string
  apiMode?: string | null
}

export interface LlmEndpointTestResult {
  success: boolean
  latencyMs: number
  statusCode: number | null
  errorType: LlmEndpointTestErrorType
  errorMessage: string
  sampleResponseSnippet: string
  requestUrl: string
}

export class SettingsService {
  static async getSettings(): Promise<SettingsSnapshot> {
    const response = await axios.get<SettingsSnapshot>(API_BASE)
    return response.data
  }

  static async saveSettings(payload: UpdateSettingsRequest): Promise<UpdateSettingsResponse> {
    const response = await axios.put<UpdateSettingsResponse>(API_BASE, payload)
    return response.data
  }

  static async restartInstance(): Promise<{ success: boolean; scheduled: boolean; message: string }> {
    const response = await axios.post<{ success: boolean; scheduled: boolean; message: string }>(
      `${API_BASE}/restart`
    )
    return response.data
  }

  static async testLlmEndpoint(payload: LlmEndpointTestRequest): Promise<LlmEndpointTestResult> {
    const response = await axios.post<LlmEndpointTestResult>(
      `${API_BASE}/test-llm-endpoint`,
      payload,
      { timeout: 20000 }
    )
    return response.data
  }
}
