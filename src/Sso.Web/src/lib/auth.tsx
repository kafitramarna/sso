import { create } from "zustand";
import { api, tokenStore, unwrap } from "./api";
import type { AuthPayload, UserDto } from "./types";

type Status = "boot" | "anon" | "authed";

interface AuthState {
  status: Status;
  user: UserDto | null;
  init: () => Promise<void>;
  login: (email: string, password: string) => Promise<void>;
  setAuth: (payload: AuthPayload) => void;
  refresh: () => Promise<boolean>;
  logout: () => Promise<void>;
}

let refreshTimer: ReturnType<typeof setTimeout> | undefined;
let refreshInFlight: Promise<boolean> | undefined;

const REFRESH_LEEWAY_MS = 30_000;

export const useAuth = create<AuthState>((set, get) => {
  function clear() {
    tokenStore.token = null;
    if (refreshTimer) clearTimeout(refreshTimer);
    set({ status: "anon", user: null });
  }

  function schedule(expiresInSec: number) {
    if (refreshTimer) clearTimeout(refreshTimer);
    const delay = Math.max(expiresInSec * 1000 - REFRESH_LEEWAY_MS, 10_000);
    refreshTimer = setTimeout(() => void get().refresh(), delay);
  }

  async function doRefresh(): Promise<boolean> {
    try {
      const payload = await unwrap(api.post<never>("/v1/auth/refresh"));
      get().setAuth(payload as AuthPayload);
      return true;
    } catch {
      clear();
      return false;
    }
  }

  return {
    status: "boot",
    user: null,

    init: async () => {
      await get().refresh();
    },

    setAuth: (payload) => {
      tokenStore.token = payload.token;
      schedule(payload.expires_in);
      set({ status: "authed", user: payload.user });
    },

    refresh: async () => {
      refreshInFlight ??= doRefresh().finally(() => {
        refreshInFlight = undefined;
      });
      return refreshInFlight;
    },

    login: async (email, password) => {
      const payload = await unwrap(api.post("/v1/auth/login", { email, password }));
      get().setAuth(payload as AuthPayload);
    },

    logout: async () => {
      await api.post("/v1/auth/logout").catch(() => {});
      clear();
    },
  };
});

tokenStore.refresh = () => useAuth.getState().refresh();
