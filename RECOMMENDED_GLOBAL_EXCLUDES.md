# Recommended Global Excludes

Armor keeps a single **global exclude list** — a set of rules applied to every backup policy
that has *"use global excludes"* turned on (it is on by default). It exists so the noise that is
never worth backing up — build output, package and tool caches, OS-generated metadata — stays out
of every backup without you repeating the rules on each policy.

- **See and edit it** in the TUI: press <kbd>g</kbd>. You can add rules, remove rules, or restore
  the built-in defaults.
- **Per policy**, you can turn the global list off, and each policy also has its own exclude list.
- **How a rule matches** (see also the editor's own help):
  - `name` — a bare name matches a **file or folder** of that name anywhere (e.g. `node_modules`).
  - `name/` — a trailing slash limits the rule to **folders** only.
  - `*.ext` — a wildcard on the file name (e.g. `*.tmp`).
  - `re:<regex>` — a regular expression matched against the **full path** (forward slashes), for
    location-specific rules like `re:.*/Library/Caches/.*`.
- Matching is **case-insensitive**, and the list applies on **every operating system** — a name
  that only exists on one platform (say `.DS_Store`) simply never matches on the others, so it is
  harmless to carry everywhere.

> **Before you rely on it:** the defaults are deliberately conservative, but only you know your
> data. Skim this list, and **always review your first backup** to confirm nothing you wanted was
> excluded. See *Best practices* in the [README](README.md#best-practices).

---

## What the global list excludes today

These are the built-in defaults (all matched by name, as a file or folder).

### Cross-platform — developer & build output
`.git` · `bin` · `obj` · `debug` · `release` · `node_modules` · `.vs` · `packages` · `.nuget` ·
`__pycache__` · `.gradle` · `target` · `venv` · `.venv` · `dist` · `build`

### Cross-platform — caches & transient
`.cache` · `Temp`

### Windows
`AppData` · `$RECYCLE.BIN` · `System Volume Information` · `ntuser.dat` · `ntuser.dat.*` ·
`Thumbs.db` · `desktop.ini`

### macOS
`.DS_Store` · `.Spotlight-V100` · `.fseventsd` · `.DocumentRevisions-V100` · `.TemporaryItems` ·
`.Trashes`

### Linux
`lost+found` · `.thumbnails`

> A few of the "cross-platform" names above are inherently collision-prone: `build`, `target`,
> and `dist` are common project-folder names as well as build directories. If you have real data
> in a folder with one of those names, remove the rule or scope it with a `re:` path regex.

---

## Recommended additions to consider

None of these are added by default — review and add the ones that fit how you use each machine.
Risk notes call out anything that could match real data.

### All platforms — editor & office temporaries (file rules)

| Rule | What it is | Notes |
|---|---|---|
| `*.swp` / `*.swo` | vim swap files | Transient; safe. Broad glob, but `.swp`/`.swo` are vim-specific. |
| `.~lock.*` | LibreOffice lock files | Transient; safe. |
| `~$*` | Microsoft Office lock/temp files | Transient; safe. |
| `*~` | editor backup copies (emacs, gedit, …) | **Caution:** a `file~` is a backup of a real file — you may *want* it. Broad. |

### Windows

| Rule | What it is | Notes |
|---|---|---|
| `pagefile.sys` | virtual-memory page file | Huge, system-managed, never useful in a backup. |
| `hiberfil.sys` | hibernation image | Huge, system-managed. |
| `swapfile.sys` | modern-app swap file | System-managed. |
| `$WinREAgent` / `Recovery` | Windows recovery staging | System-managed. |
| `Windows/` | the OS itself | Only if a policy includes all of `C:\`. Opinionated — the OS is reinstallable. |
| `Program Files/` · `Program Files (x86)/` · `ProgramData/` | installed applications | Opinionated — reinstallable, but `ProgramData` can hold app data you care about. Review first. |

### macOS

| Rule | What it is | Notes |
|---|---|---|
| `re:.*/Library/Caches/.*` | per-user cache store (the macOS analog of `AppData\Local`) | High value. Use the **regex** form — a bare `Caches` would match unrelated folders. |
| `.Trash` | user trash (`~/.Trash`) | Deleted items. Not "payload," but it is data you discarded — your call. |
| `.AppleDouble` | resource-fork/Finder-info sidecars on non-native volumes | Usually metadata; occasionally carries a resource fork. |
| `Icon\r` | custom folder-icon files | Cosmetic. (The name ends in a carriage return.) |
| `.Trashes` | volume trash | Already a default. |

### Linux

| Rule | What it is | Notes |
|---|---|---|
| `re:.*/\.local/share/Trash/.*` | user trash | Deleted items — your call. |
| `.Trash-*` | trash on removable/mounted volumes | Deleted items. |
| `.npm` · `.cargo` · `.rustup` · `.m2` · `.ivy2` · `.gem` · `.pub-cache` | language/tool caches under `$HOME` | High volume, fully regenerable. Bare names are safe (dotfiles). |
| `.pyenv` · `.nvm` | version-manager installs | Reinstallable toolchains. |
| `snap` · `.var` | Snap / Flatpak per-app data | Can be large; small chance of colliding with a user folder named `snap`. |

> **Whole-disk / root backups only:** if you ever point a policy at `/` (or a drive root), also
> exclude the virtual and pseudo filesystems — `re:^/proc/.*`, `re:^/sys/.*`, `re:^/dev/.*`,
> `re:^/run/.*` on Linux — since they are not real files. Most people back up specific folders and
> never need these.

---

## Adding these

Press <kbd>g</kbd> in the TUI to open the global exclude editor, then add each rule (`name`,
`name/`, `*.ext`, or `re:<regex>`). To apply a rule to a single policy instead of everywhere, add
it to that policy's own exclude list in the policy editor. After changing excludes, run a backup
and review its restore points to confirm the result is what you intended.
