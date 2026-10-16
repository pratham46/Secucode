import { Component, type ReactNode } from 'react';

// One broken page shouldn't take the shell down with it.
export class PageErrorBoundary extends Component<{ children: ReactNode }, { failed: boolean }> {
  state = { failed: false };

  static getDerivedStateFromError() {
    return { failed: true };
  }

  componentDidCatch(error: unknown) {
    console.error('Page crashed', error);
  }

  render() {
    if (!this.state.failed) return this.props.children;
    return (
      <div className="infobar infobar-error" role="alert">
        <div><strong>Something went wrong.</strong> Reload the page to try again.</div>
        <button type="button" className="btn btn-subtle" onClick={() => location.reload()}>Reload</button>
      </div>
    );
  }
}
