import { Component, type ErrorInfo, type ReactNode } from 'react';
import { useLanguage } from '../i18n/LanguageContext';
import { Button } from './ui';

function Fallback({ onRetry, inline }: { onRetry: () => void; inline: boolean }) {
  const { t } = useLanguage();
  return (
    <div role="alert" className={inline ? 'flex items-center justify-center px-4 py-12' : 'flex min-h-screen items-center justify-center bg-slate-200 px-4'}>
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
 *
 * `inline` is for the page area of the layout: the message takes the place of that one screen only, so the header and the
 * navigation stay and the person can simply go somewhere else. `resetKey` (the address) clears a failure when they do.
 */
type Props = { children: ReactNode; inline?: boolean; resetKey?: string };
type State = { failed: boolean; resetKey?: string };

export class ErrorBoundary extends Component<Props, State> {
  state: State = { failed: false, resetKey: this.props.resetKey };

  static getDerivedStateFromProps(props: Props, state: State): Partial<State> | null {
    return props.resetKey !== state.resetKey ? { failed: false, resetKey: props.resetKey } : null;
  }

  static getDerivedStateFromError(): Partial<State> {
    return { failed: true };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error('Unhandled render error', error, info.componentStack);
  }

  render() {
    return this.state.failed ? <Fallback inline={this.props.inline ?? false} onRetry={() => this.setState({ failed: false })} /> : this.props.children;
  }
}
