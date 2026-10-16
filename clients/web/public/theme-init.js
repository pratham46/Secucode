// Runs before first paint so a saved theme never flashes. External (not inline) because of the CSP.
try {
  var t = localStorage.getItem('secucode.theme');
  if (t === 'light' || t === 'dark') document.documentElement.dataset.theme = t;
} catch (e) { /* storage blocked: follow the system */ }
