import { useLocaleText } from "../i18n/I18nProvider";

type SystemStatusCardProps = {
  statusTitle: string;
  statusBody: string;
  level: "normal" | "warning" | "error";
  eyebrow?: string;
};

export function SystemStatusCard({
  statusTitle,
  statusBody,
  level,
  eyebrow,
}: SystemStatusCardProps) {
  const text = useLocaleText({
    zh: {
      eyebrow: "系统态势",
    },
    en: {
      eyebrow: "System Status",
    },
  });

  return (
    <section className={`status-card status-card--${level}`} data-testid="system-status-card">
      <div className="section-eyebrow">{eyebrow ?? text.eyebrow}</div>
      <h2 className="section-title" data-testid="system-status-title">{statusTitle}</h2>
      <p className="section-copy" data-testid="system-status-body">{statusBody}</p>
    </section>
  );
}
