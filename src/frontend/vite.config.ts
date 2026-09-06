import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Aspire's Vite resource injects PORT and expects the dev server to bind it; falling back to
// Vite's default keeps `npm run dev` usable standalone.
export default defineConfig({
  plugins: [react()],
  server: {
    port: Number(process.env.PORT) || 5173,
    host: true,
  },
});
