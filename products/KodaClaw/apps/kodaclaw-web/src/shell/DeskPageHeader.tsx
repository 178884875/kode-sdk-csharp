import type { ReactNode } from 'react';

type DeskPageHeaderProps = {
  icon: ReactNode;
  title: string;
  actions?: ReactNode;
};

export function DeskPageHeader({ icon, title, actions }: DeskPageHeaderProps) {
  return (
    <div className="kc-desk-header">
      <div className="kc-desk-header__left">
        <span className="kc-desk-header__icon" aria-hidden="true">{icon}</span>
        <h1 className="kc-desk-header__title">{title}</h1>
      </div>
      {actions && (
        <div className="kc-desk-header__actions">{actions}</div>
      )}
    </div>
  );
}
