import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent, type ReactNode } from 'react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { api } from '@/lib/api'
import { useSessionChanged, type Me } from '@/lib/auth'

type Mode = 'login' | 'signup' | 'recover'

// Visas i stället för appen när ingen är inloggad.
export function AuthPage() {
  const resetFailed = new URLSearchParams(window.location.search).get('reset') === 'failed'
  const [mode, setMode] = useState<Mode>(resetFailed ? 'recover' : 'login')
  const [checkEmail, setCheckEmail] = useState<string | null>(null)
  const sessionChanged = useSessionChanged()
  // Bekräftelselänken (/api/auth/confirm) skickar hit med ?confirm=failed när länken är ogiltig eller använd
  const confirmFailed = new URLSearchParams(window.location.search).get('confirm') === 'failed'

  const submit = useMutation({
    mutationFn: async (form: FormData) => {
      const credentials = { email: form.get('email'), password: form.get('password') }
      if (mode === 'recover')
        return api.send<{ message: string }>('POST', '/api/auth/recover', {
          email: credentials.email,
        })
      if (mode === 'login') return api.send('POST', '/api/auth/login', credentials)
      return api.send<{ message: string } | undefined>('POST', '/api/auth/signup', credentials)
    },
    onSuccess: async (result) => {
      // Registrering med e-postbekräftelse svarar 202 med ett meddelande; annars är vi inloggade
      if (result) setCheckEmail(result.message)
      else await sessionChanged()
    },
  })

  function onSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    submit.mutate(new FormData(e.currentTarget))
  }

  if (checkEmail)
    return (
      <AuthShell title="Kolla din e-post">
        <p className="text-muted-foreground">{checkEmail}</p>
        <Button
          variant="outline"
          className="mt-6 w-full"
          onClick={() => {
            if (resetFailed) {
              window.location.assign('/')
              return
            }
            setCheckEmail(null)
            setMode('login')
            submit.reset()
          }}
        >
          Tillbaka till inloggningen
        </Button>
      </AuthShell>
    )

  return (
    <AuthShell
      title={
        mode === 'login' ? 'Logga in' : mode === 'signup' ? 'Skapa konto' : 'Återställ lösenord'
      }
    >
      {resetFailed && mode === 'recover' && (
        <p role="alert" className="mb-4 text-sm text-destructive">
          Återställningslänken är ogiltig eller redan använd. Begär en ny länk.
        </p>
      )}
      {confirmFailed && !submit.error && (
        <p role="alert" className="mb-4 text-sm text-destructive">
          Bekräftelselänken är ogiltig eller redan använd. Logga in eller skapa kontot igen.
        </p>
      )}
      <form onSubmit={onSubmit} className="space-y-4">
        <div className="space-y-2">
          <Label htmlFor="email">E-post</Label>
          <Input id="email" name="email" type="email" autoComplete="email" required />
        </div>
        {mode !== 'recover' && (
          <div className="space-y-2">
            <Label htmlFor="password">Lösenord</Label>
            <Input
              id="password"
              name="password"
              type="password"
              autoComplete={mode === 'login' ? 'current-password' : 'new-password'}
              minLength={mode === 'signup' ? 8 : undefined}
              required
            />
          </div>
        )}
        {submit.error && (
          <p role="alert" className="text-sm text-destructive">
            {submit.error.message}
          </p>
        )}
        <Button type="submit" className="w-full" disabled={submit.isPending}>
          {submit.isPending
            ? 'Vänta…'
            : mode === 'login'
              ? 'Logga in'
              : mode === 'signup'
                ? 'Skapa konto'
                : 'Skicka återställningslänk'}
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
          Glömt lösenord?
        </Button>
      )}
      <Button
        variant="link"
        className="mt-4 w-full"
        onClick={() => {
          if (resetFailed) {
            window.location.assign('/')
            return
          }
          setMode(mode === 'login' ? 'signup' : 'login')
          submit.reset()
        }}
      >
        {mode === 'login' ? 'Inget konto? Skapa ett' : 'Tillbaka till inloggningen'}
      </Button>
    </AuthShell>
  )
}

export function ResetPasswordPage() {
  const sessionChanged = useSessionChanged()
  const save = useMutation({
    mutationFn: (password: string) => api.send('POST', '/api/auth/reset-password', { password }),
    onSuccess: () => sessionChanged(),
  })

  if (save.isSuccess)
    return (
      <AuthShell title="Lösenordet är ändrat">
        <p className="text-muted-foreground">
          Logga in med ditt nya lösenord. Dina tidigare sessioner har avslutats.
        </p>
        <Button asChild className="mt-6 w-full">
          <a href="/">Till inloggningen</a>
        </Button>
      </AuthShell>
    )

  return (
    <AuthShell title="Välj nytt lösenord">
      <form
        className="space-y-4"
        onSubmit={(e) => {
          e.preventDefault()
          save.mutate(String(new FormData(e.currentTarget).get('password')))
        }}
      >
        <div className="space-y-2">
          <Label htmlFor="new-password">Nytt lösenord</Label>
          <Input
            id="new-password"
            name="password"
            type="password"
            autoComplete="new-password"
            minLength={8}
            required
          />
          <p className="text-sm text-muted-foreground">Använd minst 8 tecken.</p>
        </div>
        {save.error && (
          <p role="alert" className="text-sm text-destructive">
            {save.error.message}
          </p>
        )}
        <Button type="submit" className="w-full" disabled={save.isPending}>
          {save.isPending ? 'Sparar…' : 'Spara nytt lösenord'}
        </Button>
      </form>
      <Button asChild variant="link" className="mt-4 w-full">
        <a href="/?reset=failed">Begär en ny återställningslänk</a>
      </Button>
    </AuthShell>
  )
}

// Första inloggningen: profilen (users-raden) skapas när användaren väljer ett namn.
export function ChooseNamePage() {
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
    <AuthShell title="Välkommen till Kuta">
      <form onSubmit={onSubmit} className="space-y-4">
        <div className="space-y-2">
          <Label htmlFor="displayName">Vad vill du kallas?</Label>
          <Input id="displayName" name="displayName" autoComplete="name" maxLength={100} required />
        </div>
        {save.error && (
          <p role="alert" className="text-sm text-destructive">
            {save.error.message}
          </p>
        )}
        <Button type="submit" className="w-full" disabled={save.isPending}>
          Fortsätt
        </Button>
      </form>
    </AuthShell>
  )
}

function AuthShell({ title, children }: { title: string; children: ReactNode }) {
  return (
    <main className="mx-auto flex min-h-dvh max-w-sm flex-col justify-center px-4 py-12">
      <p className="text-sm font-semibold uppercase tracking-widest text-primary">Kuta</p>
      <h1 className="mb-6 mt-2 text-2xl font-semibold tracking-tight">{title}</h1>
      {children}
    </main>
  )
}
