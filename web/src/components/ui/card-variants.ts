import { cva } from 'class-variance-authority'

// The app's one card look. `card()` gives the classes for elements that are not a div (section, li, form, links).
export const card = cva('rounded-xl border bg-card text-card-foreground', {
  variants: {
    tone: {
      default: '',
      highlight: 'border-primary/30 bg-primary/5',
      warning: 'border-amber-500/50 bg-amber-500/10',
      destructive: 'border-destructive/40',
    },
    padding: {
      default: 'p-4 sm:p-5',
      compact: 'p-3 sm:p-4',
      spacious: 'p-6 sm:p-8',
      none: '',
    },
    interactive: {
      true: 'transition-colors hover:bg-accent focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring',
      false: '',
    },
  },
  defaultVariants: { tone: 'default', padding: 'default', interactive: false },
})
