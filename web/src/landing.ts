const hero = document.querySelector<HTMLElement>('.hero')!
const images = hero.querySelectorAll<HTMLImageElement>('img')

await Promise.allSettled(Array.from(images, (image) => image.decode()))
hero.classList.add('is-ready')
