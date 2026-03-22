import type { LucideIcon } from 'lucide-react';

type IconProps = {
  icon: LucideIcon;
  size?: number;
  className?: string;
};

export function Icon({ icon: Ic, size = 16, className }: IconProps) {
  return <Ic size={size} className={className} strokeWidth={1.75} />;
}
