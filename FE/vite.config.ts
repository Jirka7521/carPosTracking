import { defineConfig } from 'vite';
import plugin from '@vitejs/plugin-react';

// https://vitejs.dev/config/
export default defineConfig({
    plugins: [plugin()],
    // Relative asset URLs, so the built index.html asks for ./assets/… instead
    // of /assets/…. Combined with the <base href> that index.html carries (and
    // that nginx rewrites to whatever prefix the request arrived under), one
    // build serves correctly both at the site root and under a path prefix such
    // as the tunnel's /carPosFE. An absolute base would hard-code the root and
    // every asset would 404 behind the prefix.
    //
    // Vite serves the dev server from / regardless, so this changes nothing
    // locally.
    base: './',
    server: {
        port: 61074,
        strictPort: true,
        proxy: {
            // Mirrors what nginx does in the container: the API is served from
            // the same origin as the app, under /api. That is not merely a
            // convenience — the session lives in a SameSite=Strict cookie, so a
            // cross-origin dev setup would behave differently from production
            // in exactly the area that is hardest to debug.
            //
            // The target is the "http" launch profile in
            // API/CarPosAPI/Properties/launchSettings.json.
            '/api': {
                target: 'http://localhost:5135',
                // Keep the browser's Host header: the API's cookies are
                // host-only, and rewriting the origin would make them land on
                // the wrong name.
                changeOrigin: false,
            },
        },
    },
})
