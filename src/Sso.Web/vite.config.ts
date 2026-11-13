import { defineConfig, loadEnv } from "vite";
import react from "@vitejs/plugin-react";

// Dev API on the SSO port (deploy/docker-compose.dev.yml, KD-12).
// Admin API lives under /v1/admin — the SPA owns the bare /admin route,
// so no path collides between page navigations and API calls.
const proxied = ["/v1", "/oauth", "/.well-known", "/health"];

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, ".", "");
  const apiBase = env.VITE_API_BASE ?? "http://localhost:5140";
  return {
    plugins: [react()],
    server: {
      port: Number(env.VITE_PORT ?? 5175),
      proxy: Object.fromEntries(proxied.map((p) => [p, apiBase])),
    },
  };
});
