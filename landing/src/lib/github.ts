const REPO = 'DiFuks/ark-manager';
const API_URL = `https://api.github.com/repos/${REPO}/releases/latest`;

export interface ReleaseAsset {
  name: string;
  size: number;
  browser_download_url: string;
}

export interface LatestRelease {
  tag_name: string;
  published_at: string;
  html_url: string;
  assets: ReleaseAsset[];
}

export async function fetchLatestRelease(): Promise<LatestRelease | null> {
  try {
    const res = await fetch(API_URL, {
      headers: { Accept: 'application/vnd.github+json' },
    });
    if (!res.ok) return null;
    return (await res.json()) as LatestRelease;
  } catch {
    return null;
  }
}

export type OsKey = 'mac' | 'linux' | 'win';

const SUFFIX: Record<OsKey, RegExp> = {
  mac:   /-macos-arm64\.zip$/i,
  linux: /-linux-x64\.tar\.gz$/i,
  win:   /-windows-x64\.zip$/i,
};

export function pickAsset(release: LatestRelease, os: OsKey): ReleaseAsset | undefined {
  return release.assets.find(a => SUFFIX[os].test(a.name));
}

export function formatSize(bytes: number): string {
  const mb = bytes / 1024 / 1024;
  return `${mb.toFixed(0)} MB`;
}

export function formatDate(iso: string): string {
  const d = new Date(iso);
  return d.toISOString().slice(0, 10);
}
