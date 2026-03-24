import { type ButtonHTMLAttributes, forwardRef } from "react";

export type ButtonVariant = "primary" | "secondary" | "ghost" | "danger" | "link";

/**
 * sm      — compact, xs font, 5px v-padding  (inside cards / dense lists)
 * md      — standard, sm font, 7px v-padding  (default if no size given)
 * lg      — spacious, base font, 10px v-padding
 * control — matches kc-select height (8px v-padding + line-height 1.5 + sm font)
 *           Use whenever the button sits in the same row as a <Select> or <input>.
 */
export type ButtonSize = "sm" | "md" | "lg" | "control";

/**
 * default — var(--radius-md) rectangle
 * pill    — border-radius 999px, visually matches mode-badge chips
 */
export type ButtonShape = "default" | "pill";

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  size?: ButtonSize;
  shape?: ButtonShape;
  /** Adds btn--selected ring (toggle-pressed state) */
  selected?: boolean;
}

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(
  (
    {
      variant = "secondary",
      size = "md",
      shape = "default",
      selected = false,
      className,
      children,
      ...rest
    },
    ref,
  ) => {
    const classes = [
      "btn",
      `btn--${variant}`,
      size !== "md" ? `btn--${size}` : "",
      shape === "pill" ? "btn--pill" : "",
      selected ? "btn--selected" : "",
      className ?? "",
    ]
      .filter(Boolean)
      .join(" ");

    return (
      <button ref={ref} type="button" className={classes} {...rest}>
        {children}
      </button>
    );
  },
);

Button.displayName = "Button";
