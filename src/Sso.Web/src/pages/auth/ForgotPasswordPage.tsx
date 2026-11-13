import { useState, type FormEvent } from "react";
import { Link } from "react-router-dom";
import { Loader2 } from "lucide-react";
import { api, unwrap } from "../../lib/api";
import { Alert } from "../../components/ui/alert";
import { Button } from "../../components/ui/button";
import { Field } from "../../components/Field";
import { StatusView } from "../../components/StatusView";

export default function ForgotPasswordPage() {
  const [email, setEmail] = useState("");
  const [sent, setSent] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      await unwrap(api.post("/v1/auth/forgot-password", { email }));
      setSent(true);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Request failed.");
    } finally {
      setBusy(false);
    }
  }

  if (sent) {
    return (
      <div className="animate-rise-in">
        <StatusView state="success" title="Check your inbox">
          If this account exists, a reset link is on its way. The link works once and expires in an hour.
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
        <h1 className="font-display text-2xl font-semibold tracking-tight">Reset password</h1>
        <p className="mt-1 text-sm text-muted-foreground">We'll email you a single-use reset link.</p>
      </header>
      <form onSubmit={onSubmit} className="space-y-4">
        {error && <Alert variant="destructive">{error}</Alert>}
        <Field label="Email" type="email" autoComplete="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
        <Button type="submit" disabled={busy} className="w-full">
          {busy && <Loader2 className="animate-spin" />}
          {busy ? "Sending" : "Send reset link"}
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
