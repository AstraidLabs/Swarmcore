module.exports = {
  content: ["./index.html", "./src/**/*.{ts,tsx}"],
  theme: {
    extend: {
      colors: {
        ink: "#0f172a",
        sand: "#f8fafc",
        ember: "#b91c1c",
        moss: "#0f766e",
        mist: "#cbd5e1",
        steel: "#475569",
        brand: "#1d4ed8",
        panel: "#ffffff",
        honey: {
          100: "#fff6cc",
          200: "#ffe38a",
          300: "#facc15",
          400: "#f6b73c",
          500: "#d89b00"
        }
      },
      fontFamily: {
        sans: ["IBM Plex Sans", "Inter", "Segoe UI", "sans-serif"],
        mono: ["IBM Plex Mono", "Consolas", "monospace"]
      },
      boxShadow: {
        panel: "0 20px 60px rgba(15, 23, 42, 0.10)",
        shell: "0 28px 80px rgba(15, 23, 42, 0.16)"
      }
    }
  },
  plugins: []
};
