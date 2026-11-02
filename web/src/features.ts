const labels =
  document.documentElement.lang === 'en'
    ? { pause: 'Pause slideshow', play: 'Play slideshow' }
    : { pause: 'Pausa bildspel', play: 'Starta bildspel' }

for (const carousel of document.querySelectorAll<HTMLElement>('.statistics-carousel')) {
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

  let request = 0

  // Keeps the previous slide visible while the next one loads, then cross-fades to it.
  function show(index: number) {
    if (index === current) return
    current = index
    choices.forEach((button, i) => button.setAttribute('aria-pressed', String(i === index)))
    const token = ++request
    const next = slides[index]
    next.loading = 'eager'
    const loaded =
      next.complete && next.naturalWidth > 0
        ? Promise.resolve()
        : next.decode().catch(() => undefined)
    void loaded.then(() => {
      if (token !== request) return
      const previous = slides.filter((slide) => slide !== next && !slide.hidden)
      const duration = motion.matches ? 0 : 300
      // An interrupted fade restarts from the slide's current opacity.
      const from = new Map(previous.map((slide) => [slide, getComputedStyle(slide).opacity]))
      next.getAnimations().forEach((animation) => animation.cancel())
      next.hidden = false
      next.classList.add('is-current')
      previous.forEach((slide) => slide.classList.remove('is-current'))
      const fadeIn = next.animate([{ opacity: 0 }, { opacity: 1 }], { duration, easing: 'ease' })
      previous.forEach((slide) =>
        slide.animate([{ opacity: from.get(slide) }, { opacity: 0 }], {
          duration,
          easing: 'ease',
          fill: 'forwards',
        }),
      )
      void fadeIn.finished
        .catch(() => undefined)
        .then(() => {
          if (token !== request) return
          previous.forEach((slide) => {
            slide.hidden = true
            slide.getAnimations().forEach((animation) => animation.cancel())
          })
        })
    })
  }

  function updateRotation() {
    window.clearInterval(timer)
    rotation.textContent = playing ? labels.pause : labels.play
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
