# FFmpeg source, as the release carries it

The engine plays video through the Godot extension EIRTeam.FFmpeg, whose prebuilt FFmpeg libraries
sit in `engine/godot/addons/ffmpeg/win64/`. FFmpeg is passed on under the LGPL, which lets it go out
only with its source in the same place - so the release check (`.github/workflows/release-check.yml`)
attaches both archives here to every release, and `engine/licenses/THIRD_PARTY_NOTICES.txt` says so.

- `FFmpeg-5b11ee94299fe4e07d3921a3b8f29fff72e1daa1-source.tar.gz` - FFmpeg at the commit the libraries
  were built from (their version string: `N-111611-g5b11ee9429-20230724`). GitHub's archive of
  `FFmpeg/FFmpeg` at that commit, unchanged.
- `EIRTeam.FFmpeg-84ea333cb6b836b3a8951a70830b836d18c893d1-source.tar.gz` - the extension at the commit
  of the autobuild the libraries come from (MIT; its license text is
  `engine/licenses/EIRTeam.FFmpeg-MIT.txt`). Its `gdextension_build/ffmpeg_download.py` names the
  FFmpeg build it bundles: release `autobuild-2023-07-24-08-52` of `EIRTeam/FFmpeg-Builds`, variant
  `win64-lgpl-godot`. GitHub's archive of `EIRTeam/EIRTeam.FFmpeg` at that commit, unchanged.

`Gooner.Build.Tests` checks that the FFmpeg archive is the revision the libraries name, and that the
notices and the release check name these files. A new build of the extension means replacing all of
it together: the libraries, both archives, the notices.
