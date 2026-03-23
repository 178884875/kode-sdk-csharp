import { useLocaleText } from './I18nProvider';
import type { DeskMeta } from '../shell-shared/types';

export function useAppStrings() {
  return useLocaleText({
    zh: {
      documentTitle: 'KodaClaw 现场中枢',
      documentDescription: 'KodaClaw 现场中枢：面向本地优先代理的引导、对话与控制台工作台。',
      notCreatedYet: '尚未创建',
      gatewayFallback: '代理 / 同源',
      healthLabels: {
        healthy: '健康',
        warning: '降级',
        error: '异常',
        unknown: '未知',
      },
      desks: [
        { id: 'chat',        label: '对话',        eyebrow: '协同对话',   summary: '在持续可见的消息时间线上发起任务、接收流式回复。' },
        { id: 'inbox',       label: '收件箱',      eyebrow: '行动队列',   summary: '把待处理事项、审批请求与人工决策集中在一个操作面里。' },
        { id: 'canvas',      label: '画布',        eyebrow: '发布界面',   summary: '查看 Agent 发布的报告、看板与结构化内容。' },
        { id: 'channels',    label: '渠道',        eyebrow: '外部线程',   summary: '检查账号、线程绑定与待发审批。' },
        { id: 'automations', label: '自动化',      eyebrow: '周期任务',   summary: '浏览计划、近期运行结果与启停状态。' },
        { id: 'models',      label: '模型设置',    eyebrow: '运行控制',   summary: '调整模型端点与运行偏好。' },
        { id: 'plugins',     label: '插件',        eyebrow: '扩展能力',   summary: '从信任、权限、运行态与日志四个维度掌控插件。' },
        { id: 'sessions',    label: '会话诊断',    eyebrow: '审计追踪',   summary: '检查会话生命周期、关键事件与诊断证据。' },
        { id: 'skills',      label: '技能',        eyebrow: '能力层',     summary: '浏览已发现的技能，在会话中激活。' },
        { id: 'mcpServers',  label: 'MCP 工具',    eyebrow: '能力层',     summary: '管理 workspace/mcp.json 中的 MCP 服务器：启用/禁用、测试连接。' },
        { id: 'settings',    label: '设置',        eyebrow: '工作区配置', summary: '编辑身份文件、绑定渠道账号、调整偏好与系统操作。' },
      ] as DeskMeta[],
      chat: {
        initialSystemNote: '现场中枢已就绪。',
        placeholderMain: '描述你希望 Koda 处理的任务…',
        emptyCompletion: '[空响应]',
        unknownStreamError: '未知流式错误。',
        streamClosed: '[流式通道已关闭]',
        failedToReachStream: '无法连接到聊天流。',
        newSessionNote: '已开始新对话。',
        sessionResumedNote: '已切换到历史会话，Agent 记得之前的上下文。',
      },
      notes: {
        gatewaySnapshot: (status: string, version: number, rootPath: string) =>
          `Gateway ${status}。工作区版本 ${version}，路径 ${rootPath}。`,
        gatewayError: (detail: string) => `Gateway 快照错误：${detail}`,
      },
    },
    en: {
      documentTitle: 'KodaClaw Field Console',
      documentDescription: 'KodaClaw field console for local-first agent bootstrap, conversation, and control-plane operations.',
      notCreatedYet: 'not created yet',
      gatewayFallback: 'proxy / same-origin',
      healthLabels: {
        healthy: 'Healthy',
        warning: 'Degraded',
        error: 'Unhealthy',
        unknown: 'Unknown',
      },
      desks: [
        { id: 'chat',        label: 'Chat',           eyebrow: 'Conversation',    summary: 'Dispatch tasks and receive streaming responses.' },
        { id: 'inbox',       label: 'Inbox',          eyebrow: 'Action queue',    summary: 'Keep approvals and follow-up items in one place.' },
        { id: 'canvas',      label: 'Canvas',         eyebrow: 'Published',       summary: 'View reports, boards, and structured outputs.' },
        { id: 'channels',    label: 'Channels',       eyebrow: 'External threads',summary: 'Inspect accounts, threads, and outbound approvals.' },
        { id: 'automations', label: 'Automations',    eyebrow: 'Recurring jobs',  summary: 'Review schedules, recent runs, and enablement.' },
        { id: 'models',      label: 'Models',         eyebrow: 'Runtime control', summary: 'Tune model endpoints and workspace preferences.' },
        { id: 'plugins',     label: 'Plugins',        eyebrow: 'Extensibility',   summary: 'Control trust, permissions, and runtime state.' },
        { id: 'sessions',    label: 'Sessions',       eyebrow: 'Audit trail',     summary: 'Inspect lifecycle traces and diagnostic evidence.' },
        { id: 'skills',      label: 'Skills',         eyebrow: 'Capability layer',summary: 'Browse discovered skills and activate them in chat.' },
        { id: 'mcpServers',  label: 'MCP Tools',      eyebrow: 'Capability layer',summary: 'Manage MCP servers from workspace/mcp.json: enable, disable, test connections.' },
        { id: 'settings',    label: 'Settings',       eyebrow: 'Workspace config', summary: 'Edit identity files, connect channels, set preferences, and system actions.' },
      ] as DeskMeta[],
      chat: {
        initialSystemNote: 'Field console ready.',
        placeholderMain: 'Describe the task you want Koda to handle...',
        emptyCompletion: '[empty completion]',
        unknownStreamError: 'Unknown stream error.',
        streamClosed: '[stream closed]',
        failedToReachStream: 'Failed to reach chat stream.',
        newSessionNote: 'New conversation started.',
        sessionResumedNote: 'Switched to a previous session. Agent remembers prior context.',
      },
      notes: {
        gatewaySnapshot: (status: string, version: number, rootPath: string) =>
          `Gateway ${status}. Workspace v${version} at ${rootPath}.`,
        gatewayError: (detail: string) => `Gateway snapshot error: ${detail}`,
      },
    },
  });
}
