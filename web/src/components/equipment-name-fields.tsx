import { useTranslation } from 'react-i18next'
import { FormField } from '@/components/ui/form-field'
import { Input } from '@/components/ui/input'

export function EquipmentNameFields({
  name,
  brand,
  onNameChange,
  onBrandChange,
  nameLabel,
  brandClassName,
  disabled = false,
}: {
  name: string
  brand: string
  onNameChange: (value: string) => void
  onBrandChange: (value: string) => void
  nameLabel?: string
  brandClassName?: string
  disabled?: boolean
}) {
  const { t } = useTranslation()
  return (
    <>
      <FormField
        label={nameLabel ?? t('equipment.name')}
        className="col-span-2 text-sm font-medium"
      >
        {(id) => (
          <Input
            id={id}
            autoFocus
            required
            maxLength={100}
            disabled={disabled}
            value={name}
            onChange={(event) => onNameChange(event.currentTarget.value)}
          />
        )}
      </FormField>
      <FormField label={t('equipment.brand')} className={brandClassName}>
        {(id) => (
          <Input
            id={id}
            maxLength={100}
            disabled={disabled}
            value={brand}
            onChange={(event) => onBrandChange(event.currentTarget.value)}
          />
        )}
      </FormField>
    </>
  )
}
