import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent, type ReactNode } from 'react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { api } from '@/lib/api'
import { useSessionChanged, type Me } from '@/lib/auth'

type Mode = 'login' | 'signup'

// Visas i stället för appen när ingen är inloggad.
export function AuthPage() {
  const [mode, setMode] = useState<Mode>('login')
  const [checkEmail, setCheckEmail] = useState<string | null>(null)
  const sessionChanged = useSessionChanged()
  // Bekräftelselänken (/api/auth/confirm) skickar hit med ?confirm=failed när länken är ogiltig eller använd
  const confirmFailed = new URLSearchParams(window.location.search).get('confirm') === 'failed'

  const submit = useMutation({
    mutationFn: async (form: FormData) => {
      const credentials = { email: form.get('email'), password: form.get('password') }
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
        <Button variant="outline" className="mt-6 w-full" onClick={() => setCheckEmail(null)}>
          Tillbaka till inloggningen
        </Button>
      </AuthShell>
    )

  return (
    <AuthShell title={mode === 'login' ? 'Logga in' : 'Skapa konto'}>
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
        {submit.error && (
          <p role="alert" className="text-sm text-destructive">
            {submit.error.message}
          </p>
        )}
        <Button type="submit" className="w-full" disabled={submit.isPending}>
          {mode === 'login' ? 'Logga in' : 'Skapa konto'}
        </Button>
      </form>
      <Button
        variant="link"
        className="mt-4 w-full"
        onClick={() => {
          setMode(mode === 'login' ? 'signup' : 'login')
          submit.reset()
        }}
      >
        {mode === 'login' ? 'Inget konto? Skapa ett' : 'Har du redan ett konto? Logga in'}
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
