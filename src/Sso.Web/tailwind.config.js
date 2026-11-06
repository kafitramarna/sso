/** @type {import('tailwindcss').Config} */
export default {
  content: ["./index.html", "./src/**/*.{ts,tsx}"],
  theme: {
    extend: {
      colors: {
        brand: {
          50: "var(--brand-50)",
          500: "var(--brand-500)",
          600: "var(--brand-600)",
          700: "var(--brand-700)",
        },
        surface: "var(--bg)",
        subtle: "var(--bg-subtle)",
        muted: "var(--bg-muted)",
        border: {
          DEFAULT: "var(--border)",
          strong: "var(--border-strong)",
        },
        fg: {
          DEFAULT: "var(--fg)",
          muted: "var(--fg-muted)",
          subtle: "var(--fg-subtle)",
        },
        status: {
          warn: "var(--status-warn)",
          ok: "var(--status-ok)",
          error: "var(--status-error)",
        },
      },
      fontFamily: {
        sans: ["Inter", "system-ui", "-apple-system", "sans-serif"],
        mono: ["JetBrains Mono", "SF Mono", "monospace"],
      },
      borderRadius: {
        sm: "var(--radius-sm)",
        DEFAULT: "var(--radius)",
        lg: "var(--radius-lg)",
      },
      boxShadow: {
        card: "0 1px 2px rgb(17 24 39 / 0.05)",
        pop: "0 4px 16px rgb(17 24 39 / 0.12)",
      },
    },
  },
  plugins: [],
};
