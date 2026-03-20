import { defineConfig, loadEnv } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), "");
  const gatewayUrl = env.VITE_KODACLAW_GATEWAY_URL?.trim();

  return {
    plugins: [react()],
    server: {
      host: "127.0.0.1",
      port: 4173,
      proxy: gatewayUrl
        ? {
            "/api": {
              target: gatewayUrl,
              changeOrigin: true,
              secure: false,
            },
          }
        : undefined,
    },
    preview: {
      host: "127.0.0.1",
      port: 4173,
    },
  };
});
