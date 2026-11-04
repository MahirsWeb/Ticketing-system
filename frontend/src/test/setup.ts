import '@testing-library/jest-dom/vitest';
import { afterEach } from 'vitest';
import { cleanup } from '@testing-library/react';

// Every test starts from an empty page and an empty browser storage.
afterEach(() => {
  cleanup();
  localStorage.clear();
  sessionStorage.clear();
});
