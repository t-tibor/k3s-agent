import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Aspire's Vite resource injects PORT and expects the dev server to bind it; falling back to
// Vite's default keeps `npm run dev` usable standalone.
//
// /agui is proxied to the agent rather than called cross-origin from the browser (see src/agent.ts, which
// always calls a same-origin relative "/agui"). This is Vite dev-server-side, so it works identically to
// the production container serving both the SPA and /agui from one origin — no CORS policy needed on the
// agent in either case. VITE_DEV_PROXY_TARGET is wired by the AppHost (AppHost.cs) to the agent's origin
// (no path) — dev-server-proxy-only, despite the VITE_ prefix it's read here via process.env, never
// exposed to client code through import.meta.env. No fallback: running `npm run dev` standalone (outside
// the AppHost) without setting it fails loudly on the first /agui request ("Must set target or forward")
// rather than silently pointing at a guessed port.
export default defineConfig({
  plugins: [react()],
  server: {
    port: Number(process.env.PORT) || 5173,
    host: true,
    proxy: {
      "/agui": {
        target: process.env.VITE_DEV_PROXY_TARGET,
        changeOrigin: true,
      },
    },
  },
});
