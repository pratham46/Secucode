import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import './theme/tokens.css';

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <p style={{ padding: 24 }}>SecuCode</p>
  </StrictMode>,
);
