import { useState, useEffect } from 'react';
import type { ModelPreset, ModelConnectionTestResponse } from '../../types/contracts';
import {
  fetchModelPresets,
  testModelConnection,
  createModelEndpoint,
  setDefaultModelEndpoint,
} from '../../lib/api';

interface Props {
  onNext: () => void;
  onSkip: () => void;
}

const PROVIDERS = [
  { id: 'Anthropic', label: 'Anthropic'  },
  { id: 'OpenAI',    label: 'OpenAI'     },
  { id: 'DeepSeek',  label: 'DeepSeek'   },
  { id: 'GLM',       label: '智谱 GLM'   },
  { id: 'MiniMax',   label: 'MiniMax'    },
  { id: 'Kimi',      label: 'Kimi'       },
  { id: 'Xiaomi',    label: '小米 MiMo'  },
  { id: 'Ollama',    label: 'Ollama'     },
];

const PROVIDER_FILTER: Record<string, (p: ModelPreset) => boolean> = {
  Anthropic: p => p.provider === 'Anthropic',
  OpenAI:    p => p.provider === 'OpenAI',
  DeepSeek:  p => p.presetId.startsWith('deepseek-'),
  GLM:       p => p.presetId.startsWith('glm-'),
  MiniMax:   p => p.presetId.startsWith('minimax-'),
  Kimi:      p => p.presetId.startsWith('kimi-') || p.presetId.startsWith('moonshot-'),
  Xiaomi:    p => p.presetId.startsWith('xiaomi-'),
  Ollama:    p => p.presetId.startsWith('ollama-') || p.presetId === 'custom',
};

export function ModelStep({ onNext, onSkip }: Props) {
  const [presets, setPresets]                     = useState<ModelPreset[]>([]);
  const [provider, setProvider]                   = useState<string | null>(null);
  const [preset, setPreset]                       = useState<ModelPreset | null>(null);
  const [apiKey, setApiKey]                       = useState('');
  const [customModelId, setCustomModelId]         = useState('');
  const [customBaseUrl, setCustomBaseUrl]         = useState('');
  const [showAdvanced, setShowAdvanced]           = useState(false);
  const [testing, setTesting]                     = useState(false);
  const [testResult, setTestResult]               = useState<ModelConnectionTestResponse | null>(null);
  const [protocol, setProtocol]                   = useState<'Anthropic' | 'OpenAI'>('OpenAI');
  const [saving, setSaving]                       = useState(false);
  const [saveError, setSaveError]                 = useState<string | null>(null);

  useEffect(() => {
    fetchModelPresets().then(setPresets).catch(() => {});
  }, []);

  // Only show text / multimodal models (capabilities must include TextChat = 0x1)
  const providerPresets = provider
    ? presets.filter(p => (PROVIDER_FILTER[provider] ?? (() => false))(p) && (p.defaultCapabilities & 1) !== 0)
    : [];

  const isOllama         = !!preset && preset.presetId.startsWith('ollama-');
  const effectiveModelId = customModelId.trim() || preset?.modelId || '';
  const effectiveBaseUrl = customBaseUrl.trim() || preset?.baseUrl || undefined;
  const needsModelId     = !!preset && preset.modelId === '';
  const needsApiKey      = !!preset && !isOllama;
  const showBaseUrl      = !!preset && (preset.requiresBaseUrl || isOllama);
  const showAdvancedOpt  = !!preset && !preset.requiresBaseUrl && !isOllama;
  const canTest          = !!preset && (!needsModelId || !!effectiveModelId) && (!needsApiKey || !!apiKey);

  function selectProvider(pid: string) {
    setProvider(pid);
    setPreset(null);
    setApiKey('');
    setCustomModelId('');
    setCustomBaseUrl('');
    setShowAdvanced(false);
    setTestResult(null);
    setSaveError(null);
  }

  function selectPreset(p: ModelPreset) {
    setPreset(p);
    setApiKey('');
    setCustomModelId('');
    setCustomBaseUrl('');
    setProtocol('OpenAI');
    setTestResult(null);
    setSaveError(null);
  }

  async function handleTest() {
    if (!preset) return;
    setTesting(true);
    setTestResult(null);
    try {
      const r = await testModelConnection({
        presetId: preset.presetId,
        modelId:  effectiveModelId || undefined,
        baseUrl:  effectiveBaseUrl,
        apiKey,
      });
      setTestResult(r);
    } catch {
      setTestResult({ ok: false, latencyMs: 0, error: 'network_error' });
    } finally {
      setTesting(false);
    }
  }

  async function handleSave() {
    if (!preset || !testResult?.ok) return;
    setSaving(true);
    setSaveError(null);
    try {
      const created = await createModelEndpoint({
        displayName: customModelId.trim()
          ? `${preset.displayName} (${customModelId.trim()})`
          : preset.displayName,
        provider: preset.provider === 'Anthropic' ? 'Anthropic' : protocol,
        modelId:  effectiveModelId,
        baseUrl:  effectiveBaseUrl ?? null,
        apiKeyEnvironmentVariable: null,
        apiKeyValue: apiKey || null,
        enabled: true,
        capabilities: preset.defaultCapabilities,
      });
      await setDefaultModelEndpoint(created.id);
      onNext();
    } catch (err) {
      setSaveError(err instanceof Error ? err.message : '保存失败，请重试');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="ob-step" data-testid="onboarding-step-model">
      {/* Header */}
      <h1 className="ob-title">配置 AI 模型</h1>
      <p className="ob-desc">选择一个 Provider，填入 API Key，即可开始对话</p>

      {/* Provider tabs */}
      <div className="ob-provider-tabs" role="tablist">
        {PROVIDERS.map(p => (
          <button
            key={p.id}
            role="tab"
            aria-selected={provider === p.id}
            className={`ob-provider-tab ${provider === p.id ? 'is-active' : ''}`}
            data-testid={`provider-card-${p.id.toLowerCase()}`}
            onClick={() => selectProvider(p.id)}
          >
            {p.label}
          </button>
        ))}
      </div>

      {/* Model list */}
      {provider && (
        <div className="ob-model-list">
          {providerPresets.length === 0 && (
            <p className="ob-loading">加载预设中…</p>
          )}
          {providerPresets.map(p => (
            <label
              key={p.presetId}
              className={`ob-model-row ${preset?.presetId === p.presetId ? 'is-selected' : ''}`}
              data-testid={`model-card-${p.presetId}`}
            >
              <input
                type="radio"
                name="model"
                checked={preset?.presetId === p.presetId}
                onChange={() => selectPreset(p)}
              />
              <div className="ob-model-info">
                <span className="ob-model-name">
                  {p.displayName}
                  {p.tier === 'Recommended' && <span className="ob-badge">推荐</span>}
                </span>
                <span className="ob-model-meta">{p.description}</span>
              </div>
              {p.contextWindowSize >= 100000 && (
                <span className="ob-model-ctx">{(p.contextWindowSize / 1000).toFixed(0)}K</span>
              )}
            </label>
          ))}
        </div>
      )}

      {/* Custom model ID (empty modelId presets) */}
      {needsModelId && (
        <div className="ob-field">
          <label htmlFor="ob-custom-model">模型名称 <span className="ob-required">*</span></label>
          <input
            id="ob-custom-model"
            type="text"
            className="ob-input"
            data-testid="custom-model-id-input"
            placeholder={isOllama ? 'llama3.2、qwen2.5 等' : '完整 model ID'}
            value={customModelId}
            onChange={e => { setCustomModelId(e.target.value); setTestResult(null); }}
          />
        </div>
      )}

      {/* Base URL (Ollama / custom endpoint) */}
      {showBaseUrl && (
        <div className="ob-field">
          <label htmlFor="ob-base-url">Base URL</label>
          <input
            id="ob-base-url"
            type="text"
            className="ob-input"
            data-testid="custom-base-url-input"
            placeholder={isOllama ? 'http://localhost:11434/v1' : 'https://your-endpoint/v1'}
            value={customBaseUrl || preset?.baseUrl || ''}
            onChange={e => { setCustomBaseUrl(e.target.value); setTestResult(null); }}
          />
        </div>
      )}

      {/* Protocol selector (for OpenAI-compatible providers that may also support Anthropic) */}
      {preset && preset.provider === 'OpenAICompatible' && !isOllama && (
        <div className="ob-field">
          <label>API 协议</label>
          <div className="ob-protocol-tabs">
            {(['OpenAI', 'Anthropic'] as const).map(p => (
              <button
                key={p}
                type="button"
                className={`ob-protocol-tab ${protocol === p ? 'is-active' : ''}`}
                onClick={() => { setProtocol(p); setTestResult(null); }}
              >
                {p === 'OpenAI' ? 'OpenAI 兼容' : 'Anthropic 兼容'}
              </button>
            ))}
          </div>
        </div>
      )}

      {/* API Key */}
      {needsApiKey && (
        <div className="ob-field">
          <label htmlFor="ob-apikey">API Key</label>
          <input
            id="ob-apikey"
            type="password"
            className="ob-input ob-input--mono"
            data-testid="apikey-input"
            placeholder={preset?.provider === 'Anthropic' ? 'sk-ant-api03-…' : 'sk-…'}
            value={apiKey}
            onChange={e => { setApiKey(e.target.value); setTestResult(null); }}
          />
          <span className="ob-hint">连接测试仅发送 1 token，费用 &lt; $0.0001</span>
        </div>
      )}

      {/* Advanced (proxy / custom Base URL override for non-local providers) */}
      {showAdvancedOpt && (
        <div className="ob-advanced">
          <button
            type="button"
            className="ob-advanced-toggle"
            onClick={() => setShowAdvanced(v => !v)}
          >
            {showAdvanced ? '▲ 收起' : '▼ 高级选项（代理 / 自定义 Base URL）'}
          </button>
          {showAdvanced && (
            <div className="ob-advanced-body">
              <div className="ob-field">
                <label htmlFor="ob-adv-url">
                  自定义 Base URL
                  <span className="ob-optional">可选</span>
                </label>
                <input
                  id="ob-adv-url"
                  type="text"
                  className="ob-input"
                  data-testid="custom-base-url-input"
                  placeholder={
                    preset?.provider === 'Anthropic'
                      ? 'https://api.anthropic.com（默认）'
                      : 'https://api.openai.com/v1（默认）'
                  }
                  value={customBaseUrl}
                  onChange={e => { setCustomBaseUrl(e.target.value); setTestResult(null); }}
                />
              </div>
            </div>
          )}
        </div>
      )}

      {/* Test + Save */}
      {preset && (
        <div className="ob-actions">
          {!testResult?.ok && (
            <button
              className="ob-test-btn"
              data-testid="test-connection-btn"
              onClick={() => void handleTest()}
              disabled={testing || !canTest}
            >
              {testing ? (
                <><span className="ob-spinner" /> 测试中…</>
              ) : '测试连接'}
            </button>
          )}

          {testResult && (
            <div
              className={`ob-result ${testResult.ok ? 'is-ok' : 'is-err'}`}
              data-testid="connection-result"
            >
              {testResult.ok
                ? `✓ 连接成功（${testResult.latencyMs}ms）`
                : `✗ ${testResult.error === 'authentication_error' ? 'Key 认证失败' : `连接失败：${testResult.error}`}`
              }
            </div>
          )}

          {testResult?.ok && (
            <button
              className="ob-save-btn"
              onClick={() => void handleSave()}
              disabled={saving}
            >
              {saving ? '保存中…' : '开始使用 KodaClaw →'}
            </button>
          )}

          {saveError && (
            <div className="ob-result is-err" data-testid="save-error">✗ {saveError}</div>
          )}
        </div>
      )}

      {/* Skip */}
      <button className="ob-skip" onClick={onSkip}>
        跳过，我自己配置
      </button>
    </div>
  );
}
