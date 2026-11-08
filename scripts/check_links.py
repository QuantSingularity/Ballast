#!/usr/bin/env python3
"""Checks that relative links in Markdown files point at files that exist."""
import pathlib
import re
import sys

root = pathlib.Path(__file__).resolve().parent.parent
skip = {"node_modules", "bin", "obj", ".angular", "dist", ".git"}
link = re.compile(r"\[[^\]]*\]\(([^)\s]+)\)")
bad = 0
count = 0
for md in sorted(root.rglob("*.md")):
    if skip & set(md.relative_to(root).parts):
        continue
    for target in link.findall(md.read_text(encoding="utf-8")):
        if target.startswith(("http://", "https://", "mailto:", "#")):
            continue
        path = target.split("#", 1)[0]
        if not path:
            continue
        count += 1
        if not (md.parent / path).resolve().exists():
            bad += 1
            print(f"BROKEN {md.relative_to(root)} -> {target}")
print(f"checked {count} relative links, {bad} broken")
sys.exit(1 if bad else 0)
