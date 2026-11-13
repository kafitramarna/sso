import { Toaster as Sonner, type ToasterProps } from "sonner";

const Toaster = (props: ToasterProps) => (
  <Sonner
    position="bottom-right"
    toastOptions={{
      classNames: {
        toast: "rounded border bg-card text-card-foreground shadow-md",
        description: "text-muted-foreground",
      },
    }}
    {...props}
  />
);

export { Toaster };
