import { useState, useEffect } from 'react';
import { fetchWorkspaceFile, updateWorkspaceFile } from '../../lib/api';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { queryKeys } from '../../lib/queryKeys';
import { PersonaSelector } from './PersonaSelector';
import { Skeleton } from '../ui/Skeleton';
import { useLocaleText } from '../../i18n/I18nProvider';

type FileState = {
  content: string;
  saved: string;
  saving: boolean;
  loading: boolean;
  error: string | null;
  success: boolean;
};

function useWorkspaceFile(target: 'identity' | 'soul' | 'user') {
  const queryClient = useQueryClient();
  const { data: fileData, isLoading, error: queryError } = useQuery({
    queryKey: queryKeys.workspaceFile(target),
    queryFn: () => fetchWorkspaceFile(target),
  });
  const [state, setState] = useState<FileState>({
    content: '',
    saved: '',
    saving: false,
    loading: true,
    error: null,
    success: false,
  });
  useEffect(() => {
    if (fileData && state.loading) {
      setState(s => ({ ...s, content: fileData.content ?? '', saved: fileData.content ?? '', loading: false }));
    }
    if (!isLoading && queryError && state.loading) {
      setState(s => ({ ...s, error: String(queryError), loading: false }));
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [fileData, isLoading, queryError]);

  const save = async () => {
    setState(s => ({ ...s, saving: true, error: null, success: false }));
    try {
      await updateWorkspaceFile(target, state.content);
      void queryClient.invalidateQueries({ queryKey: queryKeys.workspaceFile(target) });
      setState(s => ({ ...s, saved: s.content, saving: false, success: true }));
      setTimeout(() => setState(s => ({ ...s, success: false })), 3000);
    } catch (e) {
      setState(s => ({ ...s, saving: false, error: String(e) }));
    }
  };

  const setContent = (content: string) => setState(s => ({ ...s, content }));

  const isDirty = state.content !== state.saved;

  return { ...state, isDirty, setContent, save };
}

export function WorkspaceIdentityEditor() {
  const identity = useWorkspaceFile('identity');
  const soul = useWorkspaceFile('soul');
  const user = useWorkspaceFile('user');
  const [showPersonaSelector, setShowPersonaSelector] = useState(false);

  const text = useLocaleText({
    zh: {
      sectionTitle: '工作区身份',
      sectionDesc: '这些文件定义 Koda 的人格、行为准则和对你的了解。修改后下次会话启动时生效。',
      identityHint: 'Koda 的角色定位与名字',
      soulHint: '行为准则与价值观',
      soulTemplate: '从模板选择',
      soulTemplateCollapse: '收起模板',
      userHint: '关于你的背景与偏好',
      saved: '已保存',
      saving: '保存中…',
      save: '保存',
    },
    en: {
      sectionTitle: 'Workspace Identity',
      sectionDesc: 'These files define Koda\'s persona, behavior guidelines, and knowledge about you. Changes take effect at the next session start.',
      identityHint: 'Koda\'s role and name',
      soulHint: 'Behavioral principles and values',
      soulTemplate: 'Choose from template',
      soulTemplateCollapse: 'Collapse templates',
      userHint: 'Your background and preferences',
      saved: 'Saved',
      saving: 'Saving…',
      save: 'Save',
    },
  });

  return (
    <div className="settings-section" data-testid="settings-identity-section">
      <h2 className="settings-section-title">{text.sectionTitle}</h2>
      <p className="settings-section-desc">{text.sectionDesc}</p>

      <div className="settings-file-editor" data-testid="settings-identity-editor">
        <div className="settings-file-header">
          <label className="settings-file-label">IDENTITY.md</label>
          <span className="settings-file-hint">{text.identityHint}</span>
        </div>
        {identity.loading ? (
          <Skeleton count={3} height={20} />
        ) : (
        <textarea
          className="settings-file-textarea"
          value={identity.content}
          onChange={e => identity.setContent(e.target.value)}
          rows={8}
          spellCheck={false}
        />
        )}
        <div className="settings-file-actions">
          {identity.error && <span className="settings-file-error">{identity.error}</span>}
          {identity.success && <span className="settings-file-success">{text.saved}</span>}
          <button
            type="button"
            className="btn btn--primary"
            disabled={!identity.isDirty || identity.saving || identity.loading}
            onClick={identity.save}
          >
            {identity.saving ? text.saving : text.save}
          </button>
        </div>
      </div>

      <div className="settings-file-editor" data-testid="settings-soul-editor">
        <div className="settings-file-header">
          <label className="settings-file-label">SOUL.md</label>
          <span className="settings-file-hint">{text.soulHint}</span>
          <button
            type="button"
            className="btn btn--ghost"
            data-testid="settings-persona-trigger"
            onClick={() => setShowPersonaSelector(v => !v)}
          >
            {showPersonaSelector ? text.soulTemplateCollapse : text.soulTemplate}
          </button>
        </div>
        {showPersonaSelector && (
          <PersonaSelector
            onSelect={markdown => { soul.setContent(markdown); setShowPersonaSelector(false); }}
            onDismiss={() => setShowPersonaSelector(false)}
          />
        )}
        {soul.loading ? (
          <Skeleton count={3} height={20} />
        ) : (
        <textarea
          className="settings-file-textarea"
          value={soul.content}
          onChange={e => soul.setContent(e.target.value)}
          rows={8}
          spellCheck={false}
        />
        )}
        <div className="settings-file-actions">
          {soul.error && <span className="settings-file-error">{soul.error}</span>}
          {soul.success && <span className="settings-file-success">{text.saved}</span>}
          <button
            type="button"
            className="btn btn--primary"
            disabled={!soul.isDirty || soul.saving || soul.loading}
            onClick={soul.save}
          >
            {soul.saving ? text.saving : text.save}
          </button>
        </div>
      </div>

      <div className="settings-file-editor" data-testid="settings-user-editor">
        <div className="settings-file-header">
          <label className="settings-file-label">USER.md</label>
          <span className="settings-file-hint">{text.userHint}</span>
        </div>
        {user.loading ? (
          <Skeleton count={3} height={20} />
        ) : (
        <textarea
          className="settings-file-textarea"
          value={user.content}
          onChange={e => user.setContent(e.target.value)}
          rows={8}
          spellCheck={false}
        />
        )}
        <div className="settings-file-actions">
          {user.error && <span className="settings-file-error">{user.error}</span>}
          {user.success && <span className="settings-file-success">{text.saved}</span>}
          <button
            type="button"
            className="btn btn--primary"
            disabled={!user.isDirty || user.saving || user.loading}
            onClick={user.save}
          >
            {user.saving ? text.saving : text.save}
          </button>
        </div>
      </div>
    </div>
  );
}
