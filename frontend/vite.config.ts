import {defineConfig, type Plugin} from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import path from 'path'
import http from 'http'

const backendUrl = process.env.VITE_API_URL || 'http://localhost:5002'

// Proxy non-GET requests (PUT uploads, POST) to the backend.
// Uploads use PUT /{filename} which can't be matched by path prefix.
function backendProxy(): Plugin {
    return {
        name: 'backend-proxy',
        configureServer(server) {
            server.middlewares.use((req, res, next) => {
                if (req.method === 'GET' || req.method === 'HEAD') return next()

                const proxyReq = http.request(`${backendUrl}${req.url}`, {
                    method: req.method,
                    headers: req.headers,
                }, (proxyRes) => {
                    res.writeHead(proxyRes.statusCode!, proxyRes.headers)
                    proxyRes.pipe(res)
                })

                proxyReq.on('error', (err) => {
                    console.error('Backend proxy error:', err.message)
                    res.writeHead(502)
                    res.end('Backend unavailable')
                })

                req.pipe(proxyReq)
            })
        },
    }
}

export default defineConfig({
    plugins: [backendProxy(), react(), tailwindcss()],
    resolve: {
        alias: {
            '@': path.resolve(__dirname, './src'),
        },
    },
    server: {
        port: 3002,
        host: '0.0.0.0',
        proxy: {
            '/api': {
                target: backendUrl,
                changeOrigin: true,
            },
            '/health': {
                target: backendUrl,
                changeOrigin: true,
            },
            '/SKILL.md': {
                target: backendUrl,
                changeOrigin: false,
            },
        },
    },
    build: {
        outDir: 'build',
        sourcemap: true,
    }
})
