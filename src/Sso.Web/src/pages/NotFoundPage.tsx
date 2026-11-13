import { Link } from "react-router-dom";
import { Button } from "../components/ui/button";
import { VenusMark } from "../components/Wordmark";

export default function NotFoundPage() {
  return (
    <div className="grid min-h-screen place-items-center bg-background px-6">
      <div className="flex flex-col items-center text-center">
        <VenusMark className="size-16 text-venus" />
        <p className="mt-10 font-display text-7xl font-semibold tracking-tight">404</p>
        <h1 className="mt-3 font-display text-lg font-medium">Lost in orbit</h1>
        <p className="mt-2 max-w-sm text-sm text-muted-foreground">
          The page you are looking for drifted away, or was never charted.
        </p>
        <Button asChild className="mt-8">
          <Link to="/">Back to base</Link>
        </Button>
      </div>
    </div>
  );
}
