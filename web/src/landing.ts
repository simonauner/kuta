const phone = document.querySelector<HTMLElement>('.hero-product')!
const screenshot = phone.querySelector<HTMLImageElement>('img')!
const background = document.querySelector<HTMLImageElement>('.hero-photo')!

await Promise.allSettled([screenshot.decode(), background.decode()])
if (screenshot.naturalWidth > 0) phone.classList.add('is-ready')
