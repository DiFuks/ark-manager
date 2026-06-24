// Centralised i18n dictionary + helpers.
//
// SEO model: English lives at the site root (`/`), Russian under `/ru/`.
// `en` is the default locale and is NOT prefixed; `ru` is. hreflang alternates
// and the sitemap i18n config (astro.config.mjs) keep the two reciprocal.

export const defaultLang = 'en' as const;
export const languages = { en: 'EN', ru: 'RU' } as const;
export type Lang = keyof typeof languages;

// Absolute canonical URLs per locale — used for <link rel="canonical"> and the
// hreflang block. Origin is fixed (custom apex domain).
export const origin = 'https://arkmanager.org';
export const localeHref: Record<Lang, string> = {
  en: `${origin}/`,
  ru: `${origin}/ru/`,
};
export const ogLocale: Record<Lang, string> = {
  en: 'en_US',
  ru: 'ru_RU',
};

/** Locale of the current page, derived from the first path segment. */
export function getLang(url: URL): Lang {
  const seg = url.pathname.split('/').filter(Boolean)[0];
  return seg && seg in languages ? (seg as Lang) : defaultLang;
}

export function useTranslations(lang: Lang) {
  return ui[lang];
}

export const ui = {
  en: {
    meta: {
      title: 'ArkManager — cross-platform dedicated server manager for ARK: Survival Ascended',
      description:
        'Cross-platform dedicated server manager for ARK: Survival Ascended. No setup, no wine to install, no .NET runtime to install. macOS, Linux, Windows.',
      ogAlt: 'ArkManager — dedicated server manager for ARK: Survival Ascended',
    },
    switchTo: 'Switch to Russian',
    hero: {
      tagline: 'Cross-platform dedicated server manager for ARK: Survival Ascended.',
      lede: 'No setup. No wine to install. No .NET runtime to install. Just download and run.',
      download: 'Download',
      github: 'View on GitHub',
      allPlatforms: 'All platforms →',
      shotAlt: 'ArkManager Server tab screenshot',
      downloadFor: 'Download for', // JS: `${downloadFor} ${osLabel}`
    },
    why: [
      { label: 'BUNDLED', headline: 'Wine embedded. No external installs.' },
      { label: 'SELF-CONTAINED', headline: '.NET runtime ships with the app.' },
      { label: 'OPEN SOURCE', headline: 'Inspect, fork, contribute.' },
    ],
    features: {
      label: 'FEATURES',
      heading: 'Everything ASA admins reach for, in one place.',
      items: [
        { title: 'SteamCMD install & update', body: 'One click. Steam handles the 25 GB.' },
        { title: 'CurseForge mods', body: 'Add by ID. Names resolve automatically.' },
        { title: 'ini editor', body: 'Form view for basics, raw tabs for the rest.' },
        { title: 'Auto-backups', body: 'Snapshot Saved/ on a timer. Only while running.' },
        { title: 'RCON client', body: 'saveworld, DoExit, Broadcast — one click.' },
        { title: 'Live players', body: 'Names and counts polled in the background.' },
        { title: 'Cluster support', body: 'ClusterId + transfer dir, GUI-driven.' },
        { title: '7 map presets', body: 'Plus raw Game.ini for mod maps.' },
      ],
    },
    screenshots: {
      label: 'SCREENSHOTS',
      heading: 'Every tab.',
      shotAlt: (tab: string) => `ArkManager ${tab} tab`,
    },
    quickstart: {
      label: 'QUICK START',
      heading: 'Four steps. Two clicks each.',
      steps: [
        { title: 'Install SteamCMD', body: 'One-click bootstrap. No terminal.' },
        { title: 'Install the server', body: 'Steam pulls 25 GB. Sit back.' },
        { title: 'Configure', body: 'Session name, admin password, ports. Defaults are sensible.' },
        { title: 'Start', body: 'Wine builds its prefix on first run (~30s on mac/linux).' },
      ],
    },
    download: {
      label: 'DOWNLOAD',
      heading: 'Pick your platform.',
      latest: 'Latest release',
      released: 'released', // JS: `· ${released} 2026-06-01`
      button: 'Download',
      allReleases: 'All releases →',
      noteIntel: 'Apple Silicon only — Intel Macs untested.',
      noteArm: 'x64 only — ARM untested.',
      firstLaunch: {
        label: 'FIRST LAUNCH',
        body:
          "ArkManager isn't signed with an Apple or Microsoft certificate — those cost $99–$300 per year. Both systems will warn on first launch. Here's how to get past the warning.",
        macTitle: 'macOS',
        macStep:
          'Try <strong>right-click → Open</strong> first. On recent macOS this often fails silently for unsigned apps. If it does, open Terminal and run:',
        macNote:
          'Replace the path if you keep ArkManager elsewhere. After this the app launches normally — including subsequent updates.',
        macRosetta:
          'On Apple Silicon you may also see <em>"support for Intel applications will be discontinued soon"</em> (and, on a fresh Mac, a prompt to install Rosetta) the first time you start a server. That\'s expected — the ASA server is a Windows Intel binary, run through bundled wine under Rosetta 2. Allow it and continue; Apple keeps Rosetta around for years yet.',
        winTitle: 'Windows',
        winStep:
          'SmartScreen shows <em>"Windows protected your PC"</em>. Click <strong>More info</strong> → <strong>Run anyway</strong>. That\'s a one-time per binary.',
        winNote:
          "If your environment blocks unsigned executables outright, you'll need IT to allow-list ArkManager — same drill as with any independent open-source tool.",
      },
      help: {
        text: 'Hit any other snag? Open an issue on GitHub — describe what happened and attach the log, and we\'ll take a look.',
        cta: 'Open an issue →',
        href: 'https://github.com/DiFuks/ark-manager/issues/new',
        logsLabel: 'Grab the log from the <strong>Open logs</strong> button on the Install tab.',
      },
    },
    footer: {
      issues: 'Issues',
      license: 'License',
    },
  },

  ru: {
    meta: {
      title: 'ArkManager — кроссплатформенный менеджер выделенного сервера ARK: Survival Ascended',
      description:
        'Кроссплатформенный менеджер выделенного сервера ARK: Survival Ascended. Без настройки, без установки wine и без установки среды .NET. macOS, Linux, Windows.',
      ogAlt: 'ArkManager — менеджер выделенного сервера ARK: Survival Ascended',
    },
    switchTo: 'Switch to English',
    hero: {
      tagline: 'Кроссплатформенный менеджер выделенного сервера ARK: Survival Ascended.',
      lede: 'Без настройки. Без установки wine. Без установки .NET. Просто скачай и запусти.',
      download: 'Скачать',
      github: 'Открыть на GitHub',
      allPlatforms: 'Все платформы →',
      shotAlt: 'Скриншот вкладки Server в ArkManager',
      downloadFor: 'Скачать для', // JS: `${downloadFor} ${osLabel}`
    },
    why: [
      { label: 'В КОМПЛЕКТЕ', headline: 'Wine встроен. Никаких сторонних установок.' },
      { label: 'АВТОНОМНО', headline: 'Среда .NET поставляется вместе с приложением.' },
      { label: 'ОТКРЫТЫЙ КОД', headline: 'Смотри, форкай, контрибьють.' },
    ],
    features: {
      label: 'ВОЗМОЖНОСТИ',
      heading: 'Всё, что нужно админу ASA, — в одном месте.',
      items: [
        { title: 'Установка и обновление через SteamCMD', body: 'Один клик. Steam качает 25 ГБ.' },
        { title: 'Моды CurseForge', body: 'Добавляй по ID. Названия подтянутся сами.' },
        { title: 'Редактор ini', body: 'Форма для основ, raw-вкладки для остального.' },
        { title: 'Авто-бэкапы', body: 'Снимок Saved/ по таймеру. Только пока сервер запущен.' },
        { title: 'RCON-клиент', body: 'saveworld, DoExit, Broadcast — в один клик.' },
        { title: 'Игроки онлайн', body: 'Ники и количество опрашиваются в фоне.' },
        { title: 'Поддержка кластера', body: 'ClusterId + папка переноса, всё из GUI.' },
        { title: '7 пресетов карт', body: 'Плюс raw Game.ini для модовых карт.' },
      ],
    },
    screenshots: {
      label: 'СКРИНШОТЫ',
      heading: 'Каждая вкладка.',
      shotAlt: (tab: string) => `Вкладка ${tab} в ArkManager`,
    },
    quickstart: {
      label: 'БЫСТРЫЙ СТАРТ',
      heading: 'Четыре шага. По два клика каждый.',
      steps: [
        { title: 'Установить SteamCMD', body: 'Бутстрап в один клик. Без терминала.' },
        { title: 'Установить сервер', body: 'Steam тянет 25 ГБ. Можно расслабиться.' },
        { title: 'Настроить', body: 'Имя сессии, пароль админа, порты. Дефолты разумные.' },
        { title: 'Запустить', body: 'При первом запуске wine собирает префикс (~30 с на mac/linux).' },
      ],
    },
    download: {
      label: 'СКАЧАТЬ',
      heading: 'Выбери платформу.',
      latest: 'Последний релиз',
      released: 'выпущен', // JS: `· ${released} 2026-06-01`
      button: 'Скачать',
      allReleases: 'Все релизы →',
      noteIntel: 'Только Apple Silicon — Intel Mac не тестировался.',
      noteArm: 'Только x64 — ARM не тестировался.',
      firstLaunch: {
        label: 'ПЕРВЫЙ ЗАПУСК',
        body:
          'ArkManager не подписан сертификатом Apple или Microsoft — они стоят $99–300 в год. Обе системы предупредят при первом запуске. Вот как обойти предупреждение.',
        macTitle: 'macOS',
        macStep:
          'Сначала попробуй <strong>правый клик → Открыть</strong>. На свежих macOS для неподписанных приложений это часто молча не срабатывает. Если так — открой Терминал и выполни:',
        macNote:
          'Подставь свой путь, если держишь ArkManager в другом месте. После этого приложение запускается как обычно — включая последующие обновления.',
        macRosetta:
          'На Apple Silicon при первом запуске сервера может также появиться предупреждение <em>«поддержка приложений для Intel скоро будет прекращена»</em> (а на новом Mac — запрос на установку Rosetta). Это нормально: сервер ASA — это Windows-бинарь под Intel, который запускается через встроенный wine поверх Rosetta 2. Разреши и продолжай — Apple оставит Rosetta ещё на годы.',
        winTitle: 'Windows',
        winStep:
          'SmartScreen покажет <em>«Система Windows защитила ваш компьютер»</em>. Нажми <strong>Подробнее</strong> → <strong>Выполнить в любом случае</strong>. Это разово на каждый бинарник.',
        winNote:
          'Если среда вообще блокирует неподписанные исполняемые файлы, попроси IT добавить ArkManager в белый список — как с любым независимым open-source инструментом.',
      },
      help: {
        text: 'Столкнулся с другой проблемой? Заведи issue на GitHub — опиши, что случилось, и приложи лог. Разберёмся.',
        cta: 'Завести issue →',
        href: 'https://github.com/DiFuks/ark-manager/issues/new',
        logsLabel: 'Лог достанешь кнопкой <strong>Open logs</strong> на вкладке Install.',
      },
    },
    footer: {
      issues: 'Issues',
      license: 'Лицензия',
    },
  },
} as const;
