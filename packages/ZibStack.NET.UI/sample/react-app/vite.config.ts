import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      // Proxy API calls to the .NET SampleApi backend
      '/api': 'http://localhost:5000',
    },
  },
});
