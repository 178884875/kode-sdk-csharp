export type ShellVariant = "v1" | "v2";

const SHELL_VARIANT_STORAGE_KEY = "kodaclaw.shellVariant";

function isShellVariant(value: string | null | undefined): value is ShellVariant {
  return value === "v1" || value === "v2";
}

function readVariantFromSearch(search: string): ShellVariant | null {
  const params = new URLSearchParams(search);
  const variant = params.get("shell");
  return isShellVariant(variant) ? variant : null;
}

export function readStoredShellVariant(): ShellVariant {
  if (typeof window === "undefined") {
    return "v2";
  }

  return readVariantFromLocation(window.location.search) ?? readShellVariantFromStorage();
}

export function readVariantFromLocation(search: string): ShellVariant | null {
  return readVariantFromSearch(search);
}

export function readShellVariantFromStorage(): ShellVariant {
  if (typeof window === "undefined") {
    return "v2";
  }

  const stored = window.localStorage.getItem(SHELL_VARIANT_STORAGE_KEY);
  return isShellVariant(stored) ? stored : "v2";
}

export function persistShellVariant(variant: ShellVariant): void {
  if (typeof window === "undefined") {
    return;
  }

  window.localStorage.setItem(SHELL_VARIANT_STORAGE_KEY, variant);
}
