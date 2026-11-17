import { useState } from "react";
import { Link, Outlet } from "react-router-dom";
import { KeyRound, LayoutGrid, LogOut, Moon, ShieldCheck, Sun, User as UserIcon } from "lucide-react";
import { useAuth } from "../../lib/auth";
import { applyTheme, currentTheme } from "../../lib/theme";
import { Wordmark } from "../Wordmark";
import { Button } from "../ui/button";
import { Toaster } from "../ui/sonner";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "../ui/dropdown-menu";

export function AppLayout() {
  const { user, logout } = useAuth();
  const [dark, setDark] = useState(currentTheme() === "dark");

  function toggleTheme() {
    const next = !dark;
    setDark(next);
    applyTheme(next ? "dark" : "light");
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="border-b">
        <div className="mx-auto flex h-14 max-w-4xl items-center justify-between px-4">
          <Link to="/dashboard" aria-label="Apps">
            <Wordmark />
          </Link>
          <div className="flex items-center gap-1">
            <Button variant="ghost" size="sm" asChild>
              <Link to="/dashboard">
                <LayoutGrid /> Apps
              </Link>
            </Button>
            {user?.is_sso_admin && (
              <Button variant="ghost" size="sm" asChild>
                <Link to="/admin">
                  <KeyRound /> Admin
                </Link>
              </Button>
            )}
            <Button variant="ghost" size="icon" onClick={toggleTheme} aria-label="Toggle theme">
              {dark ? <Sun /> : <Moon />}
            </Button>
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="ghost" size="icon" aria-label="Account menu">
                  <UserIcon />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                <DropdownMenuLabel className="max-w-56 truncate">{user?.email}</DropdownMenuLabel>
                <DropdownMenuSeparator />
                <DropdownMenuItem asChild>
                  <Link to="/account">
                    <UserIcon /> Account
                  </Link>
                </DropdownMenuItem>
                {user?.is_sso_admin && (
                  <DropdownMenuItem className="text-xs text-venus">
                    <ShieldCheck className="size-3.5" /> sso_admin
                  </DropdownMenuItem>
                )}
                <DropdownMenuItem onClick={() => void logout()}>
                  <LogOut /> Sign out
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
        </div>
      </header>
      <main className="mx-auto w-full max-w-4xl px-4 py-8">
        <Outlet />
      </main>
      <Toaster />
    </div>
  );
}
