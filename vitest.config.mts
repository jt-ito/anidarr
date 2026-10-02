import tsconfigPaths from 'vite-tsconfig-paths';
import { defineConfig } from 'vitest/config';

export default defineConfig({
  plugins: [tsconfigPaths({ projects: ['frontend/tsconfig.json'] })],
  test: {
    environment: 'jsdom',
    include: ['frontend/src/**/*.test.{ts,tsx}'],
    setupFiles: ['frontend/src/testSetup.ts'],
  },
});
