import { useState, useCallback } from 'react';
import type { OnboardingState } from '../types/contracts';
import { updateOnboardingState, completeOnboarding } from '../lib/api';
import { ModelStep } from './steps/ModelStep';
import { DoneStep } from './steps/DoneStep';
import './onboarding.css';

interface Props {
  initialState: OnboardingState;
  onComplete: () => void;
}

type StepId = 'model' | 'done';

const STEPS: StepId[] = ['model', 'done'];

const STEP_LABELS: Record<StepId, string> = {
  model: '模型',
  done: '完成',
};

export function OnboardingShell({ initialState, onComplete }: Props) {
  const resolveInitialStep = (s: string | undefined): StepId => {
    if (s === 'model' || s === 'done') return s;
    return 'model';
  };

  const [currentStep, setCurrentStep] = useState<StepId>(
    resolveInitialStep(initialState.currentStepId)
  );
  const [state, setState] = useState<OnboardingState>(initialState);

  const advance = useCallback(async (nextStep: StepId, updates?: Partial<OnboardingState>) => {
    const newCompleted = (state.completedSteps ?? []).includes(currentStep)
      ? (state.completedSteps ?? [])
      : [...(state.completedSteps ?? []), currentStep];

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
          return (
            <button
              key={step}
              className={`onboarding-stepper-item ${isCurrent ? 'is-current' : ''} ${isDone ? 'is-done' : ''}`}
              disabled={!isDone}
              onClick={isDone ? () => void advance(step) : undefined}
              title={isDone ? `返回「${STEP_LABELS[step]}」步骤` : undefined}
            >
              <span className="onboarding-stepper-dot">{isDone ? '✓' : i + 1}</span>
              <span className="onboarding-stepper-label">{STEP_LABELS[step]}</span>
            </button>
          );
        })}
      </div>

      <div className="onboarding-content">
        {currentStep === 'model' && (
          <ModelStep
            onNext={(presetId) => void advance('done', { selectedPresetId: presetId })}
            onSkip={() => void advance('done')}
          />
        )}
        {currentStep === 'done' && (
          <DoneStep state={state} onComplete={handleComplete} />
        )}
      </div>
    </div>
  );
}
