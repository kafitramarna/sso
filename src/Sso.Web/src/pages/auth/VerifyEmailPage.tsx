import { useEffect, useRef, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { api, unwrap } from "../../lib/api";
import { useAuth } from "../../lib/auth";
import type { AuthPayload } from "../../lib/types";
import { Button } from "../../components/ui/button";
import { StatusView } from "../../components/StatusView";

export default function VerifyEmailPage() {
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const setAuth = useAuth((s) => s.setAuth);
  const consumed = useRef(false);
  const [result, setResult] = useState<{ ok: boolean; message?: string } | null>(null);
  const token = params.get("token");

  useEffect(() => {
    if (consumed.current) return;
    consumed.current = true;
    if (!token) {
      setResult({ ok: false, message: "This link is missing its token." });
      return;
    }
    unwrap(api.post("/v1/auth/verify-email", { token }))
      .then((p) => {
        setAuth(p as AuthPayload);
        setResult({ ok: true });
        setTimeout(() => navigate("/account", { replace: true }), 1200);
      })
      .catch((err) => setResult({ ok: false, message: err instanceof Error ? err.message : "Verification failed." }));
  }, [token, setAuth, navigate]);

  return (
    <div className="animate-rise-in">
      {result === null ? (
        <StatusView state="loading" title="Verifying your email" />
      ) : result.ok ? (
        <StatusView state="success" title="Email verified">Signing you in.</StatusView>
      ) : (
        <StatusView state="error" title="Verification failed">
          {result.message}
          <div className="mt-2">
            <Button variant="outline" asChild>
              <Link to="/login">Back to sign in</Link>
            </Button>
          </div>
        </StatusView>
      )}
    </div>
  );
}
