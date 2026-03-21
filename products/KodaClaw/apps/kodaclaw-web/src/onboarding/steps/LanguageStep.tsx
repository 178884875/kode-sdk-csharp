interface Props {
  onNext: (lang: string) => void;
}

export function LanguageStep({ onNext }: Props) {
  const handleSelect = (lang: string) => {
    try {
      localStorage.setItem('kodaclaw.locale', lang);
    } catch {
      // ignore
    }
    onNext(lang);
  };

  return (
    <div className="onboarding-step" data-testid="onboarding-step-language">
      <h1 className="onboarding-step-title">选择语言 / Choose Language</h1>
      <p className="onboarding-step-desc">选择你偏好的界面语言</p>

      <div className="language-cards">
        <button
          className="language-card"
          data-testid="language-card-zh"
          onClick={() => handleSelect('zh-CN')}
        >
          <span className="language-card-flag">🇨🇳</span>
          <span className="language-card-name">中文</span>
          <span className="language-card-note">Chinese (Simplified)</span>
        </button>
        <button
          className="language-card"
          data-testid="language-card-en"
          onClick={() => handleSelect('en-US')}
        >
          <span className="language-card-flag">🇺🇸</span>
          <span className="language-card-name">English</span>
          <span className="language-card-note">English (US)</span>
        </button>
      </div>
    </div>
  );
}
