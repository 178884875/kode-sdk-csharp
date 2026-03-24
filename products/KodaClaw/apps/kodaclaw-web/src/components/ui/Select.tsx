import { type SelectHTMLAttributes, forwardRef } from "react";

interface SelectProps extends SelectHTMLAttributes<HTMLSelectElement> {
  /** Extra class names to merge (e.g. a width override) */
  className?: string;
}

/**
 * Thin wrapper around <select> that applies kc-select styling.
 * Use this everywhere a styled dropdown is needed so the base styles
 * stay in one place and context-specific overrides are unnecessary.
 */
export const Select = forwardRef<HTMLSelectElement, SelectProps>(
  ({ className, children, ...rest }, ref) => {
    const classes = ["kc-select", className].filter(Boolean).join(" ");
    return (
      <select ref={ref} className={classes} {...rest}>
        {children}
      </select>
    );
  },
);

Select.displayName = "Select";
