import { cn } from "../lib/utils";

/** Venus: the brightest point in the sky — a gold disc on a thin orbit. */
export function VenusMark({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 32 32" className={cn("size-6", className)} aria-hidden>
      <ellipse cx="16" cy="16" rx="14" ry="5.5" fill="none" stroke="currentColor" strokeWidth="1" opacity="0.45" transform="rotate(-18 16 16)" />
      <circle cx="16" cy="16" r="6" fill="currentColor" />
    </svg>
  );
}

export function Wordmark({ dark = false, className }: { dark?: boolean; className?: string }) {
  return (
    <span className={cn("inline-flex items-center gap-2", className)}>
      <VenusMark className="text-venus" />
      <span className={cn("font-display text-lg font-semibold tracking-tight", dark ? "text-[#ece8dd]" : "text-foreground")}>
        VenusVerse
      </span>
      <span className={cn("text-sm", dark ? "text-[#9a958a]" : "text-muted-foreground")}>SSO</span>
    </span>
  );
}
