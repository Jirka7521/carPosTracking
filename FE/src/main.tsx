import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import './index.css'
import App from './App.tsx'
import { BASE_PATH } from './services/runtimeConfig'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    {/*
      basename is the path prefix the app is served under — "" at the site root,
      "/carPosFE" behind the tunnel — derived from the page's <base href> rather
      than configured. Every route in App.tsx stays written as the plain "/login"
      or "/device/:id" it always was; the router adds and strips the prefix.
    */}
    <BrowserRouter basename={BASE_PATH}>
      <App />
    </BrowserRouter>
  </StrictMode>,
)
