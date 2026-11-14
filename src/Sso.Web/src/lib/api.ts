import axios, { AxiosError, type AxiosResponse } from "axios";
import type { Envelope } from "./types";

/**
 * Same-origin client — the vite proxy (dev) or NPM (prod) forwards
 * /v1, /oauth, /.well-known, /health to Sso.Api.
 */
export const api = axios.create({ baseURL: "/", withCredentials: true });

export class ApiError extends Error {
  constructor(
    public code: string,
    message: string,
    public status?: number,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

/**
 * Bearer token lives only in module memory — never persisted. The auth store
 * owns refresh; it registers itself on `refresh` so the interceptor can retry
 * a 401 once without an import cycle.
 */
export const tokenStore: { token: string | null; refresh?: () => Promise<boolean> } = {
  token: null,
};

api.interceptors.request.use((cfg) => {
  if (tokenStore.token) cfg.headers.Authorization = `Bearer ${tokenStore.token}`;
  return cfg;
});

api.interceptors.response.use(
  (r) => r,
  async (err: AxiosError) => {
    const status = err.response?.status;
    const url = err.config?.url ?? "";
    if (
      status === 401 &&
      !(err.config as { __retried?: boolean }).__retried &&
      !url.startsWith("/v1/auth") &&
      tokenStore.refresh
    ) {
      (err.config as { __retried?: boolean }).__retried = true;
      if (await tokenStore.refresh()) return api(err.config!);
    }

    const e = (err.response?.data as Envelope<unknown> | undefined)?.error;
    const message =
      status === 429
        ? "Too many attempts — please wait a few minutes and try again."
        : (e?.message ?? err.message ?? "Request failed.");
    return Promise.reject(new ApiError(e?.code ?? "error", message, status));
  },
);

export const unwrap = <T>(p: Promise<AxiosResponse<Envelope<T>>>): Promise<T> =>
  p.then((r) => r.data.data);

export interface ReadyReport {
  status: string;
  dependencies: Record<string, { status: string; description?: string | null }>;
}

export const getReady = () => api.get<ReadyReport>("/health/ready").then((r) => r.data);

export interface AuthConfig {
  allow_self_registration: boolean;
}

export const getAuthConfig = () =>
  unwrap(api.get<Envelope<AuthConfig>>("/v1/auth/config"));
