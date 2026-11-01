const carousel = document.querySelector<HTMLElement>('.statistics-carousel')

if (carousel) {
  const slides = [...carousel.querySelectorAll<HTMLImageElement>('.statistics-slide')]
  const controls = carousel.querySelector<HTMLElement>('.statistics-controls')!
  const choices = [...controls.querySelectorAll<HTMLButtonElement>('.statistics-choices button')]
  const rotation = controls.querySelector<HTMLButtonElement>('.statistics-rotation')!
  const motion = window.matchMedia('(prefers-reduced-motion: reduce)')
  let current = 0
  let playing = !motion.matches
  let visible = false
  let hovered = false
  let timer: number | undefined

  function show(index: number) {
    current = index
    slides.forEach((slide, i) => (slide.hidden = i !== index))
    choices.forEach((button, i) => button.setAttribute('aria-pressed', String(i === index)))
  }

  function updateRotation() {
    window.clearInterval(timer)
    rotation.textContent = playing ? 'Pausa bildspel' : 'Starta bildspel'
    if (playing && visible && !hovered && !document.hidden) {
      timer = window.setInterval(() => show((current + 1) % slides.length), 6500)
    }
  }

  function pause() {
    playing = false
    updateRotation()
  }

  choices.forEach((button, index) => {
    button.addEventListener('click', () => {
      pause()
      show(index)
    })
  })
  rotation.addEventListener('click', () => {
    playing = !playing
    updateRotation()
  })
  controls.addEventListener('focusin', (event) => {
    if (event.target !== rotation) pause()
  })
  // Hover pauses temporarily; keyboard navigation and manual selection stop until restarted.
  carousel.addEventListener('mouseenter', () => {
    hovered = true
    updateRotation()
  })
  carousel.addEventListener('mouseleave', () => {
    hovered = false
    updateRotation()
  })
  document.addEventListener('visibilitychange', updateRotation)
  motion.addEventListener('change', () => {
    if (motion.matches) pause()
  })
  new IntersectionObserver(
    ([entry]) => {
      visible = entry.isIntersecting
      updateRotation()
    },
    { threshold: 0.25 },
  ).observe(carousel)

  controls.hidden = false
  updateRotation()
}
