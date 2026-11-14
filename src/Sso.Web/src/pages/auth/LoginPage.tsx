import { useEffect, useState, type FormEvent } from "react";
import { Link, useLocation, useNavigate, useSearchParams } from "react-router-dom";
import { Loader2 } from "lucide-react";
import { useAuth } from "../../lib/auth";
import { Alert } from "../../components/ui/alert";
import { Button } from "../../components/ui/button";
import { Field, PasswordField } from "../../components/Field";

/** Same-origin /oauth/… continuation, or null for anything else. */
function oauthContinuation(returnTo: string | null): string | null {
  if (!returnTo) return null;
  try {
    const u = new URL(returnTo);
    if (u.pathname.startsWith("/oauth/")) return u.pathname + u.search;
  } catch {
    /* not a URL */
  }
  return null;
}

export default function LoginPage() {
  const { status, login, allowSelfRegistration } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const [params] = useSearchParams();
  const continuation = oauthContinuation(params.get("return"));

  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (status === "authed" && continuation) window.location.href = continuation;
  }, [status, continuation]);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      await login(email, password);
      if (continuation) {
        window.location.href = continuation;
      } else {
        const from = (location.state as { from?: string } | null)?.from;
        navigate(from ?? "/account", { replace: true });
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "Sign-in failed.");
      setBusy(false);
    }
  }

  return (
    <div className="animate-rise-in">
      <header className="mb-8">
        <h1 className="font-display text-2xl font-semibold tracking-tight">Sign in</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          {continuation
            ? "An application asked VenusVerse SSO to confirm who you are."
            : "Use your VenusVerse account."}
        </p>
      </header>

      <form onSubmit={onSubmit} className="space-y-5">
        {error && <Alert variant="destructive">{error}</Alert>}
        <Field label="Email" type="email" autoComplete="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
        <div>
          <div className="mb-1.5 flex items-baseline justify-between">
            <label htmlFor="pw" className="text-sm font-medium text-muted-foreground">Password</label>
            <Link to="/forgot-password" className="text-xs text-muted-foreground underline-offset-4 hover:text-foreground hover:underline">
              Forgot password
            </Link>
          </div>
          <PasswordField id="pw" label="" autoComplete="current-password" required value={password} onChange={(e) => setPassword(e.target.value)} />
        </div>
        <Button type="submit" disabled={busy} size="lg" className="w-full">
          {busy && <Loader2 className="animate-spin" />}
          {busy ? "Signing in" : "Sign in"}
        </Button>
      </form>

      {allowSelfRegistration === true && (
        <>
          <div className="mt-8 flex items-center gap-3 text-xs text-muted-foreground">
            <span className="h-px flex-1 bg-border" />
            New to VenusVerse?
            <span className="h-px flex-1 bg-border" />
          </div>
          <Button variant="outline" className="mt-3 w-full" asChild>
            <Link to="/register">Create account</Link>
          </Button>
        </>
      )}
    </div>
  );
}
