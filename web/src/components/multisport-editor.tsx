import { lazy, Suspense, useId, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { AnimatePresence, motion, useReducedMotion } from 'motion/react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Modal } from '@/components/ui/modal'
import { formatDuration, type ActivityDetail } from '@/lib/activity'
import {
  multisportKinds,
  partSports,
  partStreams,
  sportNameKey,
  sportColors,
  type ActivityPart,
  type MultisportDraft,
} from '@/lib/multisport'

const MultisportPreview = lazy(() => import('./multisport-preview'))

export function MultisportEditor({
  activity: a,
  onPreview,
  onChange,
  disabled = false,
}: {
  activity: Pick<
    ActivityDetail,
    'sport' | 'parts' | 'partsEdited' | 'canSplit' | 'streams' | 'originalParts' | 'originalSport'
  >
  onPreview?: (part: ActivityPart | null) => void
  onChange?: (draft: MultisportDraft) => void
  disabled?: boolean
}) {
  const { t, i18n } = useTranslation()
  const restoreDialog = useRef<HTMLDialogElement>(null)
  const restoreTitleId = useId()
  const restoreWarningId = useId()
  const [editing, setEditing] = useState(false)
  const [kind, setKind] = useState<string>(multisportKinds.find((k) => k === a.sport) ?? 'swimrun')
  const [parts, setParts] = useState<ActivityPart[]>(a.parts)
  const [partKeys, setPartKeys] = useState(() => a.parts.map((_, i) => i))
  const nextPartKey = useRef(Math.max(2, a.parts.length))
  const reduceMotion = useReducedMotion()
  const [selected, setSelected] = useState<number | null>(null)
  const number = new Intl.NumberFormat(i18n.resolvedLanguage, { maximumFractionDigits: 2 })
  const shown = editing ? parts : a.parts
  function preview(part: ActivityPart | null) {
    setSelected(part?.partIndex ?? null)
    onPreview?.(part)
  }
  function update(next: ActivityPart[]) {
    const measured = next.map((p, i) => {
      const stream = a.streams && p.endS > p.startS ? partStreams(a.streams, p) : null
      return { ...p, partIndex: i, distanceM: stream?.dist?.at(-1) ?? 0 }
    })
    setParts(measured)
    onChange?.({ kind, parts: measured })
    if (selected !== null) onPreview?.(measured[selected] ?? null)
  }
  function begin() {
    const from = a.streams!.t[0],
      until = a.streams!.t.at(-1)!
    const middle = Math.floor((from + until) / 2)
    setEditing(true)
    setPartKeys(Array.from({ length: a.parts.length || 2 }, (_, i) => i))
    update(
      a.parts.length
        ? a.parts
        : [
            {
              partIndex: 0,
              sport: 'running',
              startS: from,
              endS: middle,
              distanceM: 0,
              useEquipment: true,
            },
            {
              partIndex: 1,
              sport: 'swimming',
              startS: middle,
              endS: until,
              distanceM: 0,
              useEquipment: true,
            },
          ],
    )
  }
  if (!a.parts.length && !onChange) return null
  if (!a.canSplit && !a.parts.length) return null
  return (
    <section className="mt-6 space-y-3 rounded-lg border p-4" aria-label={t('multisport.title')}>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="font-semibold">{t('multisport.title')}</h2>
        {onChange && !editing && a.canSplit && (
          <Button type="button" size="sm" variant="outline" onClick={begin}>
            {t(a.parts.length ? 'multisport.edit' : 'multisport.split')}
          </Button>
        )}
      </div>
      {editing && (
        <>
          <p className="text-sm text-muted-foreground">{t('multisport.instructions')}</p>
          <label className="flex items-center gap-3 text-sm">
            {t('multisport.kind')}
            <select
              className="rounded-md border bg-background p-2"
              value={kind}
              disabled={disabled}
              onChange={(e) => {
                setKind(e.target.value)
                onChange?.({ kind: e.target.value, parts })
              }}
            >
              {multisportKinds.map((k) => (
                <option key={k} value={k}>
                  {t(sportNameKey(k))}
                </option>
              ))}
            </select>
          </label>
        </>
      )}
      {shown.length > 0 && (
        <Button
          type="button"
          size="sm"
          variant={selected === null ? 'secondary' : 'ghost'}
          onClick={() => preview(null)}
        >
          {t('multisport.whole')}
        </Button>
      )}
      <fieldset disabled={disabled} className="space-y-3">
        <AnimatePresence initial={false}>
          {shown.map((p, i) => (
            <motion.div
              key={partKeys[i] ?? i}
              initial={reduceMotion ? false : { height: 0, opacity: 0, overflow: 'hidden' }}
              animate={{ height: 'auto', opacity: 1, transitionEnd: { overflow: 'visible' } }}
              exit={{ height: 0, opacity: 0, overflow: 'hidden' }}
              transition={{ duration: reduceMotion ? 0 : 0.25, ease: 'easeOut' }}
            >
              <div
                className={`relative cursor-pointer space-y-2 rounded-md border p-3 ${selected === i ? 'bg-secondary' : 'hover:bg-accent/50'}`}
              >
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <Button
                    type="button"
                    size="sm"
                    variant={selected === i ? 'secondary' : 'ghost'}
                    aria-pressed={selected === i}
                    className="h-auto justify-start whitespace-normal text-left after:absolute after:inset-0 after:rounded-md after:content-[''] focus-visible:after:ring-2 focus-visible:after:ring-ring"
                    onClick={() => preview(p)}
                  >
                    <span aria-hidden="true" style={{ color: sportColors[p.sport] ?? '#ae3ec9' }}>
                      ●{' '}
                    </span>
                    {i + 1}. {t(sportNameKey(p.sport))} · {number.format(p.distanceM / 1000)} km ·{' '}
                    {formatDuration(p.endS - p.startS)}
                  </Button>
                  {editing && (
                    <select
                      aria-label={t('multisport.partSport', { number: i + 1 })}
                      className="relative z-10 rounded-md border bg-background p-2 text-sm"
                      value={p.sport}
                      onChange={(e) =>
                        update(parts.map((v, j) => (j === i ? { ...v, sport: e.target.value } : v)))
                      }
                    >
                      {partSports.map((s) => (
                        <option key={s} value={s}>
                          {t(sportNameKey(s))}
                        </option>
                      ))}
                    </select>
                  )}
                </div>
                <p className="text-xs text-muted-foreground">
                  {formatDuration(p.startS)} → {formatDuration(p.endS)}
                </p>
                {editing && (
                  <div className="pointer-events-none relative z-10 space-y-2 [&_button]:pointer-events-auto [&_input]:pointer-events-auto [&_label]:pointer-events-auto">
                    {i < parts.length - 1 && (
                      <label className="flex flex-wrap items-center gap-2 text-sm">
                        {t('multisport.boundary', { number: i + 1 })}
                        <Input
                          className="w-28"
                          type="number"
                          min={p.startS + 1}
                          max={parts[i + 1].endS - 1}
                          step="1"
                          required
                          key={p.endS}
                          name={`endS-${i}`}
                          defaultValue={p.endS}
                          onBlur={(e) => {
                            if (!e.currentTarget.checkValidity()) return
                            const end = Number(e.target.value)
                            if (
                              !Number.isInteger(end) ||
                              end <= p.startS ||
                              end >= parts[i + 1].endS
                            )
                              return
                            update(
                              parts.map((v, j) =>
                                j === i
                                  ? { ...v, endS: end }
                                  : j === i + 1
                                    ? { ...v, startS: end }
                                    : v,
                              ),
                            )
                          }}
                        />
                        <input
                          className="w-full"
                          style={{ accentColor: sportColors[p.sport] ?? '#ae3ec9' }}
                          type="range"
                          aria-label={t('multisport.boundary', { number: i + 1 })}
                          min={p.startS + 1}
                          max={parts[i + 1].endS - 1}
                          step="1"
                          value={p.endS}
                          onChange={(e) => {
                            const end = Number(e.target.value)
                            update(
                              parts.map((v, j) =>
                                j === i
                                  ? { ...v, endS: end }
                                  : j === i + 1
                                    ? { ...v, startS: end }
                                    : v,
                              ),
                            )
                          }}
                        />
                      </label>
                    )}
                    <label className="flex items-center gap-2 text-sm">
                      <input
                        type="checkbox"
                        checked={p.useEquipment}
                        onChange={(e) =>
                          update(
                            parts.map((v, j) =>
                              j === i ? { ...v, useEquipment: e.target.checked } : v,
                            ),
                          )
                        }
                      />
                      {t('multisport.useEquipment')}
                    </label>
                    <div className="flex flex-wrap gap-2">
                      <Button
                        type="button"
                        size="sm"
                        variant="ghost"
                        disabled={p.endS - p.startS < 2 || parts.length >= 200}
                        onClick={() => {
                          const middle = Math.floor((p.startS + p.endS) / 2)
                          const sport =
                            kind === 'swimrun'
                              ? p.sport === 'running'
                                ? 'swimming'
                                : 'running'
                              : 'transition'
                          setPartKeys([
                            ...partKeys.slice(0, i + 1),
                            nextPartKey.current++,
                            ...partKeys.slice(i + 1),
                          ])
                          update([
                            ...parts.slice(0, i),
                            { ...p, endS: middle },
                            { ...p, sport, startS: middle },
                            ...parts.slice(i + 1),
                          ])
                        }}
                      >
                        {t('multisport.addBoundary')}
                      </Button>
                      {i > 0 && parts.length > 2 && (
                        <Button
                          type="button"
                          size="sm"
                          variant="ghost"
                          onClick={() => {
                            setPartKeys(partKeys.filter((_, j) => j !== i))
                            update(
                              parts
                                .filter((_, j) => j !== i)
                                .map((v, j) => (j === i - 1 ? { ...v, endS: p.endS } : v)),
                            )
                          }}
                        >
                          {t('multisport.removeBoundary')}
                        </Button>
                      )}
                    </div>
                  </div>
                )}
              </div>
            </motion.div>
          ))}
        </AnimatePresence>
      </fieldset>
      {onChange && a.partsEdited && !editing && (
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={disabled}
          onClick={() => {
            restoreDialog.current?.showModal()
          }}
        >
          {t('multisport.restore')}
        </Button>
      )}
      <Modal
        ref={restoreDialog}
        aria-labelledby={restoreTitleId}
        aria-describedby={restoreWarningId}
        onCancel={(event) => {
          if (disabled) event.preventDefault()
        }}
      >
        <h2 id={restoreTitleId} className="text-xl font-semibold">
          {t('multisport.restore')}
        </h2>
        <p id={restoreWarningId} className="mt-3 text-sm text-muted-foreground">
          {t('multisport.confirmRestore')}
        </p>
        <div className="mt-6 flex justify-end gap-3">
          <Button
            type="button"
            variant="outline"
            autoFocus
            disabled={disabled}
            onClick={() => restoreDialog.current?.close()}
          >
            {t('multisport.cancel')}
          </Button>
          <Button
            type="button"
            disabled={disabled}
            onClick={() => {
              setEditing(false)
              preview(null)
              onChange?.(null)
              restoreDialog.current?.close()
            }}
          >
            {t('multisport.restoreConfirm')}
          </Button>
        </div>
      </Modal>
      {onChange && editing && a.streams && (
        <Suspense fallback={null}>
          <MultisportPreview streams={a.streams} parts={shown} selected={selected} />
        </Suspense>
      )}
    </section>
  )
}
