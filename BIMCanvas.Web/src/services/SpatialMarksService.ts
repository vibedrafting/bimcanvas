import axios from 'axios';
import { SERVER_API } from '../config/api';
import type { GridSelectionCell, SpatialGeometry } from '../types/aiCommandCenter';

const API_BASE = `${SERVER_API}/spatial-marks`;

export interface MergeGridSelectionRequest {
  zoneId: string;
  cellSize: number;
  cells: GridSelectionCell[];
}

export interface MergeGridSelectionResponse {
  zoneId: string;
  geometry: SpatialGeometry[];
}

export const SpatialMarksService = {
  async mergeGridSelection(request: MergeGridSelectionRequest): Promise<MergeGridSelectionResponse> {
    const response = await axios.post<MergeGridSelectionResponse>(
      `${API_BASE}/merge-grid-selection`,
      request
    );
    return response.data;
  }
};
