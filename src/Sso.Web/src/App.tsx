import { useEffect, useState } from "react";
import { Route, Routes } from "react-router-dom";
import { getReady, type ReadyReport } from "./lib/api";

function HomePage() {
  const [ready, setReady] = useState<ReadyReport | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    getReady()
      .then(setReady)
      .catch((e) => setError(e instanceof Error ? e.message : "unreachable"));
  }, []);

  return (
    <main className="min-h-screen grid place-items-center p-6">
      <div className="w-full max-w-md rounded-lg border border-border bg-white p-8 shadow-card">
        <p className="text-xs font-semibold uppercase tracking-widest text-brand-600">
          VenusVerse
        </p>
        <h1 className="mt-1 text-2xl font-bold">SSO</h1>
        <p className="mt-2 text-sm text-fg-muted">
          Skeleton OK — halaman login, consent, account, dan admin dibangun di Fase 1–3
          (lihat <code className="font-mono text-xs">docs/01-PRD.md</code>).
        </p>
        <div className="mt-6 rounded border border-border bg-subtle p-3 font-mono text-xs">
          <p className="text-fg-subtle">/health/ready</p>
          {error && <p className="mt-1 text-status-error">api unreachable: {error}</p>}
          {ready && (
            <ul className="mt-1 space-y-0.5">
              {Object.entries(ready.dependencies).map(([name, dep]) => (
                <li key={name} className="flex justify-between">
                  <span>{name}</span>
                  <span className={dep.status === "healthy" ? "text-status-ok" : "text-status-warn"}>
                    {dep.status}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>
    </main>
  );
}

export default function App() {
  return (
    <Routes>
      <Route path="/" element={<HomePage />} />
      <Route path="*" element={<HomePage />} />
    </Routes>
  );
}
