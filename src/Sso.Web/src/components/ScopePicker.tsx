import { cn } from "../lib/utils";
import { Label } from "./ui/label";
import { Check } from "lucide-react";

/** Scopes this IdP actually issues claims for. `openid` is always required. */
const SCOPES: { id: string; note: string; locked?: boolean }[] = [
  { id: "openid", note: "required", locked: true },
  { id: "profile", note: "name" },
  { id: "email", note: "email + verified" },
  { id: "roles", note: "user / sso_admin" },
  { id: "offline_access", note: "refresh token" },
];

export function ScopePicker({ values, onChange }: { values: string[]; onChange: (v: string[]) => void }) {
  function toggle(id: string) {
    if (id === "openid") return;
    onChange(values.includes(id) ? values.filter((s) => s !== id) : [...values, id]);
  }

  return (
    <div className="space-y-1.5">
      <Label className="text-muted-foreground">Allowed scopes</Label>
      <div className="flex flex-wrap gap-1.5">
        {SCOPES.map((s) => {
          const on = s.locked || values.includes(s.id);
          return (
            <button
              key={s.id}
              type="button"
              disabled={s.locked}
              aria-pressed={on}
              onClick={() => toggle(s.id)}
              title={s.note}
              className={cn(
                "inline-flex items-center gap-1.5 rounded-full border px-3 py-1 font-mono text-xs transition-colors",
                on
                  ? "border-venus/50 bg-venus/10 text-venus"
                  : "border-border text-muted-foreground hover:border-border hover:text-foreground",
                s.locked && "cursor-default opacity-70",
              )}
            >
              {on && <Check className="size-3" strokeWidth={3} />}
              {s.id}
            </button>
          );
        })}
      </div>
    </div>
  );
}
