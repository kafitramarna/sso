import type { ReactNode } from "react";
import { Navigate, useLocation } from "react-router-dom";
import { useAuth } from "../lib/auth";
import { StatusView } from "./StatusView";

export function RequireAuth({ admin = false, children }: { admin?: boolean; children: ReactNode }) {
  const { status, user } = useAuth();
  const location = useLocation();

  if (status === "boot") {
    return (
      <div className="grid min-h-screen place-items-center">
        <StatusView state="loading" title="Restoring session" />
      </div>
    );
  }

  if (status !== "authed") {
    const params = new URLSearchParams(location.search);
    const ret = params.get("return");
    const suffix = ret ? `?return=${encodeURIComponent(ret)}` : "";
    return <Navigate to={`/login${suffix}`} state={{ from: location.pathname }} replace />;
  }

  if (admin && !user?.is_sso_admin) {
    return <Navigate to="/dashboard" replace />;
  }

  return <>{children}</>;
}
