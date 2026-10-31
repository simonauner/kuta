type FooterProps = {
  labels: { privacy: string; openApp: string; links: string }
  language?: string
} & ({ variant: 'public' } | { variant: 'app'; version: string })

const copyrightYear = new Date().getFullYear()

// Vite renders this component into public HTML; Layout uses it directly in the app.
export function Footer(props: FooterProps) {
  const { labels, language = 'sv', variant } = props
  return (
    <footer className={`site-footer footer--${variant}`}>
      <div className="footer-inner">
        <div className="footer-brand">
          <a className="footer-logo" href={variant === 'public' ? '/' : '/app'}>
            <picture>
              <source media="(prefers-color-scheme: dark)" srcSet="/brand/logo-light.svg" />
              <img src="/brand/logo.svg" alt="Kuta" width="120" height="40" />
            </picture>
          </a>
          <p>
            © {copyrightYear} Kuta · <a href="https://hackskaduha.se">Hack ska du ha AB</a>
          </p>
        </div>
        <nav className="footer-links" aria-label={labels.links}>
          <a href={language === 'en' ? '/privacy#english' : '/privacy'}>{labels.privacy}</a>
          {props.variant === 'public' ? (
            <a href="/app">{labels.openApp}</a>
          ) : (
            <p>{props.version}</p>
          )}
        </nav>
      </div>
    </footer>
  )
}
