#!/usr/bin/env python3
"""Render docs/terms-of-use.md and docs/privacy-policy.md into styled HTML
pages under website/, wrapped in the site chrome.

Run after editing either markdown file:
    python website/build-legal.py

Requires: markdown (pip install markdown)
"""
from __future__ import annotations
import pathlib
import re
import markdown

REPO = pathlib.Path(__file__).resolve().parent.parent

PAGES = [
    ("docs/terms-of-use.md", "website/terms.html", "Terms of Use"),
    ("docs/privacy-policy.md", "website/privacy.html", "Privacy Policy"),
]

SHELL = """<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{title} — Battery Intelligence</title>
<meta name="robots" content="all">
<link rel="icon" href="/favicon.ico" sizes="any">
<link rel="icon" type="image/png" href="/icon-192.png">
<link rel="stylesheet" href="/styles.css">
</head>
<body>
<header class="site-header">
  <div class="wrap">
    <a class="brand" href="/"><img src="/icon-192.png" alt="">Battery Intelligence</a>
    <nav class="nav-links">
      <a href="/#features">Features</a>
      <a href="/#install">Install</a>
      <a class="nav-cta" href="/#download">Download</a>
    </nav>
  </div>
</header>
<main class="doc">
  <div class="wrap">
    <a class="back-link" href="/">&larr; Back to Battery Intelligence</a>
    <h1>{title}</h1>
    <p class="updated">Last updated: {updated}</p>
{body}
  </div>
</main>
<footer class="site-footer">
  <div class="wrap">
    <div>Battery Intelligence &mdash; an independent utility by <strong>Naeem Ahmad</strong>. MIT licensed.</div>
    <div class="foot-links">
      <a href="/terms.html">Terms of Use</a>
      <a href="/privacy.html">Privacy Policy</a>
      <a href="https://github.com/hobbygen/battery-intelligence">GitHub</a>
      <a href="mailto:awad.print@gmail.com">Contact</a>
    </div>
  </div>
</footer>
</body>
</html>
"""


def build(md_path: str, html_path: str, title: str) -> None:
    src = (REPO / md_path).read_text(encoding="utf-8")

    updated = ""
    m = re.search(r"^\*\*Last updated:\s*(.+?)\*\*", src, re.M)
    if m:
        updated = m.group(1).strip()

    # strip the leading "# Title" line and the "**Last updated…**" line
    src = re.sub(r"\A#\s+.*?\n", "", src, count=1)
    src = re.sub(r"^\*\*Last updated:.*$\n?", "", src, flags=re.M)

    body = markdown.markdown(src, extensions=["extra", "sane_lists", "smarty"])

    out = SHELL.format(title=title, updated=updated, body=body)
    (REPO / html_path).write_text(out, encoding="utf-8")
    print(f"wrote {html_path}")


if __name__ == "__main__":
    for args in PAGES:
        build(*args)
