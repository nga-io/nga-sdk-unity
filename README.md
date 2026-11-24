# NGA SDK for Unity

The two Unity packages of the NGA SDK, each in its folder as the release's tarball carries it:

- `com.nga.sdk`: the package `network.nga.sdk`, the SDK's C# API over its Android library and iOS framework;
- `com.nga.sdk-render`: the package `network.nga.sdk-render`, for games that show NGA's ads. It requires
  `network.nga.sdk` at its own version.

Add both by Git URL, at the same tag, in *Window > Package Manager > + > Add package from git URL…*:

    https://github.com/nga-io/nga-sdk-unity.git?path=com.nga.sdk#v0.1.0
    https://github.com/nga-io/nga-sdk-unity.git?path=com.nga.sdk-render#v0.1.0

The packages need the External Dependency Manager for Unity (`com.google.external-dependency-manager`). Each
release also carries the two packages as tarballs, named by the release and its build id
(`network.nga.sdk-0.1.0-f2f29974a326.tgz`, `network.nga.sdk-render-0.1.0-f2f29974a326.tgz`): add both by `file:` path,
from the same release.

- Documentation: https://nga.io/docs/sdk/
- License: the NGA SDK License, in `LICENSE` and at https://nga.io/legal/sdk-license/
- Third-party notices: `NOTICES.md`
