// ============================================================
// App — top-level router for the Car Position Tracker.
//
// URL structure:
//   /login            — sign in (public)
//   /register         — create account (public)
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

import { Navigate, Route, Routes } from 'react-router-dom'
import './App.css'
import { AuthProvider } from './auth/AuthContext'
import { useAuth } from './auth/useAuth'
import { RequireAuth } from './auth/RequireAuth'
import { AppLayout } from './components/AppLayout'
import { SessionLoading } from './components/SessionLoading'
import { LoginPage } from './pages/LoginPage'
import { RegisterPage } from './pages/RegisterPage'
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

function App() {
  return (
    // AuthProvider stores the JWT token + user profile and exposes
    // login / register / logout helpers to all descendant components.
    <AuthProvider>
      <AppRoutes />
    </AuthProvider>
  )
}

export default App
