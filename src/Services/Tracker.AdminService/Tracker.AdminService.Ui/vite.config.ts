import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  build: {
    outDir: "../Tracker.AdminService.Api/wwwroot",
    emptyOutDir: true
  }
});
