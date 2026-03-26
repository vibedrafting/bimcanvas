import axios from 'axios'
import { SERVER_API } from '../config/api'
import type {
  SettingsSnapshot,
  UpdateSettingsRequest,
  UpdateSettingsResponse
} from '../types/settings'

const API_BASE = `${SERVER_API}/settings`

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
}
