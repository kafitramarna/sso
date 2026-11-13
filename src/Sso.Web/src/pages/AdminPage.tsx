import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { Copy, Loader2, Plus, Search, ShieldAlert } from "lucide-react";
import { toast } from "sonner";
import { api, unwrap } from "../lib/api";
import type { ClientDto, InviteResult } from "../lib/types";
import { Alert } from "../components/ui/alert";
import { Badge } from "../components/ui/badge";
import { Button } from "../components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "../components/ui/card";
import { Checkbox } from "../components/ui/checkbox";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle, DialogTrigger, DialogClose } from "../components/ui/dialog";
import { Input } from "../components/ui/input";
import { Skeleton } from "../components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "../components/ui/table";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "../components/ui/tabs";
import { Field } from "../components/Field";
import { TagInput } from "../components/TagInput";
import { ScopePicker } from "../components/ScopePicker";

const fmt = (iso: string) => new Date(iso).toLocaleString();

export default function AdminPage() {
  return (
    <div className="animate-rise-in">
      <header className="mb-6">
        <h1 className="font-display text-2xl font-semibold tracking-tight">Admin</h1>
        <p className="mt-1 text-sm text-muted-foreground">Invites, OIDC clients, and account control.</p>
      </header>
      <Tabs defaultValue="clients">
        <TabsList>
          <TabsTrigger value="clients">Clients</TabsTrigger>
          <TabsTrigger value="invites">Invites</TabsTrigger>
          <TabsTrigger value="users">Users</TabsTrigger>
        </TabsList>
        <TabsContent value="clients"><ClientsTab /></TabsContent>
        <TabsContent value="invites"><InvitesTab /></TabsContent>
        <TabsContent value="users"><UsersTab /></TabsContent>
      </Tabs>
    </div>
  );
}

/* ---------- one-shot reveal for secrets & links ---------- */

function RevealDialog({
  open,
  onClose,
  title,
  description,
  value,
}: {
  open: boolean;
  onClose: () => void;
  title: string;
  description: string;
  value: string;
}) {
  const [ack, setAck] = useState(false);
  return (
    <Dialog open={open} onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="overflow-hidden" onInteractOutside={(e) => e.preventDefault()}>
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2 break-all">
            <ShieldAlert className="size-5 shrink-0 text-warning" /> {title}
          </DialogTitle>
          <DialogDescription>{description}</DialogDescription>
        </DialogHeader>
        <div className="flex min-w-0 items-center gap-2">
          <code className="min-w-0 flex-1 break-all rounded border bg-secondary px-3 py-2 font-mono text-xs leading-relaxed">{value}</code>
          <Button variant="outline" size="icon" aria-label="Copy" onClick={() => void navigator.clipboard.writeText(value).then(() => toast.success("Copied."))}>
            <Copy />
          </Button>
        </div>
        <label className="flex items-center gap-2 text-sm text-muted-foreground">
          <Checkbox checked={ack} onCheckedChange={(v) => setAck(v === true)} />
          I've stored this somewhere safe.
        </label>
        <DialogFooter>
          <DialogClose asChild>
            <Button disabled={!ack} onClick={onClose}>Done</Button>
          </DialogClose>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/* ---------- clients ---------- */

interface ClientForm {
  client_id: string;
  name: string;
  type: "confidential" | "public";
  redirect_uris: string[];
  scopes: string[];
  skip_consent: boolean;
}

const emptyClient: ClientForm = {
  client_id: "",
  name: "",
  type: "confidential",
  redirect_uris: [],
  scopes: ["openid", "profile", "email"],
  skip_consent: true,
};

function validateUri(v: string): string | null {
  try {
    const u = new URL(v);
    return u.protocol === "http:" || u.protocol === "https:" ? null : "Must be an http(s) URL.";
  } catch {
    return "Not a valid URL.";
  }
}

function ClientsTab() {
  const [clients, setClients] = useState<ClientDto[] | null>(null);
  const [query, setQuery] = useState("");
  const [sortAsc, setSortAsc] = useState(true);
  const [form, setForm] = useState<ClientForm>(emptyClient);
  const [createOpen, setCreateOpen] = useState(false);
  const [reveal, setReveal] = useState<{ title: string; value: string } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(() => {
    unwrap(api.get("/admin/clients"))
      .then((d) => setClients((d as { clients: ClientDto[] }).clients))
      .catch((err) => setError(err instanceof Error ? err.message : "Could not load clients."));
  }, []);

  useEffect(load, [load]);

  const visible = useMemo(() => {
    const q = query.trim().toLowerCase();
    const list = (clients ?? []).filter(
      (c) => !q || c.name.toLowerCase().includes(q) || c.client_id.toLowerCase().includes(q),
    );
    return list.sort((a, b) => (sortAsc ? 1 : -1) * a.name.localeCompare(b.name));
  }, [clients, query, sortAsc]);

  async function create(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    try {
      const r = await unwrap<{ client: ClientDto; client_secret: string }>(
        api.post("/admin/clients", {
          client_id: form.client_id || undefined,
          name: form.name,
          type: form.type,
          redirect_uris: form.redirect_uris,
          allowed_scopes: form.scopes,
          skip_consent: form.skip_consent,
        }),
      );
      setCreateOpen(false);
      setForm(emptyClient);
      load();
      if (r.client_secret) {
        setReveal({ title: `Secret for ${r.client.client_id}`, value: r.client_secret });
      } else {
        toast.success(`Client ${r.client.client_id} created.`);
      }
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Create failed.");
    } finally {
      setBusy(false);
    }
  }

  async function patch(id: string, body: Record<string, unknown>, msg = "Client updated.") {
    try {
      await unwrap(api.patch(`/admin/clients/${id}`, body));
      toast.success(msg);
      load();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Update failed.");
    }
  }

  async function rotate(c: ClientDto) {
    try {
      const r = await unwrap<{ client_secret: string }>(api.post(`/admin/clients/${c.id}/rotate-secret`));
      setReveal({ title: `New secret for ${c.client_id}`, value: r.client_secret });
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Rotate failed.");
    }
  }

  return (
    <Card>
      <CardHeader className="flex-row items-center justify-between space-y-0">
        <div>
          <CardTitle>OIDC clients</CardTitle>
          <CardDescription>Applications allowed to delegate sign-in to this server.</CardDescription>
        </div>
        <Dialog open={createOpen} onOpenChange={setCreateOpen}>
          <DialogTrigger asChild>
            <Button size="sm"><Plus /> New client</Button>
          </DialogTrigger>
          <DialogContent>
            <DialogHeader>
              <DialogTitle>New client</DialogTitle>
              <DialogDescription>Confidential clients get a secret — shown once.</DialogDescription>
            </DialogHeader>
            <form onSubmit={create} className="space-y-3">
              <Field label="Client ID" placeholder="blank = generated" value={form.client_id} onChange={(e) => setForm({ ...form, client_id: e.target.value })} />
              <Field label="Name" required value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
              <div className="space-y-1.5">
                <label className="text-sm font-medium text-muted-foreground">Type</label>
                <select
                  value={form.type}
                  onChange={(e) => setForm({ ...form, type: e.target.value as ClientForm["type"] })}
                  className="h-9 w-full rounded border border-input bg-transparent px-3 text-sm focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
                >
                  <option value="confidential">confidential — server-side, gets a secret</option>
                  <option value="public">public — SPA or mobile, no secret</option>
                </select>
              </div>
              <TagInput
                label="Redirect URIs"
                placeholder="https://app.example.com/callback"
                mono
                values={form.redirect_uris}
                onChange={(v) => setForm({ ...form, redirect_uris: v })}
                validate={validateUri}
              />
              <ScopePicker values={form.scopes} onChange={(v) => setForm({ ...form, scopes: v })} />
              <label className="flex items-center gap-2 text-sm text-muted-foreground">
                <Checkbox checked={form.skip_consent} onCheckedChange={(v) => setForm({ ...form, skip_consent: v === true })} />
                skip_consent — first-party, no consent screen
              </label>
              <DialogFooter>
                <Button type="submit" disabled={busy}>{busy && <Loader2 className="animate-spin" />} Create</Button>
              </DialogFooter>
            </form>
          </DialogContent>
        </Dialog>
      </CardHeader>
      <CardContent>
        {error && <Alert variant="destructive" className="mb-4">{error}</Alert>}
        <div className="mb-4 flex items-center gap-3">
          <div className="relative flex-1">
            <Search className="absolute left-2.5 top-2.5 size-4 text-muted-foreground" />
            <Input placeholder="Search name or client_id" className="pl-8" value={query} onChange={(e) => setQuery(e.target.value)} />
          </div>
          <Button variant="outline" size="sm" onClick={() => setSortAsc((v) => !v)}>
            Name {sortAsc ? "A–Z" : "Z–A"}
          </Button>
        </div>
        {clients === null ? (
          <div className="space-y-2"><Skeleton className="h-10 w-full" /><Skeleton className="h-10 w-full" /></div>
        ) : visible.length === 0 ? (
          <p className="py-6 text-center text-sm text-muted-foreground">
            {query ? "No clients match that search." : "No clients yet — create the first one."}
          </p>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Name</TableHead>
                <TableHead>client_id</TableHead>
                <TableHead>Type</TableHead>
                <TableHead>Status</TableHead>
                <TableHead className="text-right">Actions</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {visible.map((c) => (
                <ClientRow key={c.id} c={c} onPatch={patch} onRotate={rotate} />
              ))}
            </TableBody>
          </Table>
        )}
      </CardContent>
      {reveal && (
        <RevealDialog
          open
          title={reveal.title}
          description="This is shown once and never stored in plain text. Copy it now."
          value={reveal.value}
          onClose={() => setReveal(null)}
        />
      )}
    </Card>
  );
}

function ClientRow({ c, onPatch, onRotate }: { c: ClientDto; onPatch: (id: string, body: Record<string, unknown>, msg?: string) => Promise<void>; onRotate: (c: ClientDto) => Promise<void> }) {
  const [open, setOpen] = useState(false);
  const [name, setName] = useState(c.name);
  const [uris, setUris] = useState<string[]>(c.redirect_uris);
  const [scopes, setScopes] = useState<string[]>(c.allowed_scopes);
  const [skipConsent, setSkipConsent] = useState(c.skip_consent);

  return (
    <>
      <TableRow>
        <TableCell className="font-medium">{c.name}</TableCell>
        <TableCell className="font-mono text-xs">{c.client_id}</TableCell>
        <TableCell><Badge variant="secondary">{c.type}</Badge></TableCell>
        <TableCell>{c.active ? <Badge variant="success">active</Badge> : <Badge variant="secondary">disabled</Badge>}</TableCell>
        <TableCell className="text-right">
          <div className="flex justify-end gap-1">
            <Button variant="ghost" size="sm" onClick={() => setOpen((v) => !v)}>{open ? "Close" : "Edit"}</Button>
            {c.type === "confidential" && (
              <RotateConfirm onConfirm={() => onRotate(c)} clientId={c.client_id} />
            )}
            <Button variant="ghost" size="sm" onClick={() => void onPatch(c.id, { active: !c.active }, c.active ? "Client disabled." : "Client enabled.")}>
              {c.active ? "Disable" : "Enable"}
            </Button>
          </div>
        </TableCell>
      </TableRow>
      {open && (
        <TableRow className="hover:bg-transparent">
          <TableCell colSpan={5} className="bg-secondary/40">
            <form
              className="grid gap-3 py-3 sm:grid-cols-2"
              onSubmit={(e) => {
                e.preventDefault();
                void onPatch(c.id, {
                  name,
                  redirect_uris: uris,
                  allowed_scopes: scopes,
                  skip_consent: skipConsent,
                });
                setOpen(false);
              }}
            >
              <Field label="Name" required value={name} onChange={(e) => setName(e.target.value)} />
              <ScopePicker values={scopes} onChange={setScopes} />
              <div className="sm:col-span-2">
                <TagInput label="Redirect URIs" mono values={uris} onChange={setUris} validate={validateUri} />
              </div>
              <label className="flex items-center gap-2 text-sm text-muted-foreground">
                <Checkbox checked={skipConsent} onCheckedChange={(v) => setSkipConsent(v === true)} /> skip_consent
              </label>
              <div className="flex justify-end gap-2 sm:col-span-2">
                <Button type="button" variant="outline" size="sm" onClick={() => setOpen(false)}>Cancel</Button>
                <Button type="submit" size="sm">Save</Button>
              </div>
            </form>
          </TableCell>
        </TableRow>
      )}
    </>
  );
}

function RotateConfirm({ onConfirm, clientId }: { onConfirm: () => Promise<void>; clientId: string }) {
  const [busy, setBusy] = useState(false);
  return (
    <Dialog>
      <DialogTrigger asChild>
        <Button variant="ghost" size="sm">Rotate</Button>
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Rotate secret for {clientId}?</DialogTitle>
          <DialogDescription>The current secret stops working immediately. Apps using it must be updated.</DialogDescription>
        </DialogHeader>
        <DialogFooter>
          <DialogClose asChild><Button variant="outline">Cancel</Button></DialogClose>
          <DialogClose asChild>
            <Button
              variant="destructive"
              disabled={busy}
              onClick={async () => {
                setBusy(true);
                await onConfirm();
                setBusy(false);
              }}
            >
              Rotate
            </Button>
          </DialogClose>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/* ---------- invites ---------- */

function InvitesTab() {
  const [email, setEmail] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [reveal, setReveal] = useState<{ title: string; value: string; description?: string } | null>(null);
  const [busy, setBusy] = useState(false);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    try {
      const r = await unwrap<InviteResult>(
        api.post("/admin/invites", { email, display_name: displayName || undefined }),
      );
      setEmail("");
      setDisplayName("");
      setReveal({
        title: "Invite created",
        value: r.invite_url,
        description: `Send this link to ${r.email}. It works once and expires ${fmt(r.expires_at)}.`,
      });
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Invite failed.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Invite a user</CardTitle>
        <CardDescription>Creates a single-use set-password link, valid 72 hours. The only way non-admin accounts exist in production.</CardDescription>
      </CardHeader>
      <CardContent>
        <form onSubmit={onSubmit} className="grid gap-4 sm:grid-cols-2">
          <Field label="Email" type="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
          <Field label="Display name" value={displayName} onChange={(e) => setDisplayName(e.target.value)} />
          <div className="sm:col-span-2">
            <Button type="submit" disabled={busy}>{busy && <Loader2 className="animate-spin" />} Create invite</Button>
          </div>
        </form>
      </CardContent>
      {reveal && (
        <RevealDialog
          open
          title={reveal.title}
          description={reveal.description ?? "Send this link to the person you're inviting. It works once."}
          value={reveal.value}
          onClose={() => setReveal(null)}
        />
      )}
    </Card>
  );
}

/* ---------- users ---------- */

function UsersTab() {
  const [userId, setUserId] = useState("");
  const [busy, setBusy] = useState<"disable" | "enable" | null>(null);

  async function act(action: "disable" | "enable") {
    setBusy(action);
    try {
      await api.post(`/admin/users/${userId.trim()}/${action}`);
      toast.success(`User ${action}d.`);
      setUserId("");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Action failed.");
    } finally {
      setBusy(null);
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Users</CardTitle>
        <CardDescription>Enable or disable an account by user ID. Disabling revokes all sessions immediately.</CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <Field
          label="User ID"
          placeholder="00000000-0000-0000-0000-000000000000"
          value={userId}
          onChange={(e) => setUserId(e.target.value)}
          className="font-mono text-xs"
        />
        <div className="flex gap-2">
          <Button variant="destructive" disabled={busy !== null || !userId.trim()} onClick={() => void act("disable")}>
            {busy === "disable" && <Loader2 className="animate-spin" />} Disable
          </Button>
          <Button variant="outline" disabled={busy !== null || !userId.trim()} onClick={() => void act("enable")}>
            {busy === "enable" && <Loader2 className="animate-spin" />} Enable
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}
