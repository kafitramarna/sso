import { useState, type FormEvent } from "react";
import { Link } from "react-router-dom";
import { Loader2, MailCheck, Lock } from "lucide-react";
import { api, ApiError, unwrap } from "../../lib/api";
import { Alert } from "../../components/ui/alert";
import { Button } from "../../components/ui/button";
import { Field, PasswordField } from "../../components/Field";
import { StatusView } from "../../components/StatusView";

export default function RegisterPage() {
  const [email, setEmail] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [password, setPassword] = useState("");
  const [state, setState] = useState<"form" | "sent" | "closed">("form");
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
      await unwrap(api.post("/v1/auth/register", { email, password, display_name: displayName || undefined }));
      setState("sent");
    } catch (err) {
      if (err instanceof ApiError && err.status === 403) setState("closed");
      else setError(err instanceof Error ? err.message : "Registration failed.");
    } finally {
      setBusy(false);
    }
  }

  if (state === "closed") {
    return (
      <div className="animate-rise-in">
        <StatusView state="error" title="Registration is invite-only">
          <Lock className="hidden" />
          New accounts are created from admin invites. Ask an administrator for one.
          <div className="mt-2">
            <Button variant="outline" asChild>
              <Link to="/login">Back to sign in</Link>
            </Button>
          </div>
        </StatusView>
      </div>
    );
  }

  if (state === "sent") {
    return (
      <div className="animate-rise-in">
        <StatusView state="success" title="Check your inbox">
          <MailCheck className="mx-auto mb-1 hidden" />
          If this email can be registered, a verification link is on its way.
          <div className="mt-2">
            <Button variant="outline" asChild>
              <Link to="/login">Back to sign in</Link>
            </Button>
          </div>
        </StatusView>
      </div>
    );
  }

  return (
    <div className="animate-rise-in">
      <header className="mb-8">
        <h1 className="font-display text-2xl font-semibold tracking-tight">Create account</h1>
        <p className="mt-1 text-sm text-muted-foreground">One account for every VenusVerse app.</p>
      </header>
      <form onSubmit={onSubmit} className="space-y-4">
        {error && <Alert variant="destructive">{error}</Alert>}
        <Field label="Email" type="email" autoComplete="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
        <Field label="Display name" autoComplete="name" value={displayName} onChange={(e) => setDisplayName(e.target.value)} hint="Shown to admins and in your profile." />
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
          {busy ? "Creating account" : "Create account"}
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
