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

That build also linked other people's libraries into the FFmpeg libraries - read off the libraries
themselves on 2026-10-24, since the build's configure line lists far more than was linked:
libvorbis, libogg, libopus and libiconv (avcodec), zlib (avformat), the OpenCL ICD loader (avutil),
soxr (swresample), and the MinGW-w64 runtime and winpthreads (all of them, and the extension). Each
has its notice and license text in `engine/licenses`. libiconv and soxr are under the LGPL, so their
source goes out too, with the scripts that built the libraries:

- `FFmpeg-Builds-0e1b9758c4a6b97fac077eb3ecf9596cf858adea-source.tar.gz` - `EIRTeam/FFmpeg-Builds` at
  the commit of release `autobuild-2023-07-24-08-52`: every library and revision that went into the
  build (`scripts.d/`), and the variant (`variants/win64-lgpl-godot.sh`). GitHub's archive, unchanged.
- `libiconv-6e2b31f6d66739c5abd850338ea68c6bd2012812-source.tar.gz` - libiconv at the commit the build
  scripts pin, `git archive` of `git.savannah.gnu.org/git/libiconv.git`.
- `gnulib-b70b4f0cfc8f623c80012bde14047efaef8c720c-source.tar.gz` - Gnulib, which libiconv's build
  pulled unpinned: its state on the build day (last commit before 2023-07-24 08:52 UTC), GitHub's
  archive of the mirror `coreutils/gnulib`. It supplies the build's configure macros (e.g.
  `codeset.m4`) - the LGPL counts the scripts that control compilation as part of the source.
- `soxr-945b592b70470e29f917f4de89b4281fbbd540c0-source.tar.gz` - soxr at the pinned commit,
  `git archive` of `git.code.sf.net/p/soxr/code`.

`Gooner.Build.Tests` checks that the FFmpeg archive is the revision the libraries name, that the
notices and the release check name these files, and that every library found linked into the FFmpeg
libraries has its notice. A new build of the extension means replacing all of it together: the
libraries, the archives, the notices - and reading the new libraries again for what is linked in.
