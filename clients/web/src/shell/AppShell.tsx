import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { NavLink, Outlet, useLocation } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { PageErrorBoundary } from './PageErrorBoundary';
import './shell.css';

const NAV = [
  { to: '/exams', label: 'Exams', icon: 'M5 4h14v16H5z M9 9h6 M9 13h6 M9 17h4' },
  { to: '/settings', label: 'Settings', icon: 'M12 15.5a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7z M4 12h2 M18 12h2 M12 4v2 M12 18v2 M6.3 6.3l1.4 1.4 M16.3 16.3l1.4 1.4 M6.3 17.7l1.4-1.4 M16.3 7.7l1.4-1.4' },
];

export function AppShell() {
  const { session } = useAuth();
  const location = useLocation();
  const [collapsed, setCollapsed] = useState(() => window.matchMedia?.('(max-width: 899px)').matches ?? false);
  // Collapse/expand when the window crosses the 900px breakpoint; the button still works in between.
  useEffect(() => {
    const mq = window.matchMedia?.('(max-width: 899px)');
    if (!mq) return;
    const onChange = (e: MediaQueryListEvent) => setCollapsed(e.matches);
    mq.addEventListener('change', onChange);
    return () => mq.removeEventListener('change', onChange);
  }, []);

  const navRef = useRef<HTMLElement>(null);
  const [indicator, setIndicator] = useState<{ top: number; height: number } | null>(null);

  // The selection indicator slides to the active item (CSS transition on transform).
  useLayoutEffect(() => {
    const active = navRef.current?.querySelector<HTMLElement>('a.active');
    setIndicator(active ? { top: active.offsetTop, height: active.offsetHeight } : null);
  }, [location.pathname, collapsed]);

  return (
    <div className={`shell${collapsed ? ' collapsed' : ''}`}>
      <aside className="sidebar">
        <div className="brand">
          <button type="button" className="btn btn-subtle icon" aria-label={collapsed ? 'Expand menu' : 'Collapse menu'}
            aria-expanded={!collapsed} onClick={() => setCollapsed(c => !c)}>
            <svg width="16" height="16" viewBox="0 0 16 16" aria-hidden="true"><path d="M2 4h12M2 8h12M2 12h12" stroke="currentColor" strokeWidth="1.5" /></svg>
          </button>
          <span className="brand-name">SecuCode</span>
        </div>
        <nav ref={navRef} aria-label="Main" className="nav">
          {indicator && <span className="nav-indicator" aria-hidden="true" style={{ transform: `translateY(${indicator.top}px)`, height: indicator.height }} />}
          {NAV.map(n => (
            <NavLink key={n.to} to={n.to} className="nav-item" title={collapsed ? n.label : undefined} aria-label={collapsed ? n.label : undefined}>
              <svg className="nav-icon" width="18" height="18" viewBox="0 0 24 24" aria-hidden="true">
                <path d={n.icon} fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
              </svg>
              <span className="nav-label">{n.label}</span>
            </NavLink>
          ))}
        </nav>
        <div className="sidebar-user nav-label">{session?.user.fullName}</div>
      </aside>
      <main className="content">
        <PageErrorBoundary key={location.pathname}>
          <div className="page" key={location.pathname}>
            <Outlet />
          </div>
        </PageErrorBoundary>
      </main>
    </div>
  );
}
