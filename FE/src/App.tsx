// ============================================================
// App — top-level router for the Car Position Tracker.
//
// URL structure:
//   /login            — sign in (public)
//   /register         — create account (public)
//   /privacy          — privacy policy (public, readable while signed in too)
//   /legal            — legal notice / imprint (public, same)
//   /share/:token     — a temporary share link, opened with a code (public)
//   /share            — the same page after the token is taken out of the URL
//   /home             — device list (protected)
//   /device/:deviceId — device shell with four sub-tabs:
//     /map            — live map with auto-refresh
//     /positions      — paginated GPS position list
//     /charts         — telemetry series plotted over time
//     /settings       — device info, sharing, delete
//
// Auth guard: <RequireAuth> redirects unauthenticated users to
// /login, storing the intended destination in location state so
// the login page can bounce them back after a successful sign-in.
// ============================================================

import { useEffect } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import { I18nextProvider, useTranslation } from 'react-i18next'
import './App.css'
import i18n from './i18n'
import { AuthProvider } from './auth/AuthContext'
import { useAuth } from './auth/useAuth'
import { RequireAuth } from './auth/RequireAuth'
import { AppLayout } from './components/AppLayout'
import { SessionLoading } from './components/SessionLoading'
import { LoginPage } from './pages/LoginPage'
import { RegisterPage } from './pages/RegisterPage'
import { PrivacyPage } from './pages/PrivacyPage'
import { TermsPage } from './pages/TermsPage'
import { SharePage } from './pages/SharePage'
import { HomePage } from './pages/HomePage'
import { ProfilePage } from './pages/ProfilePage'
import { DevicePage } from './pages/DevicePage'
import { DeviceMapTab } from './pages/DeviceMapTab'
import { PositionListTab } from './pages/PositionListTab'
import { DeviceChartsTab } from './pages/DeviceChartsTab'
import { DeviceSettingsTab } from './pages/DeviceSettingsTab'

// Redirects the root path based on authentication state.
// Authenticated users go to /home; guests go to /login.
//
// While the session probe is still running there is no correct destination yet,
// so it waits rather than guessing — guessing wrong means a redirect the user
// then has to undo.
function RootRedirect() {
  const { status } = useAuth()

  if (status === 'loading') {
    return <SessionLoading />
  }

  return <Navigate to={status === 'authenticated' ? '/home' : '/login'} replace />
}

function AppRoutes() {
  const { status, isAuthenticated } = useAuth()

  // The public routes bounce signed-in users to /home. Same reasoning as above:
  // until the probe answers, rendering the login form would let someone start
  // typing credentials they turn out not to need.
  if (status === 'loading') {
    return <SessionLoading />
  }

  return (
    <Routes>
      {/* Root — redirect based on whether the user is signed in */}
      <Route path="/" element={<RootRedirect />} />

      {/*
       * Public routes — if the user is already authenticated these
       * redirect directly to the home page so they don't see login/register.
       */}
      <Route
        path="/login"
        element={isAuthenticated ? <Navigate to="/home" replace /> : <LoginPage />}
      />
      <Route
        path="/register"
        element={isAuthenticated ? <Navigate to="/home" replace /> : <RegisterPage />}
      />

      {/*
       * Privacy policy and terms of use. Public like the two above, but WITHOUT
       * the redirect: somebody who is already signed in still has to be able to
       * read what happens to their data, and bouncing them to /home would make
       * the footer links dead for exactly the people whose data it is. They also
       * sit outside <RequireAuth> on purpose — a visitor deciding whether to
       * register has to be able to read the policy before they have an account.
       */}
      <Route path="/privacy" element={<PrivacyPage />} />
      <Route path="/legal" element={<TermsPage />} />

      {/*
       * Temporary share links. Public, and without the signed-in redirect for a
       * sharper reason than the legal pages have: the person opening one usually
       * has no account at all, and an owner checking a link they created must
       * see exactly what the recipient sees rather than their own dashboard.
       *
       * Two paths, one page. The first carries the link secret and is what a
       * recipient clicks; SharePage reads it once and then replaces the address
       * with the second, so the token stops appearing in the address bar, in
       * screenshots and in the visible history entry. The bare path is therefore
       * also where a reload lands, and it works because the share session is an
       * HttpOnly cookie — nothing has to be stored in the token's place.
       */}
      <Route path="/share/:token" element={<SharePage />} />
      <Route path="/share" element={<SharePage />} />

      {/*
       * Protected routes — all share the <AppLayout> shell which renders
       * the sticky top navigation bar and an <Outlet /> for page content.
       * <RequireAuth> redirects to /login if the token is absent.
       */}
      <Route
        element={
          <RequireAuth>
            <AppLayout />
          </RequireAuth>
        }
      >
        {/* Home page: list of devices + add-device form */}
        <Route path="/home" element={<HomePage />} />

        {/* Profile: edit name and change password */}
        <Route path="/profile" element={<ProfilePage />} />

        {/*
         * Device shell: loads the device and renders the tab bar.
         * Sub-routes are the four tabs. The index sub-route redirects
         * /device/:id straight to /device/:id/map so links don't land
         * on a blank page.
         */}
        <Route path="/device/:deviceId" element={<DevicePage />}>
          <Route index element={<Navigate to="map" replace />} />
          <Route path="map"       element={<DeviceMapTab />} />
          <Route path="positions" element={<PositionListTab />} />
          <Route path="charts"    element={<DeviceChartsTab />} />
          <Route path="settings"  element={<DeviceSettingsTab />} />
        </Route>
      </Route>

      {/* Catch-all: anything else bounces to the root redirect above */}
      <Route path="*" element={<RootRedirect />} />
    </Routes>
  )
}

// Keeps the two pieces of chrome that live OUTSIDE the React tree in step with
// the chosen language: the document's lang attribute — which is what a screen
// reader picks its voice from and what the browser offers to translate against
// — and the browser tab's title. index.html ships lang="en" and an English
// <title> as the pre-hydration default; this replaces both once i18next has
// settled on a language.
//
// The title is `documentTitle`, not `appTitle`: it carries the "non-commercial
// test project" qualifier, so the browser tab and any bookmark say what this is
// without anyone having to open it. index.html says the same thing before
// hydration, and this must not quietly undo that.
function useDocumentLanguage(): void {
  const { t, i18n: instance } = useTranslation('common')
  const language: string = instance.resolvedLanguage ?? instance.language

  useEffect(() => {
    document.documentElement.lang = language
    document.title = t('documentTitle')
  }, [language, t])
}

function AppChrome() {
  useDocumentLanguage()
  return <AppRoutes />
}

function App() {
  return (
    // I18nextProvider is what useTranslation() reads the instance from. It sits
    // outside AuthProvider because the auth layer's own messages need it too.
    <I18nextProvider i18n={i18n}>
      {/* AuthProvider stores the current user profile and exposes
          login / register / logout helpers to all descendant components. */}
      <AuthProvider>
        <AppChrome />
      </AuthProvider>
    </I18nextProvider>
  )
}

export default App
