// ============================================================
// ProfilePage — lets the authenticated user edit their own account.
//
// Two independent sections:
//
//   1. Personal Information
//      Edit first name and last name. Changes are saved to the API and
//      immediately reflected in the header (via updateCurrentUser) without
//      requiring a page reload.
//
//   2. Change Password
//      Requires the current password as proof of identity — a stolen session
//      cookie alone cannot be used to lock the account owner out. The new
//      password must be at least 12 characters, matching the registration rule.
//
// The email address is shown read-only; changing it is not supported because
// it would require a verification round-trip that is out of scope here.
// ============================================================

import { useAuth } from '../auth/AuthContext'
import { PersonalInfoSection } from '../components/PersonalInfoSection'
import { ChangePasswordSection } from '../components/ChangePasswordSection'

// ============================================================
// Main component
// ============================================================

export function ProfilePage() {
  const { currentUser, updateCurrentUser } = useAuth()

  // Guard: this page is inside <RequireAuth> so currentUser should always be set,
  // but TypeScript doesn't know that — handle the edge case gracefully.
  if (!currentUser) {
    return null
  }

  return (
    <div className="page-content">
      {/* page-header is a flex row (for title + action button) — use a plain
          block here so the subtitle sits below the title, not beside it. */}
      <div style={{ marginBottom: 24 }}>
        <h2 style={{ fontSize: '1.5rem', margin: 0 }}>Profile</h2>
        <p className="hint" style={{ marginTop: 6 }}>
          Manage your personal information and account security.
        </p>
      </div>

      {/* Section 1: Personal Information */}
      <PersonalInfoSection
        userId={currentUser.id}
        initialFirstName={currentUser.firstName}
        initialLastName={currentUser.lastName}
        email={currentUser.email}
        onSaved={updateCurrentUser}
      />

      {/* Section 2: Change Password */}
      <ChangePasswordSection userId={currentUser.id} />
    </div>
  )
}

// PersonalInfoSection is defined in src/components/PersonalInfoSection.tsx
// ChangePasswordSection is defined in src/components/ChangePasswordSection.tsx
