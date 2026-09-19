export const SIDES = [4, 6, 8, 10, 12, 20, 100];
// Rejection sampling avoids modulo bias. RNG never uses the animation seed.
export function randomInt(max) {
  if (!Number.isSafeInteger(max) || max < 1 || max > 0x100000000) throw Error('range');
  const limit = Math.floor(0x100000000 / max) * max;
  let x;
  do { x = crypto.getRandomValues(new Uint32Array(1))[0]; } while (x >= limit);
  return x % max;
}
export function token() {
  return Array.from(crypto.getRandomValues(new Uint8Array(32)), x => x.toString(16).padStart(2, '0')).join('');
}
export async function hash(value) {
  return Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(value))), x => x.toString(16).padStart(2, '0')).join('');
}
export function resolve(count, sides, modifier, rng = randomInt) {
  if (!Number.isInteger(count) || count < 1 || count > 20 || !SIDES.includes(sides) || !Number.isInteger(modifier) || Math.abs(modifier) > 2000) throw Error('invalid dice');
  // Apply the full assignment to EACH natural die, with an independent physical
  // clamp. Do not transfer overflow between dice or redistribute the final total.
  const faces = Array.from({length:count}, () => Math.max(1, Math.min(sides, rng(sides) + 1 + modifier)));
  return { faces, total:faces.reduce((sum, face) => sum + face, 0) };
}

