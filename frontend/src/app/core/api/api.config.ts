/**
 * API origin. Empty in the container build, where nginx reverse-proxies /api
 * and /hubs to the API service on the same origin, so no CORS preflight and no
 * baked-in hostname. Overridden for `ng serve` by the dev file replacement.
 */
export const API_BASE = '';

export const apiUrl = (path: string): string => `${API_BASE}${path}`;

export const HUB_URL = `${API_BASE}/hubs/saakh`;
