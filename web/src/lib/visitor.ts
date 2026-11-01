import { createContext, useContext } from 'react'

// Set by the layout when someone without an account opens a shared profile or activity link.
export const VisitorContext = createContext<{ signIn: (mode: 'login' | 'signup') => void } | null>(
  null,
)

export const useVisitor = () => useContext(VisitorContext)
