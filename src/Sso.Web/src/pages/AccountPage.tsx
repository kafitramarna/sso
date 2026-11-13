import { useCallback, useEffect, useState, type FormEvent } from "react";
import { Loader2, Monitor, Smartphone, Tablet, Globe } from "lucide-react";
import { toast } from "sonner";
import { api, unwrap } from "../lib/api";
import { useAuth } from "../lib/auth";
import type { SessionDto, UserDto } from "../lib/types";
import { Alert } from "../components/ui/alert";
import { Badge } from "../components/ui/badge";
import { Button } from "../components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "../components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle, DialogTrigger, DialogClose } from "../components/ui/dialog";
import { Skeleton } from "../components/ui/skeleton";
import { Field, PasswordField } from "../components/Field";

const fmt = (iso: string | null) => (iso ? new Date(iso).toLocaleString() : "—");

function deviceIcon(ua: string | null) {
  const s = (ua ?? "").toLowerCase();
  if (s.includes("mobile") || s.includes("iphone") || s.includes("android")) return Smartphone;
  if (s.includes("ipad") || s.includes("tablet")) return Tablet;
  if (!s) return Globe;
  return Monitor;
}

function uaLabel(ua: string | null) {
  if (!ua) return "Unknown device";
  const m = ua.match(/(Edg|Chrome|Firefox|Safari|curl|node)[\/ ]([\d.]+)/i);
  const os = ua.match(/(Windows|Mac OS X|Linux|Android|iPhone OS)/i);
  return m ? `${m[1]} ${m[2].split(".")[0]}${os ? ` on ${os[1]}` : ""}` : ua.slice(0, 60);
}

export default function AccountPage() {
  const user = useAuth((s) => s.user);

  return (
    <div className="mx-auto max-w-2xl space-y-8 animate-rise-in">
      <header>
        <h1 className="font-display text-2xl font-semibold tracking-tight">{user?.display_name || "Account"}</h1>
        <div className="mt-1.5 flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
          <span>{user?.email}</span>
          {user?.email_verified ? <Badge variant="success">verified</Badge> : <Badge variant="warning">unverified</Badge>}
          {user?.is_sso_admin && <Badge variant="venus">sso_admin</Badge>}
        </div>
      </header>
      <ProfileCard user={user} />
      <PasswordCard />
      <SessionsCard />
    </div>
  );
}

function Section({ title, description, children }: { title: string; description?: string; children: React.ReactNode }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>{title}</CardTitle>
        {description && <CardDescription>{description}</CardDescription>}
      </CardHeader>
      <CardContent>{children}</CardContent>
    </Card>
  );
}

function ProfileCard({ user }: { user: UserDto | null }) {
  const [name, setName] = useState(user?.display_name ?? "");
  const [busy, setBusy] = useState(false);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    try {
      const u = await unwrap(api.patch("/v1/me", { display_name: name }));
      useAuth.setState({ user: u as UserDto });
      toast.success("Display name updated.");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Update failed.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <Section title="Profile">
      <form onSubmit={onSubmit} className="flex items-end gap-3">
        <div className="flex-1">
          <Field label="Display name" value={name} onChange={(e) => setName(e.target.value)} />
        </div>
        <Button type="submit" disabled={busy || name === (user?.display_name ?? "")}>
          {busy && <Loader2 className="animate-spin" />} Save
        </Button>
      </form>
    </Section>
  );
}

function PasswordCard() {
  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [pwError, setPwError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    if (next.length < 12) {
      setPwError("At least 12 characters.");
      return;
    }
    setBusy(true);
    try {
      await unwrap(api.post("/v1/me/change-password", { current_password: current, new_password: next }));
      setCurrent("");
      setNext("");
      toast.success("Password changed. Other sessions were signed out.");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Change failed.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <Section title="Password" description="Changing it signs out every other session.">
      <form onSubmit={onSubmit} className="space-y-4">
        <PasswordField label="Current password" autoComplete="current-password" required value={current} onChange={(e) => setCurrent(e.target.value)} />
        <PasswordField
          label="New password"
          autoComplete="new-password"
          required
          value={next}
          error={pwError}
          onChange={(e) => {
            setNext(e.target.value);
            if (pwError) setPwError(null);
          }}
          hint="Minimum 12 characters."
        />
        <Button type="submit" disabled={busy}>
          {busy && <Loader2 className="animate-spin" />} Change password
        </Button>
      </form>
    </Section>
  );
}

function SessionsCard() {
  const [sessions, setSessions] = useState<SessionDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  const load = useCallback(() => {
    unwrap(api.get("/v1/sessions"))
      .then((d) => setSessions((d as { sessions: SessionDto[] }).sessions))
      .catch((err) => setError(err instanceof Error ? err.message : "Could not load sessions."));
  }, []);

  useEffect(load, [load]);

  async function revoke(familyId: string) {
    setBusy(familyId);
    try {
      await api.delete(`/v1/sessions/${familyId}`);
      toast.success("Session revoked.");
      load();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Revoke failed.");
    } finally {
      setBusy(null);
    }
  }

  async function revokeAll() {
    setBusy("all");
    try {
      await api.post("/v1/sessions/revoke-all");
      toast.success("All other sessions revoked.");
      load();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Revoke failed.");
    } finally {
      setBusy(null);
    }
  }

  const others = sessions?.filter((s) => !s.current) ?? [];

  return (
    <Section title="Sessions" description="Where your account is signed in.">
      {error && <Alert variant="destructive" className="mb-4">{error}</Alert>}
      {sessions === null ? (
        <div className="space-y-3">
          <Skeleton className="h-12 w-full" />
          <Skeleton className="h-12 w-full" />
        </div>
      ) : sessions.length === 0 ? (
        <p className="text-sm text-muted-foreground">No active sessions.</p>
      ) : (
        <ul className="divide-y">
          {sessions.map((s) => {
            const Icon = deviceIcon(s.user_agent);
            return (
              <li key={s.family_id} className="flex items-center justify-between gap-3 py-3">
                <div className="flex min-w-0 items-center gap-3">
                  <Icon className="size-5 shrink-0 text-muted-foreground" strokeWidth={1.5} />
                  <div className="min-w-0 text-sm">
                    <p className="flex items-center gap-2 font-medium">
                      <span className="truncate">{uaLabel(s.user_agent)}</span>
                      {s.current && <Badge variant="venus">this device</Badge>}
                    </p>
                    <p className="mt-0.5 text-xs text-muted-foreground">
                      {s.ip ?? "unknown ip"} · first seen {fmt(s.created_at)} · active {fmt(s.last_seen_at)}
                    </p>
                  </div>
                </div>
                {!s.current && (
                  <Button variant="outline" size="sm" disabled={busy !== null} onClick={() => void revoke(s.family_id)}>
                    {busy === s.family_id ? <Loader2 className="animate-spin" /> : "Revoke"}
                  </Button>
                )}
              </li>
            );
          })}
        </ul>
      )}
      {others.length > 0 && (
        <Dialog>
          <DialogTrigger asChild>
            <Button variant="destructive" className="mt-4" disabled={busy !== null}>
              Sign out all other sessions
            </Button>
          </DialogTrigger>
          <DialogContent>
            <DialogHeader>
              <DialogTitle>Sign out everywhere else?</DialogTitle>
              <DialogDescription>
                {others.length} other {others.length === 1 ? "session" : "sessions"} will lose access immediately. This device stays signed in.
              </DialogDescription>
            </DialogHeader>
            <DialogFooter>
              <DialogClose asChild>
                <Button variant="outline">Cancel</Button>
              </DialogClose>
              <DialogClose asChild>
                <Button variant="destructive" onClick={() => void revokeAll()}>Sign them out</Button>
              </DialogClose>
            </DialogFooter>
          </DialogContent>
        </Dialog>
      )}
    </Section>
  );
}
