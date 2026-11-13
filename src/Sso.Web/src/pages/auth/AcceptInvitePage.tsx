import { useState, type FormEvent } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { Loader2 } from "lucide-react";
import { api, unwrap } from "../../lib/api";
import { useAuth } from "../../lib/auth";
import type { AuthPayload } from "../../lib/types";
import { Alert } from "../../components/ui/alert";
import { Button } from "../../components/ui/button";
import { Field, PasswordField } from "../../components/Field";
import { StatusView } from "../../components/StatusView";

export default function AcceptInvitePage() {
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const setAuth = useAuth((s) => s.setAuth);
  const token = params.get("token");

  const [displayName, setDisplayName] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [pwError, setPwError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    if (password.length < 12) {
      setPwError("At least 12 characters.");
      return;
    }
    setBusy(true);
    try {
      const payload = await unwrap(
        api.post("/v1/auth/accept-invite", { token, password, display_name: displayName || undefined }),
      );
      setAuth(payload as AuthPayload);
      navigate("/account", { replace: true });
    } catch (err) {
      setError(err instanceof Error ? err.message : "This invite could not be accepted.");
      setBusy(false);
    }
  }

  if (!token) {
    return (
      <div className="animate-rise-in">
        <StatusView state="error" title="Invite incomplete">
          This invite link is missing its token. Open the full link from your email.
        </StatusView>
      </div>
    );
  }

  return (
    <div className="animate-rise-in">
      <header className="mb-8">
        <h1 className="font-display text-2xl font-semibold tracking-tight">Accept invite</h1>
        <p className="mt-1 text-sm text-muted-foreground">Set a password to activate your account.</p>
      </header>
      <form onSubmit={onSubmit} className="space-y-4">
        {error && <Alert variant="destructive">{error}</Alert>}
        <Field label="Display name" autoComplete="name" value={displayName} onChange={(e) => setDisplayName(e.target.value)} />
        <PasswordField
          label="Password"
          autoComplete="new-password"
          required
          value={password}
          error={pwError}
          onChange={(e) => {
            setPassword(e.target.value);
            if (pwError) setPwError(null);
          }}
          onBlur={() => password.length > 0 && password.length < 12 && setPwError("At least 12 characters.")}
          hint="Minimum 12 characters."
        />
        <Button type="submit" disabled={busy} className="w-full">
          {busy && <Loader2 className="animate-spin" />}
          {busy ? "Activating" : "Activate account"}
        </Button>
      </form>
      <p className="mt-6 text-center text-sm">
        <Link to="/login" className="text-muted-foreground underline-offset-4 hover:text-foreground hover:underline">
          Back to sign in
        </Link>
      </p>
    </div>
  );
}
