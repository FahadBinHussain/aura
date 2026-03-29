'use client';

import { motion } from 'motion/react';
import { useEffect, useState } from 'react';
import {
  Download,
  Github,
  Timer,
  Globe,
  History,
  MonitorPlay,
  Shield,
  Rocket,
  CheckCircle2,
  ArrowRight,
} from 'lucide-react';

const DAISY_THEMES = [
  'light',
  'dark',
  'cupcake',
  'bumblebee',
  'emerald',
  'corporate',
  'synthwave',
  'retro',
  'cyberpunk',
  'valentine',
  'halloween',
  'garden',
  'forest',
  'aqua',
  'lofi',
  'pastel',
  'fantasy',
  'wireframe',
  'black',
  'luxury',
  'dracula',
  'cmyk',
  'autumn',
  'business',
  'acid',
  'lemonade',
  'night',
  'coffee',
  'winter',
  'dim',
  'nord',
  'sunset',
  'caramellatte',
  'abyss',
  'silk',
] as const;

const features = [
  {
    icon: Globe,
    title: 'Multi-Platform Browsing',
    description:
      "Access millions of curated wallpapers from Backiee and AlphaCoders in one unified interface.",
  },
  {
    icon: Timer,
    title: 'Smart Slideshows',
    description:
      'Automated desktop and lock screen slideshows with custom intervals and safe download timing.',
  },
  {
    icon: History,
    title: 'Wallpaper History',
    description:
      'Every change is recorded with thumbnail, timestamp, and source with persistence across restarts.',
  },
  {
    icon: MonitorPlay,
    title: 'Desktop + Lock Screen',
    description:
      'Independently manage desktop and lock screen wallpapers from different sources.',
  },
  {
    icon: Shield,
    title: 'No Duplicates',
    description:
      'Stable file naming and existence checks prevent duplicate downloads and clutter.',
  },
  {
    icon: Rocket,
    title: 'Start with Windows',
    description:
      'Enable auto-start in settings so Aura launches with Windows and keeps your slideshow running.',
  },
];

const perks = ['Free and open source', 'No account required', 'No ads, no tracking', 'Windows 10 and 11'];

const stats = [
  { value: '2M+', label: 'Wallpapers' },
  { value: '2', label: 'Platforms' },
  { value: 'Infinite', label: 'Combinations' },
  { value: '100%', label: 'Free' },
];

function FadeIn({
  children,
  delay = 0,
  className = '',
}: {
  children: React.ReactNode;
  delay?: number;
  className?: string;
}) {
  return (
    <motion.div
      initial={{ opacity: 0, y: 24 }}
      whileInView={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.7, delay, ease: 'easeOut' }}
      viewport={{ once: false, amount: 0.4 }}
      className={className}
    >
      {children}
    </motion.div>
  );
}

export default function Overlay() {
  const formatThemeLabel = (name: string) => name.charAt(0).toUpperCase() + name.slice(1);

  const [theme, setTheme] = useState<string>(() => {
    if (typeof window === 'undefined') {
      return 'dark';
    }
    const savedTheme = localStorage.getItem('theme')?.toLowerCase();
    if (savedTheme && DAISY_THEMES.includes(savedTheme as (typeof DAISY_THEMES)[number])) {
      return savedTheme;
    }
    return 'dark';
  });

  useEffect(() => {
    document.documentElement.setAttribute('data-theme', theme);
    localStorage.setItem('theme', theme);
  }, [theme]);

  return (
    <div className="w-full text-base-content font-sans pointer-events-none">
      <div className="fixed top-4 right-4 z-50 pointer-events-auto">
        <select
          value={theme}
          onChange={(event) => setTheme(event.target.value.toLowerCase())}
          className="select select-sm select-bordered w-44 bg-base-100 text-base-content"
          aria-label="Select theme"
        >
          {DAISY_THEMES.map((themeName) => (
            <option key={themeName} value={themeName.toLowerCase()}>
              {formatThemeLabel(themeName.toLowerCase())}
            </option>
          ))}
        </select>
      </div>

      <section className="h-screen flex flex-col items-center justify-center text-center px-6 pointer-events-auto">
        <FadeIn delay={0}>
          <span className="inline-flex items-center gap-2 mb-6 rounded-full border border-base-300 bg-base-200 px-4 py-1.5 text-sm text-base-content/70">
            Next-Gen Windows Wallpaper App
            <span className="badge badge-outline badge-sm">WinUI 3</span>
          </span>
        </FadeIn>

        <FadeIn delay={0.1}>
          <h1 className="text-5xl md:text-7xl font-bold tracking-tight mb-6 leading-[1.1] text-base-content">
            Your Desktop.
            <br />
            <span className="text-base-content/80">Reimagined.</span>
          </h1>
        </FadeIn>

        <FadeIn delay={0.2}>
          <p className="text-xl md:text-2xl text-base-content/70 mb-10 max-w-2xl leading-relaxed">
            Browse millions of wallpapers from Backiee and AlphaCoders, automate slideshows, and track your full
            history in one app.
          </p>
        </FadeIn>

        <FadeIn delay={0.35}>
          <div className="flex flex-wrap items-center justify-center gap-4">
            <a
              href="https://github.com/fahadbinhussain/aura/releases/latest/download/Aura-x64.zip"
              target="_blank"
              rel="noopener noreferrer"
              className="btn btn-primary btn-lg rounded-full"
            >
              <Download className="w-4 h-4" />
              Download for Windows
              <ArrowRight className="w-4 h-4" />
            </a>
            <a
              href="https://github.com/fahadbinhussain/aura"
              target="_blank"
              rel="noopener noreferrer"
              className="btn btn-outline btn-lg rounded-full"
            >
              <Github className="w-4 h-4" />
              View on GitHub
            </a>
          </div>
        </FadeIn>
      </section>

      <section className="h-screen flex flex-col items-center justify-center px-6 pointer-events-auto">
        <FadeIn delay={0} className="w-full max-w-4xl">
          <div className="mb-12 text-center">
            <p className="text-base-content/60 text-sm uppercase tracking-widest font-semibold mb-2">By the numbers</p>
            <h2 className="text-4xl md:text-5xl font-bold text-base-content">Everything you need. Nothing you do not.</h2>
          </div>

          <div className="grid grid-cols-2 sm:grid-cols-4 gap-4 mb-16">
            {stats.map((s, i) => (
              <motion.div
                key={s.label}
                initial={{ opacity: 0, y: 20 }}
                whileInView={{ opacity: 1, y: 0 }}
                transition={{ duration: 0.5, delay: i * 0.1 }}
                viewport={{ once: false, amount: 0.5 }}
                className="flex flex-col items-center justify-center rounded-2xl border border-base-300 bg-base-200 py-8 text-center"
              >
                <span className="text-4xl font-bold text-base-content leading-none mb-2">{s.value}</span>
                <span className="text-xs text-base-content/60 uppercase tracking-wider">{s.label}</span>
              </motion.div>
            ))}
          </div>
        </FadeIn>
      </section>

      <section className="min-h-screen flex flex-col items-center justify-center px-6 py-20 pointer-events-auto">
        <FadeIn delay={0} className="w-full max-w-5xl">
          <div className="text-center mb-12">
            <p className="text-base-content/60 text-sm uppercase tracking-widest font-semibold mb-2">Features</p>
            <h2 className="text-4xl md:text-5xl font-bold text-base-content">Fluid. Dynamic. Yours.</h2>
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
            {features.map((f, i) => (
              <motion.div
                key={f.title}
                initial={{ opacity: 0, y: 28 }}
                whileInView={{ opacity: 1, y: 0 }}
                transition={{ duration: 0.6, delay: i * 0.08, ease: 'easeOut' }}
                viewport={{ once: false, amount: 0.3 }}
                className="rounded-2xl border border-base-300 bg-base-200 p-6"
              >
                <div className="inline-flex items-center justify-center w-10 h-10 rounded-xl bg-base-300 mb-4">
                  <f.icon className="w-5 h-5 text-base-content/80" />
                </div>
                <h3 className="text-base font-semibold text-base-content mb-2">{f.title}</h3>
                <p className="text-sm text-base-content/70 leading-relaxed">{f.description}</p>
              </motion.div>
            ))}
          </div>
        </FadeIn>
      </section>

      <section className="h-screen flex flex-col items-center justify-center text-center px-6 pointer-events-auto">
        <FadeIn delay={0}>
          <p className="text-base-content/60 text-sm uppercase tracking-widest font-semibold mb-4">Get started</p>
          <h2 className="text-5xl md:text-7xl font-bold mb-6">Elevate your workspace.</h2>
          <p className="text-base-content/70 text-xl mb-8 max-w-xl mx-auto">
            One download. No installer drama. Unzip and run on Windows 10 and 11.
          </p>

          <div className="flex flex-wrap items-center justify-center gap-3 mb-10">
            {perks.map((p) => (
              <div key={p} className="flex items-center gap-1.5 text-sm text-base-content/70">
                <CheckCircle2 className="w-4 h-4 text-success flex-shrink-0" />
                {p}
              </div>
            ))}
          </div>

          <div className="flex flex-wrap items-center justify-center gap-4 mb-10">
            <a
              href="https://github.com/fahadbinhussain/aura/releases/latest/download/Aura-x64.zip"
              target="_blank"
              rel="noopener noreferrer"
              className="btn btn-primary btn-lg rounded-full"
            >
              <Download className="w-5 h-5" />
              Get Aura Now
              <ArrowRight className="w-5 h-5" />
            </a>
            <a
              href="https://github.com/fahadbinhussain/aura"
              target="_blank"
              rel="noopener noreferrer"
              className="btn btn-outline btn-lg rounded-full"
            >
              <Github className="w-5 h-5" />
              Star on GitHub
            </a>
          </div>

          <p className="text-xs text-base-content/60">Requires Windows 10 (1903+) or Windows 11, x64, about 80 MB</p>
        </FadeIn>
      </section>
    </div>
  );
}
