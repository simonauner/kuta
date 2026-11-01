import type { ReactNode } from 'react'
import { motion, useReducedMotion } from 'motion/react'

// Grows from zero height when content arrives after the page, so it doesn't push the page down in one jump.
export function Reveal({ children }: { children: ReactNode }) {
  const reduceMotion = useReducedMotion()
  return (
    <motion.div
      initial={reduceMotion ? false : { height: 0, opacity: 0, overflow: 'hidden' }}
      animate={{ height: 'auto', opacity: 1, overflow: 'visible' }}
      transition={{ duration: 0.25, ease: 'easeOut', overflow: { delay: 0.25, duration: 0 } }}
    >
      {children}
    </motion.div>
  )
}
