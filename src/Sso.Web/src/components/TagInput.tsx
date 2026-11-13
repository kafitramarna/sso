import { useState, type KeyboardEvent } from "react";
import { X } from "lucide-react";
import { cn } from "../lib/utils";
import { Label } from "./ui/label";

interface TagInputProps {
  label: string;
  values: string[];
  onChange: (values: string[]) => void;
  placeholder?: string;
  mono?: boolean;
  validate?: (v: string) => string | null;
}

/** Token-style input: Enter or comma commits a chip, × removes, Backspace pops. */
export function TagInput({ label, values, onChange, placeholder, mono, validate }: TagInputProps) {
  const [draft, setDraft] = useState("");
  const [error, setError] = useState<string | null>(null);

  function commit() {
    const v = draft.trim().replace(/,+$/, "");
    if (!v) return;
    if (values.includes(v)) {
      setError("Already added.");
      return;
    }
    const err = validate?.(v) ?? null;
    if (err) {
      setError(err);
      return;
    }
    onChange([...values, v]);
    setDraft("");
    setError(null);
  }

  function onKeyDown(e: KeyboardEvent<HTMLInputElement>) {
    if (e.key === "Enter" || e.key === ",") {
      e.preventDefault();
      commit();
    } else if (e.key === "Backspace" && !draft && values.length) {
      onChange(values.slice(0, -1));
    }
  }

  return (
    <div className="space-y-1.5">
      <Label className="text-muted-foreground">{label}</Label>
      <div
        className={cn(
          "flex min-h-9 w-full flex-wrap items-center gap-1.5 rounded border border-input bg-transparent px-2 py-1.5",
          "focus-within:ring-1 focus-within:ring-ring",
        )}
      >
        {values.map((v) => (
          <span
            key={v}
            className={cn(
              "inline-flex items-center gap-1 rounded-sm border border-border bg-secondary px-1.5 py-0.5 text-xs",
              mono && "font-mono",
            )}
          >
            {v}
            <button
              type="button"
              aria-label={`Remove ${v}`}
              onClick={() => onChange(values.filter((x) => x !== v))}
              className="rounded-sm text-muted-foreground hover:text-foreground"
            >
              <X className="size-3" />
            </button>
          </span>
        ))}
        <input
          value={draft}
          onChange={(e) => {
            setDraft(e.target.value);
            if (error) setError(null);
          }}
          onKeyDown={onKeyDown}
          onBlur={commit}
          placeholder={values.length ? undefined : placeholder}
          className={cn(
            "min-w-32 flex-1 bg-transparent px-1 py-0.5 text-sm outline-none placeholder:text-muted-foreground",
            mono && "font-mono text-xs",
          )}
        />
      </div>
      {error ? (
        <p className="text-xs text-destructive">{error}</p>
      ) : (
        <p className="text-xs text-muted-foreground">Press Enter to add each one.</p>
      )}
    </div>
  );
}
