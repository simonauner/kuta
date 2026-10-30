import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useId, type ChangeEvent } from 'react'
import { Label } from '@/components/ui/label'
import { api } from '@/lib/api'
import { meQuery, type Me } from '@/lib/auth'

// Puls är hälsodata: sparas bara med uttryckligt samtycke, som går att återkalla (då raderas all sparad puls).
export function HeartRateConsent({ className }: { className?: string }) {
  const queryClient = useQueryClient()
  const { data: me } = useQuery(meQuery)
  const id = useId()

  const change = useMutation({
    mutationFn: (consent: boolean) =>
      api.send<Me>(consent ? 'PUT' : 'DELETE', '/api/me/hr-consent'),
    onSuccess: async (updated) => {
      queryClient.setQueryData(['me'], updated)
      await queryClient.invalidateQueries({ queryKey: ['activities'] })
    },
  })

  function onChange(e: ChangeEvent<HTMLInputElement>) {
    const consent = e.currentTarget.checked
    if (
      !consent &&
      !window.confirm('All puls som redan sparats från dina pass raderas. Fortsätta?')
    )
      return
    change.mutate(consent)
  }

  return (
    <div className={`rounded-lg border p-4 ${className ?? ''}`}>
      <div className="flex items-start gap-3">
        <input
          id={id}
          type="checkbox"
          className="mt-1 size-4 accent-primary"
          checked={me?.hrConsent ?? false}
          disabled={change.isPending}
          onChange={onChange}
        />
        <Label htmlFor={id} className="block font-normal leading-relaxed">
          <span className="font-medium">Spara puls från mina pass.</span>{' '}
          <span className="text-muted-foreground">
            Puls är hälsodata. Utan ditt samtycke sparar Kuta passen utan puls och behåller inte
            originalfilen. Stänger du av det raderas all sparad puls.
          </span>
        </Label>
      </div>
      {change.error && (
        <p role="alert" className="mt-2 text-sm text-destructive">
          {change.error.message}
        </p>
      )}
    </div>
  )
}
