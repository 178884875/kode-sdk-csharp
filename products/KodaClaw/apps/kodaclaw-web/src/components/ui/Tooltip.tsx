import type { ReactNode } from "react";

interface TooltipProps {
  content: string;
  children: ReactNode;
  /** @default "top" */
  placement?: "top" | "bottom";
}

/**
 * Lightweight CSS-only tooltip. Wraps any inline element and shows a dark
 * bubble on hover. Uses `data-tooltip` + `::after` — no JS positioning needed
 * for short single-line labels.
 */
export function Tooltip({ content, children, placement = "top" }: TooltipProps) {
  return (
    <span
      className={`kc-tooltip kc-tooltip--${placement}`}
      data-tooltip={content}
      aria-label={content}
    >
      {children}
    </span>
  );
}
