'use client';

import { motion } from 'motion/react';
import { Download, Github, Timer, Globe, History, MonitorPlay, Shield, Rocket, CheckCircle2, ArrowRight } from 'lucide-react';

const features = [
  {
    icon: Globe,
    title: 'Multi-Platform Browsing',
    description: 'Access millions of curated wallpapers from Backiee and AlphaCoders — two of the internet\'s largest wallpaper libraries — in one unified interface.',
    color: 'from-purple-500/30 to-blue-500/10',
    iconColor: 'text-purple-400',
  },
  {
    icon: Timer,
    title: 'Smart Slideshows',
    description: 'Automated desktop & lock screen slideshows with custom intervals. Race-free one-shot timer ensures each wallpaper fully downloads before the next countdown.',
    color: 'from-pink-500/30 to-purple-500/10',
    iconColor: 'text-pink-400',
  },
  {
    icon: History,
    title: 'Wallpaper History',
    description: 'Every change — manual or automated — is recorded with thumbnail, timestamp, and source. History persists across restarts with full disk persistence.',
    color: 'from-cyan-500/30 to-blue-500/10',
    iconColor: 'text-cyan-400',
  },
  {
    icon: MonitorPlay,
    title: 'Desktop + Lock Screen',
    description: 'Independently manage your desktop wallpaper and Windows lock screen. Mix platforms — Backiee for desktop, AlphaCoders for lock screen.',
    color: 'from-green-500/30 to-cyan-500/10',
    iconColor: 'text-green-400',
  },
  {
    icon: Shield,
    title: 'No Duplicates',
    description: 'Stable file naming and existence checks mean wallpapers are never re-downloaded. Your Pictures folder stays clean with one file per unique wallpaper.',
    color: 'from-yellow-500/30 to-orange-500/10',
    iconColor: 'text-yellow-400',
  },
  {
    icon: Rocket,
    title: 'Start with Windows',
    description: 'Enable auto-start in Settings so Aura launches silently with Windows, keeping your slideshow running from the moment you sign in.',
    color: 'from-orange-500/30 to-pink-500/10',
    iconColor: 'text-orange-400',
  },
];

const perks = [
  'Free & open source',
  'No account required',
  'No ads, no tracking',
  'Windows 10 & 11',
];

const stats = [
  { value: '2M+', label: 'Wallpapers' },
  { value: '2', label: 'Platforms' },
  { value: '∞', label: 'Combinations' },
  { value: '100%', label: 'Free' },
];

function FadeIn({ children, delay = 0, className = '' }: { children: React.ReactNode; delay?: number; className?: string }) {
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
  return (
    <div className="w-full text-white font-sans pointer-events-none">

      {/* ── PAGE 1: Hero ── */}
      <section className="h-screen flex flex-col items-center justify-center text-center px-6 pointer-events-auto">
        <FadeIn delay={0}>
          <span className="inline-flex items-center gap-2 mb-6 rounded-full border border-white/10 bg-white/5 backdrop-blur-md px-4 py-1.5 text-sm text-white/60">
            ✦ Next-Gen Windows Wallpaper App &nbsp;
            <span className="rounded-full bg-indigo-500/20 px-2 py-0.5 text-xs text-indigo-300 font-medium">WinUI 3</span>
          </span>
        </FadeIn>

        <FadeIn delay={0.1}>
          <h1 className="text-5xl md:text-7xl font-bold tracking-tight mb-6 leading-[1.1]">
            <span className="text-transparent bg-clip-text bg-gradient-to-r from-white via-indigo-200 to-purple-400">Your Desktop.</span>
            <br />
            <span className="text-white/90">Reimagined.</span>
          </h1>
        </FadeIn>

        <FadeIn delay={0.2}>
          <p className="text-xl md:text-2xl text-gray-400 mb-10 max-w-2xl leading-relaxed">
            Browse millions of wallpapers from Backiee &amp; AlphaCoders, automate slideshows, and track your full history — all inside a beautiful native Windows app.
          </p>
        </FadeIn>

        <FadeIn delay={0.35}>
          <div className="flex flex-wrap items-center justify-center gap-4">
            <a
              href="https://github.com/fa-hat/aura/releases"
              target="_blank"
              rel="noopener noreferrer"
              className="group flex items-center gap-2 rounded-full px-7 py-3.5 font-semibold text-white transition-all duration-300 hover:scale-105"
              style={{ background: 'linear-gradient(135deg, #4f46e5 0%, #a855f7 100%)', boxShadow: '0 0 30px rgba(79,70,229,0.5)' }}
            >
              <Download className="w-4 h-4" />
              Download for Windows
              <ArrowRight className="w-4 h-4 transition-transform group-hover:translate-x-1" />
            </a>
            <a
              href="https://github.com/fa-hat/aura"
              target="_blank"
              rel="noopener noreferrer"
              className="flex items-center gap-2 rounded-full border border-white/10 bg-white/5 backdrop-blur-md px-7 py-3.5 font-semibold text-white/70 hover:text-white hover:bg-white/10 transition-all duration-300"
            >
              <Github className="w-4 h-4" />
              View on GitHub
            </a>
          </div>
        </FadeIn>

        {/* Scroll hint */}
        <motion.div
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          transition={{ delay: 1.2, duration: 0.6 }}
          className="absolute bottom-10 flex flex-col items-center gap-2 text-white/25 text-xs"
        >
          <span>Scroll to explore</span>
          <motion.div
            animate={{ y: [0, 6, 0] }}
            transition={{ duration: 1.5, repeat: Infinity, ease: 'easeInOut' }}
            className="w-px h-8 bg-gradient-to-b from-white/20 to-transparent"
          />
        </motion.div>
      </section>

      {/* ── PAGE 2: Stats strip ── */}
      <section className="h-screen flex flex-col items-center justify-center px-6 pointer-events-auto">
        <FadeIn delay={0} className="w-full max-w-4xl">
          <div className="mb-12 text-center">
            <p className="text-white/30 text-sm uppercase tracking-widest font-semibold mb-2">By the numbers</p>
            <h2 className="text-4xl md:text-5xl font-bold text-white/90">
              Everything you need.<br />
              <span className="text-transparent bg-clip-text bg-gradient-to-r from-indigo-400 to-purple-400">Nothing you don&apos;t.</span>
            </h2>
          </div>

          <div className="grid grid-cols-2 sm:grid-cols-4 gap-4 mb-16">
            {stats.map((s, i) => (
              <motion.div
                key={s.label}
                initial={{ opacity: 0, y: 20 }}
                whileInView={{ opacity: 1, y: 0 }}
                transition={{ duration: 0.5, delay: i * 0.1 }}
                viewport={{ once: false, amount: 0.5 }}
                className="flex flex-col items-center justify-center rounded-2xl border border-white/8 bg-white/4 backdrop-blur-md py-8 text-center"
              >
                <span className="text-4xl font-bold text-transparent bg-clip-text bg-gradient-to-r from-indigo-300 to-purple-300 leading-none mb-2">
                  {s.value}
                </span>
                <span className="text-xs text-white/35 uppercase tracking-wider">{s.label}</span>
              </motion.div>
            ))}
          </div>
        </FadeIn>
      </section>

      {/* ── PAGE 3: Features ── */}
      <section className="min-h-screen flex flex-col items-center justify-center px-6 py-20 pointer-events-auto">
        <FadeIn delay={0} className="w-full max-w-5xl">
          <div className="text-center mb-12">
            <p className="text-white/30 text-sm uppercase tracking-widest font-semibold mb-2">Features</p>
            <h2 className="text-4xl md:text-5xl font-bold text-white/90">
              Fluid. Dynamic. <span className="text-transparent bg-clip-text bg-gradient-to-r from-indigo-400 to-purple-400">Yours.</span>
            </h2>
            <p className="mt-4 text-gray-400 max-w-xl mx-auto">
              Aura is built for power users who care about their desktop environment as much as their workflow.
            </p>
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
            {features.map((f, i) => (
              <motion.div
                key={f.title}
                initial={{ opacity: 0, y: 28 }}
                whileInView={{ opacity: 1, y: 0 }}
                transition={{ duration: 0.6, delay: i * 0.08, ease: 'easeOut' }}
                viewport={{ once: false, amount: 0.3 }}
                whileHover={{ y: -4, transition: { duration: 0.2 } }}
                className={`rounded-2xl border border-white/8 bg-gradient-to-br ${f.color} backdrop-blur-md p-6`}
              >
                <div className="inline-flex items-center justify-center w-10 h-10 rounded-xl bg-white/8 mb-4">
                  <f.icon className={`w-5 h-5 ${f.iconColor}`} />
                </div>
                <h3 className="text-base font-semibold text-white/90 mb-2">{f.title}</h3>
                <p className="text-sm text-gray-400 leading-relaxed">{f.description}</p>
              </motion.div>
            ))}
          </div>
        </FadeIn>
      </section>

      {/* ── PAGE 4: Platforms ── */}
      <section className="h-screen flex flex-col items-center justify-center px-6 pointer-events-auto">
        <FadeIn delay={0} className="max-w-4xl w-full text-center">
          <p className="text-white/30 text-sm uppercase tracking-widest font-semibold mb-4">Supported Platforms</p>
          <h2 className="text-4xl md:text-6xl font-bold mb-6 text-white/90">
            Two powerhouses.<br />
            <span className="text-transparent bg-clip-text bg-gradient-to-r from-indigo-400 to-purple-400">One app.</span>
          </h2>
          <p className="text-gray-400 text-lg mb-12 max-w-xl mx-auto">
            Aura integrates deeply with the best wallpaper platforms available — giving you access to an enormous, curated library at your fingertips.
          </p>

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-6">
            {[
              {
                name: 'Backiee',
                desc: 'A massive curated wallpaper library with categories ranging from nature and cities to abstract and dark themes. Millions of high-quality wallpapers in 4K, 5K and 8K.',
                badge: '4K / 5K / 8K',
                color: 'from-indigo-500/20 to-blue-500/10',
                dot: 'bg-indigo-400',
              },
              {
                name: 'AlphaCoders',
                desc: 'One of the web\'s oldest and most comprehensive wallpaper archives. AI art, digital art, photography, anime and more — all in one place.',
                badge: 'Art • Photography • Anime',
                color: 'from-purple-500/20 to-pink-500/10',
                dot: 'bg-purple-400',
              },
            ].map((p, i) => (
              <motion.div
                key={p.name}
                initial={{ opacity: 0, x: i === 0 ? -40 : 40 }}
                whileInView={{ opacity: 1, x: 0 }}
                transition={{ duration: 0.7, delay: i * 0.15 }}
                viewport={{ once: false, amount: 0.5 }}
                className={`rounded-2xl border border-white/8 bg-gradient-to-br ${p.color} backdrop-blur-md p-8 text-left`}
              >
                <div className="flex items-center gap-3 mb-4">
                  <span className={`w-2.5 h-2.5 rounded-full ${p.dot}`} />
                  <h3 className="text-2xl font-bold text-white/90">{p.name}</h3>
                  <span className="ml-auto rounded-full border border-white/10 bg-white/5 px-2.5 py-0.5 text-xs text-white/40">{p.badge}</span>
                </div>
                <p className="text-gray-400 leading-relaxed">{p.desc}</p>
              </motion.div>
            ))}
          </div>
        </FadeIn>
      </section>

      {/* ── PAGE 5: Download CTA ── */}
      <section className="h-screen flex flex-col items-center justify-center text-center px-6 pointer-events-auto">
        <FadeIn delay={0}>
          <p className="text-white/30 text-sm uppercase tracking-widest font-semibold mb-4">Get started</p>
          <h2 className="text-5xl md:text-7xl font-bold mb-6 drop-shadow-2xl">
            Elevate your<br />
            <span className="text-transparent bg-clip-text bg-gradient-to-r from-indigo-400 to-purple-400">workspace.</span>
          </h2>
          <p className="text-gray-400 text-xl mb-8 max-w-xl mx-auto">
            One download. No installer drama. Unzip and run — works out of the box on Windows 10 &amp; 11.
          </p>

          <div className="flex flex-wrap items-center justify-center gap-3 mb-10">
            {perks.map((p) => (
              <div key={p} className="flex items-center gap-1.5 text-sm text-white/45">
                <CheckCircle2 className="w-4 h-4 text-green-400 flex-shrink-0" />
                {p}
              </div>
            ))}
          </div>

          <div className="flex flex-wrap items-center justify-center gap-4 mb-10">
            <a
              href="https://github.com/fa-hat/aura/releases"
              target="_blank"
              rel="noopener noreferrer"
              className="group flex items-center gap-2 rounded-full px-10 py-5 text-xl font-bold text-white transition-all duration-300 hover:scale-105"
              style={{ background: 'linear-gradient(135deg, #4f46e5 0%, #a855f7 100%)', boxShadow: '0 0 50px rgba(79,70,229,0.6), 0 0 100px rgba(168,85,247,0.2)' }}
            >
              <Download className="w-5 h-5" />
              Get Aura Now
              <ArrowRight className="w-5 h-5 transition-transform group-hover:translate-x-1" />
            </a>
            <a
              href="https://github.com/fa-hat/aura"
              target="_blank"
              rel="noopener noreferrer"
              className="flex items-center gap-2 rounded-full border border-white/10 bg-white/5 backdrop-blur-md px-8 py-4 text-lg font-semibold text-white/70 hover:text-white hover:bg-white/10 transition-all duration-300"
            >
              <Github className="w-5 h-5" />
              Star on GitHub
            </a>
          </div>

          <p className="text-xs text-white/20">Requires Windows 10 (1903+) or Windows 11 · x64 · ~80 MB</p>
        </FadeIn>
      </section>

    </div>
  );
}
