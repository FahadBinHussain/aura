# ArtStation API Findings

Sources checked:
- IndieWeb documents that ArtStation has no official API but exposes public JSON endpoints, including specific artwork data at `https://www.artstation.com/project/[artwork hash].json` (the live endpoint currently works with `/projects/[hash].json`). Source: https://indieweb.org/Artstation
- Live ArtStation list endpoint verified on 2026-05-08: `https://www.artstation.com/projects.json?sorting=trending&page=1` returns JSON with `data`, `total_count`, and 50 project records per page.
- Live ArtStation detail endpoint verified on 2026-05-08: `https://www.artstation.com/projects/{hash}.json` returns project metadata and an `assets` array containing image assets with `image_url`, `width`, and `height`.

Useful response fields:
- List project: `hash_id`, `title`, `description`, `likes_count`, `views_count`, `permalink`, `cover.thumb_url`, `cover.small_square_url`, `user.full_name`.
- Detail project: `title`, `description`, `permalink`, `cover_url`, `assets[].has_image`, `assets[].asset_type`, `assets[].image_url`, `assets[].width`, `assets[].height`.

Implementation choice:
- Use `projects.json` for grid browsing.
- Fetch `projects/{hash}.json` only when opening detail so we do not issue detail requests for every card.
- Use browser-like headers and normal HttpClient; live curl requests work from this machine.
