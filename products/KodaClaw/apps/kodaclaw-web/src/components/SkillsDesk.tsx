import { type CSSProperties, useEffect, useRef, useState } from "react";
import { getGatewayUrl } from "../lib/config";
import { useLocaleText } from "../i18n/I18nProvider";

type SkillSource = "built-in" | "global" | "workspace";

type SkillDescriptor = {
  name: string;
  description: string | null;
  source: SkillSource;
  path: string;
  hasResources: boolean;
};

const toolbarStyle: CSSProperties = {
  marginTop: 12,
  display: "flex",
  flexWrap: "wrap",
  alignItems: "center",
  gap: 10,
};

const listStyle: CSSProperties = {
  listStyle: "none",
  margin: 0,
  padding: 0,
  display: "grid",
  gap: 12,
  marginTop: 16,
};

const sourceBadgeMap: Record<SkillSource, string> = {
  "built-in": "mode-badge",
  "global": "mode-badge",
  "workspace": "mode-badge mode-badge--main",
};

const SOURCE_ORDER: SkillSource[] = ["built-in", "global", "workspace"];

async function fetchSkills(signal?: AbortSignal): Promise<SkillDescriptor[]> {
  const base = getGatewayUrl() || "";
  const response = await fetch(`${base}/api/skills`, { signal });
  if (!response.ok) {
    throw new Error(`Failed to load skills: ${response.status} ${response.statusText}`);
  }

  return response.json() as Promise<SkillDescriptor[]>;
}

export function SkillsDesk() {
  const text = useLocaleText({
    zh: {
      eyebrow: "能力层",
      title: "技能浏览台",
      copy: "查看已发现的技能，了解每个技能的来源和描述。在对话中用 skill_activate 激活技能，或用 fs_write 向 workspace/skills/ 写入 SKILL.md 来创建新技能。",
      refresh: "刷新列表",
      refreshing: "刷新中...",
      loading: "正在加载技能...",
      empty: "未发现任何技能。",
      loaded: (count: number) => `已发现 ${count} 个技能`,
      sourceLabels: {
        "built-in": "内置",
        "global": "全局",
        "workspace": "工作区",
      } as Record<SkillSource, string>,
      hasResources: "含资源文件",
      noDescription: "未提供描述",
      loadError: "加载技能列表失败。",
    },
    en: {
      eyebrow: "Capability Layer",
      title: "Skills Browser",
      copy: "Browse discovered skills and their sources. Use skill_activate in chat to load a skill, or write a SKILL.md to workspace/skills/ to author a new one.",
      refresh: "Refresh list",
      refreshing: "Refreshing...",
      loading: "Loading skills...",
      empty: "No skills discovered.",
      loaded: (count: number) => `${count} skill${count === 1 ? "" : "s"} discovered`,
      sourceLabels: {
        "built-in": "Built-in",
        "global": "Global",
        "workspace": "Workspace",
      } as Record<SkillSource, string>,
      hasResources: "Has resources",
      noDescription: "No description provided",
      loadError: "Failed to load skills.",
    },
  });

  const [skills, setSkills] = useState<SkillDescriptor[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const requestIdRef = useRef(0);

  async function loadSkills(mode: "initial" | "refresh") {
    const requestId = ++requestIdRef.current;

    if (mode === "initial") {
      setIsLoading(true);
    } else {
      setIsRefreshing(true);
    }
    setError(null);

    try {
      const items = await fetchSkills();
      if (requestIdRef.current !== requestId) {
        return;
      }

      setSkills(items);
    } catch (nextError) {
      if (requestIdRef.current !== requestId) {
        return;
      }

      setSkills([]);
      setError(nextError instanceof Error ? nextError.message : text.loadError);
    } finally {
      if (requestIdRef.current === requestId) {
        if (mode === "initial") {
          setIsLoading(false);
        } else {
          setIsRefreshing(false);
        }
      }
    }
  }

  useEffect(() => {
    void loadSkills("initial");
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Group skills by source in display order
  const groupedSkills = SOURCE_ORDER
    .map((source) => ({
      source,
      items: skills.filter((s) => s.source === source),
    }))
    .filter((group) => group.items.length > 0);

  return (
    <section className="bootstrap-panel" data-testid="skills-desk">
      <div className="section-eyebrow">{text.eyebrow}</div>
      <h2 className="section-title">{text.title}</h2>
      <p className="section-copy">{text.copy}</p>

      <div style={toolbarStyle}>
        <button
          type="button"
          className="secondary-button"
          data-testid="skills-refresh"
          disabled={isLoading || isRefreshing}
          onClick={() => {
            void loadSkills("refresh");
          }}
        >
          {isRefreshing ? text.refreshing : text.refresh}
        </button>
        <span className="composer__status">
          {isLoading ? text.loading : text.loaded(skills.length)}
        </span>
      </div>

      {error ? (
        <p
          className="bootstrap-panel__feedback bootstrap-panel__feedback--error"
          data-testid="skills-error"
        >
          {error}
        </p>
      ) : null}

      {!isLoading && skills.length === 0 && !error ? (
        <p className="timeline__empty" data-testid="skills-empty">{text.empty}</p>
      ) : null}

      {groupedSkills.map(({ source, items }) => (
        <section key={source} data-testid={`skills-group-${source}`}>
          <p className="metric-label" style={{ marginTop: 16, marginBottom: 8 }}>
            {text.sourceLabels[source]}
          </p>
          <ul style={listStyle}>
            {items.map((skill) => (
              <li
                key={`${source}-${skill.name}`}
                className="message message--assistant"
                data-testid={`skill-item-${skill.name}`}
              >
                <div className="message__meta">
                  <span className={sourceBadgeMap[skill.source]}>
                    {text.sourceLabels[skill.source]}
                  </span>
                  {skill.hasResources ? (
                    <span className="mode-badge">{text.hasResources}</span>
                  ) : null}
                </div>
                <strong>{skill.name}</strong>
                <span className="section-copy">
                  {skill.description ?? text.noDescription}
                </span>
              </li>
            ))}
          </ul>
        </section>
      ))}
    </section>
  );
}
