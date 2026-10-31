import { Link } from '@tanstack/react-router'
import { useRef, useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { api } from '@/lib/api'
import { shoesQuery, type Shoe } from '@/lib/shoes'
import { ErrorMessage } from '@/components/error-message'
import { Button } from '@/components/ui/button'
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from '@/components/ui/breadcrumb'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

export function EquipmentPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const shoes = useQuery(shoesQuery)
  const [name, setName] = useState('')
  const [brand, setBrand] = useState('')
  const deleteDialog = useRef<HTMLDialogElement>(null)
  const [deleting, setDeleting] = useState<Shoe | null>(null)
  const refresh = () => queryClient.invalidateQueries({ queryKey: shoesQuery.queryKey })
  const add = useMutation({
    mutationFn: () => api.send('POST', '/api/shoes', { name, brand }),
    onSuccess: async () => {
      setName('')
      setBrand('')
      await refresh()
    },
  })
  const retire = useMutation({
    mutationFn: (id: string) => api.send('PUT', `/api/shoes/${id}/retire`),
    onSuccess: refresh,
  })
  const remove = useMutation({
    mutationFn: (id: string) => api.send('DELETE', `/api/shoes/${id}`),
    onSuccess: async () => {
      deleteDialog.current?.close()
      await Promise.all([refresh(), queryClient.invalidateQueries({ queryKey: ['activities'] })])
    },
  })
  function onSubmit(e: FormEvent) {
    e.preventDefault()
    add.mutate()
  }

  return (
    <section>
      <Breadcrumb aria-label={t('breadcrumbs')} className="mb-4">
        <BreadcrumbList>
          <BreadcrumbItem>
            <BreadcrumbLink render={<Link to="/app/profile" activeOptions={{ exact: true }} />}>
              {t('nav.profile')}
            </BreadcrumbLink>
          </BreadcrumbItem>
          <BreadcrumbSeparator />
          <BreadcrumbItem>
            <BreadcrumbPage>{t('equipment.title')}</BreadcrumbPage>
          </BreadcrumbItem>
        </BreadcrumbList>
      </Breadcrumb>
      <h1 className="text-2xl font-semibold tracking-tight">{t('equipment.title')}</h1>
      <section aria-labelledby="shoes-title" className="mt-6">
        <h2 id="shoes-title" className="text-lg font-semibold">
          {t('shoes.title')}
        </h2>
        {shoes.isPending && (
          <p role="status" className="mt-3 text-sm text-muted-foreground">
            {t('shoes.loading')}
          </p>
        )}
        {shoes.data &&
          (shoes.data.shoes.length ? (
            <ul className="mt-3 divide-y">
              {shoes.data.shoes.map((shoe) => (
                <li key={shoe.id} className="flex items-center justify-between gap-4 py-3">
                  <div className="min-w-0 break-words">
                    <p className="font-medium">{shoe.name}</p>
                    {shoe.brand && <p className="text-sm text-muted-foreground">{shoe.brand}</p>}
                  </div>
                  <div className="flex shrink-0 flex-wrap items-center justify-end gap-2">
                    {shoe.retired ? (
                      <span className="shrink-0 text-sm text-muted-foreground">
                        {t('shoes.retired')}
                      </span>
                    ) : (
                      <Button
                        variant="outline"
                        size="sm"
                        disabled={retire.isPending || remove.isPending}
                        aria-label={t('shoes.retireLabel', { name: shoe.name })}
                        onClick={() => retire.mutate(shoe.id)}
                      >
                        {t('shoes.retire')}
                      </Button>
                    )}
                    <Button
                      variant="ghost"
                      size="sm"
                      className="text-destructive"
                      disabled={remove.isPending}
                      aria-label={t('shoes.deleteLabel', { name: shoe.name })}
                      onClick={() => {
                        setDeleting(shoe)
                        remove.reset()
                        deleteDialog.current?.showModal()
                      }}
                    >
                      {t('shoes.delete')}
                    </Button>
                  </div>
                </li>
              ))}
            </ul>
          ) : (
            <p className="mt-3 text-sm text-muted-foreground">{t('shoes.empty')}</p>
          ))}
        <form onSubmit={onSubmit} className="mt-4 space-y-3">
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <div>
              <Label htmlFor="shoe-name" className="mb-2">
                {t('shoes.name')}
              </Label>
              <Input
                id="shoe-name"
                required
                maxLength={100}
                value={name}
                onChange={(e) => setName(e.currentTarget.value)}
              />
            </div>
            <div>
              <Label htmlFor="shoe-brand" className="mb-2">
                {t('shoes.brand')}
              </Label>
              <Input
                id="shoe-brand"
                maxLength={100}
                value={brand}
                onChange={(e) => setBrand(e.currentTarget.value)}
              />
            </div>
          </div>
          <Button type="submit" variant="outline" disabled={add.isPending}>
            {t('shoes.add')}
          </Button>
        </form>
        {[shoes.error, add.error, retire.error]
          .filter((error): error is Error => error !== null)
          .map((error, i) => (
            <p role="alert" key={i} className="mt-3 text-sm text-destructive">
              <ErrorMessage error={error} />
            </p>
          ))}
      </section>
      <dialog
        ref={deleteDialog}
        aria-labelledby="delete-shoe-title"
        aria-describedby="delete-shoe-warning"
        onClose={() => setDeleting(null)}
        onCancel={(event) => {
          if (remove.isPending) event.preventDefault()
        }}
        className="fixed inset-0 m-auto max-h-[calc(100dvh-2rem)] w-[calc(100%-2rem)] max-w-md overflow-y-auto rounded-xl border bg-background p-6 text-foreground shadow-xl backdrop:bg-black/70"
      >
        <h2 id="delete-shoe-title" className="break-words text-xl font-semibold">
          {t('shoes.confirmDelete', { name: deleting?.name })}
        </h2>
        <p id="delete-shoe-warning" className="mt-3 text-sm leading-relaxed text-muted-foreground">
          {t('shoes.deleteWarning')}
        </p>
        {remove.error && (
          <p role="alert" className="mt-3 text-sm text-destructive">
            <ErrorMessage error={remove.error} />
          </p>
        )}
        <div className="mt-6 flex flex-wrap justify-end gap-3">
          <Button
            variant="outline"
            disabled={remove.isPending}
            onClick={() => deleteDialog.current?.close()}
          >
            {t('shoes.cancel')}
          </Button>
          <Button
            variant="destructive"
            disabled={remove.isPending || !deleting}
            onClick={() => {
              if (deleting) remove.mutate(deleting.id)
            }}
          >
            {remove.isPending ? t('shoes.deleting') : t('shoes.delete')}
          </Button>
        </div>
      </dialog>
    </section>
  )
}
