> [!CAUTION]
> The only official place to download Catstrap is this GitHub repository.
> Any other websites offering downloads or claiming to be us are not controlled
> by us, do not download from them.

<div align="center">

![][banner]

![][badge-license]
![][badge-actions]
![][badge-downloads]
[![][badge-latest]][repo-latest]
![][badge-stars]

</div>

Catstrap is a custom bootstrapper for Roblox, forked from
[Fishstrap][fishstrap], which is itself based on [Bloxstrap][bloxstrap].
It provides additional features to enhance your experience, including a
built-in local asset proxy (inspired by [Fleasion][fleasion]) for swapping
game assets in real time.

If you found any bugs, please [open an issue here][repo-new-issue].

> [!NOTE]
> Catstrap is an application for **Windows 10 and above.** For other operating
> systems, such as Mac OS and various Linux distributions, you can try
> [AppleBlox][appleblox] and [Sober][sober] respectively.

## Features

- Detailed server information using [RoValra][rovalra]'s API
- Support for Roblox Studio
- Unhidden FastFlags editor
  - You cannot apply FastFlags not present in the allowlist. This does not
    affect Roblox Studio. [Learn more][devforum-fflags]
- Global Basic Settings editor
  - Ability to increase frame rate cap, toggle quality levels and more
- Catstrap's own game invites
- **Asset Proxy** — a local MITM proxy that sits between Roblox and its
  servers, letting you swap textures, audio, meshes, animations and other
  assets before they reach the game client. Replace assets by ID, remove
  them from batch requests, redirect them to CDN URLs or local files, and
  cache everything Roblox downloads.
- Performance optimizations for faster boot times
- Cache cleaner, channel switcher and many more

## Special thanks

- [Valra](https://github.com/NotValra) for providing their API
- [Fleasion](https://github.com/fleasion/Fleasion) for the asset proxy concept
- The Bloxstrap and Fishstrap teams, for the project this is based on
- Other independent contributors

[banner]: https://github.com/cursed-blip/catstrap/raw/main/Images/Catstrap-Logo.png

[repo-latest]:  https://github.com/cursed-blip/catstrap/releases/latest
[badge-license]: https://img.shields.io/github/license/cursed-blip/catstrap
[badge-actions]: https://img.shields.io/github/actions/workflow/status/cursed-blip/catstrap/ci-release.yml?branch=main
[badge-downloads]: https://img.shields.io/github/downloads/cursed-blip/catstrap/total
[badge-latest]: https://img.shields.io/github/v/release/catstrap/catstrap?display_name=release
[badge-stars]: https://img.shields.io/github/stars/catstrap/catstrap

[bloxstrap]: https://github.com/bloxstraplabs/bloxstrap
[fishstrap]: https://github.com/fishstrap/fishstrap
[fleasion]:  https://github.com/fleasion/Fleasion
[repo-new-issue]: https://github.com/catstrap/catstrap/issues/new
[rovalra]:  https://rovalra.com
[appleblox]: https://github.com/appleblox/appleblox
[sober]: https://sober.vinegarhq.org/
[devforum-fflags]: https://devforum.roblox.com/t/2499150