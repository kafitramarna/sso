import { Outlet } from "react-router-dom";
import { Wordmark } from "../Wordmark";
import { Toaster } from "../ui/sonner";

export function AuthLayout() {
  return (
    <>
      <div className="grid min-h-screen lg:grid-cols-2">
      {/* Brand panel — the one place the design spends its boldness. */}
      <aside className="starfield relative hidden flex-col justify-between bg-[#0b0e13] p-10 text-[#ece8dd] lg:flex">
        <Wordmark dark />
        <div className="space-y-4">
          <h1 className="font-display text-3xl font-semibold leading-tight tracking-tight">
            One sign-in for the
            <br />
            whole system.
          </h1>
          <p className="max-w-sm text-sm leading-relaxed text-[#9a958a]">
            VenusVerse SSO is the single identity every VenusVerse application steers by.
            Authenticate once; every connected app takes your word for it.
          </p>
        </div>
        <p className="text-xs text-[#6b675f]">Venus — first light, last light. The reference point.</p>
      </aside>

      <main className="flex flex-col">
        <header className="flex items-center justify-between px-6 py-5 lg:hidden">
          <Wordmark />
        </header>
        <div className="flex flex-1 items-center justify-center px-6 pb-16 lg:pb-0">
          <div className="w-full max-w-sm">
            <Outlet />
          </div>
        </div>
      </main>
      </div>
      <Toaster />
    </>
  );
}
