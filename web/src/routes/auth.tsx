import { LanguageSelect } from '@/components/language-select'
import { ErrorMessage } from '@/components/error-message'
import { Trans, useTranslation } from 'react-i18next'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent, type ReactNode } from 'react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { FormField } from '@/components/ui/form-field'
import { api } from '@/lib/api'
import { useSessionChanged, type Me } from '@/lib/auth'

type Mode = 'login' | 'signup' | 'recover'

// Visas i stället för appen när ingen är inloggad.
export function AuthPage() {
  const { t, i18n } = useTranslation()
  const english = i18n.resolvedLanguage === 'en'
  const resetFailed = new URLSearchParams(window.location.search).get('reset') === 'failed'
  const [mode, setMode] = useState<Mode>(() =>
    resetFailed ? 'recover' : window.location.hash === '#signup' ? 'signup' : 'login',
  )
  const [checkEmail, setCheckEmail] = useState<'auth.confirmSent' | 'auth.recoverySent' | null>(
    null,
  )
  const sessionChanged = useSessionChanged()
  // Bekräftelselänken (/api/auth/confirm) loggar inte in: den skickar hit med ?confirm=ok, eller ?confirm=failed
  // när länken är ogiltig eller använd
  const confirm = new URLSearchParams(window.location.search).get('confirm')
  const confirmFailed = confirm === 'failed'

  const submit = useMutation({
    mutationFn: async (form: FormData) => {
      const credentials = { email: form.get('email'), password: form.get('password') }
      if (mode === 'recover')
        return api.send<{ message: string }>('POST', '/api/auth/recover', {
          email: credentials.email,
        })
      if (mode === 'login') return api.send('POST', '/api/auth/login', credentials)
      return api.send<{ message: string } | undefined>('POST', '/api/auth/signup', {
        ...credentials,
        acceptTerms: form.get('acceptTerms') === 'on',
      })
    },
    onSuccess: async (result) => {
      // Registrering med e-postbekräftelse svarar 202 med ett meddelande; annars är vi inloggade
      if (result) setCheckEmail(mode === 'recover' ? 'auth.recoverySent' : 'auth.confirmSent')
      else await sessionChanged()
    },
  })

  function onSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    submit.mutate(new FormData(e.currentTarget))
  }

  if (checkEmail)
    return (
      <AuthShell title={t('auth.checkEmail')}>
        <p className="text-muted-foreground">{t(checkEmail)}</p>
        <Button
          variant="outline"
          className="mt-6 w-full"
          onClick={() => {
            if (resetFailed) {
              window.location.assign('/app')
              return
            }
            setCheckEmail(null)
            setMode('login')
            submit.reset()
          }}
        >
          {t('auth.back')}
        </Button>
      </AuthShell>
    )

  return (
    <AuthShell
      title={
        mode === 'login'
          ? t('auth.login')
          : mode === 'signup'
            ? t('auth.signup')
            : t('auth.recover')
      }
    >
      {resetFailed && mode === 'recover' && (
        <p role="alert" className="mb-4 text-sm text-destructive">
          {t('auth.resetFailed')}
        </p>
      )}
      {confirmFailed && !submit.error && (
        <p role="alert" className="mb-4 text-sm text-destructive">
          {t('auth.confirmFailed')}
        </p>
      )}
      {confirm === 'ok' && mode === 'login' && !submit.error && (
        <p role="status" className="mb-4 text-sm">
          {t('auth.confirmed')}
        </p>
      )}
      <form onSubmit={onSubmit} className="space-y-4">
        <FormField label={t('auth.email')}>
          {(id) => <Input id={id} name="email" type="email" autoComplete="email" required />}
        </FormField>
        {mode !== 'recover' && (
          <FormField label={t('auth.password')}>
            {(id) => (
              <Input
                id={id}
                name="password"
                type="password"
                autoComplete={mode === 'login' ? 'current-password' : 'new-password'}
                minLength={mode === 'signup' ? 8 : undefined}
                required
              />
            )}
          </FormField>
        )}
        {mode === 'signup' && (
          <FormField
            layout="choice"
            className="items-start"
            labelClassName="leading-relaxed"
            label={
              <Trans
                i18nKey="auth.acceptTerms"
                components={{
                  terms: <PolicyLink href={english ? '/terms#english' : '/terms'} />,
                  privacy: <PolicyLink href={english ? '/privacy#english' : '/privacy'} />,
                }}
              />
            }
          >
            {(id) => (
              <input
                id={id}
                name="acceptTerms"
                type="checkbox"
                className="mt-1 size-4 accent-primary"
                required
              />
            )}
          </FormField>
        )}
        {submit.error && (
          <p role="alert" className="text-sm text-destructive">
            <ErrorMessage error={submit.error} />
          </p>
        )}
        <Button type="submit" className="w-full" disabled={submit.isPending}>
          {submit.isPending
            ? t('auth.wait')
            : mode === 'login'
              ? t('auth.login')
              : mode === 'signup'
                ? t('auth.signup')
                : t('auth.sendRecovery')}
        </Button>
      </form>
      {mode === 'login' && (
        <Button
          variant="link"
          className="mt-2 w-full"
          onClick={() => {
            setMode('recover')
            submit.reset()
          }}
        >
          {t('auth.forgot')}
        </Button>
      )}
      <Button
        variant="link"
        className="mt-4 w-full"
        onClick={() => {
          if (resetFailed) {
            window.location.assign('/app')
            return
          }
          setMode(mode === 'login' ? 'signup' : 'login')
          submit.reset()
        }}
      >
        {mode === 'login' ? t('auth.noAccount') : t('auth.back')}
      </Button>
    </AuthShell>
  )
}

export function ResetPasswordPage() {
  const { t } = useTranslation()
  const sessionChanged = useSessionChanged()
  const save = useMutation({
    mutationFn: (password: string) => api.send('POST', '/api/auth/reset-password', { password }),
    onSuccess: () => sessionChanged(),
  })

  if (save.isSuccess)
    return (
      <AuthShell title={t('auth.passwordChanged')}>
        <p className="text-muted-foreground">{t('auth.signInAgain')}</p>
        <Button asChild className="mt-6 w-full">
          <a href="/app">{t('auth.toLogin')}</a>
        </Button>
      </AuthShell>
    )

  return (
    <AuthShell title={t('auth.choosePassword')}>
      <form
        className="space-y-4"
        onSubmit={(e) => {
          e.preventDefault()
          save.mutate(String(new FormData(e.currentTarget).get('password')))
        }}
      >
        <div className="space-y-2">
          <FormField label={t('auth.newPassword')}>
            {(id) => (
              <Input
                id={id}
                name="password"
                type="password"
                autoComplete="new-password"
                minLength={8}
                required
              />
            )}
          </FormField>
          <p className="text-sm text-muted-foreground">{t('auth.minPassword')}</p>
        </div>
        {save.error && (
          <p role="alert" className="text-sm text-destructive">
            <ErrorMessage error={save.error} />
          </p>
        )}
        <Button type="submit" className="w-full" disabled={save.isPending}>
          {save.isPending ? t('auth.saving') : t('auth.savePassword')}
        </Button>
      </form>
      <Button asChild variant="link" className="mt-4 w-full">
        <a href="/app?reset=failed">{t('auth.newLink')}</a>
      </Button>
    </AuthShell>
  )
}

// Första inloggningen: profilen (users-raden) skapas när användaren väljer ett namn.
export function ChooseNamePage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const save = useMutation({
    mutationFn: (displayName: string) => api.send<Me>('PUT', '/api/me', { displayName }),
    onSuccess: (me) => queryClient.setQueryData(['me'], me),
  })

  function onSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    save.mutate(String(new FormData(e.currentTarget).get('displayName')))
  }

  return (
    <AuthShell title={t('auth.welcome')}>
      <form onSubmit={onSubmit} className="space-y-4">
        <FormField label={t('auth.name')}>
          {(id) => (
            <Input id={id} name="displayName" autoComplete="name" maxLength={100} required />
          )}
        </FormField>
        {save.error && (
          <p role="alert" className="text-sm text-destructive">
            <ErrorMessage error={save.error} />
          </p>
        )}
        <Button type="submit" className="w-full" disabled={save.isPending}>
          {t('auth.continue')}
        </Button>
      </form>
    </AuthShell>
  )
}

// Opens in a new tab so the signup form keeps what has been typed.
function PolicyLink({ href, children }: { href: string; children?: ReactNode }) {
  return (
    <a href={href} target="_blank" rel="noopener" className="underline underline-offset-4">
      {children}
    </a>
  )
}

function AuthShell({ title, children }: { title: string; children: ReactNode }) {
  const { t, i18n } = useTranslation()
  const english = i18n.resolvedLanguage === 'en'
  return (
    <div className="flex min-h-dvh flex-col">
      <header className="hidden px-6 pt-6 md:block">
        <LanguageSelect />
      </header>
      <main className="mx-auto flex w-full max-w-sm flex-1 flex-col justify-center px-4 py-12">
        <div className="mb-6 md:hidden">
          <LanguageSelect />
        </div>
        <a href="/" aria-label={t('home')} className="self-start">
          <picture>
            <source media="(prefers-color-scheme: dark)" srcSet="/brand/logo-light.svg" />
            <img src="/brand/logo.svg" alt="" width="144" height="48" className="h-12 w-auto" />
          </picture>
        </a>
        <h1 className="mb-6 mt-2 text-2xl font-semibold tracking-tight">{title}</h1>
        {children}
        <div className="mt-6 flex justify-center gap-4 text-sm text-muted-foreground">
          <a
            href={english ? '/privacy#english' : '/privacy'}
            className="underline underline-offset-4"
          >
            {t('footer.privacy')}
          </a>
          <a href={english ? '/terms#english' : '/terms'} className="underline underline-offset-4">
            {t('footer.terms')}
          </a>
        </div>
      </main>
    </div>
  )
}
