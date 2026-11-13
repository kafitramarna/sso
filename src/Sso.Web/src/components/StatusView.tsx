import { CheckCircle2, Loader2, XCircle } from "lucide-react";
import type { ReactNode } from "react";
import { cn } from "../lib/utils";

export function StatusView({
  state,
  title,
  children,
}: {
  state: "loading" | "success" | "error";
  title: string;
  children?: ReactNode;
}) {
  const Icon = state === "loading" ? Loader2 : state === "success" ? CheckCircle2 : XCircle;
  return (
    <div className="flex flex-col items-center gap-3 py-6 text-center">
      <Icon
        className={cn(
          "size-10",
          state === "loading" && "animate-spin text-muted-foreground",
          state === "success" && "text-success",
          state === "error" && "text-destructive",
        )}
        strokeWidth={1.5}
      />
      <p className="font-display text-lg font-semibold">{title}</p>
      {children && <div className="text-sm text-muted-foreground">{children}</div>}
    </div>
  );
}
