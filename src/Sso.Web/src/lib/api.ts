import axios from "axios";

/**
 * Same-origin client — the vite proxy (dev) or NPM (prod) forwards
 * /v1, /oauth, /.well-known, /admin, /health to Sso.Api.
 */
export const api = axios.create({ baseURL: "/", withCredentials: true });

export interface ReadyReport {
  status: string;
  dependencies: Record<string, { status: string; description?: string }>;
}

export const getReady = () => api.get<ReadyReport>("/health/ready").then((r) => r.data);
