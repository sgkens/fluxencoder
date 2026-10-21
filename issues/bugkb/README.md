# Pending bugkb entries

Knowledge-base entries recorded while the bugkb server was unreachable, kept here for later ingestion.

## Ingest

Each `.json` file is one entry in the format `kb add --json-stdin` accepts. Run from the repository root (bash) once
the server is up:

```bash
for f in issues/bugkb/*.json; do
  python3 ~/.claude/skills/bugkb/scripts/kb.py add --json-stdin < "$f" && git rm -q "$f"
done
```

`kb add` refuses near-duplicates with exit code 3; for those, `kb confirm <id>` the existing entry instead.
