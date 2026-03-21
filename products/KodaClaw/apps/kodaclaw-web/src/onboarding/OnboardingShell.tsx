import { useState, useCallback } from 'react';
import type { OnboardingState } from '../types/contracts';
import { updateOnboardingState, completeOnboarding } from '../lib/api';
import { LanguageStep } from './steps/LanguageStep';
import { ModelStep } from './steps/ModelStep';
import { PersonaStep } from './steps/PersonaStep';
import { ChannelStep } from './steps/ChannelStep';
import { DoneStep } from './steps/DoneStep';
import './onboarding.css';

interface Props {
  initialState: OnboardingState;
  onComplete: () => void;
}

type StepId = 'language' | 'model' | 'persona' | 'channel' | 'done';

const STEPS: StepId[] = ['language', 'model', 'persona', 'channel', 'done'];

const STEP_LABELS: Record<StepId, string> = {
  language: '语言',
  model: '模型',
  persona: '风格',
  channel: '渠道',
  done: '完成',
};

export function OnboardingShell({ initialState, onComplete }: Props) {
  const [currentStep, setCurrentStep] = useState<StepId>(
    (initialState.currentStepId as StepId) || 'language'
  );
  const [state, setState] = useState<OnboardingState>(initialState);

  const goToStep = useCallback(async (targetStep: StepId) => {
    const newState: OnboardingState = { ...state, currentStepId: targetStep };
    setState(newState);
    await updateOnboardingState(newState).catch(() => {});
    setCurrentStep(targetStep);
  }, [state]);

  const advance = useCallback(async (nextStep: StepId, updates?: Partial<OnboardingState>) => {
    const prevStep = currentStep;
    const prevCompleted = state.completedSteps ?? [];
    const newCompleted = prevCompleted.includes(prevStep)
      ? prevCompleted
      : [...prevCompleted, prevStep];

    const newState: OnboardingState = {
      ...state,
      currentStepId: nextStep,
      completedSteps: newCompleted,
      ...updates,
    };
    setState(newState);
    await updateOnboardingState(newState).catch(() => {});
    setCurrentStep(nextStep);
  }, [state, currentStep]);

  const handleSkipAll = useCallback(async () => {
    await completeOnboarding().catch(() => {});
    onComplete();
  }, [onComplete]);

  const handleComplete = useCallback(async () => {
    await completeOnboarding().catch(() => {});
    onComplete();
  }, [onComplete]);

  const stepIndex = STEPS.indexOf(currentStep);
  const progress = (stepIndex / (STEPS.length - 1)) * 100;

  return (
    <div className="onboarding-shell" data-testid="onboarding-shell">
      <div className="onboarding-topbar">
        <div className="onboarding-brand">KodaClaw</div>
        <button className="onboarding-skip-all" onClick={() => void handleSkipAll()}>
          跳过，我自己配置 / Skip setup
        </button>
      </div>

      <div className="onboarding-progress">
        <div className="onboarding-progress-bar" style={{ width: `${progress}%` }} />
      </div>

      <div className="onboarding-stepper">
        {STEPS.filter(s => s !== 'done').map((step, i) => {
          const idx = STEPS.indexOf(step);
          const isCurrent = step === currentStep;
          const isDone = stepIndex > idx;
          const isClickable = isDone;
          return (
            <button
              key={step}
              className={`onboarding-stepper-item ${isCurrent ? 'is-current' : ''} ${isDone ? 'is-done' : ''}`}
              disabled={!isClickable}
              onClick={isClickable ? () => void goToStep(step) : undefined}
              title={isDone ? `返回「${STEP_LABELS[step]}」步骤` : undefined}
            >
              <span className="onboarding-stepper-dot">{isDone ? '✓' : i + 1}</span>
              <span className="onboarding-stepper-label">{STEP_LABELS[step]}</span>
            </button>
          );
        })}
      </div>

      <div className="onboarding-content">
        {currentStep === 'language' && (
          <LanguageStep onNext={(lang) => void advance('model', { selectedLanguage: lang })} />
        )}
        {currentStep === 'model' && (
          <ModelStep
            onNext={(presetId) => void advance('persona', { selectedPresetId: presetId })}
            onSkip={() => void advance('persona')}
          />
        )}
        {currentStep === 'persona' && (
          <PersonaStep
            onNext={(presetId) => void advance('channel', { selectedPersonaPresetId: presetId })}
            onSkip={() => void advance('channel')}
          />
        )}
        {currentStep === 'channel' && (
          <ChannelStep
            onNext={() => void advance('done')}
            onSkip={() => void advance('done', { channelStepSkipped: true })}
          />
        )}
        {currentStep === 'done' && (
          <DoneStep state={state} onComplete={handleComplete} />
        )}
      </div>
    </div>
  );
}
