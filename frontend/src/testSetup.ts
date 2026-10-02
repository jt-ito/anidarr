// minimal stand-in for the globals the app sets up in index.ejs
window.Sonarr = {
  apiKey: 'test',
  apiRoot: '',
  instanceName: 'Test',
  theme: 'light',
  urlBase: '',
  version: '0.0.0',
  isProduction: false,
};

window.matchMedia =
  window.matchMedia ||
  (((q: string) => ({
    matches: false,
    media: q,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
    addListener: () => undefined,
    removeListener: () => undefined,
  })) as never);
