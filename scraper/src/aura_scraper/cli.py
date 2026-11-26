"""Command line entry point.

    aura-scraper doctor                      # is every site reachable, and why not
    aura-scraper categories alphacoders      # the site's own category list
    aura-scraper crawl alphacoders --pages 2 # scrape -> data/<site>/
    aura-scraper index alphacoders           # what is in the dataset
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from .config import Settings
from .crawler import crawl_site
from .errors import ScraperError
from .sites import SITES, get_site
from .store import DatasetStore


def _print_site_status(site_name: str, settings: Settings) -> bool:
    site = get_site(site_name, settings)
    try:
        categories = site.categories()
    except ScraperError as exc:
        print(f"  {site_name:<14} FAILED  {type(exc).__name__}: {exc}")
        return False
    except Exception as exc:  # unexpected, still loud
        print(f"  {site_name:<14} FAILED  {type(exc).__name__}: {exc}")
        return False
    print(
        f"  {site_name:<14} OK      {len(categories)} categories "
        f"(html via {site.html_transport})"
    )
    return True


def cmd_doctor(args: argparse.Namespace) -> int:
    settings = Settings.from_env()
    print(f"data root : {settings.data_root}")
    print(f"bypass    : {settings.bypass_base_url}")
    print("")

    bypass = get_site("alphacoders", settings).bypass
    try:
        stats = bypass.health()
        print(f"  {'cf-bypass':<14} OK      {stats}")
    except ScraperError as exc:
        print(f"  {'cf-bypass':<14} FAILED  {exc}")
    print("")

    failures = 0
    for name in sorted(SITES):
        if not _print_site_status(name, settings):
            failures += 1
    return 1 if failures else 0


def cmd_categories(args: argparse.Namespace) -> int:
    settings = Settings.from_env()
    site = get_site(args.site, settings)
    for category in site.categories():
        print(f"{category.key}\t{category.name}")
    return 0


def cmd_crawl(args: argparse.Namespace) -> int:
    settings = Settings.from_env()
    store = DatasetStore(settings.data_root)
    site = get_site(args.site, settings)

    categories = site.categories()
    if args.category:
        wanted = {c.lower() for c in args.category}
        categories = [
            c for c in categories if c.key.lower() in wanted or c.name.lower() in wanted
        ]
        if not categories:
            print(
                f"no category matched {args.category} - known keys: "
                f"{', '.join(c.key for c in site.categories())}",
                file=sys.stderr,
            )
            return 2

    print(
        f"crawling {site.name}: {len(categories)} categories x {args.pages} page(s)"
    )
    report = crawl_site(
        site=site,
        store=store,
        categories=categories,
        pages=args.pages,
        want_thumb=not args.no_thumb,
        want_full=args.full,
        limit=args.limit,
        skip_existing=not args.force,
    )

    index_path = store.write_index(
        site=site.name,
        items=report.items,
        categories=report.categories,
        failures=report.failures,
    )

    print(f"saved {report.ok_count} items -> {index_path}")
    if report.fail_count:
        print(f"FAILED {report.fail_count} item(s) - the reasons are in the index:")
        for failure in report.failures[:5]:
            print(f"  - {failure['stage']} {failure.get('item_id') or failure.get('category')}: {failure['error']}")
        if report.fail_count > 5:
            print(f"  ... and {report.fail_count - 5} more (see index.json)")
    return 1 if report.fail_count and not report.ok_count else 0


def cmd_index(args: argparse.Namespace) -> int:
    settings = Settings.from_env()
    store = DatasetStore(settings.data_root)
    index = store.load_index(args.site)
    if index is None:
        print(
            f"no dataset for '{args.site}' in {settings.data_root} - run "
            f"'aura-scraper crawl {args.site}' first.",
            file=sys.stderr,
        )
        return 2
    print(json.dumps(
        {
            "site": index["site"],
            "generated_at": index["generated_at"],
            "categories": len(index["categories"]),
            "item_count": index["item_count"],
            "failures": len(index.get("failures", [])),
        },
        indent=2,
    ))
    return 0


def cmd_install(args: argparse.Namespace) -> int:
    """Point the installed app at this dataset.

    The app resolves its dataset root in this order:
      1. the AURA_SCRAPER_DATA environment variable
      2. <repo>/scraper/data (a dev checkout)
      3. %LOCALAPPDATA%\\Aura\\dataset (the installed app)

    This command links (or copies) the dataset into the third location, so
    the INSTALLED app reads the same files the dev checkout does.
    """
    import os
    import shutil
    import subprocess

    settings = Settings.from_env()
    source = settings.data_root
    if not source.exists():
        print(
            f"no dataset at {source} - run 'aura-scraper crawl <site>' first.",
            file=sys.stderr,
        )
        return 2

    target = Path(
        args.target
        or Path(
            os.environ.get("LOCALAPPDATA", str(Path.home()))
        )
        / "Aura"
        / "dataset"
    )
    target.parent.mkdir(parents=True, exist_ok=True)

    if target.exists():
        print(f"{target} already exists - remove it first if you want a fresh link.")
        return 1

    # a directory junction is instant and stays in sync with the repo copy
    try:
        result = subprocess.run(
            ["cmd", "/c", "mklink", "/J", str(target), str(source)],
            capture_output=True,
            text=True,
        )
        if result.returncode == 0:
            print(f"linked {target} -> {source}")
            return 0
        print(f"mklink failed ({result.stdout.strip()} {result.stderr.strip()}) - copying instead")
    except Exception as exc:  # pragma: no cover
        print(f"mklink unavailable ({exc}) - copying instead")

    shutil.copytree(source, target)
    print(f"copied {source} -> {target}")
    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="aura-scraper",
        description="Real scraper for the Aura wallpaper app.",
    )
    sub = parser.add_subparsers(dest="command", required=True)

    sub.add_parser("doctor", help="check every site and print why it fails").set_defaults(
        func=cmd_doctor
    )

    categories = sub.add_parser(
        "categories", help="list a site's categories"
    )
    categories.add_argument("site")
    categories.set_defaults(func=cmd_categories)

    crawl = sub.add_parser("crawl", help="scrape a site into the dataset")
    crawl.add_argument("site")
    crawl.add_argument(
        "--category", action="append", help="only this category (repeatable)"
    )
    crawl.add_argument("--pages", type=int, default=1)
    crawl.add_argument("--full", action="store_true", help="also download full images")
    crawl.add_argument("--no-thumb", action="store_true", help="skip the thumbnails")
    crawl.add_argument("--limit", type=int, default=None)
    crawl.add_argument("--force", action="store_true", help="re-download existing items")
    crawl.set_defaults(func=cmd_crawl)

    index = sub.add_parser("index", help="show the dataset summary")
    index.add_argument("site")
    index.set_defaults(func=cmd_index)

    install = sub.add_parser(
        "install", help="point the installed app at this dataset"
    )
    install.add_argument(
        "--target", default=None, help="where to link (default %LOCALAPPDATA%\\Aura\\dataset)"
    )
    install.set_defaults(func=cmd_install)

    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    try:
        return args.func(args)
    except ScraperError as exc:
        print(f"{type(exc).__name__}: {exc}", file=sys.stderr)
        return 2
    except KeyError as exc:
        print(str(exc).strip("'\""), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
