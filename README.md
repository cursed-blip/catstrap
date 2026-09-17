> [!CAUTION]
> The only official place to download Catstrap is this GitHub repository. Any
> other site offering downloads, or claiming to be us, is not affiliated with
> the project. Do not download from them.

<div align="center">

![Catstrap][banner]

![][badge-latest]
![][badge-downloads]
![][badge-license]
![][badge-actions]
![][badge-stars]

![][badge-platform]
![][badge-dotnet]
![][badge-status]

![][preview]

</div>

Catstrap is a custom bootstrapper for Roblox, forked from [Fishstrap][fishstrap],
which is itself based on [Bloxstrap][bloxstrap]. It adds a set of features the
upstream projects do not have, including a built-in local asset proxy for
swapping game assets before they reach the client.

Catstrap is in **beta**. Expect rough edges, and expect settings and behaviour to
change between releases. If something breaks, please
[open an issue][repo-new-issue] rather than assuming it is intentional.

> [!NOTE]
> Catstrap is built for **Windows 10 and above**. On macOS or Linux, take a look
> at [AppleBlox][appleblox] and [Sober][sober] respectively.

## Features

**Asset Proxy**

A local proxy that sits between Roblox and its servers and swaps textures, audio,
meshes and animations before they reach the game client. Assets can be replaced
by ID, redirected to a CDN URL or a local file, removed from batch requests, and
cached so they are only downloaded once. The rules are stored as JSON, so packs
can be shared, imported and exported instead of written by hand.

**Quickplay**

Keep a library of the games you actually play and launch them in one click. Each
entry shows live player counts and server regions, so you know what you are
joining before you join it, and any game can be given a desktop shortcut.

**Accounts**

Add your accounts and switch between them from the top of the window. Your avatar
and banner are rendered in Catstrap's own style, and the wardrobe lets you browse
and equip items without opening a browser. Accounts are saved automatically
unless you turn that off, and playtime is tracked per game.

**Personalisation**

Pick any accent colour with a full colour picker, set a GIF as an animated
background, and choose between light and dark themes. A reduce visual effects
option strips the heavier animations out for slower machines.

**Rendering**

Texture quality, mesh detail, MSAA, the frame rate cap and the rendering mode are
grouped in one place rather than spread across flag lists.

**Tuning and privacy**

A FastFlags editor with an allowlist, an editor for Roblox's global basic
settings, and a Roblox version pinning option for rolling the client back when a
new build misbehaves. Cache cleaner and channel switcher are here too. Analytics
are opt-in, and Roblox's own telemetry is blocked unless you turn that off.

**Config packages**

Export your whole setup to a `.cfg` file and hand it to someone else, or load
theirs. Settings and asset rules travel, and nothing about your account does.

**Server information**

Live server details through [RoValra][rovalra]'s API, along with Discord Rich
Presence support and Catstrap's own invite handling.

> FastFlags outside the allowlist cannot be applied. This does not affect Roblox
> Studio. [Learn more][devforum-fflags]

## Special thanks

- [Valra](https://github.com/NotValra) for providing their API
- [Fleasion](https://github.com/fleasion/Fleasion) for the asset proxy concept
- The Bloxstrap and Fishstrap teams, for the project this is based on
- Other independent contributors

[banner]: https://github.com/cursed-blip/catstrap/raw/main/Images/Catstrap-Logo.png
[preview]: https://github.com/cursed-blip/catstrap/raw/main/Images/Catstrap-Preview.png

[repo-latest]:  https://github.com/cursed-blip/catstrap/releases/latest
[repo-new-issue]: https://github.com/cursed-blip/catstrap/issues/new

[badge-latest]: https://img.shields.io/github/v/release/cursed-blip/catstrap?display_name=release&label=release
[badge-downloads]: https://img.shields.io/github/downloads/cursed-blip/catstrap/total?label=downloads
[badge-license]: https://img.shields.io/github/license/cursed-blip/catstrap?label=licence
[badge-actions]: https://img.shields.io/github/actions/workflow/status/cursed-blip/catstrap/ci-release.yml?branch=main&label=build
[badge-stars]: https://img.shields.io/github/stars/cursed-blip/catstrap?label=stars
[badge-platform]: https://img.shields.io/badge/platform-Windows%2010%2B-0078D6?logo=windows
[badge-dotnet]: https://img.shields.io/badge/.NET-6.0-512BD4?logo=dotnet
[badge-status]: https://img.shields.io/badge/status-beta-orange

[bloxstrap]: https://github.com/bloxstraplabs/bloxstrap
[fishstrap]: https://github.com/fishstrap/fishstrap
[fleasion]:  https://github.com/fleasion/Fleasion
[rovalra]:  https://rovalra.com
[appleblox]: https://github.com/appleblox/appleblox
[sober]: https://sober.vinegarhq.org/
[devforum-fflags]: https://devforum.roblox.com/t/2499150
