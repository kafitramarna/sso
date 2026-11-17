import { useEffect, useState } from "react";
import { ArrowUpRight, LayoutGrid } from "lucide-react";
import { getMyApps } from "../lib/api";
import type { AppDto } from "../lib/types";
import { Alert } from "../components/ui/alert";
import { Skeleton } from "../components/ui/skeleton";

function initials(name: string) {
  const parts = name.trim().split(/\s+/).filter(Boolean);
  return (parts.length > 1
    ? parts[0][0] + parts[parts.length - 1][0]
    : name.slice(0, 2)
  ).toUpperCase();
}

/** /dashboard — the VenusVerse app launcher (KD-14). */
export default function DashboardPage() {
  const [apps, setApps] = useState<AppDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    getMyApps()
      .then(setApps)
      .catch((err) => setError(err instanceof Error ? err.message : "Could not load apps."));
  }, []);

  return (
    <div className="animate-rise-in">
      <header className="mb-6">
        <h1 className="font-display text-2xl font-semibold tracking-tight">Apps</h1>
        <p className="mt-1 text-sm text-muted-foreground">Your VenusVerse applications.</p>
      </header>
      {error && (
        <Alert variant="destructive" className="mb-4">
          {error}
        </Alert>
      )}
      {apps === null && !error ? (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          <Skeleton className="h-24 w-full" />
          <Skeleton className="h-24 w-full" />
          <Skeleton className="h-24 w-full" />
        </div>
      ) : apps?.length === 0 ? (
        <div className="grid place-items-center rounded-lg border border-dashed px-6 py-16 text-center">
          <LayoutGrid className="mb-3 size-8 text-muted-foreground" strokeWidth={1.5} />
          <p className="text-sm text-muted-foreground">No apps assigned — ask an administrator.</p>
        </div>
      ) : (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {apps?.map((app) => (
            <button
              key={app.client_id}
              type="button"
              onClick={() => {
                if (app.launch_url) window.location.href = app.launch_url;
              }}
              className="group flex items-start gap-4 rounded-lg border bg-card p-5 text-left transition-colors hover:border-venus/50 hover:bg-accent/40"
            >
              <span className="grid size-11 shrink-0 place-items-center rounded-md bg-venus/10 font-display text-sm font-semibold text-venus">
                {initials(app.name)}
              </span>
              <span className="min-w-0 flex-1">
                <span className="flex items-center gap-1.5 font-medium">
                  <span className="truncate">{app.name}</span>
                  <ArrowUpRight className="size-4 shrink-0 text-muted-foreground opacity-0 transition-opacity group-hover:opacity-100" />
                </span>
                <span className="mt-1 line-clamp-2 block text-sm text-muted-foreground">
                  {app.description ?? app.client_id}
                </span>
              </span>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
