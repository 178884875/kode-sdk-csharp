import { type InputHTMLAttributes } from 'react';

interface ToggleProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'type'> {
  /** Optional accessible label rendered via aria-label (use when no visible text label exists) */
  'aria-label'?: string;
}

/**
 * iOS-style toggle switch backed by a hidden checkbox.
 * Renders the `.kc-toggle` + `.kc-toggle__track` CSS pattern.
 *
 * Usage:
 *   <Toggle checked={value} onChange={e => setValue(e.target.checked)} />
 */
export function Toggle({ className, ...rest }: ToggleProps) {
  return (
    <label className={['kc-toggle', className].filter(Boolean).join(' ')}>
      <input type="checkbox" {...rest} />
      <span className="kc-toggle__track" />
    </label>
  );
}
