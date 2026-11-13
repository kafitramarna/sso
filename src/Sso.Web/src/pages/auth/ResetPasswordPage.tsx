import { useState, type FormEvent } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { Loader2 } from "lucide-react";
import { api, unwrap } from "../../lib/api";
import { Alert } from "../../components/ui/alert";
import { Button } from "../../components/ui/button";
import { PasswordField } from "../../components/Field";
import { StatusView } from "../../components/StatusView";

export default function ResetPasswordPage() {
  const [params] = useSearchParams();
  const token = params.get("token");
  const [password, setPassword] = useState("");
  const [done, setDone] = useState(false);
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
      await unwrap(api.post("/v1/auth/reset-password", { token, password }));
      setDone(true);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Reset failed.");
    } finally {
      setBusy(false);
    }
  }

  if (!token) {
    return (
      <div className="animate-rise-in">
        <StatusView state="error" title="Link incomplete">
          This reset link is missing its token. Open the full link from your email.
          <div className="mt-2">
            <Button variant="outline" asChild>
              <Link to="/forgot-password">Request a new link</Link>
            </Button>
          </div>
        </StatusView>
      </div>
    );
  }

  if (done) {
    return (
      <div className="animate-rise-in">
        <StatusView state="success" title="Password updated">
          Your other sessions were signed out.
          <div className="mt-2">
            <Button asChild>
              <Link to="/login">Sign in</Link>
            </Button>
          </div>
        </StatusView>
      </div>
    );
  }

  return (
    <div className="animate-rise-in">
      <header className="mb-8">
        <h1 className="font-display text-2xl font-semibold tracking-tight">Set a new password</h1>
        <p className="mt-1 text-sm text-muted-foreground">Minimum 12 characters. Other sessions get signed out.</p>
      </header>
      <form onSubmit={onSubmit} className="space-y-4">
        {error && <Alert variant="destructive">{error}</Alert>}
        <PasswordField
          label="New password"
          autoComplete="new-password"
          required
          value={password}
          error={pwError}
          onChange={(e) => {
            setPassword(e.target.value);
            if (pwError) setPwError(null);
          }}
          onBlur={() => password.length > 0 && password.length < 12 && setPwError("At least 12 characters.")}
        />
        <Button type="submit" disabled={busy} className="w-full">
          {busy && <Loader2 className="animate-spin" />}
          {busy ? "Updating" : "Update password"}
        </Button>
      </form>
    </div>
  );
}
