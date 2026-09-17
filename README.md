> [!CAUTION]
> The only official place to download Catstrap is this GitHub repository.
> Any other websites offering downloads or claiming to be us are not controlled
> by us, do not download from them.

<div align="center">

![][banner]

![][badge-status]
![][badge-license]
![][badge-actions]
![][badge-downloads]
[![][badge-latest]][repo-latest]
![][badge-stars]

![][preview]

</div>

Catstrap is a custom bootstrapper for Roblox, forked from
[Fishstrap][fishstrap], which is itself based on [Bloxstrap][bloxstrap].
It provides additional features to enhance your experience, including a
built-in local asset proxy (inspired by [Fleasion][fleasion]) for swapping
game assets in real time.

> [!IMPORTANT]
> **Catstrap is in beta.** Things are still moving quickly, so expect rough
> edges, and expect settings or behaviour to change between releases. If
> something breaks, please [open an issue here][repo-new-issue] rather than
> assuming it is intentional.

If you found any bugs, please [open an issue here][repo-new-issue].

> [!NOTE]
> Catstrap is an application for **Windows 10 and above.** For other operating
> systems, such as Mac OS and various Linux distributions, you can try
> [AppleBlox][appleblox] and [Sober][sober] respectively.

## Features

### Personalisation

- **Make it yours** — pick any accent colour with a proper colour picker, and
  the whole interface follows it.
- **Custom animated backgrounds** — point Catstrap at a GIF and it becomes the
  window background.
- **Themes** — switch between light and dark, with a dark theme that is
  actually dark.
- **Tidy interface** — a reorganised settings window with everything grouped
  into tabs, plus a "reduce visual effects" option that strips the heavier
  animations out for slower machines.

### Games

- **Quickplay** — keep a library of the games you actually play, launch them in
  one click, and watch **live player counts** and **server regions** for each
  one so you know what you are joining before you join it.
- **Game shortcuts** — drop a shortcut for any game straight onto your desktop,
  so you can open Rivals without opening Catstrap first.
- **Playtime tracking** — see how long you have spent in each game.
- **Game invites** — Catstrap's own invite support.

### Accounts

- **Account profiles** — add your accounts, switch between them from the top of
  the window, and see your avatar and banner rendered in Catstrap's own style.
- **Wardrobe** — browse and equip avatar items without opening a browser:
  avatars, clothing, accessories and animations.
- **Saved outfits** — save the avatars and creations you like, and put them
  back on in a click.
- **Auto-saving** — accounts are saved as you add them, unless you turn that off
  in Config.

### Assets and performance

- **Asset Proxy** — a local MITM proxy that sits between Roblox and its servers,
  letting you swap textures, audio, meshes, animations and other assets before
  they reach the game client. Replace assets by ID, remove them from batch
  requests, redirect them to CDN URLs or local files, and cache everything
  Roblox downloads.
  - **Config files** — asset rules live in JSON, so packs can be shared,
    imported and exported instead of hand-written, with configs included for
    common things like skyboxes.
- **Rendering controls in one place** — texture quality, mesh detail, MSAA,
  frame rate cap and more, grouped with the rest of the graphics settings.
- **Roblox version pinning** — roll the client back to an older build when a
  new one misbehaves.
- **Cache cleaner, channel switcher** and a faster boot path.

### Tuning and privacy

- **Unhidden FastFlags editor**
  - You cannot apply FastFlags not present in the allowlist. This does not
    affect Roblox Studio. [Learn more][devforum-fflags]
- **Global Basic Settings editor**
  - Ability to increase frame rate cap, toggle quality levels and more.
- **Config packages** — export your entire setup to a `.cfg` file and hand it to
  someone else, or load theirs. Settings and asset rules travel; nothing about
  your account does.
- **Telemetry is off by default** — analytics are opt-in, and Roblox's own
  telemetry is blocked unless you turn that off.
- **Server information** using [RoValra][rovalra]'s API.

## Special thanks

- [Valra](https://github.com/NotValra) for providing their API
- [Fleasion](https://github.com/fleasion/Fleasion) for the asset proxy concept
- The Bloxstrap and Fishstrap teams, for the project this is based on
- Other independent contributors

[banner]: https://github.com/cursed-blip/catstrap/raw/main/Images/Catstrap-Logo.png
[preview]: https://github.com/cursed-blip/catstrap/raw/main/Images/Catstrap-Preview.png

[repo-latest]:  https://github.com/cursed-blip/catstrap/releases/latest
[repo-new-issue]: https://github.com/cursed-blip/catstrap/issues/new
[badge-status]: https://img.shields.io/badge/status-beta-orange
[badge-license]: https://img.shields.io/github/license/cursed-blip/catstrap
[badge-actions]: https://img.shields.io/github/actions/workflow/status/cursed-blip/catstrap/ci-release.yml?branch=main
[badge-downloads]: https://img.shields.io/github/downloads/cursed-blip/catstrap/total
[badge-latest]: https://img.shields.io/github/v/release/cursed-blip/catstrap?display_name=release
[badge-stars]: https://img.shields.io/github/stars/cursed-blip/catstrap

[bloxstrap]: https://github.com/bloxstraplabs/bloxstrap
[fishstrap]: https://github.com/fishstrap/fishstrap
[fleasion]:  https://github.com/fleasion/Fleasion
[rovalra]:  https://rovalra.com
[appleblox]: https://github.com/appleblox/appleblox
[sober]: https://sober.vinegarhq.org/
[devforum-fflags]: https://devforum.roblox.com/t/2499150
