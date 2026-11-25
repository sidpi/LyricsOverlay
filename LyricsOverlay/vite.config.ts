import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

// Dev server for the hosted studio: LYRICOVERLAY_STUDIO_DEV=http://127.0.0.1:1420.
export default defineConfig({
  // Relative asset URLs: the built studio is loaded from the app's own folder, not a server root.
  base: "./",
  plugins: [react(), tailwindcss()],
  clearScreen: false,
  server: {
    host: "127.0.0.1",
    port: 1420,
    strictPort: true,
  },
  envPrefix: ["VITE_"],
  build: {
    // Built straight into the WPF app, which ships the studio inside LyricOverlay.exe.
    outDir: "../LyricOverlay/Studio",
    emptyOutDir: true,
    target: "chrome105",
    minify: "esbuild",
    sourcemap: false,
  },
});
