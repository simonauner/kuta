import { useId, useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { Heart } from 'lucide-react'
import { MetalGradient } from '@/components/record-award'
import { ErrorMessage } from '@/components/error-message'
import { Button } from '@/components/ui/button'
import { FormField } from '@/components/ui/form-field'
import { Input } from '@/components/ui/input'
import { Modal } from '@/components/ui/modal'
import { api } from '@/lib/api'
import { meQuery, type Me, type SupporterTier } from '@/lib/auth'

// Outline in the same tone as the records' metals; the fill gets the sheen stripe.
const tierColors: Record<SupporterTier, string> = {
  bronze: 'text-[#9a5a2c]',
  silver: 'text-[#879aaf]',
  gold: 'text-[#be841d]',
}
const tiers = [
  { key: 'bronze', amount: 50 },
  { key: 'silver', amount: 150 },
  { key: 'gold', amount: 300 },
] as const

export function SupporterBadge({ tier }: { tier?: SupporterTier | null }) {
  const { t } = useTranslation()
  const gradientId = useId()
  if (!tier) return null
  const label = t(`supporter.badge.${tier}`)
  // Heart, not medal: must not be confused with the records' trophy and medal.
  return (
    <span role="img" aria-label={label} title={label} className="inline-flex shrink-0">
      <Heart
        className={`size-[1em] ${tierColors[tier]}`}
        fill={`url(#${gradientId})`}
        strokeWidth={1.6}
        aria-hidden="true"
      >
        <MetalGradient id={gradientId} metal={tier} />
      </Heart>
    </span>
  )
}

export function SupporterSection() {
  const { t, i18n } = useTranslation()
  const { data: me } = useQuery(meQuery)
  const queryClient = useQueryClient()
  const dialog = useRef<HTMLDialogElement>(null)
  const manageDialog = useRef<HTMLDialogElement>(null)
  const [manageNotice, setManageNotice] = useState<'change' | 'end' | null>(null)
  // New key on every open, so a cancelled choice doesn't linger.
  const [manageKey, setManageKey] = useState(0)
  const supporter = me?.supporter
  const save = useMutation({
    mutationFn: (privacy: Me['supporter']['badgePrivacy']) =>
      api.send<Me>('PUT', '/api/me/supporter-badge-privacy', { privacy }),
    onSuccess: async (updated) => {
      queryClient.setQueryData(meQuery.queryKey, updated)
      await Promise.all(
        ['runner', 'follows', 'feed'].map((key) =>
          queryClient.invalidateQueries({ queryKey: [key] }),
        ),
      )
    },
  })
  const date = new Intl.DateTimeFormat(i18n.resolvedLanguage, { dateStyle: 'long' })

  return (
    <section aria-labelledby="supporter-heading" className="mt-8 border-t pt-6">
      <h2 id="supporter-heading" className="text-lg font-semibold">
        {t('supporter.title')}
      </h2>
      <p className="mt-2 text-sm text-muted-foreground">{t('supporter.intro')}</p>
      {supporter?.tier && supporter.until && (
        <p className="mt-4 flex items-center gap-2 text-sm font-medium">
          <SupporterBadge tier={supporter.tier} />
          {t('supporter.active', {
            tier: t(`supporter.tiers.${supporter.tier}`),
            until: date.format(new Date(supporter.until)),
          })}
        </p>
      )}
      {supporter?.tier && supporter.until ? (
        <Button
          variant="link"
          className="mt-2"
          onClick={() => {
            setManageNotice(null)
            setManageKey((key) => key + 1)
            manageDialog.current?.showModal()
          }}
        >
          {t('supporter.manage')}
        </Button>
      ) : (
        <TierForm
          initial="silver"
          submitLabel={t('supporter.become')}
          onSubmit={() => dialog.current?.showModal()}
        />
      )}

      <FormField label={t('supporter.badgePrivacy')} className="mt-6 text-sm font-medium">
        {(id) => (
          <select
            id={id}
            className="block w-full rounded-md border bg-background px-3 py-2"
            value={supporter?.badgePrivacy ?? 'private'}
            disabled={!me || save.isPending}
            aria-describedby="supporter-badge-help"
            onChange={(event) => save.mutate(event.target.value as Me['supporter']['badgePrivacy'])}
          >
            <option value="private">{t('social.private')}</option>
            <option value="followers">{t('social.followersVisibility')}</option>
            <option value="public">{t('social.public')}</option>
          </select>
        )}
      </FormField>
      <p id="supporter-badge-help" className="mt-2 text-sm text-muted-foreground">
        {t('supporter.badgeHelp')}
      </p>
      {save.error && (
        <p role="alert" className="mt-2 text-sm text-destructive">
          <ErrorMessage error={save.error} />
        </p>
      )}

      <Modal
        ref={dialog}
        aria-labelledby="supporter-modal-title"
        aria-describedby="supporter-modal-text"
      >
        <h3 id="supporter-modal-title" className="text-lg font-semibold">
          {t('supporter.modalTitle')}
        </h3>
        <p id="supporter-modal-text" className="mt-3 text-sm text-muted-foreground">
          {t('supporter.modalText')}
        </p>
        <div className="mt-5 flex justify-end">
          <Button onClick={() => dialog.current?.close()}>{t('supporter.modalClose')}</Button>
        </div>
      </Modal>
      {supporter?.tier && supporter.until && (
        <Modal ref={manageDialog} aria-labelledby="supporter-manage-title">
          <h3 id="supporter-manage-title" className="text-lg font-semibold">
            {t('supporter.manage')}
          </h3>
          {manageNotice ? (
            <>
              <p role="status" className="mt-3 text-sm text-muted-foreground">
                {t('supporter.manageNotice', { until: date.format(new Date(supporter.until)) })}
              </p>
              <div className="mt-5 flex justify-end">
                <Button onClick={() => manageDialog.current?.close()}>
                  {t('supporter.modalClose')}
                </Button>
              </div>
            </>
          ) : (
            <>
              <TierForm
                key={manageKey}
                initial={supporter.tier}
                submitLabel={t('supporter.changeLevel')}
                onSubmit={() => setManageNotice('change')}
              />
              <div className="mt-6 border-t pt-4">
                <Button variant="secondary-outline" onClick={() => setManageNotice('end')}>
                  {t('supporter.end')}
                </Button>
                <p className="mt-2 text-sm text-muted-foreground">
                  {t('supporter.endHelp', { until: date.format(new Date(supporter.until)) })}
                </p>
              </div>
            </>
          )}
        </Modal>
      )}
    </section>
  )
}

function TierForm({
  initial,
  submitLabel,
  onSubmit,
}: {
  initial: SupporterTier
  submitLabel: string
  onSubmit: () => void
}) {
  const { t } = useTranslation()
  const [choice, setChoice] = useState<SupporterTier | 'custom'>(initial)
  const [custom, setCustom] = useState('')
  return (
    <form
      className="mt-4"
      onSubmit={(event) => {
        event.preventDefault()
        onSubmit()
      }}
    >
      <fieldset className="grid gap-2">
        <legend className="mb-2 text-sm font-medium">{t('supporter.choose')}</legend>
        {tiers.map(({ key, amount }) => (
          <FormField
            key={key}
            layout="choice"
            label={t('supporter.tierOption', { tier: t(`supporter.tiers.${key}`), amount })}
            className="text-sm"
          >
            {(id) => (
              <input
                id={id}
                type="radio"
                name="supporter-tier"
                checked={choice === key}
                onChange={() => setChoice(key)}
              />
            )}
          </FormField>
        ))}
        <FormField layout="choice" label={t('supporter.customOption')} className="text-sm">
          {(id) => (
            <input
              id={id}
              type="radio"
              name="supporter-tier"
              checked={choice === 'custom'}
              onChange={() => setChoice('custom')}
            />
          )}
        </FormField>
      </fieldset>
      {choice === 'custom' && (
        <FormField label={t('supporter.customAmount')} className="mt-3 text-sm font-medium">
          {(id) => (
            <Input
              id={id}
              type="number"
              inputMode="numeric"
              min={301}
              step={1}
              required
              value={custom}
              onChange={(event) => setCustom(event.target.value)}
              aria-describedby="supporter-custom-help"
              className="max-w-40"
            />
          )}
        </FormField>
      )}
      {choice === 'custom' && (
        <p id="supporter-custom-help" className="mt-2 text-sm text-muted-foreground">
          {t('supporter.customHelp')}
        </p>
      )}
      <Button type="submit" className="mt-4">
        {submitLabel}
      </Button>
    </form>
  )
}
