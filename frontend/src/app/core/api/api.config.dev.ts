/**
 * Development API origin. `ng serve` runs on :4200 and the API on :5080, so the
 * dev build swaps this file in for api.config.ts (see angular.json).
 */
export const API_BASE = 'http://localhost:5080';

export const apiUrl = (path: string): string => `${API_BASE}${path}`;

export const HUB_URL = `${API_BASE}/hubs/saakh`;
