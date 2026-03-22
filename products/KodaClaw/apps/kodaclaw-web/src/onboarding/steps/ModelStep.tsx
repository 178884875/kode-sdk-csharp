import { useState, useEffect } from 'react';
import type { ModelPreset, ModelConnectionTestResponse } from '../../types/contracts';
import { fetchModelPresets, testModelConnection, createModelEndpoint, setDefaultModelEndpoint } from '../../lib/api';

interface Props {
  onNext: (presetId: string) => void;
  onSkip: () => void;
}

type SubPhase = 'provider' | 'model' | 'apikey';

const PROVIDERS = [
  { id: 'Anthropic', name: 'Anthropic', note: 'Claude — KodaClaw 推荐的默认选择' },
  { id: 'OpenAI', name: 'OpenAI', note: 'GPT 系列' },
  { id: 'DeepSeek', name: 'DeepSeek', note: '性价比极高的国产大模型' },
  { id: 'Google', name: 'Google', note: 'Gemini 超长上下文' },
  { id: 'Ollama', name: 'Ollama / 自定义', note: '本地运行或自定义 endpoint' },
];

export function ModelStep({ onNext, onSkip }: Props) {
  const [phase, setPhase] = useState<SubPhase>('provider');
  const [presets, setPresets] = useState<ModelPreset[]>([]);
  const [selectedProvider, setSelectedProvider] = useState<string | null>(null);
  const [selectedPreset, setSelectedPreset] = useState<ModelPreset | null>(null);
  const [apiKey, setApiKey] = useState('');
  // 自定义覆盖字段：Ollama/Custom 必填 modelId，任何 provider 均可覆盖 baseUrl
  const [customModelId, setCustomModelId] = useState('');
  const [customBaseUrl, setCustomBaseUrl] = useState('');
  const [showAdvanced, setShowAdvanced] = useState(false);
  const [testing, setTesting] = useState(false);
  const [testResult, setTestResult] = useState<ModelConnectionTestResponse | null>(null);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);

  useEffect(() => {
    fetchModelPresets().then(setPresets).catch(() => {});
  }, []);

  const providerPresets = presets.filter(p =>
    selectedProvider === 'Ollama'
      ? (p.provider === 'Ollama' || p.provider === 'Custom')
      : p.provider === selectedProvider
  );

  const effectiveModelId = customModelId.trim() || selectedPreset?.modelId || '';
  const effectiveBaseUrl = customBaseUrl.trim() || selectedPreset?.baseUrl || undefined;
  const needsModelId = !!selectedPreset && selectedPreset.modelId === '';
  const needsApiKey = !!selectedPreset && selectedPreset.provider !== 'Ollama';
  const canTest = !!selectedPreset && (!needsModelId || !!effectiveModelId) && (!needsApiKey || !!apiKey);

  const handleTestConnection = async () => {
    if (!selectedPreset) return;
    setTesting(true);
    setTestResult(null);
    try {
      const result = await testModelConnection({
        presetId: selectedPreset.presetId,
        modelId: effectiveModelId || undefined,
        baseUrl: effectiveBaseUrl,
        apiKey,
      });
      setTestResult(result);
    } catch {
      setTestResult({ ok: false, latencyMs: 0, error: 'network_error' });
    } finally {
      setTesting(false);
    }
  };

  const handleSaveAndNext = async () => {
    if (!selectedPreset || !testResult?.ok) return;
    setSaving(true);
    setSaveError(null);
    try {
      const created = await createModelEndpoint({
        displayName: customModelId.trim()
          ? `${selectedPreset.displayName} (${customModelId.trim()})`
          : selectedPreset.displayName,
        provider: selectedPreset.provider === 'Anthropic' ? 'Anthropic' : 'OpenAI',
        modelId: effectiveModelId,
        baseUrl: effectiveBaseUrl ?? null,
        apiKeyEnvironmentVariable: null,
        apiKeyValue: apiKey || null,
        enabled: true,
        capabilities: selectedPreset.defaultCapabilities,
      });
      // Set as default so RegistryAwareModelProvider routes all sessions to it immediately.
      await setDefaultModelEndpoint(created.id);
      onNext(selectedPreset.presetId);
    } catch (err) {
      const msg = err instanceof Error ? err.message : '保存模型配置失败，请重试';
      setSaveError(msg);
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="onboarding-step" data-testid="onboarding-step-model">
      <h1 className="onboarding-step-title">配置 AI 模型</h1>
      <p className="onboarding-step-desc">选择一个模型 provider 并填入 API Key</p>

      {phase === 'provider' && (
        <div className="provider-grid">
          {PROVIDERS.map(p => (
            <button
              key={p.id}
              className={`provider-card ${selectedProvider === p.id ? 'is-selected' : ''}`}
              data-testid={`provider-card-${p.id.toLowerCase()}`}
              onClick={() => { setSelectedProvider(p.id); setPhase('model'); }}
            >
              <span className="provider-card-name">{p.name}</span>
              <span className="provider-card-note">{p.note}</span>
            </button>
          ))}
        </div>
      )}

      {phase === 'model' && (
        <div>
          <button className="onboarding-back-btn" onClick={() => setPhase('provider')}>← 返回</button>
          <div className="model-grid">
            {providerPresets.length === 0 && (
              <p className="onboarding-step-desc">正在加载预设…</p>
            )}
            {providerPresets.map(preset => (
              <button
                key={preset.presetId}
                className={`model-card ${selectedPreset?.presetId === preset.presetId ? 'is-selected' : ''}`}
                data-testid={`model-card-${preset.presetId}`}
                onClick={() => { setSelectedPreset(preset); setPhase('apikey'); }}
              >
                <div className="model-card-name">{preset.displayName}</div>
                <div className="model-card-tier">{preset.tier}</div>
                <div className="model-card-desc">{preset.description}</div>
                {preset.costHint && <div className="model-card-cost">{preset.costHint}</div>}
                {preset.contextWindowSize >= 100000 && (
                  <div className="model-card-ctx">{(preset.contextWindowSize / 1000).toFixed(0)}K context</div>
                )}
                {preset.tier === 'Recommended' && (
                  <span className="model-card-badge">推荐</span>
                )}
              </button>
            ))}
          </div>
        </div>
      )}

      {phase === 'apikey' && selectedPreset && (
        <div className="apikey-phase">
          <button className="onboarding-back-btn" onClick={() => { setPhase('model'); setCustomModelId(''); setCustomBaseUrl(''); setShowAdvanced(false); setTestResult(null); }}>← 返回</button>
          <div className="selected-model-info">
            <span>已选：</span><strong>{selectedPreset.displayName}</strong>
          </div>

          {/* Ollama / Custom：必须填写模型名称 */}
          {needsModelId && (
            <div className="apikey-input-group">
              <label htmlFor="custom-model-id">模型名称 <span style={{ color: '#e53e3e' }}>*</span></label>
              <input
                id="custom-model-id"
                type="text"
                className="apikey-input"
                data-testid="custom-model-id-input"
                placeholder={selectedPreset.provider === 'Ollama' ? 'llama3.2、qwen2.5 等' : '填入完整 model ID'}
                value={customModelId}
                onChange={e => { setCustomModelId(e.target.value); setTestResult(null); }}
              />
            </div>
          )}

          {/* Ollama / Custom：Base URL */}
          {(selectedPreset.requiresBaseUrl || selectedPreset.provider === 'Ollama') && (
            <div className="apikey-input-group">
              <label htmlFor="custom-base-url">Base URL</label>
              <input
                id="custom-base-url"
                type="text"
                className="apikey-input"
                data-testid="custom-base-url-input"
                placeholder={selectedPreset.provider === 'Ollama' ? 'http://localhost:11434/v1' : 'https://your-endpoint/v1'}
                value={customBaseUrl || selectedPreset.baseUrl || ''}
                onChange={e => { setCustomBaseUrl(e.target.value); setTestResult(null); }}
              />
            </div>
          )}

          {/* Anthropic / OpenAI：API Key */}
          {needsApiKey && (
            <div className="apikey-input-group">
              <label htmlFor="apikey-input">API Key</label>
              <input
                id="apikey-input"
                type="password"
                className="apikey-input"
                data-testid="apikey-input"
                placeholder={
                  selectedPreset.provider === 'Anthropic' ? 'sk-ant-...' :
                  selectedPreset.provider === 'OpenAI' ? 'sk-...' :
                  selectedPreset.provider === 'DeepSeek' ? 'sk-...' : '输入 API Key'
                }
                value={apiKey}
                onChange={e => { setApiKey(e.target.value); setTestResult(null); }}
              />
              <p className="apikey-cost-note">* 连接测试发送 1 token 请求，费用不足 $0.0001</p>
            </div>
          )}

          {/* 高级选项：Anthropic / OpenAI 可覆盖 Base URL（代理 / 企业网关场景） */}
          {!selectedPreset.requiresBaseUrl && selectedPreset.provider !== 'Ollama' && (
            <div className="advanced-options">
              <button
                className="advanced-options-toggle"
                type="button"
                onClick={() => setShowAdvanced(v => !v)}
              >
                {showAdvanced ? '▲ 收起高级选项' : '▼ 高级选项（自定义 Base URL / Model ID）'}
              </button>
              {showAdvanced && (
                <div className="advanced-options-body">
                  <div className="apikey-input-group">
                    <label htmlFor="adv-base-url">自定义 Base URL <span className="optional-hint">可选，留空使用官方地址</span></label>
                    <input
                      id="adv-base-url"
                      type="text"
                      className="apikey-input"
                      data-testid="custom-base-url-input"
                      placeholder={
                        selectedPreset.provider === 'Anthropic'
                          ? 'https://api.anthropic.com（默认）'
                          : 'https://api.openai.com/v1（默认）'
                      }
                      value={customBaseUrl}
                      onChange={e => { setCustomBaseUrl(e.target.value); setTestResult(null); }}
                    />
                  </div>
                  <div className="apikey-input-group">
                    <label htmlFor="adv-model-id">自定义 Model ID <span className="optional-hint">可选，留空使用预设值（{selectedPreset.modelId}）</span></label>
                    <input
                      id="adv-model-id"
                      type="text"
                      className="apikey-input"
                      data-testid="custom-model-id-input"
                      placeholder={selectedPreset.modelId}
                      value={customModelId}
                      onChange={e => { setCustomModelId(e.target.value); setTestResult(null); }}
                    />
                  </div>
                </div>
              )}
            </div>
          )}

          <button
            className="test-connection-btn"
            data-testid="test-connection-btn"
            onClick={() => void handleTestConnection()}
            disabled={testing || !canTest}
          >
            {testing ? '测试中...' : '测试连接'}
          </button>

          {testResult && (
            <div
              className={`connection-result ${testResult.ok ? 'is-ok' : 'is-error'}`}
              data-testid="connection-result"
            >
              {testResult.ok
                ? `✅ 连接成功（${testResult.latencyMs}ms）`
                : `❌ ${testResult.error === 'authentication_error' ? '认证失败，请检查 Key 是否正确' : `连接失败：${testResult.error}`}`
              }
            </div>
          )}

          {saveError && (
            <div className="connection-result is-error" data-testid="save-error">
              ❌ {saveError}
            </div>
          )}

          {testResult?.ok && (
            <button
              className="onboarding-next-btn"
              onClick={() => void handleSaveAndNext()}
              disabled={saving}
            >
              {saving ? '保存中...' : '下一步 →'}
            </button>
          )}
        </div>
      )}

      <button className="onboarding-skip-step-btn" onClick={onSkip}>
        跳过此步骤
      </button>
    </div>
  );
}
