// ============================================================
// DeviceAddress — "📍 D1 · Průhonice": where the device's last fix was, in
// words, under its name on the Home card and in the device header.
//
// The lookup and its rationing live in hooks/useDeviceAddress.ts. This renders
// nothing until there is an answer — without a standing maps consent, without
// a position, or when Google has none — so no empty line ever shows.
// ============================================================

import { useTranslation } from 'react-i18next'
import { useDeviceAddress } from '../hooks/useDeviceAddress'
import type { DeviceAddressText } from '../utils/addressLabel'
import type { DeviceDto } from '../services/apiTypes'

type DeviceAddressProps = {
  device: DeviceDto
  // The viewer's standing maps consent, read once by the page
  // (useAccountMapsConsent().isStanding) rather than once per card.
  enabled: boolean
  className: string
}

export function DeviceAddress({ device, enabled, className }: DeviceAddressProps) {
  const { t } = useTranslation('device')
  const address: DeviceAddressText | null = useDeviceAddress(device.deviceId, device.lastPosition, enabled)

  if (address === null) {
    return null
  }
  return (
    <div className={className} title={t('address.tooltip', { address: address.full })}>
      <span aria-hidden="true">📍 </span>
      {address.label}
    </div>
  )
}
