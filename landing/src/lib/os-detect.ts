export type Os = 'mac' | 'linux' | 'win';

export function detectOs(): Os | null {
  if (typeof navigator === 'undefined') return null;
  const ua = navigator.userAgent;
  if (/Mac|iPhone|iPad|iPod/.test(ua)) return 'mac';
  if (/Windows|Win64|Win32|WOW64/.test(ua)) return 'win';
  if (/Linux|X11|CrOS/.test(ua)) return 'linux';
  return null;
}

export function osLabel(os: Os): string {
  return os === 'mac' ? 'macOS' : os === 'linux' ? 'Linux' : 'Windows';
}
