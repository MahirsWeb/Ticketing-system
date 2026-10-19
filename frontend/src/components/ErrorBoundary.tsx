import { Component, type ErrorInfo, type ReactNode } from 'react';
import { useLanguage } from '../i18n/LanguageContext';
import { Button } from './ui';

function Fallback({ onRetry }: { onRetry: () => void }) {
  const { t } = useLanguage();
  return (
    <div role="alert" className="flex min-h-screen items-center justify-center bg-slate-200 px-4">
      <div className="w-full max-w-md rounded-xl bg-white p-8 text-center shadow-md">
        <h1 className="text-xl font-bold text-slate-900">{t('error_title')}</h1>
        <p className="mt-3 text-sm text-slate-600">{t('error_body')}</p>
        <div className="mt-6 flex justify-center gap-3">
          <Button variant="secondary" onClick={onRetry}>
            {t('error_retry')}
          </Button>
          <Button onClick={() => window.location.reload()}>{t('error_reload')}</Button>
        </div>
      </div>
    </div>
  );
}

/**
 * Catches an error thrown while rendering anything below it, so one broken screen shows a message instead of a blank
 * page. It does not catch errors in event handlers or in requests; those are handled where they happen.
 */
export class ErrorBoundary extends Component<{ children: ReactNode }, { failed: boolean }> {
  state = { failed: false };

  static getDerivedStateFromError() {
    return { failed: true };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error('Unhandled render error', error, info.componentStack);
  }

  render() {
    return this.state.failed ? <Fallback onRetry={() => this.setState({ failed: false })} /> : this.props.children;
  }
}
