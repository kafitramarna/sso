import { useId, useState, type InputHTMLAttributes, type ReactNode } from "react";
import { cn } from "../lib/utils";
import { Eye, EyeOff } from "lucide-react";
import { Input } from "./ui/input";
import { Label } from "./ui/label";
import { Button } from "./ui/button";

interface FieldProps extends InputHTMLAttributes<HTMLInputElement> {
  label: string;
  error?: string | null;
  hint?: ReactNode;
}

export function Field({ label, error, hint, id, ...rest }: FieldProps) {
  const autoId = useId();
  const inputId = id ?? autoId;
  return (
    <div className="space-y-1.5">
      {label && (
        <Label htmlFor={inputId} className="text-muted-foreground">
          {label}
        </Label>
      )}
      <Input id={inputId} aria-invalid={!!error} aria-describedby={error ? `${inputId}-err` : undefined} {...rest} />
      {error ? (
        <p id={`${inputId}-err`} className="text-xs text-destructive">
          {error}
        </p>
      ) : (
        hint && <p className="text-xs text-muted-foreground">{hint}</p>
      )}
    </div>
  );
}

export function PasswordField(props: FieldProps) {
  const [show, setShow] = useState(false);
  return (
    <div className="relative">
      <Field {...props} type={show ? "text" : "password"} className="pr-9" />
      <Button
        type="button"
        variant="ghost"
        size="icon"
        aria-label={show ? "Hide password" : "Show password"}
        onClick={() => setShow((v) => !v)}
        className={cn(
          "absolute right-0.5 h-8 w-8 text-muted-foreground",
          props.label ? "top-[27px]" : "top-0.5",
        )}
      >
        {show ? <EyeOff /> : <Eye />}
      </Button>
    </div>
  );
}
