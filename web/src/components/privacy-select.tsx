import type { ComponentProps } from 'react'
import { useTranslation } from 'react-i18next'
import { Select } from '@/components/ui/select'
import { privacies, type Privacy } from '@/lib/privacy'

const labelKeys = {
  private: 'social.private',
  followers: 'social.followersVisibility',
  public: 'social.public',
} as const

export function usePrivacyLabel() {
  const { t } = useTranslation()
  return (privacy: Privacy) => t(labelKeys[privacy])
}

// The visibility choices for activities, goals and the supporter badge.
export function PrivacySelect({
  value,
  onChange,
  ...props
}: Omit<ComponentProps<typeof Select>, 'value' | 'onChange' | 'children'> & {
  value: Privacy
  onChange: (privacy: Privacy) => void
}) {
  const label = usePrivacyLabel()
  return (
    <Select {...props} value={value} onChange={(event) => onChange(event.target.value as Privacy)}>
      {privacies.map((privacy) => (
        <option key={privacy} value={privacy}>
          {label(privacy)}
        </option>
      ))}
    </Select>
  )
}
